using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Seam that separates the agentView slot-write logic from consumers
/// (<see cref="PingService"/>). The production implementation is
/// <see cref="AgentViewClient"/>; tests supply hand-written fakes.
/// </summary>
public interface IAgentViewClient
{
    /// <summary>
    /// Writes <paramref name="content"/> to the agentView data slot
    /// identified by <paramref name="slug"/> via
    /// <c>PUT {baseUrl}/api/v1/data/{slug}</c>.
    /// </summary>
    /// <param name="baseUrl">agentView instance base URL.</param>
    /// <param name="slug">Slot slug, e.g. <c>claude-usage</c>.</param>
    /// <param name="apiKey">Scoped <c>avk_…</c> write key.</param>
    /// <param name="content">Slot payload to PUT.</param>
    /// <param name="groupId">Optional group scope.</param>
    /// <param name="label">
    /// When non-null, sent as the <c>?label=…</c> query parameter.
    /// Required on first write; pass <c>null</c> on updates to
    /// preserve user-edited labels in the portal.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    Task WriteSlotAsync(
        string baseUrl,
        string slug,
        string apiKey,
        SlotContent content,
        string? groupId  = null,
        string? label    = null,
        CancellationToken ct = default);
}
