using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;
using AgentViewTokenCounter.Tests.Fakes;
using AgentViewTokenCounter.Tests.Helpers;
using Xunit;

namespace AgentViewTokenCounter.Tests;

/// <summary>
/// Tests for <see cref="SetupCoordinator"/> orchestration logic.
/// All external dependencies (agentView API, Claude API) are replaced
/// by hand-written fakes so tests run with no network and no WebView.
/// </summary>
public sealed class SetupCoordinatorTests
{
    // ── Build helper ─────────────────────────────────────────────────────

    private sealed record Fixture(
        SetupCoordinator    Coordinator,
        FakeClaudeApiClient  Claude,
        FakeAgentViewApiClient AgentView,
        AppConfig           Config,
        TempDir             Dir) : IDisposable
    {
        public void Dispose() => Dir.Dispose();
    }

    /// <summary>
    /// Builds a coordinator wired to fakes that all succeed by default.
    /// Override specific fakes as needed.
    /// </summary>
    private static Fixture Build(
        FakeClaudeApiClient?    claude    = null,
        FakeAgentViewApiClient? agentView = null,
        AppConfig?              config    = null)
    {
        var dir   = new TempDir();
        var store = new ConfigStore(dir.Path);
        var cfg   = config ?? new AppConfig
        {
            ClaudeOrgId       = "org-123",
            AgentViewBaseUrl  = "https://agentview.de",
            AgentViewSlotSlug = "claude-usage",
        };
        var claudeFake    = claude    ?? new FakeClaudeApiClient();
        var agentViewFake = agentView ?? new FakeAgentViewApiClient();

        var coordinator = new SetupCoordinator(
            store, cfg, claudeFake, agentViewFake, new DiagnosticsLog());

        return new Fixture(coordinator, claudeFake, agentViewFake, cfg, dir);
    }

    // ── ApplySlotOnlyApiKey ───────────────────────────────────────────────

    [Fact]
    public void ApplySlotOnlyApiKey_PersistsCredentialsAndMarksSetupComplete()
    {
        using var f = Build();

        f.Coordinator.ApplySlotOnlyApiKey("avk_slot_key", "my-slug", "grp-1");

        Assert.Equal("avk_slot_key", f.Config.AgentViewApiKey);
        Assert.Equal("my-slug",      f.Config.AgentViewSlotSlug);
        Assert.Equal("grp-1",        f.Config.AgentViewGroupId);
        Assert.True(f.Config.SetupComplete);
    }

    [Fact]
    public void ApplySlotOnlyApiKey_DoesNotSetApiKeyFullSetupActive()
    {
        using var f = Build();

        f.Coordinator.ApplySlotOnlyApiKey("avk_slot_key", "my-slug", null);

        Assert.False(f.Coordinator.ApiKeyFullSetupActive);
    }

    // ── ApplyFullSetupApiKeyAsync ─────────────────────────────────────────

    [Fact]
    public async Task ApplyFullSetupApiKeyAsync_ValidKey_SetsApiKeyModeAndReturnsLoggedIn()
    {
        var avFake = new FakeAgentViewApiClient { IsLoggedIn = true };
        using var f = Build(agentView: avFake);

        var result = await f.Coordinator.ApplyFullSetupApiKeyAsync(
            "avk_broad_key", "claude-usage", null);

        Assert.True(result.LoggedIn);
        Assert.Null(result.ErrorMessage);
        Assert.True(f.Coordinator.ApiKeyFullSetupActive);
        Assert.True(avFake.IsApiKeyMode);
        Assert.Equal("avk_broad_key", avFake.AppliedApiKey);
    }

