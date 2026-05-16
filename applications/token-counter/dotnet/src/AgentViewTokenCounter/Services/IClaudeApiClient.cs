using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Seam that separates the claude.ai fetch logic from consumers
/// (<see cref="PingService"/>, <see cref="SetupCoordinator"/>).
/// The production implementation is <see cref="ClaudeApiClient"/>;
/// tests supply hand-written fakes.
/// </summary>
public interface IClaudeApiClient
{
    /// <summary>
    /// Returns true when the WebView2 session is authenticated with
    /// claude.ai (i.e. <c>GET /api/organizations</c> returns at least
    /// one entry).
    /// </summary>
    Task<bool> IsLoggedInAsync();

    /// <summary>
    /// Lists the claude.ai organisations visible to the current session.
    /// </summary>
    Task<List<ClaudeOrganization>> ListOrganizationsAsync();

    /// <summary>
    /// Fetches <c>GET /api/organizations/{orgId}/usage</c>.
    /// </summary>
    /// <param name="orgId">Organisation UUID.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<ClaudeUsageResponse> FetchUsageAsync(string orgId, CancellationToken ct = default);

    /// <summary>
    /// Signs the WebView session out of claude.ai by clearing its
    /// cookies, so the user can switch accounts. Best-effort.
    /// </summary>
    Task SignOutAsync();
}
