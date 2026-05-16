using System.Text.Json.Serialization;

namespace AgentView.TokenCounter.Models;

/// <summary>
/// Shape of the JSON body PUT to
/// <c>{baseUrl}/api/v1/data/{slug}</c>. The display reads this slot
/// every 120 s and renders the values verbatim.
/// </summary>
/// <remarks>
/// The agentView PUT endpoint stores the request body literally - no
/// envelope, no wrapper. The display template expects this exact shape:
/// <code>
/// {
///   "buckets":   [ { key, label, usedPct, resetIso }, ... ],
///   "sparkline": null | { label, rangeLabel, points: [...] },
///   "status":    null | { state, model }
/// }
/// </code>
/// We don't populate <c>sparkline</c> or <c>status</c> from the
/// claude.ai endpoint because they aren't part of the response.
/// </remarks>
public sealed class SlotContent
{
    [JsonPropertyName("buckets")]
    public List<Bucket> Buckets { get; set; } = new();

    /// <summary>
    /// Hourly token-rate sparkline. Always null when sourced from
    /// claude.ai - that endpoint doesn't expose hourly history. The
    /// display gracefully hides the sparkline when this is null.
    /// </summary>
    [JsonPropertyName("sparkline")]
    public Sparkline? Sparkline { get; set; }

    /// <summary>
    /// Bottom-line status row (state + model). Always null from this
    /// source. The display hides the status line when null.
    /// </summary>
    [JsonPropertyName("status")]
    public Status? Status { get; set; }
}

/// <summary>
/// One bar on the display.
/// </summary>
public sealed class Bucket
{
    /// <summary>Stable identifier, kebab-case.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>Display label shown next to the percentage.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>Bar fill percent, integer 0..100.</summary>
    [JsonPropertyName("usedPct")]
    public int UsedPct { get; set; }

    /// <summary>ISO-8601 timestamp of the next reset. The display
    /// computes the human-readable countdown ("Reset in 1h 22m").
    /// Null when no scheduled reset.</summary>
    [JsonPropertyName("resetIso")]
    public DateTimeOffset? ResetIso { get; set; }
}

public sealed class Sparkline
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("rangeLabel")]
    public string RangeLabel { get; set; } = "";

    [JsonPropertyName("points")]
    public List<double> Points { get; set; } = new();
}

public sealed class Status
{
    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";
}
