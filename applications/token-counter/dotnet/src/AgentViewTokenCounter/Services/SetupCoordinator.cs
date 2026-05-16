using System.Text.Json;
using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

// ─── Result types ───────────────────────────────────────────────────────────

/// <summary>
/// Outcome of <see cref="SetupCoordinator.RefreshClaudeStateAsync"/>.
/// </summary>
public sealed record ClaudeStateResult(
    bool LoggedIn,
    IReadOnlyList<ClaudeOrganization> Organizations,
    string? ErrorMessage);

/// <summary>
/// Outcome of <see cref="SetupCoordinator.RefreshAgentStateAsync"/> and
/// <see cref="SetupCoordinator.ApplyFullSetupApiKeyAsync"/>.
/// </summary>
public sealed record AgentStateResult(
    bool LoggedIn,
    IReadOnlyList<AgentViewDisplay> Displays,
    string? UserEmail,
    string? ErrorMessage);

/// <summary>
/// Outcome of <see cref="SetupCoordinator.PublishAsync"/>.
/// </summary>
public sealed record PublishResult(
    bool Success,
    string? DisplayId,
    string? DisplayName,
    string? SlotSlug,
    string? ApiKey,
    DateTimeOffset? PublishedAt,
    string? ErrorMessage,
    bool IsAuthError);

/// <summary>
/// Outcome of <see cref="SetupCoordinator.PairByCodeAsync"/>.
/// </summary>
public sealed record PairResult(
    bool Success,
    string? ErrorMessage,
    bool IsAuthError);

// ─── Coordinator ────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates the Setup wizard's business logic: Claude org detection,
/// agentView session/API-key validation, display listing, the full
/// publish sequence (slot seed → HTML send → key mint), and pair-by-code.
/// </summary>
/// <remarks>
/// <para>
/// This class contains <strong>no WPF types</strong>. It returns plain
/// result records that the code-behind (<c>SetupTab.xaml.cs</c>) maps
/// to visual state. The separation makes every flow testable with
/// hand-written fakes — no WPF application loop required.
/// </para>
/// </remarks>
public sealed class SetupCoordinator
{
    private const string DefaultSlotSlug = "claude-usage";
    private const string DefaultSlotLabel = "Claude plan usage";
    private const string DefaultDisplayName = "Token Counter";

    private readonly ConfigStore          _store;
    private readonly AppConfig            _config;
    private readonly IClaudeApiClient     _claude;
    private readonly IAgentViewApiClient  _agentView;
    private readonly DiagnosticsLog       _log;

    /// <summary>
    /// True when the user authenticated via a broad API key (Full-Setup
    /// mode). Publish then reuses that key instead of minting a new
    /// slot-scoped one — the user already controls the key's scope.
    /// </summary>
    public bool ApiKeyFullSetupActive { get; private set; }

    public SetupCoordinator(
        ConfigStore store,
        AppConfig config,
        IClaudeApiClient claude,
        IAgentViewApiClient agentView,
        DiagnosticsLog log)
    {
        _store     = store;
        _config    = config;
        _claude    = claude;
        _agentView = agentView;
        _log       = log;
    }

    // ─── Claude state ────────────────────────────────────────────────

    /// <summary>
    /// Probes claude.ai for the current login state and, when signed in,
    /// lists the available organisations.
    /// </summary>
    public async Task<ClaudeStateResult> RefreshClaudeStateAsync()
    {
        bool loggedIn;
        try
        {
            loggedIn = await _claude.IsLoggedInAsync().ConfigureAwait(true);
        }
        catch
        {
            loggedIn = false;
        }

        if (!loggedIn)
        {
            return new ClaudeStateResult(
                LoggedIn: false,
                Organizations: Array.Empty<ClaudeOrganization>(),
                ErrorMessage: null);
        }

        try
        {
            var orgs = await _claude.ListOrganizationsAsync().ConfigureAwait(true);
            return new ClaudeStateResult(
                LoggedIn: true,
                Organizations: orgs,
                ErrorMessage: orgs.Count == 0
                    ? "Signed in, but no organisations on this account."
                    : null);
        }
        catch (Exception ex)
        {
            return new ClaudeStateResult(
                LoggedIn: true,
                Organizations: Array.Empty<ClaudeOrganization>(),
                ErrorMessage: "Detection failed: " + ex.Message);
        }
    }

