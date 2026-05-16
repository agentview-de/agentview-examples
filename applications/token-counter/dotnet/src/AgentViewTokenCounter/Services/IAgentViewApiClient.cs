using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Subset of the agentView provisioning API used by
/// <see cref="SetupCoordinator"/>. Extracted as an interface so the
/// coordinator can be tested with hand-written fakes — no WebView or live
/// HTTP required.
/// </summary>
/// <remarks>
/// This interface covers the <em>one-time wizard flow</em> only. The
/// long-running ping loop uses the separate, simpler
/// <see cref="IAgentViewClient"/> interface. See the README's
/// "Two agentView clients" section for the rationale behind the split.
/// </remarks>
public interface IAgentViewApiClient
{
    /// <summary>The base URL the client is configured against.</summary>
    string BaseUrl { get; }

    /// <summary>True iff currently configured for API-key auth.</summary>
    bool IsApiKeyMode { get; }

    /// <summary>Switches all subsequent requests to API-key header auth.</summary>
    void UseApiKey(string apiKey);

    /// <summary>Switches back to cookie-session auth via the WebView.</summary>
    void UseCookieSession();

    /// <summary>Probes <c>/auth/me</c>; returns true when authenticated.</summary>
    Task<bool> IsLoggedInAsync();

    /// <summary>Fetches <c>GET /auth/me</c>.</summary>
    Task<AgentViewMeResponse?> GetMeAsync();

    /// <summary>Fetches <c>GET /api/v1/agent/displays</c>.</summary>
    Task<List<AgentViewDisplay>> ListDisplaysAsync();

    /// <summary>Creates a personal display via <c>POST /api/v1/agent/displays</c>.</summary>
    Task<CreateDisplayResponse> CreatePersonalDisplayAsync(string name);

    /// <summary>Creates or updates a data slot via <c>PUT /api/v1/data/{slug}</c>.</summary>
    Task<DataSlotItem> PutDataSlotAsync(
        string slug,
        string jsonBody,
        string? label   = null,
        string? groupId = null);

    /// <summary>Sends HTML to a display via <c>POST /api/v1/agent/displays/{id}/content</c>.</summary>
    Task SendDisplayHtmlAsync(string displayId, string html, string? description = null);

    /// <summary>Mints a scoped <c>avk_…</c> key via <c>POST /api/v1/agent/api-keys</c>.</summary>
    Task<CreateApiKeyResponse> CreateScopedApiKeyAsync(string name, string slotSlug);

    /// <summary>Binds a physical device to a display via pair-by-code.</summary>
    Task PairByCodeAsync(string code, string? targetDisplayId = null);

    /// <summary>
    /// Signs out of agentView: clears the WebView cookies for the
    /// agentView origin and drops any in-memory API-key transport
    /// (falls back to cookie mode). Best-effort.
    /// </summary>
    Task SignOutAsync();
}
