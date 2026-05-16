namespace AgentView.TokenCounter.Models;

/// <summary>
/// Outcome of a single ping cycle. Used by the tray icon to update its
/// tooltip + colour, and by the Overview tab to render the last run.
/// </summary>
public sealed record PingResult
{
    public required DateTimeOffset At { get; init; }
    public required PingStatus Status { get; init; }
    public required string Message { get; init; }
    public int BucketsPosted { get; init; }

    /// <summary>
    /// The slot content that was just PUT to agentView. Allows the
    /// tray icon to display live usage values in its tooltip and the
    /// status menu item. Null for failure / paused results.
    /// </summary>
    public SlotContent? Slot { get; init; }

    public static PingResult Success(SlotContent slot) => new()
    {
        At            = DateTimeOffset.Now,
        Status        = PingStatus.Ok,
        Message       = $"Posted {slot.Buckets.Count} bucket(s).",
        BucketsPosted = slot.Buckets.Count,
        Slot          = slot,
    };

    public static PingResult Failure(string message) => new()
    {
        At      = DateTimeOffset.Now,
        Status  = PingStatus.Failed,
        Message = message,
    };

    public static PingResult Paused() => new()
    {
        At      = DateTimeOffset.Now,
        Status  = PingStatus.Paused,
        Message = "Paused.",
    };
}

public enum PingStatus
{
    Unknown = 0,
    Ok = 1,
    Failed = 2,
    Paused = 3,
}
