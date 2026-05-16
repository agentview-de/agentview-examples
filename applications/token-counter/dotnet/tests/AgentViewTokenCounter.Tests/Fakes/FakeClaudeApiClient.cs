using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;

namespace AgentViewTokenCounter.Tests.Fakes;

/// <summary>
/// Hand-written fake for <see cref="IClaudeApiClient"/>.
/// Configure <see cref="UsageToReturn"/> before calling
/// <see cref="FetchUsageAsync"/>; set <see cref="ThrowOnFetch"/> to
/// inject failure scenarios.
/// </summary>
internal sealed class FakeClaudeApiClient : IClaudeApiClient
{
    public bool IsLoggedIn { get; set; } = true;

    /// <summary>
    /// When set, <see cref="FetchUsageAsync"/> throws this exception.
    /// </summary>
    public Exception? ThrowOnFetch { get; set; }

    /// <summary>Response returned by <see cref="FetchUsageAsync"/>.</summary>
    public ClaudeUsageResponse UsageToReturn { get; set; } = new ClaudeUsageResponse();

    /// <summary>Arguments captured by <see cref="FetchUsageAsync"/> calls.</summary>
    public List<string> FetchCalls { get; } = new List<string>();

    public Task<bool> IsLoggedInAsync() =>
        Task.FromResult(IsLoggedIn);

    public Task<List<ClaudeOrganization>> ListOrganizationsAsync() =>
        Task.FromResult(new List<ClaudeOrganization>());

    public Task<ClaudeUsageResponse> FetchUsageAsync(string orgId, CancellationToken ct = default)
    {
        FetchCalls.Add(orgId);
        if (ThrowOnFetch is not null)
        {
            throw ThrowOnFetch;
        }
        return Task.FromResult(UsageToReturn);
    }

    /// <summary>Number of times <see cref="SignOutAsync"/> was called.</summary>
    public int SignOutCalls { get; private set; }

    public Task SignOutAsync()
    {
        SignOutCalls++;
        IsLoggedIn = false;
        return Task.CompletedTask;
    }
}
