using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;

namespace AgentViewTokenCounter.Tests.Fakes;

/// <summary>
/// Hand-written fake for <see cref="IAgentViewClient"/>.
/// Records every <see cref="WriteSlotAsync"/> call so tests can assert
/// on arguments. Set <see cref="ThrowOnWrite"/> to inject failure.
/// </summary>
internal sealed class FakeAgentViewClient : IAgentViewClient
{
    public sealed record WriteCall(
        string BaseUrl, string Slug, string ApiKey,
        SlotContent Content, string? GroupId, string? Label);

    /// <summary>All WriteSlotAsync invocations in call order.</summary>
    public List<WriteCall> WriteCalls { get; } = new List<WriteCall>();

    /// <summary>
    /// When set, the next <see cref="WriteSlotAsync"/> throws this and
    /// clears itself (throw-once behaviour).
    /// </summary>
    public Exception? ThrowOnWrite { get; set; }

    public Task WriteSlotAsync(
        string baseUrl,
        string slug,
        string apiKey,
        SlotContent content,
        string? groupId   = null,
        string? label     = null,
        CancellationToken ct = default)
    {
        if (ThrowOnWrite is not null)
        {
            var ex = ThrowOnWrite;
            ThrowOnWrite = null;
            throw ex;
        }
        WriteCalls.Add(new WriteCall(baseUrl, slug, apiKey, content, groupId, label));
        return Task.CompletedTask;
    }
}
