using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Maps a <see cref="ClaudeUsageResponse"/> (claude.ai shape) onto a
/// <see cref="SlotContent"/> (agentView Token Counter display shape).
/// </summary>
/// <remarks>
/// <para>
/// The display's <c>token-usage</c> slot accepts up to a handful of
/// buckets and renders them as percent-fill bars. The first bucket is
/// rendered larger ("primary"); the rest sit below in equal-size rows.
/// </para>
/// <para>
/// Order is therefore deliberate: <strong>5-hour first</strong>
/// because that's the limit that bites <em>during</em> a coding
/// session, then the weekly aggregate, then the model splits, then
/// the Claude Design weekly budget, then any paid extra credits.
/// </para>
/// </remarks>
public static class BucketMapper
{
    /// <summary>
    /// Build a slot payload.
    /// </summary>
    /// <param name="raw">claude.ai /usage response.</param>
    /// <param name="includeModelSplits">
    /// When true (default), the weekly Opus and Sonnet splits are
    /// included even when their utilisation is 0 — so the user always
    /// sees the breakdown, not only when one model dominates.
    /// </param>
    public static SlotContent Map(ClaudeUsageResponse raw, bool includeModelSplits)
    {
        var content = new SlotContent
        {
            Buckets   = new List<Bucket>(6),
            Sparkline = null,   // claude.ai does not expose hourly history
            Status    = null,   // populated below when extra credits are active
        };

        // 1) The 5-hour bucket sits on top: this is the limit the user
        //    cares about mid-session. Rendered as the primary (big) bar.
        AddBucket(content.Buckets, "five_hour",        "5-Hour Limit",          raw.FiveHour);

        // 2) Weekly all-models: the bigger-picture budget.
        AddBucket(content.Buckets, "seven_day",        "Weekly · all models", raw.SevenDay);

        // 3) Model splits. Showing them at 0 % is intentional — the user
        //    knows the breakdown without having to wait for it to ramp.
        if (includeModelSplits)
        {
            AddBucket(content.Buckets, "seven_day_opus",   "Weekly · Opus",   raw.SevenDayOpus);
            AddBucket(content.Buckets, "seven_day_sonnet", "Weekly · Sonnet", raw.SevenDaySonnet);
        }

        // 4) Claude Design (web chat, internally "omelette") — a separate
        //    weekly budget. Drops out if the user has no Design usage.
        AddBucket(content.Buckets, "seven_day_design", "Weekly · Claude Design", raw.SevenDayDesign);

        // 5) Paid top-up credits. Only shown when the user has actually
        //    enabled them in Claude's billing flow; otherwise it'd be a
        //    dead row.
        if (raw.ExtraUsage is { IsEnabled: true })
        {
            var extraPct = raw.ExtraUsage.Utilization is { } u
                ? (int)Math.Clamp(Math.Round(u), 0, 100)
                : 0;
            content.Buckets.Add(new Bucket
            {
                Key      = "extra_usage",
                Label    = "Extra credits",
                UsedPct  = extraPct,
                ResetIso = null, // top-ups don't auto-reset
            });

            // Surface the fact that paid credits are live in the bottom
            // status row so it doesn't get lost between the bars. State
            // is the mascot-mood label that the template already styles;
            // "model" is repurposed as a small annotation line.
            content.Status = new Status
            {
                State = HighestMood(content.Buckets),
                Model = $"extra credits · {extraPct}% used",
            };
        }

        return content;
    }

    private static void AddBucket(
        List<Bucket> sink,
        string key,
        string label,
        ClaudeBucket? src)
    {
        if (src is null) return;
        if (src.Utilization is null) return;

        var pct = (int)Math.Clamp(Math.Round(src.Utilization.Value), 0, 100);
        sink.Add(new Bucket
        {
            Key      = key,
            Label    = label,
            UsedPct  = pct,
            ResetIso = src.ResetsAt,
        });
    }

    /// <summary>
    /// Returns the mascot-mood label matching the most-loaded bucket.
    /// Mirrors the JS-side <c>moodFor()</c> in display.html so the
    /// status line agrees with the ghost colour / cycle speed.
    /// </summary>
    private static string HighestMood(IEnumerable<Bucket> buckets)
    {
        var maxPct = 0;
        foreach (var b in buckets)
        {
            if (b.UsedPct > maxPct) maxPct = b.UsedPct;
        }
        return maxPct switch
        {
            >= 95 => "crit",
            >= 80 => "hot",
            >= 60 => "busy",
            <= 30 => "cool",
            _     => "focus",
        };
    }
}