    [Fact]
    public async Task ApplyFullSetupApiKeyAsync_RejectedLogin_ReturnsFailureAndRollsBackToCookie()
    {
        var avFake = new FakeAgentViewApiClient { RejectLogin = true };
        using var f = Build(agentView: avFake);

        var result = await f.Coordinator.ApplyFullSetupApiKeyAsync(
            "avk_bad_key", "claude-usage", null);

        Assert.False(result.LoggedIn);
        Assert.NotNull(result.ErrorMessage);
        Assert.False(f.Coordinator.ApiKeyFullSetupActive);

        // Must have rolled back to cookie session.
        Assert.False(avFake.IsApiKeyMode);
    }

    [Fact]
    public async Task ApplyFullSetupApiKeyAsync_ListDisplaysThrowsAuth_ReturnsFailureWithScopeHint()
    {
        var avFake = new FakeAgentViewApiClient
        {
            IsLoggedIn          = true,
            ThrowOnListDisplays = new AgentViewAuthException("403"),
        };
        using var f = Build(agentView: avFake);

        var result = await f.Coordinator.ApplyFullSetupApiKeyAsync(
            "avk_narrow_key", "claude-usage", null);

        Assert.False(result.LoggedIn);
        Assert.False(f.Coordinator.ApiKeyFullSetupActive);
        Assert.Contains("display.read", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── PublishAsync — happy path (cookie flow) ──────────────────────────

    [Fact]
    public async Task PublishAsync_ExistingDisplay_SkipsCreateAndReturnsSuccess()
    {
        using var f = Build();
        f.Claude.UsageToReturn = new ClaudeUsageResponse
        {
            FiveHour = new ClaudeBucket { Utilization = 80 },
        };
        var org     = new ClaudeOrganization { Uuid = "org-123", Name = "Acme" };
        var display = new AgentViewDisplay { Id = "disp-1", Name = "Office TV" };

        var result = await f.Coordinator.PublishAsync(org, display);

        Assert.True(result.Success);
        Assert.Equal("disp-1",    result.DisplayId);
        Assert.Equal("Office TV", result.DisplayName);
        Assert.NotNull(result.SlotSlug);
        Assert.NotNull(result.ApiKey);
        Assert.NotNull(result.PublishedAt);

        // No new display was created.
        Assert.Empty(f.AgentView.CreateDisplayCalls);
        // Slot was PUT exactly once.
        Assert.Single(f.AgentView.PutSlugCalls);
        // HTML was sent to the correct display.
        Assert.Single(f.AgentView.SendHtmlCalls);
        Assert.Equal("disp-1", f.AgentView.SendHtmlCalls[0]);
        // A key was minted (cookie flow has no pre-existing broad key).
        Assert.Single(f.AgentView.CreateKeyCalls);
    }

    [Fact]
    public async Task PublishAsync_NoDisplaySelected_CreatesNewDisplay()
    {
        using var f = Build();
        var org = new ClaudeOrganization { Uuid = "org-123", Name = "Acme" };

        var result = await f.Coordinator.PublishAsync(org, selectedDisplay: null);

        Assert.True(result.Success);
        Assert.Single(f.AgentView.CreateDisplayCalls);
        Assert.Equal("disp-created", result.DisplayId);
    }

    [Fact]
    public async Task PublishAsync_PersistsConfigAndMarksSetupComplete()
    {
        using var f = Build();
        var org     = new ClaudeOrganization { Uuid = "org-456", Name = "Acme" };
        var display = new AgentViewDisplay { Id = "disp-7", Name = "TV" };

        await f.Coordinator.PublishAsync(org, display);

        Assert.True(f.Config.SetupComplete);
        Assert.Equal("org-456", f.Config.ClaudeOrgId);
        Assert.Equal("disp-7",  f.Config.AgentViewDisplayId);
        Assert.Equal("TV",      f.Config.AgentViewDisplayName);
        Assert.NotEmpty(f.Config.AgentViewApiKey!);
    }

    // ── PublishAsync — Full-Setup key path (skip mint) ───────────────────

    [Fact]
    public async Task PublishAsync_WhenFullSetupActive_ReusesExistingKeyAndSkipsMint()
    {
        // Pre-configure the config with a broad key.
        var cfg = new AppConfig
        {
            ClaudeOrgId       = "org-123",
            AgentViewApiKey   = "avk_broad_key",
            AgentViewSlotSlug = "claude-usage",
        };
        var avFake = new FakeAgentViewApiClient { IsLoggedIn = true };
        using var f = Build(config: cfg, agentView: avFake);

        // Activate the full-setup flag — mirrors what the UI does when
        // the user goes through the API-key Full-Setup path.
        var fullSetupResult = await f.Coordinator.ApplyFullSetupApiKeyAsync(
            "avk_broad_key", "claude-usage", null);
        Assert.True(fullSetupResult.LoggedIn);
        Assert.True(f.Coordinator.ApiKeyFullSetupActive);

        var org     = new ClaudeOrganization { Uuid = "org-123", Name = "Acme" };
        var display = new AgentViewDisplay { Id = "disp-1", Name = "TV" };
        avFake.CreateKeyCalls.Clear(); // reset — we only care about what Publish does

        var result = await f.Coordinator.PublishAsync(org, display);

        Assert.True(result.Success);
        // The broad key is reused — no new key was minted.
        Assert.Equal("avk_broad_key", result.ApiKey);
        Assert.Empty(avFake.CreateKeyCalls);
    }

    // ── PublishAsync — slot read URL guard ───────────────────────────────

    [Fact]
    public async Task PublishAsync_SlotHasNoReadUrl_ReturnsFailure()
    {
        var avFake = new FakeAgentViewApiClient
        {
            SlotToReturn = new DataSlotItem
            {
                SlotId  = "slot-1",
                Slug    = "claude-usage",
                ReadUrl = null,   // server omitted the URL
            },
        };
        using var f = Build(agentView: avFake);
        var org     = new ClaudeOrganization { Uuid = "org-123" };
        var display = new AgentViewDisplay { Id = "disp-1", Name = "TV" };

        var result = await f.Coordinator.PublishAsync(org, display);

        Assert.False(result.Success);
        Assert.False(result.IsAuthError);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── PublishAsync — auth error mapping ────────────────────────────────

    [Fact]
    public async Task PublishAsync_AgentViewAuthException_SetsIsAuthErrorTrue()
    {
        var avFake = new FakeAgentViewApiClient
        {
            ThrowOnMutate = new AgentViewAuthException("session expired"),
        };
        using var f = Build(agentView: avFake);
        var org     = new ClaudeOrganization { Uuid = "org-123" };
        var display = new AgentViewDisplay { Id = "disp-1", Name = "TV" };

        var result = await f.Coordinator.PublishAsync(org, display);

        Assert.False(result.Success);
        Assert.True(result.IsAuthError);
    }

    // ── PairByCodeAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task PairByCodeAsync_NoDisplayId_ReturnsFailureWithHint()
    {
        var cfg = new AppConfig { AgentViewDisplayId = null };
        using var f = Build(config: cfg);

        var result = await f.Coordinator.PairByCodeAsync("ABC123");

        Assert.False(result.Success);
        Assert.Contains("step 3", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PairByCodeAsync_HappyPath_ReturnsTrueAndForwardsCode()
    {
        var cfg    = new AppConfig { AgentViewDisplayId = "disp-9" };
        var avFake = new FakeAgentViewApiClient();
        using var f = Build(config: cfg, agentView: avFake);

        var result = await f.Coordinator.PairByCodeAsync("XYZABC");

        Assert.True(result.Success);
        Assert.False(result.IsAuthError);
        Assert.Single(avFake.PairCodeCalls);
        Assert.Equal("XYZABC", avFake.PairCodeCalls[0]);
    }

    [Fact]
    public async Task PairByCodeAsync_AuthException_SetsIsAuthErrorTrue()
    {
        var cfg    = new AppConfig { AgentViewDisplayId = "disp-9" };
        var avFake = new FakeAgentViewApiClient
        {
            ThrowOnMutate = new AgentViewAuthException("401"),
        };
        using var f = Build(config: cfg, agentView: avFake);

        var result = await f.Coordinator.PairByCodeAsync("ZZZZZZ");

        Assert.False(result.Success);
        Assert.True(result.IsAuthError);
    }

    // ── RefreshClaudeStateAsync ───────────────────────────────────────────

    [Fact]
    public async Task RefreshClaudeState_NotLoggedIn_ReturnsLoggedInFalse()
    {
        var claudeFake = new FakeClaudeApiClient { IsLoggedIn = false };
        using var f = Build(claude: claudeFake);

        var result = await f.Coordinator.RefreshClaudeStateAsync();

        Assert.False(result.LoggedIn);
        Assert.Empty(result.Organizations);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task RefreshClaudeState_LoggedInEmptyOrgList_ReturnsErrorMessage()
    {
        // FakeClaudeApiClient.ListOrganizationsAsync returns an empty list by default.
        var claudeFake = new FakeClaudeApiClient { IsLoggedIn = true };
        using var f = Build(claude: claudeFake);

        var result = await f.Coordinator.RefreshClaudeStateAsync();

        Assert.True(result.LoggedIn);
        Assert.Empty(result.Organizations);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("no organisation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Sign-out ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SignOutClaudeAsync_CallsSeam_ClearsOrgId_AndReturnsNotLoggedIn()
    {
        var claudeFake = new FakeClaudeApiClient { IsLoggedIn = true };
        using var f = Build(claude: claudeFake);

        var result = await f.Coordinator.SignOutClaudeAsync();

        Assert.Equal(1, claudeFake.SignOutCalls);
        Assert.Null(f.Config.ClaudeOrgId);
        Assert.False(result.LoggedIn);
    }

    [Fact]
    public async Task SignOutAgentViewAsync_CallsSeam_WipesEveryAgentViewCredential()
    {
        var cfg = new AppConfig
        {
            ClaudeOrgId          = "org-123",
            AgentViewBaseUrl     = "https://agentview.de",
            AgentViewApiKey      = "avk_secret",
            AgentViewDisplayId   = "disp-1",
            AgentViewDisplayName = "Token Counter",
            AgentViewSlotSlug    = "custom-slug",
            AgentViewGroupId     = "grp-9",
            AgentViewUserEmail   = "user@example.com",
            SetupComplete        = true,
        };
        var avFake = new FakeAgentViewApiClient { IsLoggedIn = true };
        using var f = Build(agentView: avFake, config: cfg);

        var result = await f.Coordinator.SignOutAgentViewAsync();

        Assert.Equal(1, avFake.SignOutCalls);
        Assert.Null(f.Config.AgentViewApiKey);
        Assert.Null(f.Config.AgentViewDisplayId);
        Assert.Null(f.Config.AgentViewDisplayName);
        Assert.Null(f.Config.AgentViewGroupId);
        Assert.Null(f.Config.AgentViewUserEmail);
        Assert.Equal("claude-usage", f.Config.AgentViewSlotSlug); // back to default
        Assert.False(f.Config.SetupComplete);
        Assert.False(f.Coordinator.ApiKeyFullSetupActive);
        Assert.False(result.LoggedIn);
        // Claude side must be untouched by an agentView sign-out.
        Assert.Equal("org-123", f.Config.ClaudeOrgId);
    }

    [Fact]
    public async Task SignOutAgentViewAsync_DropsInMemoryApiKeyTransport()
    {
        var avFake = new FakeAgentViewApiClient { IsLoggedIn = true };
        avFake.UseApiKey("avk_in_memory");
        using var f = Build(agentView: avFake);

        await f.Coordinator.SignOutAgentViewAsync();

        Assert.False(avFake.IsApiKeyMode);
    }
}
