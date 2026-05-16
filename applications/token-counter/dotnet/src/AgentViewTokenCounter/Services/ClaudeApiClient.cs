using System.Text.Json;
using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Wraps the claude.ai web endpoints we depend on (organisations,
/// plan-usage). Authenticates by routing every call through the
/// embedded <see cref="WebViewSession"/>, so the user's existing
/// claude.ai session cookie is used transparently.
/// </summary>
/// <remarks>
/// The endpoints called here are <strong>not part of any documented
/// Anthropic API.</strong> They are the same endpoints the official
/// Claude client uses internally. Anthropic may change or remove
/// them at any time.
/// </remarks>
public sealed class ClaudeApiClient : IClaudeApiClient
{
    private const string ClaudeOrigin = "https://claude.ai";

    private readonly WebViewSession _session;
    private readonly DiagnosticsLog _log;

    public ClaudeApiClient(WebViewSession session, DiagnosticsLog log)
    {
        _session = session;
        _log     = log;
    }

    /// <summary>
    /// Probes <c>/api/organizations</c>. Returns true on 200 with
    /// at least one entry; false on auth failure or empty response.
    /// </summary>
    public async Task<bool> IsLoggedInAsync()
    {
        try
        {
            var orgs = await ListOrganizationsAsync().ConfigureAwait(true);
            return orgs.Count > 0;
        }
        catch (ClaudeAiAuthException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Warn("Claude IsLoggedIn probe threw " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Fetches <c>GET /api/organizations</c>.
    /// </summary>
    public async Task<List<ClaudeOrganization>> ListOrganizationsAsync()
    {
        var res = await _session.FetchAsync(
            ClaudeOrigin + "/api/organizations",
            method: "GET",
            acceptOrigin: ClaudeOrigin).ConfigureAwait(true);

        if (!res.Ok)
        {
            if (res.Status is 401 or 403)
            {
                throw new ClaudeAiAuthException(
                    $"claude.ai returned {res.Status}. Please log in again.");
            }
            throw new InvalidOperationException(
                $"claude.ai /api/organizations returned {res.Status}: {Truncate(res.Body, 200)}");
        }

        var list = JsonSerializer.Deserialize<List<ClaudeOrganization>>(res.Body)
                   ?? new List<ClaudeOrganization>();
        return list;
    }

    /// <summary>
    /// Fetches <c>GET /api/organizations/{orgId}/usage</c>.
    /// </summary>
    public async Task<ClaudeUsageResponse> FetchUsageAsync(string orgId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orgId))
            throw new ArgumentException("orgId is required.", nameof(orgId));

        var url = $"{ClaudeOrigin}/api/organizations/{Uri.EscapeDataString(orgId)}/usage";
        var res = await _session.FetchAsync(url, method: "GET", acceptOrigin: ClaudeOrigin)
                                .ConfigureAwait(true);

        if (!res.Ok)
        {
            if (res.Status is 401 or 403)
            {
                throw new ClaudeAiAuthException(
                    $"claude.ai returned {res.Status} for usage. Please log in again.");
            }
            throw new InvalidOperationException(
                $"claude.ai usage returned {res.Status}: {Truncate(res.Body, 200)}");
        }
        return JsonSerializer.Deserialize<ClaudeUsageResponse>(res.Body)
               ?? throw new InvalidOperationException("Empty usage response.");
    }

    /// <summary>Sends the WebView to the claude.ai login page.</summary>
    public void NavigateToLogin() => _session.Navigate(ClaudeOrigin + "/login");

    /// <inheritdoc/>
    public Task SignOutAsync() => _session.ClearCookiesForOriginAsync(ClaudeOrigin);

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}

/// <summary>
/// Thrown when claude.ai rejects our request because the session is
/// no longer authenticated.
/// </summary>
public sealed class ClaudeAiAuthException : Exception
{
    public ClaudeAiAuthException(string message) : base(message) { }
}
