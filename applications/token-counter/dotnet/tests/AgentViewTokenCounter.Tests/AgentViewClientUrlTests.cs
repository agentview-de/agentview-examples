using System.Net;
using System.Net.Http;
using System.Text;
using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;
using Xunit;

namespace AgentViewTokenCounter.Tests;

/// <summary>
/// Tests for <see cref="AgentViewClient"/> URL construction, header
/// handling, and error-response parsing.
/// Uses a <see cref="CapturingHandler"/> (DelegatingHandler) that
/// intercepts requests without sending them to a real server.
/// </summary>
public sealed class AgentViewClientUrlTests
{
    private static readonly SlotContent EmptySlot = new SlotContent();

    // ── Helpers ──────────────────────────────────────────────────────────

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpResponseMessage Response { get; set; } =
            new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }

    private static (AgentViewClient client, CapturingHandler handler) Build(
        HttpResponseMessage? response = null)
    {
        var handler = new CapturingHandler();
        if (response is not null)
        {
            handler.Response = response;
        }
        var http   = new HttpClient(handler);
        var client = new AgentViewClient(http);
        return (client, handler);
    }

    // ── URL construction ─────────────────────────────────────────────────

    [Fact]
    public async Task WriteSlotAsync_BuiltCorrectPutUri()
    {
        var (client, handler) = Build();

        await client.WriteSlotAsync(
            "https://agentview.de", "claude-usage", "avk_key", EmptySlot);

        Assert.Equal(
            "https://agentview.de/api/v1/data/claude-usage",
            handler.LastRequest!.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal(HttpMethod.Put, handler.LastRequest.Method);
    }

    [Fact]
    public async Task WriteSlotAsync_GroupId_AppendsQueryParam()
    {
        var (client, handler) = Build();

        await client.WriteSlotAsync(
            "https://agentview.de", "my-slug", "avk_key", EmptySlot,
            groupId: "grp-1");

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("groupId=grp-1", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteSlotAsync_Label_AppendsQueryParam()
    {
        var (client, handler) = Build();

        await client.WriteSlotAsync(
            "https://agentview.de", "my-slug", "avk_key", EmptySlot,
            label: "Claude plan usage");

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("label=Claude", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteSlotAsync_TrailingSlashOnBaseUrl_NoDoubleSlash()
    {
        var (client, handler) = Build();

        await client.WriteSlotAsync(
            "https://agentview.de/", "my-slug", "avk_key", EmptySlot);

        var path = handler.LastRequest!.RequestUri!.AbsolutePath;
        Assert.DoesNotContain("//", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteSlotAsync_SetsXApiKeyHeader()
    {
        var (client, handler) = Build();

        await client.WriteSlotAsync(
            "https://agentview.de", "my-slug", "avk_testkey", EmptySlot);

        Assert.True(handler.LastRequest!.Headers.TryGetValues("X-API-Key", out var values));
        Assert.Equal("avk_testkey", values!.Single());
    }

    // ── Error responses ───────────────────────────────────────────────────

    [Fact]
    public async Task WriteSlotAsync_400WithMissingLabelBody_ThrowsMissingSlotLabelException()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"code":"missing_label","message":"label is required"}""",
                Encoding.UTF8, "application/json"),
        };
        var (client, _) = Build(resp);

        await Assert.ThrowsAsync<MissingSlotLabelException>(() =>
            client.WriteSlotAsync("https://agentview.de", "slug", "avk_key", EmptySlot));
    }

    [Fact]
    public async Task WriteSlotAsync_Non2xx_ThrowsAgentViewApiException()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("forbidden"),
        };
        var (client, _) = Build(resp);

        await Assert.ThrowsAsync<AgentViewApiException>(() =>
            client.WriteSlotAsync("https://agentview.de", "slug", "avk_key", EmptySlot));
    }
}
