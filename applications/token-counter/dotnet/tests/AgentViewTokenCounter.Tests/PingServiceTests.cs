using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;
using AgentViewTokenCounter.Tests.Fakes;
using Xunit;

namespace AgentViewTokenCounter.Tests;

public sealed class PingServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static AppConfig ConfigOk() => new AppConfig
    {
        ClaudeOrgId       = "org-123",
        AgentViewApiKey   = "avk_testkey",
        AgentViewSlotSlug = "claude-usage",
        AgentViewBaseUrl  = "https://agentview.de",
    };

    private static ClaudeUsageResponse SimpleUsage() => new ClaudeUsageResponse
    {
        FiveHour = new ClaudeBucket { Utilization = 42 },
    };

    private static DiagnosticsLog NullLog()
    {
        // Use a temp file to avoid touching %APPDATA%. The log is not
        // asserted on in these tests — we only need a valid instance.
        return new DiagnosticsLog();
    }

    // ── Precondition guards ───────────────────────────────────────────────

    [Fact]
    public async Task Paused_ReturnsPausedWithoutCallingClaude()
    {
        var claudeFake = new FakeClaudeApiClient();
        var avFake     = new FakeAgentViewClient();
        var svc        = new PingService(claudeFake, avFake, NullLog());
        var config     = ConfigOk();
        config.Paused  = true;

        var result = await svc.RunOnceAsync(config);

        Assert.Equal(PingStatus.Paused, result.Status);
        Assert.Empty(claudeFake.FetchCalls);
        Assert.Empty(avFake.WriteCalls);
    }

    [Fact]
    public async Task MissingOrgId_ReturnsFailure()
    {
        var svc    = new PingService(new FakeClaudeApiClient(), new FakeAgentViewClient(), NullLog());
        var config = ConfigOk();
        config.ClaudeOrgId = null;

        var result = await svc.RunOnceAsync(config);

        Assert.Equal(PingStatus.Failed, result.Status);
        Assert.Contains("organisation ID", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingApiKey_ReturnsFailure()
    {
        var svc    = new PingService(new FakeClaudeApiClient(), new FakeAgentViewClient(), NullLog());
        var config = ConfigOk();
        config.AgentViewApiKey = null;

        var result = await svc.RunOnceAsync(config);

        Assert.Equal(PingStatus.Failed, result.Status);
    }

    // ── Claude fetch errors ──────────────────────────────────────────────

    [Fact]
    public async Task ClaudeAuthException_ReturnsFailure()
    {
        var claudeFake = new FakeClaudeApiClient
        {
            ThrowOnFetch = new ClaudeAiAuthException("401 Unauthorized"),
        };
        var svc = new PingService(claudeFake, new FakeAgentViewClient(), NullLog());

        var result = await svc.RunOnceAsync(ConfigOk());

        Assert.Equal(PingStatus.Failed, result.Status);
        Assert.Contains("401", result.Message);
    }

    [Fact]
    public async Task ClaudeGenericException_ReturnsFailure()
    {
        var claudeFake = new FakeClaudeApiClient
        {
            ThrowOnFetch = new InvalidOperationException("network down"),
        };
        var svc = new PingService(claudeFake, new FakeAgentViewClient(), NullLog());

        var result = await svc.RunOnceAsync(ConfigOk());

        Assert.Equal(PingStatus.Failed, result.Status);
        Assert.Contains("network down", result.Message);
    }

    // ── Happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_MapsUsageAndWritesAndReturnsSuccess()
    {
        var claudeFake = new FakeClaudeApiClient { UsageToReturn = SimpleUsage() };
        var avFake     = new FakeAgentViewClient();
        var config     = ConfigOk();
        var svc        = new PingService(claudeFake, avFake, NullLog());

        var result = await svc.RunOnceAsync(config);

        Assert.Equal(PingStatus.Ok, result.Status);
        Assert.Single(avFake.WriteCalls);
        var call = avFake.WriteCalls[0];
        Assert.Equal("https://agentview.de", call.BaseUrl);
        Assert.Equal("claude-usage",          call.Slug);
        Assert.Equal("avk_testkey",           call.ApiKey);
        Assert.Null(call.Label);  // happy path: label=null to preserve portal edits
        Assert.NotNull(result.Slot);
    }

    // ── MissingSlotLabel retry ───────────────────────────────────────────

    [Fact]
    public async Task FirstWriteThrowsMissingSlotLabel_RetriesWithDefaultLabel()
    {
        var claudeFake = new FakeClaudeApiClient { UsageToReturn = SimpleUsage() };
        var avFake     = new FakeAgentViewClient
        {
            ThrowOnWrite = new MissingSlotLabelException("missing_label"),
        };
        var svc = new PingService(claudeFake, avFake, NullLog());

        var result = await svc.RunOnceAsync(ConfigOk());

        // The first write threw (throw-once); only the successful retry
        // is recorded. The retry must carry the default label.
        Assert.Single(avFake.WriteCalls);
        Assert.Equal("Claude plan usage", avFake.WriteCalls[0].Label);
        Assert.Equal(PingStatus.Ok, result.Status);
    }

    [Fact]
    public async Task RetryAlsoFails_ReturnsFailure()
    {
        var claudeFake = new FakeClaudeApiClient { UsageToReturn = SimpleUsage() };

        // Make every write throw MissingSlotLabel.
        var avFake = new AlwaysThrowingAgentViewClient(
            new MissingSlotLabelException("missing_label"));
        var svc = new PingService(claudeFake, avFake, NullLog());

        var result = await svc.RunOnceAsync(ConfigOk());

        Assert.Equal(PingStatus.Failed, result.Status);
    }

    /// <summary>Helper that always throws the same exception.</summary>
    private sealed class AlwaysThrowingAgentViewClient : AgentView.TokenCounter.Services.IAgentViewClient
    {
        private readonly Exception _ex;
        public AlwaysThrowingAgentViewClient(Exception ex) => _ex = ex;
        public Task WriteSlotAsync(string baseUrl, string slug, string apiKey,
            AgentView.TokenCounter.Models.SlotContent content,
            string? groupId = null, string? label = null, CancellationToken ct = default)
            => throw _ex;
    }
}