    // ─── agentView state (cookie path) ──────────────────────────────

    /// <summary>
    /// Probes agentView for the current cookie-session login state.
    /// When signed in, fetches the user email and lists displays.
    /// </summary>
    public async Task<AgentStateResult> RefreshAgentStateAsync()
    {
        bool loggedIn;
        try
        {
            loggedIn = await _agentView.IsLoggedInAsync().ConfigureAwait(true);
        }
        catch
        {
            loggedIn = false;
        }

        if (!loggedIn)
        {
            return new AgentStateResult(
                LoggedIn: false,
                Displays: Array.Empty<AgentViewDisplay>(),
                UserEmail: null,
                ErrorMessage: null);
        }

        var email = await TryFetchEmailAsync().ConfigureAwait(true);
        if (email is not null && email != _config.AgentViewUserEmail)
        {
            _config.AgentViewUserEmail = email;
            _store.Save(_config);
        }

        try
        {
            var displays = await _agentView.ListDisplaysAsync().ConfigureAwait(true);
            return new AgentStateResult(
                LoggedIn: true,
                Displays: displays,
                UserEmail: email,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new AgentStateResult(
                LoggedIn: true,
                Displays: Array.Empty<AgentViewDisplay>(),
                UserEmail: email,
                ErrorMessage: "Could not load displays: " + ex.Message);
        }
    }

    // ─── Sign-out ────────────────────────────────────────────────────

    /// <summary>
    /// Signs out of claude.ai (clears its WebView cookies) and forgets
    /// the cached organisation. Returns the refreshed — now
    /// signed-out — Claude state so the caller just renders it.
    /// </summary>
    public async Task<ClaudeStateResult> SignOutClaudeAsync()
    {
        await _claude.SignOutAsync().ConfigureAwait(true);
        _config.ClaudeOrgId = null;
        _store.Save(_config);
        return await RefreshClaudeStateAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Signs out of agentView: clears its WebView cookies, drops the
    /// in-memory API-key transport, and forgets every persisted
    /// agentView credential (key, display, slot, group, email) so the
    /// wizard returns to a clean "not connected" state. The Claude
    /// side and the WebView2 cookie jar for other origins are left
    /// untouched. Returns the refreshed — now signed-out — state.
    /// </summary>
    public async Task<AgentStateResult> SignOutAgentViewAsync()
    {
        await _agentView.SignOutAsync().ConfigureAwait(true);

        _config.AgentViewApiKey      = null;
        _config.AgentViewDisplayId   = null;
        _config.AgentViewDisplayName = null;
        _config.AgentViewSlotSlug    = DefaultSlotSlug;
        _config.AgentViewGroupId     = null;
        _config.AgentViewUserEmail   = null;
        _config.SetupComplete        = false;
        ApiKeyFullSetupActive        = false;
        _store.Save(_config);

        return await RefreshAgentStateAsync().ConfigureAwait(true);
    }

    // ─── agentView state (API-key Full-Setup path) ───────────────────

    /// <summary>
    /// Switches the agentView client to API-key auth, probes liveness,
    /// fetches the user email, and lists displays — all via the key.
    /// Returns success only when the key has sufficient scope to list
    /// displays. Rolls back to cookie mode on any failure.
    /// </summary>
    /// <param name="apiKey">The <c>avk_…</c> key to use.</param>
    /// <param name="slotSlug">Slug to write into (persisted immediately).</param>
    /// <param name="groupId">Optional group scope.</param>
    public async Task<AgentStateResult> ApplyFullSetupApiKeyAsync(
        string apiKey, string slotSlug, string? groupId)
    {
        _config.AgentViewApiKey   = apiKey;
        _config.AgentViewSlotSlug = slotSlug;
        _config.AgentViewGroupId  = groupId;
        _store.Save(_config);

        _agentView.UseApiKey(apiKey);

        // 1) Liveness probe.
        try
        {
            var loggedIn = await _agentView.IsLoggedInAsync().ConfigureAwait(true);
            if (!loggedIn)
            {
                return FailFullSetup(
                    "agentView rejected that key. Check you pasted it whole and it isn't revoked or expired.");
            }
        }
        catch (Exception ex)
        {
            return FailFullSetup("Could not reach agentView with that key: " + ex.Message);
        }

        // 2) Email (best-effort).
        var email = await TryFetchEmailAsync().ConfigureAwait(true);
        if (email is not null && email != _config.AgentViewUserEmail)
        {
            _config.AgentViewUserEmail = email;
            _store.Save(_config);
        }

        // 3) Capability probe — list displays needs display.read.
        try
        {
            var displays = await _agentView.ListDisplaysAsync().ConfigureAwait(true);
            ApiKeyFullSetupActive = true;
            return new AgentStateResult(
                LoggedIn: true,
                Displays: displays,
                UserEmail: email,
                ErrorMessage: null);
        }
        catch (AgentViewAuthException)
        {
            return FailFullSetup(
                "The key is valid but cannot list displays. Grant it display.read + display.manage + display.send " +
                "capabilities (plus slot.write), or pick \"Slot write only\" and set the display up in the dashboard yourself.");
        }
        catch (Exception ex)
        {
            return FailFullSetup("Listing displays via the API key failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Applies a Slot-only key: persists credentials, marks setup
    /// complete, and returns immediately without any API call. The
    /// caller fires <c>InstallCompleted</c>.
    /// </summary>
    public void ApplySlotOnlyApiKey(string apiKey, string slotSlug, string? groupId)
    {
        _config.AgentViewApiKey   = apiKey;
        _config.AgentViewSlotSlug = slotSlug;
        _config.AgentViewGroupId  = groupId;
        _config.SetupComplete     = true;
        _store.Save(_config);
    }

    // ─── Publish ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full publish sequence:
    /// <list type="number">
    ///   <item>Create or select a display.</item>
    ///   <item>Fetch live Claude usage (falls back to embedded defaults).</item>
    ///   <item>PUT the data slot.</item>
    ///   <item>Push the HTML to the display.</item>
    ///   <item>Mint a scoped API key (unless the user supplied a broad key).</item>
    ///   <item>Persist config and mark setup complete.</item>
    /// </list>
    /// </summary>
    /// <param name="org">Selected Claude organisation.</param>
    /// <param name="selectedDisplay">
    /// The picked display, or <c>null</c> to create a new one.
    /// </param>
    /// <param name="progress">
    /// Optional callback for user-facing progress strings, e.g.
    /// "Creating display…", "Provisioning slot…".
    /// </param>
    public async Task<PublishResult> PublishAsync(
        ClaudeOrganization org,
        AgentViewDisplay? selectedDisplay,
        Action<string>? progress = null)
    {
        try
        {
            // Step 1 — resolve display.
            string displayId, displayName;
            if (selectedDisplay is null)
            {
                progress?.Invoke("Creating display…");
                var created = await _agentView.CreatePersonalDisplayAsync(DefaultDisplayName).ConfigureAwait(true);
                displayId   = created.Id;
                displayName = created.Name;
            }
            else
            {
                displayId   = selectedDisplay.Id;
                displayName = selectedDisplay.Name;
            }

            // Step 2 — seed slot JSON (live Claude data or embedded default).
            progress?.Invoke("Fetching live Claude usage…");
            var slotJson = DisplayHtmlBuilder.LoadDefaultSlotJson();
            try
            {
                var rawUsage    = await _claude.FetchUsageAsync(org.Uuid).ConfigureAwait(true);
                var slotContent = BucketMapper.Map(rawUsage, _config.IncludeModelSplits);
                slotJson = JsonSerializer.Serialize(slotContent);
            }
            catch (Exception fetchEx)
            {
                _log.Warn(
                    "Could not pre-populate slot with live Claude data, falling back to defaults: " +
                    fetchEx.Message);
            }

            // Step 3 — PUT the data slot.
            var targetSlug = string.IsNullOrWhiteSpace(_config.AgentViewSlotSlug)
                ? DefaultSlotSlug
                : _config.AgentViewSlotSlug.Trim();

            progress?.Invoke("Provisioning data slot…");
            var slot = await _agentView.PutDataSlotAsync(
                slug: targetSlug, jsonBody: slotJson, label: DefaultSlotLabel).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(slot.ReadUrl))
            {
                throw new InvalidOperationException(
                    "Slot create succeeded but no public read URL was returned.");
            }

            // Step 4 — push HTML.
            progress?.Invoke($"Publishing HTML to {displayName}…");
            var html = DisplayHtmlBuilder.Build(slot.ReadUrl);
            await _agentView.SendDisplayHtmlAsync(
                displayId:   displayId,
                html:        html,
                description: $"Claude plan usage (slot {slot.Slug})").ConfigureAwait(true);

            // Step 5 — key strategy.
            // When the user already supplied a broad key via the Full-Setup
            // path, reuse it — minting a new key would be a surprising
            // side-effect and likely needs admin scope the pasted key lacks.
            string workingKey;
            if (ApiKeyFullSetupActive && !string.IsNullOrEmpty(_config.AgentViewApiKey))
            {
                workingKey = _config.AgentViewApiKey!;
            }
            else
            {
                progress?.Invoke("Minting API key…");
                var keyResp = await _agentView.CreateScopedApiKeyAsync(
                    name:     $"Token Counter ({displayName})",
                    slotSlug: slot.Slug).ConfigureAwait(true);
                workingKey = keyResp.Key;
            }

            // Step 6 — persist.
            _config.ClaudeOrgId          = org.Uuid;
            _config.AgentViewDisplayId   = displayId;
            _config.AgentViewDisplayName = displayName;
            _config.AgentViewSlotSlug    = slot.Slug;
            _config.AgentViewGroupId     = slot.GroupId;
            _config.AgentViewApiKey      = workingKey;
            _config.AgentViewBaseUrl     = _agentView.BaseUrl;
            _config.SetupComplete        = true;
            _store.Save(_config);

            return new PublishResult(
                Success:     true,
                DisplayId:   displayId,
                DisplayName: displayName,
                SlotSlug:    slot.Slug,
                ApiKey:      workingKey,
                PublishedAt: DateTimeOffset.Now,
                ErrorMessage: null,
                IsAuthError: false);
        }
        catch (AgentViewAuthException ex)
        {
            return new PublishResult(
                Success: false, DisplayId: null, DisplayName: null,
                SlotSlug: null, ApiKey: null, PublishedAt: null,
                ErrorMessage: ex.Message,
                IsAuthError: true);
        }
        catch (Exception ex)
        {
            _log.Error("Setup publish failed", ex);
            return new PublishResult(
                Success: false, DisplayId: null, DisplayName: null,
                SlotSlug: null, ApiKey: null, PublishedAt: null,
                ErrorMessage: "Setup failed: " + ex.Message,
                IsAuthError: false);
        }
    }

    // ─── Pair ─────────────────────────────────────────────────────────

    /// <summary>
    /// Binds a physical device (showing a 6-char pairing code) to the
    /// configured display profile.
    /// </summary>
    /// <param name="code">The 6-character code shown on the device.</param>
    public async Task<PairResult> PairByCodeAsync(string code)
    {
        if (string.IsNullOrEmpty(_config.AgentViewDisplayId))
        {
            return new PairResult(
                Success: false,
                ErrorMessage: "Publish a display first (step 3 above) — pairing binds the screen to that profile.",
                IsAuthError: false);
        }

        try
        {
            await _agentView.PairByCodeAsync(code, _config.AgentViewDisplayId).ConfigureAwait(true);
            return new PairResult(Success: true, ErrorMessage: null, IsAuthError: false);
        }
        catch (AgentViewAuthException)
        {
            return new PairResult(
                Success: false,
                ErrorMessage: "Sign in to agentView first.",
                IsAuthError: true);
        }
        catch (Exception ex)
        {
            _log.Error("Pair-by-code failed", ex);
            return new PairResult(Success: false, ErrorMessage: ex.Message, IsAuthError: false);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private AgentStateResult FailFullSetup(string reason)
    {
        _agentView.UseCookieSession();
        ApiKeyFullSetupActive = false;
        return new AgentStateResult(
            LoggedIn: false,
            Displays: Array.Empty<AgentViewDisplay>(),
            UserEmail: null,
            ErrorMessage: reason);
    }

    private async Task<string?> TryFetchEmailAsync()
    {
        try
        {
            var me = await _agentView.GetMeAsync().ConfigureAwait(true);
            return me?.User?.Email;
        }
        catch (Exception ex)
        {
            _log.Warn("Could not fetch agentView user email: " + ex.Message);
            return null;
        }
    }
}
