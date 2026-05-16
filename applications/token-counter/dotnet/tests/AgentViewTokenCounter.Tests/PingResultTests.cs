using AgentView.TokenCounter.Models;
using Xunit;

namespace AgentViewTokenCounter.Tests;

public sealed class PingResultTests
{
    [Fact]
    public void Success_SetsOkStatusAndSlot()
    {
        var slot = new SlotContent { Buckets = new List<Bucket> { new Bucket { Key = "five_hour", UsedPct = 50 } } };

        var result = PingResult.Success(slot);

        Assert.Equal(PingStatus.Ok, result.Status);
        Assert.Equal(1, result.BucketsPosted);
        Assert.Same(slot, result.Slot);
        Assert.True(result.At > DateTimeOffset.MinValue);
    }

    [Fact]
    public void Failure_SetsFailedStatusAndMessage()
    {
        var result = PingResult.Failure("something went wrong");

        Assert.Equal(PingStatus.Failed, result.Status);
        Assert.Equal("something went wrong", result.Message);
        Assert.Null(result.Slot);
    }

    [Fact]
    public void Paused_SetsPausedStatus()
    {
        var result = PingResult.Paused();

        Assert.Equal(PingStatus.Paused, result.Status);
        Assert.Null(result.Slot);
    }
}
