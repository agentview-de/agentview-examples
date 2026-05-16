using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;

namespace AgentViewTokenCounter.Tests.Fakes;

/// <summary>
/// Hand-written fake for <see cref="IAgentViewApiClient"/>.
/// Configure the properties below before exercising
/// <see cref="SetupCoordinator"/> in tests.
/// </summary>
internal sealed class FakeAgentViewApiClient : IAgentViewApiClient
{
    // ── Configuration ────────────────────────────────────────────────────

    public string BaseUrl { get; set; } = "https://agentview.de";

    public bool IsLoggedIn { get; set; } = true;

    /// <summary>When set, the next <see cref="IsLoggedInAsync"/> returns false.</summary>
    public bool RejectLogin { get; set; }

    /// <summary>Throws this exception from <see cref="ListDisplaysAsync"/> when set.</summary>
    public Exception? ThrowOnListDisplays { get; set; }

    /// <summary>Throws this exception from every mutating call when set.</summary>
    public Exception? ThrowOnMutate { get; set; }

    public List<AgentViewDisplay> Displays { get; set; } =
        new List<AgentViewDisplay>
        {
            new AgentViewDisplay { Id = "disp-default", Name = "My Display" },
        };

    public AgentViewMeResponse Me { get; set; } = new AgentViewMeResponse
    {
        User = new AgentViewUser { UserId = "user-1", Email = "user@example.com" },
    };

    /// <summary>
    /// The <see cref="DataSlotItem"/> returned by <see cref="PutDataSlotAsync"/>.
    /// </summary>
    public DataSlotItem SlotToReturn { get; set; } = new DataSlotItem
    {
        SlotId  = "slot-1",
        Slug    = "claude-usage",
        ReadUrl = "https://content.agentview.de/data/u/user-1/claude-usage.json",
    };

    /// <summary>Key returned by <see cref="CreateScopedApiKeyAsync"/>.</summary>
    public string MintedApiKey { get; set; } = "avk_minted_key";

    // ── State observation ─────────────────────────────────────────────────

    public bool IsApiKeyMode { get; private set; }
    public string? AppliedApiKey { get; private set; }

    public List<string> SendHtmlCalls   { get; } = new List<string>();
    public List<string> PutSlugCalls    { get; } = new List<string>();
    public List<string> CreateKeyCalls  { get; } = new List<string>();
    public List<string> PairCodeCalls   { get; } = new List<string>();
    public List<string> CreateDisplayCalls { get; } = new List<string>();

    // ── IAgentViewApiClient ───────────────────────────────────────────────

    public void UseApiKey(string apiKey)
    {
        IsApiKeyMode  = true;
        AppliedApiKey = apiKey;
    }

    public void UseCookieSession()
    {
        IsApiKeyMode  = false;
        AppliedApiKey = null;
    }

    public Task<bool> IsLoggedInAsync() =>
        Task.FromResult(!RejectLogin && IsLoggedIn);

    public Task<AgentViewMeResponse?> GetMeAsync() =>
        Task.FromResult<AgentViewMeResponse?>(Me);

    public Task<List<AgentViewDisplay>> ListDisplaysAsync()
    {
        if (ThrowOnListDisplays is not null) throw ThrowOnListDisplays;
        return Task.FromResult(Displays);
    }

    public Task<CreateDisplayResponse> CreatePersonalDisplayAsync(string name)
    {
        if (ThrowOnMutate is not null) throw ThrowOnMutate;
        CreateDisplayCalls.Add(name);
        return Task.FromResult(new CreateDisplayResponse
        {
            Id   = "disp-created",
            Name = name,
        });
    }

    public Task<DataSlotItem> PutDataSlotAsync(
        string slug, string jsonBody, string? label = null, string? groupId = null)
    {
        if (ThrowOnMutate is not null) throw ThrowOnMutate;
        PutSlugCalls.Add(slug);
        return Task.FromResult(SlotToReturn);
    }

    public Task SendDisplayHtmlAsync(string displayId, string html, string? description = null)
    {
        if (ThrowOnMutate is not null) throw ThrowOnMutate;
        SendHtmlCalls.Add(displayId);
        return Task.CompletedTask;
    }

    public Task<CreateApiKeyResponse> CreateScopedApiKeyAsync(string name, string slotSlug)
    {
        if (ThrowOnMutate is not null) throw ThrowOnMutate;
        CreateKeyCalls.Add(name);
        return Task.FromResult(new CreateApiKeyResponse
        {
            KeyId = "kid-1",
            Key   = MintedApiKey,
            Name  = name,
        });
    }

    public Task PairByCodeAsync(string code, string? targetDisplayId = null)
    {
        if (ThrowOnMutate is not null) throw ThrowOnMutate;
        PairCodeCalls.Add(code);
        return Task.CompletedTask;
    }

    /// <summary>Number of times <see cref="SignOutAsync"/> was called.</summary>
    public int SignOutCalls { get; private set; }

    public Task SignOutAsync()
    {
        SignOutCalls++;
        UseCookieSession();   // mirrors production: drop in-memory key
        IsLoggedIn = false;
        return Task.CompletedTask;
    }
}
