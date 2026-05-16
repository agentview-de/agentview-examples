using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;

namespace AgentView.TokenCounter.Views;

/// <summary>
/// "Settings" tab. Two-column layout:
/// <list type="bullet">
///   <item><description>Left: Connection card (URL / slot / group / API key with
///   inline Clear button) + Advanced card (open config folder, reset).</description></item>
///   <item><description>Right: Behavior card (sync-interval stepper +
///   model-split toggle + auto-start toggle).</description></item>
/// </list>
/// Persistent save bar at the bottom shows
/// <c>● Unsaved changes</c> while edits are pending.
/// </summary>
public partial class SettingsTab : UserControl
{
    private const int IntervalMin  = 30;
    private const int IntervalMax  = 3600;
    private const int IntervalStep = 30;

    private readonly ConfigStore _store;
    private readonly AppConfig   _config;
    private bool                 _suppressDirty;

    /// <summary>Raised when the user clicks Save and the config has been written.</summary>
    public event EventHandler? Saved;

    public SettingsTab(ConfigStore store, AppConfig config)
    {
        InitializeComponent();
        _store  = store;
        _config = config;
        Populate();
    }

    private void Populate()
    {
        _suppressDirty = true;
        try
        {
            BaseUrlBox.Text  = _config.AgentViewBaseUrl;
            SlugBox.Text     = _config.AgentViewSlotSlug;
            GroupIdBox.Text  = _config.AgentViewGroupId ?? "";
            ApiKeyBox.Password = _config.AgentViewApiKey ?? "";
            IntervalBox.Text = _config.PollIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            IncludeSplitsBox.IsChecked = _config.IncludeModelSplits;
            AutoStartBox.IsChecked     = _config.AutoStart;
        }
        finally { _suppressDirty = false; }
        SetDirty(false);
    }

    private void OnAnyFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressDirty) return;
        SetDirty(true);
    }

    private void OnAnyFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressDirty) return;
        SetDirty(true);
    }

    private void SetDirty(bool dirty)
    {
        UnsavedHint.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = dirty;
        RevertButton.IsEnabled = dirty;
    }

    private void OnIntervalDecrement(object sender, RoutedEventArgs e)
        => BumpInterval(-IntervalStep);

    private void OnIntervalIncrement(object sender, RoutedEventArgs e)
        => BumpInterval(IntervalStep);

    private void BumpInterval(int delta)
    {
        if (!int.TryParse(IntervalBox.Text.Trim(), out var current))
            current = _config.PollIntervalSeconds;
        var next = Math.Clamp(current + delta, IntervalMin, IntervalMax);
        if (next == current) return;
        IntervalBox.Text = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetDirty(true);
    }

    private void OnRevertClicked(object sender, RoutedEventArgs e) => Populate();

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text.Trim(), out var interval)
            || interval < IntervalMin || interval > IntervalMax)
        {
            MessageBox.Show(Window.GetWindow(this)!,
                $"Poll interval must be a whole number between {IntervalMin} and {IntervalMax} seconds.",
                "Invalid interval", MessageBoxButton.OK, MessageBoxImage.Warning);
            IntervalBox.Focus();
            return;
        }

        // The API key travels as a plaintext X-API-Key header on every
        // slot write. Refuse a non-HTTPS base URL so the key can never
        // be put on the wire in the clear (also stops a tampered
        // config.json from silently downgrading the channel).
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text)
            ? "https://agentview.de"
            : BaseUrlBox.Text.Trim();
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(Window.GetWindow(this)!,
                "The agentView server URL must start with https://. " +
                "The API key is sent on every request and must not travel over plain HTTP.",
                "Insecure server URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            BaseUrlBox.Focus();
            return;
        }

        _config.AgentViewBaseUrl    = baseUrl;
        _config.AgentViewSlotSlug   = string.IsNullOrWhiteSpace(SlugBox.Text)     ? "claude-usage"         : SlugBox.Text.Trim();
        _config.AgentViewGroupId    = string.IsNullOrWhiteSpace(GroupIdBox.Text)  ? null                   : GroupIdBox.Text.Trim();
        _config.AgentViewApiKey     = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null                 : ApiKeyBox.Password.Trim();
        _config.PollIntervalSeconds = interval;
        _config.IncludeModelSplits  = IncludeSplitsBox.IsChecked == true;

        var autoStartChanged = _config.AutoStart != (AutoStartBox.IsChecked == true);
        _config.AutoStart    = AutoStartBox.IsChecked == true;
        _store.Save(_config);

        if (autoStartChanged)
        {
            try
            {
                if (_config.AutoStart) AutoStartManager.Enable(Environment.ProcessPath!);
                else                   AutoStartManager.Disable();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this)!,
                    "Auto-start registration failed: " + ex.Message,
                    "Auto-start", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        SetDirty(false);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears the stored API key. The user has to open Setup → Re-publish
    /// to mint a fresh scoped key — there is no in-place "rotate" on the
    /// agentView API key endpoint (it creates new keys), so we don't
    /// pretend to do one here.
    /// </summary>
    private void OnClearApiKeyClicked(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            "Clear the stored API key?\n\n" +
            "The bridge will stop pushing data until you open the Setup " +
            "tab and click Re-publish, which mints a new scoped key.",
            "Clear API key",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        ApiKeyBox.Password = "";
        SetDirty(true);
    }

    /// <summary>
    /// Opens the per-user config + log folder in Explorer.
    /// </summary>
    private void OnOpenLogsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            // Deliberately NOT routed through ExternalLink: this opens
            // a folder, not a web link. The path is composed purely
            // from Environment.GetFolderPath + a constant — no
            // user/attacker-controlled segment — so shell-executing the
            // directory is safe and the http(s) allow-list would
            // (correctly) reject a filesystem path.
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "agentView-token-counter");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName        = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this)!,
                "Could not open the config folder: " + ex.Message,
                "Open logs", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Wipes the published-display state so the user can re-run the
    /// Setup wizard from scratch. WebView cookies stay so they don't
    /// have to log in again.
    /// </summary>
    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            "Reset configuration?\n\n" +
            "This clears the configured display, slot, API key and " +
            "marks Setup as incomplete. Your claude.ai + agentView " +
            "sessions in the WebView are kept so you don't have to " +
            "sign in again.",
            "Reset Token Counter",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        _config.ClaudeOrgId          = null;
        _config.AgentViewDisplayId   = null;
        _config.AgentViewDisplayName = null;
        _config.AgentViewSlotSlug    = "claude-usage";
        _config.AgentViewApiKey      = null;
        _config.AgentViewGroupId     = null;
        _config.SetupComplete        = false;
        _store.Save(_config);
        Populate();
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
