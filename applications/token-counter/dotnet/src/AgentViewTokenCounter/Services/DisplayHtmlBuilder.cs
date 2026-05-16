using System.Reflection;
using System.Text;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Loads the embedded <c>display.html</c> resource and produces a
/// fully self-contained, send-ready HTML string by substituting the
/// data-slot placeholders the template author wrote.
/// </summary>
/// <remarks>
/// <para>
/// The template HTML originally lived under
/// <c>scripts/seed-store/templates/token-counter-standalone/display.html</c>
/// inside the agentView server repo, where the server-side
/// <c>StoreTemplateMaterializationService</c> did the placeholder
/// substitution at template-send time. To make this bridge work
/// without depending on the store-template system at all, we now
/// ship the same HTML as an embedded resource inside the .exe and
/// do the substitution client-side.
/// </para>
/// <para>
/// Placeholders we replace:
/// <list type="bullet">
///   <item><description><c>{{slot:token-usage.readUrl}}</c> — public read URL of the slot the bridge writes into.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class DisplayHtmlBuilder
{
    /// <summary>
    /// Hard-coded placeholder name used by display.html. The
    /// substitution must mirror the server-side regex
    /// <c>StoreTemplateMaterializationService.SlotPlaceholderRegex</c>
    /// so the same source HTML works in both pipelines.
    /// </summary>
    public const string SlotPlaceholderKey = "token-usage";

    private const string HtmlResourceName    = "AgentView.TokenCounter.Resources.display.html";
    private const string DefaultJsonResource = "AgentView.TokenCounter.Resources.token-usage.default.json";

    /// <summary>
    /// Returns the raw embedded display HTML, no substitutions applied.
    /// </summary>
    public static string LoadRawHtml() => ReadEmbeddedString(HtmlResourceName);

    /// <summary>
    /// Returns the embedded default JSON payload used to seed the
    /// data slot on first creation (every <c>usedPct</c> is 0, ghost
    /// shows the "cool" idle mood). Used in the wizard so the display
    /// renders something sensible the moment the user finishes setup,
    /// before the first live ping cycle lands.
    /// </summary>
    public static string LoadDefaultSlotJson() => ReadEmbeddedString(DefaultJsonResource);

    /// <summary>
    /// Returns display.html with the slot read URL substituted in.
    /// </summary>
    /// <param name="slotReadUrl">
    /// Full public URL of the data slot, e.g.
    /// <c>https://content.agentview.de/data/u/rafael-2/token-usage.json</c>.
    /// Comes from the <c>readUrl</c> field returned by
    /// <c>PUT /api/v1/data/{slug}</c>.
    /// </param>
    public static string Build(string slotReadUrl)
    {
        if (string.IsNullOrWhiteSpace(slotReadUrl))
            throw new ArgumentException("slotReadUrl is required.", nameof(slotReadUrl));

        var html = LoadRawHtml();
        // Match the server-side placeholder shape exactly so the same
        // source file works for both publication paths.
        var placeholder = "{{slot:" + SlotPlaceholderKey + ".readUrl}}";
        return html.Replace(placeholder, slotReadUrl, StringComparison.Ordinal);
    }

    private static string ReadEmbeddedString(string name)
    {
        // Use typeof(...).Assembly so the resource is always found in the
        // defining assembly, even when called from test runners or other
        // assemblies that reference this project.
        var asm = typeof(DisplayHtmlBuilder).Assembly;
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{name}' not found. Resources actually present: " +
                string.Join(", ", asm.GetManifestResourceNames()));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
