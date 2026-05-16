using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Writes a slot value to agentView via
/// <c>PUT {baseUrl}/api/v1/data/{slug}</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two-clients rationale.</strong> This class is the
/// minimal, loop-only writer. It does exactly one thing: PUT a slot.
/// It never needs a browser session, never lists displays, never mints
/// keys. Keeping it separate from <see cref="AgentViewApiClient"/>
/// (the wizard client) means the background loop has no dependency on
/// the WebView or the broad-scope session — it only needs an API key
/// and an HTTP client. See also the class-level doc on
/// <see cref="AgentViewApiClient"/> and <c>dotnet/README.md</c>.
/// </para>
/// <para>
/// The agentView REST API is documented at
/// <c>https://agentview.de/swagger/index.html</c>. The relevant
/// endpoint for this app:
/// </para>
/// <code>
/// PUT /api/v1/data/{slug}
/// Headers:
///   X-API-Key:    avk_...    (scoped key issued in the portal)
///   Content-Type: application/json
/// Query:
///   ?groupId={id}             (optional, for group-scoped slots)
///   ?label={display-label}    (required on first write so the slot can be created;
///                              optional on updates - the server only rewrites the label
///                              when the parameter is present)
///   ?type=value | aggregate   (optional, defaults to "value")
/// Body: raw JSON, top-level object/array/value, max ~2 MB.
/// </code>
/// <para>
/// The body is stored verbatim. The display polls the slot's read URL
/// and renders the JSON directly.
/// </para>
/// </remarks>
public sealed class AgentViewClient : IAgentViewClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private readonly HttpClient _http;

    public AgentViewClient(HttpClient http)
    {
        _http = http;
    }

    /// <param name="label">
    /// When non-null, sent as the <c>?label=...</c> query parameter.
    /// Required on first write (the slot doesn't exist yet); for
    /// subsequent updates pass <c>null</c> so a user-edited label in
    /// the portal isn't clobbered on every sync.
    /// </param>
    public async Task WriteSlotAsync(
        string baseUrl,
        string slug,
        string apiKey,
        SlotContent content,
        string? groupId = null,
        string? label   = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl is required.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("slug is required.",    nameof(slug));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("apiKey is required.",  nameof(apiKey));

        var uri = $"{baseUrl.TrimEnd('/')}/api/v1/data/{Uri.EscapeDataString(slug)}";
        var query = new List<string>();
        if (!string.IsNullOrEmpty(groupId))
            query.Add("groupId=" + Uri.EscapeDataString(groupId));
        if (!string.IsNullOrEmpty(label))
            query.Add("label=" + Uri.EscapeDataString(label));
        if (query.Count > 0)
            uri += "?" + string.Join("&", query);

        using var req = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = JsonContent.Create(content, options: Json),
        };
        req.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // The server requires ?label= on slot creation (first PUT).
            // Surface that as a typed exception so PingService can
            // transparently retry with a label set.
            if ((int)res.StatusCode == 400 && body.Contains("\"missing_label\"", StringComparison.Ordinal))
            {
                throw new MissingSlotLabelException(body);
            }
            throw new AgentViewApiException(
                $"agentView slot write failed ({(int)res.StatusCode} {res.ReasonPhrase}): {Truncate(body, 400)}");
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}

public sealed class AgentViewApiException : Exception
{
    public AgentViewApiException(string message) : base(message) { }
}

/// <summary>
/// Thrown when the agentView PUT returns 400 / <c>missing_label</c>,
/// i.e. the slot doesn't exist yet and the server needs a label to
/// create it.
/// </summary>
public sealed class MissingSlotLabelException : Exception
{
    public MissingSlotLabelException(string body) : base(body) { }
}
