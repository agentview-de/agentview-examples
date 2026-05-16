using System.Text.Json;
using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Wraps the cookie-authenticated agentView REST endpoints the setup
/// wizard needs to provision a display end-to-end:
/// <list type="bullet">
///   <item><description><c>GET /auth/me</c> — login probe.</description></item>
///   <item><description><c>GET /api/v1/agent/displays</c> — list the user's displays.</description></item>
///   <item><description><c>POST /api/v1/agent/displays</c> — create a personal display.</description></item>
///   <item><description><c>PUT /api/v1/data/{slug}</c> — create the data slot the display reads.</description></item>
///   <item><description><c>POST /api/v1/agent/displays/{id}/content</c> — push the rendered HTML.</description></item>
///   <item><description><c>POST /api/v1/agent/api-keys</c> — mint a scoped <c>avk_...</c> key.</description></item>
///   <item><description><c>POST /api/v1/agent/displays/pair-by-code</c> — bind a physical screen.</description></item>
/// </list>
/// All traffic is routed through the shared <see cref="WebViewSession"/>
/// so the request rides on the same browser cookies the user just
/// established in the login dialog.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two-clients rationale.</strong> This class (<c>AgentViewApiClient</c>)
/// handles the wizard (one-time provisioning): it needs dual-transport
/// (cookie via WebView, or X-API-Key for the Full-Setup API-key path)
/// because the wizard either borrows the user's browser session OR
/// operates with a broad dashboard key. Neither transport is suitable
/// for the background loop — the WebView session may idle out, and a
/// broad key has more scope than the loop needs.
/// </para>
/// <para>
/// The long-running ping loop uses the separate <see cref="AgentViewClient"/>,
/// which is intentionally minimal: one method, one transport (X-API-Key),
/// one responsibility (slot write). That separation is load-bearing —
/// do not merge the two classes without re-reading the design note in
/// <c>dotnet/README.md</c>.
/// </para>
/// </remarks>
public sealed class AgentViewApiClient : IAgentViewApiClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] ScopedKeyCapabilities = ["slot.write"];

    private readonly WebViewSession _session;
    private readonly HttpClient     _http;
    private readonly DiagnosticsLog _log;
    private readonly string         _baseUrl;

    /// <summary>
    /// When set, every request rides on an <c>X-API-Key</c> header via
    /// the plain HttpClient. When null, the WebView session (cookie
    /// auth) is used. Set by <see cref="UseApiKey"/>; cleared by
    /// <see cref="UseCookieSession"/>.
    /// </summary>
    private string? _apiKey;

    public AgentViewApiClient(WebViewSession session, HttpClient http, DiagnosticsLog log, string baseUrl)
    {
        _session = session;
        _http    = http;
        _log     = log;
        // Frozen at construction (startup config value). The wizard is
        // a one-shot flow, so a base-URL change in Settings only takes
        // full effect after a restart — acceptable here because the
        // background ping loop (AgentViewClient) reads the live config
        // each cycle, so data delivery follows the new URL immediately
        // even if a mid-session re-publish would still use the old one.
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public string BaseUrl => _baseUrl;

    /// <summary>True iff the client is currently configured for
    /// API-key auth instead of cookie auth.</summary>
    public bool IsApiKeyMode => !string.IsNullOrEmpty(_apiKey);

    /// <summary>
    /// Switches all subsequent requests to API-key (header) auth. The
    /// key is held in memory only; persistence lives in
    /// <c>AppConfig.AgentViewApiKey</c>.
    /// </summary>
    public void UseApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("apiKey is required.", nameof(apiKey));
        _apiKey = apiKey.Trim();
    }

    /// <summary>Switches back to cookie-session auth via the WebView.</summary>
    public void UseCookieSession() => _apiKey = null;

    /// <summary>Sends the WebView to the agentView login page.</summary>
    public void NavigateToLogin() => _session.Navigate(_baseUrl + "/login.html");

    /// <inheritdoc/>
    public async Task SignOutAsync()
    {
        // Drop the in-memory API-key transport first so a subsequent
        // probe genuinely reflects the cookie session (now cleared).
        UseCookieSession();
        await _session.ClearCookiesForOriginAsync(_baseUrl).ConfigureAwait(true);
    }

    // ─── Transport selection ────────────────────────────────────────
    //
    // Every public method below routes through this single helper.
    // The mode flip is therefore opaque to callers: the same Set-up
    // wizard works whether the user signed in via the embedded
    // browser (cookie) or pasted an API key (header).

    private async Task<HttpFetchResult> FetchAsync(
        string url, string method, string? bodyJson = null)
    {
        if (_apiKey is not null)
        {
            return await FetchWithApiKeyAsync(url, method, bodyJson, _apiKey).ConfigureAwait(true);
        }
        return await _session.FetchAsync(url, method, bodyJson, acceptOrigin: _baseUrl).ConfigureAwait(true);
    }

    /// <summary>
    /// API-key transport: a plain HttpClient request with
    /// <c>X-API-Key: avk_…</c>. No WebView, no cookies, no origin dance.
    /// </summary>
    private async Task<HttpFetchResult> FetchWithApiKeyAsync(
        string url, string method, string? bodyJson, string apiKey)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        req.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        req.Headers.TryAddWithoutValidation("Accept",    "application/json");

        if (bodyJson is not null)
        {
            req.Content = new StringContent(
                bodyJson,
                System.Text.Encoding.UTF8,
                "application/json");
        }

        try
        {
            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            _log.Info($"[ApiKey] {method} {url} → {(int)res.StatusCode}");
            return new HttpFetchResult(res.IsSuccessStatusCode, (int)res.StatusCode, body);
        }
        catch (Exception ex)
        {
            _log.Warn($"[ApiKey] {method} {url} threw {ex.GetType().Name}: {ex.Message}");
            return new HttpFetchResult(false, -1, ex.Message);
        }
    }

    /// <summary>
    /// Probes <c>/auth/me</c>. Returns true on 200 with a user object;
    /// false on 401/403, redirect-to-login, or any error.
    /// </summary>
    public async Task<bool> IsLoggedInAsync()
    {
        try
        {
            var me = await GetMeAsync().ConfigureAwait(true);
            return me?.User?.UserId is { Length: > 0 };
        }
        catch (AgentViewAuthException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Warn("agentView IsLoggedIn probe threw " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    /// <summary>Fetches <c>GET /auth/me</c>.</summary>
    public async Task<AgentViewMeResponse?> GetMeAsync()
    {
        var res = await FetchAsync(_baseUrl + "/auth/me", "GET").ConfigureAwait(true);

        if (!res.Ok)
        {
            if (res.Status is 401 or 403)
            {
                throw new AgentViewAuthException(
                    $"agentView returned {res.Status} on /auth/me. Please log in again.");
            }
            // /auth/me returns 401 for unauthenticated users normally,
            // but the cookie middleware can also redirect us to the
            // login page (302 -> 200 of the HTML). Treat any non-2xx
            // as "not logged in" here rather than throwing.
            return null;
        }
        return JsonSerializer.Deserialize<AgentViewMeResponse>(res.Body, Json);
    }

    /// <summary>
    /// Fetches <c>GET /api/v1/agent/displays</c>. Returns the displays
    /// the authenticated user can see (personal + group displays they
    /// are a member of).
    /// </summary>
    public async Task<List<AgentViewDisplay>> ListDisplaysAsync()
    {
        var res = await FetchAsync(_baseUrl + "/api/v1/agent/displays?limit=200", "GET").ConfigureAwait(true);

        ThrowIfNotOk(res, "/api/v1/agent/displays");

        var env = JsonSerializer.Deserialize<ListDisplaysResponse>(res.Body, Json)
                  ?? new ListDisplaysResponse();
        return env.Displays;
    }

    /// <summary>
    /// Creates a new personal display via <c>POST /api/v1/agent/displays</c>.
    /// </summary>
    public async Task<CreateDisplayResponse> CreatePersonalDisplayAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name is required.", nameof(name));

        var body = JsonSerializer.Serialize(new { name = name.Trim() });
        var res  = await FetchAsync(_baseUrl + "/api/v1/agent/displays", "POST", body).ConfigureAwait(true);

        ThrowIfNotOk(res, "/api/v1/agent/displays (POST)");

        return JsonSerializer.Deserialize<CreateDisplayResponse>(res.Body, Json)
               ?? throw new InvalidOperationException("Empty create-display response.");
    }

    /// <summary>
    /// Creates or updates a data slot via
    /// <c>PUT /api/v1/data/{slug}</c> with cookie auth, returning the
    /// slot's public <c>readUrl</c> so callers can plug it into the
    /// display HTML.
    /// </summary>
    /// <param name="slug">URL-safe slot slug, e.g. <c>claude-usage</c>.</param>
    /// <param name="jsonBody">Raw JSON content the slot should hold.</param>
    /// <param name="label">
    /// Display label. Required by the server when the slot is being
    /// created (the bridge always passes it on the wizard's create
    /// call); ignored by the server on update.
    /// </param>
    /// <param name="groupId">Optional group scope.</param>
    public async Task<DataSlotItem> PutDataSlotAsync(
        string slug,
        string jsonBody,
        string? label   = null,
        string? groupId = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("slug is required.", nameof(slug));
        if (string.IsNullOrWhiteSpace(jsonBody))
            throw new ArgumentException("jsonBody is required.", nameof(jsonBody));

        var url = $"{_baseUrl}/api/v1/data/{Uri.EscapeDataString(slug)}";
        var query = new List<string>();
        if (!string.IsNullOrEmpty(groupId)) query.Add("groupId=" + Uri.EscapeDataString(groupId));
        if (!string.IsNullOrEmpty(label))   query.Add("label=" + Uri.EscapeDataString(label));
        if (query.Count > 0) url += "?" + string.Join("&", query);

        var res = await FetchAsync(url, "PUT", jsonBody).ConfigureAwait(true);

        ThrowIfNotOk(res, $"/api/v1/data/{slug} (PUT)");

        var env = JsonSerializer.Deserialize<PutDataSlotResponse>(res.Body, Json);
        return env?.Slot
               ?? throw new InvalidOperationException("Slot PUT response did not include a slot object.");
    }

    /// <summary>
    /// Binds a physical device (the one currently showing a 6-char
    /// pairing code) to an existing display profile via
    /// <c>POST /api/v1/agent/displays/pair-by-code</c>. Used after
    /// the user has already <see cref="SendDisplayHtmlAsync"/>-ed the
    /// HTML to a display profile and now wants to point real hardware
    /// at that profile by entering the on-screen code.
    /// </summary>
    /// <param name="code">The 6-character code shown on the display device.</param>
    /// <param name="targetDisplayId">
    /// Optional: the existing display profile to rebind the hardware
    /// to. When set, the hardware is attached to this profile instead
    /// of creating a new one — exactly what we want so the bridge
    /// keeps writing to the same slot.
    /// </param>
    public async Task PairByCodeAsync(string code, string? targetDisplayId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Pairing code is required.", nameof(code));

        var body = JsonSerializer.Serialize(new
        {
            code           = code.Trim().ToUpperInvariant(),
            targetDisplayId,
        });

        var res = await FetchAsync(_baseUrl + "/api/v1/agent/displays/pair-by-code", "POST", body)
            .ConfigureAwait(true);

        ThrowIfNotOk(res, "/api/v1/agent/displays/pair-by-code");
    }

    /// <summary>
    /// Sends raw HTML to a display via
    /// <c>POST /api/v1/agent/displays/{id}/content</c>. The display
    /// fetches and renders this HTML; any <c>fetch()</c> calls inside
    /// (e.g. polling a slot read URL) run with no auth, so the
    /// referenced URLs must be public.
    /// </summary>
    public async Task SendDisplayHtmlAsync(string displayId, string html, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(displayId))
            throw new ArgumentException("displayId is required.", nameof(displayId));
        if (string.IsNullOrWhiteSpace(html))
            throw new ArgumentException("html is required.", nameof(html));

        // The server's SendContentRequest accepts both `html` and
        // `base64Html`. We send `html` directly so the request body
        // stays readable in logs and proxies; the WebView's fetch()
        // JSON-serialises it for us, including escaping newlines.
        //
        // We intentionally OMIT the `duration` field. Server-side it
        // is an int? with [Range(1, 86400)] - a missing field reads
        // as null and skips the range validator, which the action
        // then interprets as "stay until next publish". Sending 0 (or
        // anything outside [1, 86400]) trips the validator with a
        // 400 response before our action even runs.
        var body = JsonSerializer.Serialize(new
        {
            html,
            description = string.IsNullOrEmpty(description) ? "Claude plan-usage display (agentView Token Counter bridge)" : description,
        });

        var url = $"{_baseUrl}/api/v1/agent/displays/{Uri.EscapeDataString(displayId)}/content";
        var res = await FetchAsync(url, "POST", body).ConfigureAwait(true);

        ThrowIfNotOk(res, $"/api/v1/agent/displays/{displayId}/content");
    }

    /// <summary>
    /// Mints a scoped <c>avk_...</c> API key for the Token Counter via
    /// <c>POST /api/v1/agent/api-keys</c>. The returned key is restricted
    /// to <c>slot.write</c> on a single slot slug.
    /// </summary>
    /// <param name="name">Human-readable key name shown in the dashboard.</param>
    /// <param name="slotSlug">The data-slot slug the key may write into.</param>
    public async Task<CreateApiKeyResponse> CreateScopedApiKeyAsync(string name, string slotSlug)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slotSlug))
            throw new ArgumentException("slotSlug is required.", nameof(slotSlug));

        var body = JsonSerializer.Serialize(new
        {
            name             = name.Trim(),
            scope            = "content_only",
            permissions      = "write",
            allowedSlotSlugs = new[] { slotSlug.Trim().ToLowerInvariant() },
            capabilities     = ScopedKeyCapabilities,
        });
        var res = await FetchAsync(_baseUrl + "/api/v1/agent/api-keys", "POST", body)
            .ConfigureAwait(true);

        ThrowIfNotOk(res, "/api/v1/agent/api-keys (POST)");

        var resp = JsonSerializer.Deserialize<CreateApiKeyResponse>(res.Body, Json)
                   ?? throw new InvalidOperationException("Empty api-key response.");
        if (string.IsNullOrEmpty(resp.Key))
            throw new InvalidOperationException("API-key response did not contain a key.");
        return resp;
    }

    private static void ThrowIfNotOk(HttpFetchResult res, string path)
    {
        if (res.Ok) return;
        if (res.Status is 401 or 403)
        {
            throw new AgentViewAuthException(
                $"agentView returned {res.Status} on {path}. Please log in again.");
        }
        throw new InvalidOperationException(
            $"agentView {path} returned {res.Status}: {Truncate(res.Body, 240)}");
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}

/// <summary>
/// Thrown when agentView rejects our request because the session is
/// no longer authenticated.
/// </summary>
public sealed class AgentViewAuthException : Exception
{
    public AgentViewAuthException(string message) : base(message) { }
}
