using System.Text.Json.Serialization;

namespace AgentView.TokenCounter.Models;

/// <summary>
/// Shape of <c>GET https://claude.ai/api/organizations/{orgId}/usage</c>.
/// </summary>
/// <remarks>
/// This is the response body the "Plan usage" widget in claude.ai consumes.
/// The endpoint is undocumented; the fields here are based on an observed
/// 2026-05 response and may change without notice. Unknown fields are
/// ignored, missing buckets parse as <c>null</c>.
/// <para>
/// Property names are lower_snake_case in JSON because claude.ai's
/// backend serialises them that way.
/// </para>
/// </remarks>
public sealed class ClaudeUsageResponse
{
    [JsonPropertyName("five_hour")]
    public ClaudeBucket? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public ClaudeBucket? SevenDay { get; set; }

    /// <summary>
    /// Weekly Claude Design bucket (web/claude.ai chat sessions). The
    /// codename "omelette" is Anthropic's internal label for this
    /// feature group.
    /// </summary>
    [JsonPropertyName("seven_day_omelette")]
    public ClaudeBucket? SevenDayDesign { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public ClaudeBucket? SevenDayOpus { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public ClaudeBucket? SevenDaySonnet { get; set; }

    /// <summary>
    /// Bezahlte Top-up credits. <c>is_enabled</c> indicates whether
    /// the user has bought extra; the rest is null if not.
    /// </summary>
    [JsonPropertyName("extra_usage")]
    public ClaudeExtraUsage? ExtraUsage { get; set; }
}

/// <summary>
/// A single bucket inside <see cref="ClaudeUsageResponse"/>.
/// </summary>
public sealed class ClaudeBucket
{
    /// <summary>Integer 0..100. May be missing if Anthropic hasn't
    /// produced a value for this bucket on this account.</summary>
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    /// <summary>
    /// ISO-8601 timestamp at which the bucket resets. May be null
    /// when the bucket has no scheduled reset (e.g. zero-utilization
    /// model splits).
    /// </summary>
    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; set; }
}

public sealed class ClaudeExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }
}
