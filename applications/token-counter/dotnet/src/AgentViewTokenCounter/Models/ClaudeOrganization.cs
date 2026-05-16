using System.Text.Json.Serialization;

namespace AgentView.TokenCounter.Models;

/// <summary>
/// One entry in the response of
/// <c>GET https://claude.ai/api/organizations</c>. Undocumented
/// endpoint; the fields here are based on an observed 2026-05
/// response. Unknown fields are ignored.
/// </summary>
public sealed class ClaudeOrganization
{
    /// <summary>Stable identifier used in every per-org call.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    /// <summary>Human-readable name shown in the setup dropdown.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// e.g. <c>"default_claude_max_5x"</c>, <c>"claude_pro"</c>. Mapped
    /// to a friendly tier label for the display status row.
    /// </summary>
    [JsonPropertyName("rate_limit_tier")]
    public string? RateLimitTier { get; set; }

    /// <summary>
    /// e.g. <c>["claude_max", "chat"]</c>. We use this as a secondary
    /// hint when <see cref="RateLimitTier"/> is missing.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public List<string>? Capabilities { get; set; }

    /// <summary>
    /// Returns a friendly tier label like "Max 5x" / "Pro" / "Team",
    /// or <c>null</c> if no tier hint is available.
    /// </summary>
    public string? FriendlyTier()
    {
        var t = RateLimitTier?.ToLowerInvariant() ?? "";
        if (t.Contains("max_20x")) return "Max 20x";
        if (t.Contains("max_5x"))  return "Max 5x";
        if (t.Contains("max"))     return "Max";
        if (t.Contains("team"))    return "Team";
        if (t.Contains("pro"))     return "Pro";
        if (t.Contains("free"))    return "Free";

        if (Capabilities is { Count: > 0 })
        {
            if (Capabilities.Contains("claude_max"))  return "Max";
            if (Capabilities.Contains("claude_team")) return "Team";
            if (Capabilities.Contains("claude_pro"))  return "Pro";
        }
        return null;
    }
}
