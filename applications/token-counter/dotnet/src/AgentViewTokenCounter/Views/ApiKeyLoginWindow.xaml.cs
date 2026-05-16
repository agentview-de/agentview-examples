using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace AgentView.TokenCounter.Views;

/// <summary>
/// Modal alternative to the embedded-WebView2 agentView login.
/// The user mints a scoped <c>avk_…</c> key in the agentView
/// dashboard (instructions are spelled out in the modal body), pastes
/// it here, and the bridge pushes data with it directly. No browser
/// session is kept, no display is auto-provisioned — the operator
/// owns those decisions themselves.
/// </summary>
/// <remarks>
/// <para>
/// This intentionally collects only what the long-running ping loop
/// needs: the key, the slot slug, and an optional group ID. Display
/// creation, HTML push and pair-by-code all rely on cookie-auth
/// endpoints and stay parked behind the WebView path; the API-key
/// path assumes the user has set those up in the dashboard.
/// </para>
/// <para>
/// The window stays oblivious to <c>AppConfig</c> / <c>ConfigStore</c>
/// — the caller (SetupTab) reads the result properties and decides
/// what to persist. Keeps the modal reusable and testable.
/// </para>
/// </remarks>
public partial class ApiKeyLoginWindow : Window
{
    /// <summary>True only when <see cref="OnSaveClicked"/> committed valid values.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Raw <c>avk_…</c> key the user pasted, trimmed.</summary>
    public string ApiKey { get; private set; } = "";

    /// <summary>Slot slug to write into.</summary>
    public string SlotSlug { get; private set; } = "";

    /// <summary>Optional group ID; null if the user left it blank.</summary>
    public string? GroupId { get; private set; }

    /// <summary>
    /// Which subset of the wizard the supplied key is expected to
    /// cover. <see cref="ApiKeyMode.FullSetup"/> means the bridge will
    /// keep using the API key for display listing / creation / HTML
    /// push / pairing — same end-to-end provisioning as the cookie
    /// flow. <see cref="ApiKeyMode.SlotOnly"/> means the bridge only
    /// writes to <see cref="SlotSlug"/>; display + HTML are the
    /// operator's responsibility.
    /// </summary>
    public ApiKeyMode Mode { get; private set; } = ApiKeyMode.FullSetup;

    public ApiKeyLoginWindow()
    {
        InitializeComponent();

        // Default slot slug. Set here, not in XAML — see SlugBox in
        // the .xaml for why (TextChanged fires before peer fields
        // exist).
        SlugBox.Text = "claude-usage";

        Loaded += (_, _) => ApiKeyBox.Focus();
    }

    /// <summary>
    /// Enables / disables Save based on a minimal sanity check: a key
    /// that at least looks like an <c>avk_</c> string plus a non-blank
    /// slot slug. We do NOT round-trip to the server for validation;
    /// the next sync cycle surfaces a 401 if the key is wrong, which
    /// is easier to debug than a synchronous probe that races with
    /// the WebView gate.
    /// </summary>
    private void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        // Field-change events can fire during InitializeComponent
        // (when XAML parser sets a default Text on a TextBox before
        // its sibling fields exist). Bail out until the visual tree
        // is wired up.
        if (ApiKeyBox is null || SlugBox is null || SaveButton is null) return;

        var key  = (ApiKeyBox.Password ?? "").Trim();
        var slug = (SlugBox.Text ?? "").Trim();
        SaveButton.IsEnabled =
            key.StartsWith("avk_", StringComparison.Ordinal) &&
            key.Length >= 10 &&
            slug.Length > 0;

        // Clear any prior error as soon as the user edits — premium
        // UX move: don't pin a red message to a field they are
        // already trying to correct.
        if (ErrorText is not null && ErrorText.Visibility == Visibility.Visible)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";
        }
    }

    private void OnFieldChanged(object sender, TextChangedEventArgs e)
        => OnFieldChanged(sender, (RoutedEventArgs)e);

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var key  = (ApiKeyBox.Password ?? "").Trim();
        var slug = (SlugBox.Text     ?? "").Trim();
        var grp  = (GroupBox.Text    ?? "").Trim();

        if (!key.StartsWith("avk_", StringComparison.Ordinal))
        {
            ShowError("That doesn't look like an agentView API key. They start with \"avk_\".");
            ApiKeyBox.Focus();
            return;
        }
        if (slug.Length == 0)
        {
            ShowError("A slot slug is required so the bridge knows where to write.");
            SlugBox.Focus();
            return;
        }

        ApiKey   = key;
        SlotSlug = slug;
        GroupId  = grp.Length == 0 ? null : grp;
        Mode     = ModeSlotOnly.IsChecked == true ? ApiKeyMode.SlotOnly : ApiKeyMode.FullSetup;
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Scheme-allow-listed launch (see ExternalLink). If it can't
        // open, the URL is still visible as text in the modal body.
        Services.ExternalLink.OpenInBrowser(e.Uri?.AbsoluteUri);
        e.Handled = true;
    }
}

/// <summary>
/// How much of the provisioning flow the pasted API key is expected
/// to cover.
/// </summary>
public enum ApiKeyMode
{
    /// <summary>Key has display.* capabilities — the bridge runs the
    /// full wizard (list / create displays, send HTML, pair).</summary>
    FullSetup,

    /// <summary>Key only carries slot.write — the bridge just pushes
    /// data; the operator wires up the display in the dashboard.</summary>
    SlotOnly,
}
