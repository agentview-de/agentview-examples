using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;
using AgentViewTokenCounter.Tests.Helpers;
using Xunit;

namespace AgentViewTokenCounter.Tests;

/// <summary>
/// Integration tests for <see cref="ConfigStore"/>. These hit the real
/// filesystem in a temp directory, never %APPDATA%, and are isolated
/// per test via <see cref="TempDir"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConfigStoreTests
{
    // ── Missing / corrupt file ────────────────────────────────────────────

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);

        var config = store.Load();

        Assert.NotNull(config);
        Assert.Null(config.ClaudeOrgId);
        Assert.Equal(120, config.PollIntervalSeconds);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefault()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "config.json"), "{ NOT VALID JSON }}}");
        var store = new ConfigStore(dir.Path);

        var config = store.Load();

        Assert.NotNull(config);
        Assert.Null(config.ClaudeOrgId);
    }

    // ── Round-trip ────────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_RoundTripsAllNonSecretFields()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        var original = new AppConfig
        {
            ClaudeOrgId          = "org-abc",
            AgentViewBaseUrl     = "https://agentview.de",
            AgentViewSlotSlug    = "test-slug",
            AgentViewGroupId     = "grp-1",
            AgentViewDisplayId   = "disp-1",
            AgentViewDisplayName = "My Display",
            AgentViewUserEmail   = "user@example.com",
            PollIntervalSeconds  = 60,
            IncludeModelSplits   = false,
            AutoStart            = true,
            Paused               = true,
            SetupComplete        = true,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.ClaudeOrgId,          loaded.ClaudeOrgId);
        Assert.Equal(original.AgentViewBaseUrl,      loaded.AgentViewBaseUrl);
        Assert.Equal(original.AgentViewSlotSlug,     loaded.AgentViewSlotSlug);
        Assert.Equal(original.AgentViewGroupId,      loaded.AgentViewGroupId);
        Assert.Equal(original.AgentViewDisplayId,    loaded.AgentViewDisplayId);
        Assert.Equal(original.AgentViewDisplayName,  loaded.AgentViewDisplayName);
        Assert.Equal(original.AgentViewUserEmail,    loaded.AgentViewUserEmail);
        Assert.Equal(original.PollIntervalSeconds,   loaded.PollIntervalSeconds);
        Assert.Equal(original.IncludeModelSplits,    loaded.IncludeModelSplits);
        Assert.Equal(original.AutoStart,             loaded.AutoStart);
        Assert.Equal(original.Paused,                loaded.Paused);
        Assert.Equal(original.SetupComplete,         loaded.SetupComplete);
    }

    // ── DPAPI ─────────────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_DpapiRoundTripsApiKey()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        var config = new AppConfig { AgentViewApiKey = "avk_secretkey123" };

        store.Save(config);

        // The raw JSON on disk must NOT contain the plaintext key.
        var json = File.ReadAllText(Path.Combine(dir.Path, "config.json"));
        Assert.DoesNotContain("avk_secretkey123", json, StringComparison.Ordinal);
        Assert.Contains("DPAPI:", json, StringComparison.Ordinal);

        // But loading decrypts it back to the original.
        var loaded = store.Load();
        Assert.Equal("avk_secretkey123", loaded.AgentViewApiKey);
    }

    [Fact]
    public void Save_NullApiKey_StaysNull()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        store.Save(new AppConfig { AgentViewApiKey = null });

        var loaded = store.Load();

        Assert.Null(loaded.AgentViewApiKey);
    }

    [Fact]
    public void Load_PlaintextKey_ReturnedAsIs()
    {
        // Simulates a pre-encryption config.json with a raw key.
        using var dir = new TempDir();
        File.WriteAllText(
            Path.Combine(dir.Path, "config.json"),
            """{"AgentViewApiKey":"avk_plaintext"}""");
        var store = new ConfigStore(dir.Path);

        var loaded = store.Load();

        Assert.Equal("avk_plaintext", loaded.AgentViewApiKey);
    }

    // ── URL normalisation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("https://agentview.de/",   "https://agentview.de")]
    [InlineData("https://agentview.de///", "https://agentview.de")]
    [InlineData("https://agentview.de",    "https://agentview.de")]
    public void Save_ThenLoad_StripsTrailingSlashFromBaseUrl(string input, string expected)
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        store.Save(new AppConfig { AgentViewBaseUrl = input });

        var loaded = store.Load();

        Assert.Equal(expected, loaded.AgentViewBaseUrl);
    }

    // ── No stale .tmp file ────────────────────────────────────────────────

    [Fact]
    public void Save_LeavesNoTmpFile()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        store.Save(new AppConfig());

        var tmpFiles = Directory.GetFiles(dir.Path, "*.tmp");
        Assert.Empty(tmpFiles);
    }
}
