using System.Text.Json;
using AgentView.TokenCounter.Services;
using Xunit;

namespace AgentViewTokenCounter.Tests;

public sealed class DisplayHtmlBuilderTests
{
    [Fact]
    public void Build_ReplacesSlotPlaceholder()
    {
        const string readUrl = "https://content.agentview.de/data/u/test/claude-usage.json";

        var html = DisplayHtmlBuilder.Build(readUrl);

        Assert.Contains(readUrl, html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{slot:", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_EmptyOrWhitespaceUrl_ThrowsArgumentException(string url)
    {
        Assert.Throws<ArgumentException>(() => DisplayHtmlBuilder.Build(url));
    }

    [Fact]
    public void LoadRawHtml_ContainsPlaceholder()
    {
        var html = DisplayHtmlBuilder.LoadRawHtml();
        // Regression: the embedded resource must still contain the
        // slot placeholder so Build() has something to substitute.
        Assert.Contains("{{slot:" + DisplayHtmlBuilder.SlotPlaceholderKey, html, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadDefaultSlotJson_IsValidJson()
    {
        var json = DisplayHtmlBuilder.LoadDefaultSlotJson();
        // Must parse without throwing — malformed JSON would break the
        // first-publish slot seed.
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
