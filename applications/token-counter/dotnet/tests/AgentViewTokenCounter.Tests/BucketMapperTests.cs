using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;
using Xunit;

namespace AgentViewTokenCounter.Tests;

public sealed class BucketMapperTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static ClaudeBucket Bucket(double utilization, string? resetsAt = null) => new()
    {
        Utilization = utilization,
        ResetsAt    = resetsAt is null ? null : DateTimeOffset.Parse(resetsAt, System.Globalization.CultureInfo.InvariantCulture),
    };

    private static ClaudeUsageResponse FullResponse(bool includeSplits = true) => new()
    {
        FiveHour      = Bucket(50),
        SevenDay      = Bucket(60),
        SevenDayOpus  = includeSplits ? Bucket(20) : null,
        SevenDaySonnet= includeSplits ? Bucket(30) : null,
        SevenDayDesign= Bucket(10),
    };

    // ── Bucket count ─────────────────────────────────────────────────────

    [Fact]
    public void AllBucketsPresent_WithSplits_Returns6Buckets()
    {
        // All five standard buckets + Design = 5, splits add Opus + Sonnet = 6 total.
        // But standard count without extra is: 5h + 7d + opus + sonnet + design = 5.
        var result = BucketMapper.Map(FullResponse(includeSplits: true), true);
        Assert.Equal(5, result.Buckets.Count);
    }

    [Fact]
    public void SplitsDisabled_OmitsOpusAndSonnet()
    {
        var raw = FullResponse(includeSplits: true); // has the split buckets
        var result = BucketMapper.Map(raw, includeModelSplits: false);
        Assert.DoesNotContain(result.Buckets, b => b.Key == "seven_day_opus");
        Assert.DoesNotContain(result.Buckets, b => b.Key == "seven_day_sonnet");
    }

    [Fact]
    public void NullFiveHour_OmitsFiveHourBucket()
    {
        var raw = FullResponse();
        raw.FiveHour = null;
        var result = BucketMapper.Map(raw, true);
        Assert.DoesNotContain(result.Buckets, b => b.Key == "five_hour");
    }

    [Fact]
    public void NullUtilization_OmitsBucket()
    {
        var raw = FullResponse();
        raw.SevenDay = new ClaudeBucket { Utilization = null };
        var result = BucketMapper.Map(raw, true);
        Assert.DoesNotContain(result.Buckets, b => b.Key == "seven_day");
    }

    // ── Rounding / clamping ───────────────────────────────────────────────

    [Theory]
    [InlineData(99.6, 100)]
    [InlineData(50.4, 50)]
    [InlineData(101.0, 100)]   // over-cap → 100
    [InlineData(-1.0, 0)]      // negative → 0
    public void UsedPct_RoundsAndClamps(double utilization, int expectedPct)
    {
        var raw = new ClaudeUsageResponse
        {
            FiveHour = new ClaudeBucket { Utilization = utilization },
        };
        var result = BucketMapper.Map(raw, false);
        Assert.Equal(expectedPct, result.Buckets[0].UsedPct);
    }

    // ── Extra usage ───────────────────────────────────────────────────────

    [Fact]
    public void ExtraUsage_Enabled_At70_AddsBucketAndStatus()
    {
        var raw = new ClaudeUsageResponse
        {
            FiveHour   = Bucket(50),
            ExtraUsage = new ClaudeExtraUsage { IsEnabled = true, Utilization = 70 },
        };
        var result = BucketMapper.Map(raw, false);
        var extra = result.Buckets.Single(b => b.Key == "extra_usage");
        Assert.Equal(70, extra.UsedPct);
        Assert.NotNull(result.Status);
        Assert.Contains("extra credits", result.Status!.Model);
    }

    [Fact]
    public void ExtraUsage_Enabled_NullUtilization_AddsBucketAt0()
    {
        var raw = new ClaudeUsageResponse
        {
            FiveHour   = Bucket(50),
            ExtraUsage = new ClaudeExtraUsage { IsEnabled = true, Utilization = null },
        };
        var result = BucketMapper.Map(raw, false);
        var extra = result.Buckets.Single(b => b.Key == "extra_usage");
        Assert.Equal(0, extra.UsedPct);
    }

    [Fact]
    public void ExtraUsage_Disabled_NoBucketAndNullStatus()
    {
        var raw = new ClaudeUsageResponse
        {
            FiveHour   = Bucket(50),
            ExtraUsage = new ClaudeExtraUsage { IsEnabled = false, Utilization = 80 },
        };
        var result = BucketMapper.Map(raw, false);
        Assert.DoesNotContain(result.Buckets, b => b.Key == "extra_usage");
        Assert.Null(result.Status);
    }

    // ── Bucket order ─────────────────────────────────────────────────────

    [Fact]
    public void BucketOrder_FiveHourFirst_SevenDaySecond_SplitsThirdFourth()
    {
        var result = BucketMapper.Map(FullResponse(includeSplits: true), true);
        Assert.Equal("five_hour",        result.Buckets[0].Key);
        Assert.Equal("seven_day",        result.Buckets[1].Key);
        Assert.Equal("seven_day_opus",   result.Buckets[2].Key);
        Assert.Equal("seven_day_sonnet", result.Buckets[3].Key);
    }

    // ── ResetIso passthrough ─────────────────────────────────────────────

    [Fact]
    public void ResetIso_PassedThrough()
    {
        var reset = DateTimeOffset.Parse("2026-05-19T03:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var raw   = new ClaudeUsageResponse
        {
            SevenDay = new ClaudeBucket { Utilization = 40, ResetsAt = reset },
        };
        var result = BucketMapper.Map(raw, false);
        Assert.Equal(reset, result.Buckets[0].ResetIso);
    }

    // ── Sparkline always null ─────────────────────────────────────────────

    [Fact]
    public void Sparkline_AlwaysNull()
    {
        var result = BucketMapper.Map(FullResponse(), true);
        Assert.Null(result.Sparkline);
    }

    // ── HighestMood / Status.State ────────────────────────────────────────

    [Theory]
    [InlineData(95, "crit")]
    [InlineData(100, "crit")]
    [InlineData(80, "hot")]
    [InlineData(94, "hot")]
    [InlineData(60, "busy")]
    [InlineData(79, "busy")]
    [InlineData(30, "cool")]
    [InlineData(0,  "cool")]
    [InlineData(31, "focus")]
    [InlineData(59, "focus")]
    public void HighestMood_CorrectStateForUsedPct(int pct, string expectedState)
    {
        var raw = new ClaudeUsageResponse
        {
            FiveHour   = new ClaudeBucket { Utilization = pct },
            ExtraUsage = new ClaudeExtraUsage { IsEnabled = true, Utilization = pct },
        };
        var result = BucketMapper.Map(raw, false);
        Assert.Equal(expectedState, result.Status!.State);
    }

    [Fact]
    public void Status_Model_ContainsExtraCredits()
    {
        var raw = new ClaudeUsageResponse
        {
            FiveHour   = Bucket(50),
            ExtraUsage = new ClaudeExtraUsage { IsEnabled = true, Utilization = 42 },
        };
        var result = BucketMapper.Map(raw, false);
        Assert.Contains("extra credits", result.Status!.Model, StringComparison.OrdinalIgnoreCase);
    }
}
