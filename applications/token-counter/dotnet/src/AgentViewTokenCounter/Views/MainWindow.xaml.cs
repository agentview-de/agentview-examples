using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;

namespace AgentView.TokenCounter.Views;

/// <summary>
/// Main desktop window. One window, three tabs: Overview / Setup /
/// Settings. The same window is reused for every interaction so the
/// user has a single mental model instead of separate Setup and
/// Settings dialogs.
/// </summary>
public partial class MainWindow : Window
{
    public OverviewTab Overview { get; }
    public SetupTab    Setup    { get; }
    public SettingsTab Settings { get; }

    public MainWindow(
        ConfigStore store,
        AppConfig config,
        WebViewSession session,
        SetupCoordinator coordinator,
        DiagnosticsLog log)
    {
        InitializeComponent();

        Overview = new OverviewTab(config);
        Setup    = new SetupTab(config, session, coordinator, log);
        Settings = new SettingsTab(store, config);

        OverviewHost.Content = Overview;
        SetupHost.Content    = Setup;
        SettingsHost.Content = Settings;

        // First-run convenience: if setup hasn't been done, land the
        // user directly on the Setup tab instead of the empty Overview.
        if (!config.SetupComplete)
        {
            Tabs.SelectedItem = SetupTabItem;
        }

        // When Setup completes, automatically switch to Overview so
        // the user sees the published display + fresh data instead of
        // staying on a stale Setup form.
        Setup.InstallCompleted += (_, _) => Tabs.SelectedItem = OverviewTabItem;

        // Settings holds a snapshot of the shared config in its input
        // fields. A Re-publish (new API key), sign-out or reset done on
        // another tab mutates that shared config without Settings
        // knowing. Re-read it whenever the user lands on Settings so
        // the form never shows a stale key. The TabControl's
        // SelectionChanged also bubbles up from inner ComboBoxes — the
        // e.Source guard ignores those so we only react to real tab
        // switches.
        Tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source is not TabControl) return;
            if (ReferenceEquals(Tabs.SelectedItem, SettingsTabItem))
            {
                Settings.ReloadFromConfig();
            }
        };

        // Keep the title-band email in sync with whatever the Setup
        // tab discovers via /auth/me.
        Setup.UserEmailUpdated += (_, email) => SetUserEmail(email);

        // Seed the email from cached config so the title band has it
        // immediately on a returning launch — no need to wait for the
        // first Setup-tab refresh.
        SetUserEmail(config.AgentViewUserEmail);
    }

    /// <summary>Brings the window to front and selects the named tab.</summary>
    public void ShowTab(string tabName)
    {
        Tabs.SelectedItem = tabName switch
        {
            "Setup"    => SetupTabItem,
            "Settings" => SettingsTabItem,
            _          => OverviewTabItem,
        };
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    /// <summary>
    /// Updates the title-band status pill (bullet + label). Mirrors the
    /// tray icon's state so users see "is the bridge running?" without
    /// having to click into a tab first. Called by the tray host after
    /// every sync cycle and pause toggle.
    /// </summary>
    public void SetGlobalStatus(PingResult result, bool paused)
    {
        var (label, state) = (result.Status, paused) switch
        {
            (PingStatus.Paused, _)   => ("Paused",                                       GlobalStatusState.Pending),
            (_, true)                => ("Paused",                                       GlobalStatusState.Pending),
            (PingStatus.Ok, _)       => ($"Syncing · last update {result.At:HH:mm}",     GlobalStatusState.Success),
            (PingStatus.Failed, _)   => ("Last sync failed",                             GlobalStatusState.Danger),
            _                        => ("Waiting for first sync…",                     GlobalStatusState.Pending),
        };
        GlobalStatusText.Text = label;
        var styleKey = state switch
        {
            GlobalStatusState.Success => "StatusBullet.Success",
            GlobalStatusState.Danger  => "StatusBullet.Danger",
            _                         => "StatusBullet.Pending",
        };
        GlobalStatusBullet.SetResourceReference(Ellipse.StyleProperty, styleKey);
    }

    private enum GlobalStatusState { Pending, Success, Danger }

    /// <summary>
    /// Sets / clears the user email shown in the title band. When
    /// <paramref name="email"/> is null or whitespace the separator
    /// and email collapse so the layout shifts cleanly to "status only".
    /// </summary>
    public void SetUserEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            GlobalSeparator.Visibility = Visibility.Collapsed;
            GlobalUserEmail.Visibility = Visibility.Collapsed;
            GlobalUserEmail.Text = "";
        }
        else
        {
            GlobalUserEmail.Text       = email;
            GlobalSeparator.Visibility = Visibility.Visible;
            GlobalUserEmail.Visibility = Visibility.Visible;
        }
    }
}
