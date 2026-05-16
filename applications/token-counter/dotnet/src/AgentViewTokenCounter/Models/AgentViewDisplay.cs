using System.Text.Json.Serialization;

namespace AgentView.TokenCounter.Models;

/// <summary>
/// One entry returned by <c>GET /api/v1/agent/displays</c>. Only the
/// subset the setup wizard surfaces is mapped here.
/// </summary>
public sealed class AgentViewDisplay
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("isOnline")]
    public bool? IsOnline { get; set; }

    /// <summary>
    /// Convenience helper for the picker label — "Living room  ·  online".
    /// </summary>
    public string FriendlyLabel()
    {
        var tail = (IsOnline == true) ? "online"
                 : (Status is { Length: > 0 } s) ? s
                 : "offline";
        return $"{Name}  ·  {tail}";
    }
}

/// <summary>Wraps the <c>GET /api/v1/agent/displays</c> envelope.</summary>
public sealed class ListDisplaysResponse
{
    [JsonPropertyName("displays")]
    public List<AgentViewDisplay> Displays { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>Wraps the <c>POST /api/v1/agent/displays</c> response.</summary>
public sealed class CreateDisplayResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>Wraps the <c>POST /api/v1/agent/api-keys</c> response.</summary>
public sealed class CreateApiKeyResponse
{
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = "";

    /// <summary>
    /// The raw <c>avk_...</c> key. The server returns it exactly once.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("permissions")]
    public string? Permissions { get; set; }
}

/// <summary>One data slot returned by <c>PUT /api/v1/data/{slug}</c>.</summary>
public sealed class DataSlotItem
{
    [JsonPropertyName("slotId")]
    public string SlotId { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("groupId")]
    public string? GroupId { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Public read URL the display can fetch the slot JSON from.</summary>
    [JsonPropertyName("readUrl")]
    public string? ReadUrl { get; set; }
}

/// <summary>Envelope returned by <c>PUT /api/v1/data/{slug}</c>.</summary>
public sealed class PutDataSlotResponse
{
    [JsonPropertyName("slot")]
    public DataSlotItem? Slot { get; set; }
}

/// <summary>Subset of the <c>/auth/me</c> envelope we surface in the UI.</summary>
public sealed class AgentViewMeResponse
{
    [JsonPropertyName("user")]
    public AgentViewUser? User { get; set; }
}

public sealed class AgentViewUser
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
