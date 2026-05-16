using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.ViewModels;

/// <summary>
/// View-model for a single bucket row in the Overview tab.
/// Computed once per sync cycle and bound to the ItemsControl
/// DataTemplates in <c>OverviewTab.xaml</c>.
/// </summary>
/// <remarks>
/// Deliberately a simple immutable class (not INotifyPropertyChanged)
/// — the entire collection is replaced on each update, so property-
/// level change notification is unnecessary.
/// </remarks>
public sealed class BucketRowViewModel
{
    // ── Identity ─────────────────────────────────────────────────────

    /// <summary>Stable key, e.g. <c>five_hour</c>.</summary>
    public string Key { get; }

    /// <summary>
    /// Display label, e.g. "5-Hour Limit", "Weekly · all models".
    /// </summary>
    public string Label { get; }

    // ── Values ────────────────────────────────────────────────────────

    /// <summary>Bar fill percent, integer 0..100.</summary>
    public int UsedPct { get; }

    /// <summary>
    /// Short remaining caption, e.g. "16%", used in the KV row.
    /// </summary>
    public string RemainingPct { get; }

    /// <summary>Used percent string for the KV row.</summary>
    public string UsedPctLabel { get; }

    /// <summary>
    /// Short human-readable reset countdown for the KV row
    /// ("RESETS IN" column), e.g. "50m", "1d 3h".
    /// </summary>
    public string ResetsInShort { get; }

    /// <summary>
    /// Longer reset caption shown below a card, e.g.
    /// "resets in 1h 22m".
    /// </summary>
    public string ResetCaption { get; }

    /// <summary>
    /// Caption under the big-percentage number, e.g.
    /// "of 5h allowance used".
    /// </summary>
    public string OfLabel { get; }

    // ── Layout hint ───────────────────────────────────────────────────

    /// <summary>
    /// Which card template to use. The ItemsControl uses a
    /// <see cref="System.Windows.Controls.DataTemplateSelector"/>
    /// to pick the correct visual for each bucket position.
    /// </summary>
    public BucketCardKind CardKind { get; }

    public BucketRowViewModel(Bucket bucket, BucketCardKind cardKind)
    {
        Key       = bucket.Key;
        Label     = bucket.Label;
        UsedPct   = bucket.UsedPct;
        CardKind  = cardKind;

        UsedPctLabel   = $"{UsedPct}%";
        RemainingPct   = $"{Math.Max(0, 100 - UsedPct)}%";
        ResetsInShort  = FormatResetShort(bucket.ResetIso);
        ResetCaption   = FormatResetCaption(bucket.ResetIso);
        OfLabel        = $"of {ShortBucketSuffix(bucket.Key)} used";
    }

    // ── Static formatting helpers ─────────────────────────────────────

    private static string ShortBucketSuffix(string key) => key switch
    {
        "five_hour"        => "5h allowance",
        "seven_day"        => "weekly allowance",
        "seven_day_opus"   => "Opus weekly",
        "seven_day_sonnet" => "Sonnet weekly",
        "seven_day_design" => "Design weekly",
        _                  => "allowance",
    };

    private static string FormatResetCaption(DateTimeOffset? resetIso)
    {
        if (resetIso is null)
        {
            return "no scheduled reset";
        }
        var delta = resetIso.Value - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero)  { return "resetting now"; }
        if (delta.TotalDays >= 1)    { return $"resets in {(int)delta.TotalDays}d {delta.Hours}h"; }
        if (delta.TotalHours >= 1)   { return $"resets in {(int)delta.TotalHours}h {delta.Minutes:D2}m"; }
        return $"resets in {Math.Max(1, (int)delta.TotalMinutes)}m";
    }

    private static string FormatResetShort(DateTimeOffset? resetIso)
    {
        if (resetIso is null)
        {
            return "—";
        }
        var delta = resetIso.Value - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero) { return "now"; }
        if (delta.TotalDays >= 1)   { return $"{(int)delta.TotalDays}d {delta.Hours}h"; }
        if (delta.TotalHours >= 1)  { return $"{(int)delta.TotalHours}h {delta.Minutes:D2}m"; }
        return $"{Math.Max(1, (int)delta.TotalMinutes)}m";
    }
}

/// <summary>
/// Identifies which DataTemplate an Overview bucket card should use.
/// </summary>
public enum BucketCardKind
{
    /// <summary>
    /// The primary 2-column card (5h rolling window).
    /// Big number left + KV row right + wide bar.
    /// </summary>
    Featured,

    /// <summary>
    /// Single-percentage card (col 3 of the featured row).
    /// </summary>
    Secondary,

    /// <summary>
    /// Compact one-third-width card (row 2).
    /// </summary>
    Small,
}
