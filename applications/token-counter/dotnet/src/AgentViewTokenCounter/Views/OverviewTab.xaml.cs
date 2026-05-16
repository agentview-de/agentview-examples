using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.ViewModels;

namespace AgentView.TokenCounter.Views;

/// <summary>
/// "Overview" tab. Implements the designer's mock-up layout:
/// <list type="bullet">
///   <item>Sync strip — live status + Sync now / Pause.</item>
///   <item>Featured 5-hour card (2-of-3 columns) — big percentage
///   on the left, wide bar + RESETS IN / USED / REMAINING KV column
///   on the right.</item>
///   <item>Weekly · all models card (column 3) — single-percentage
///   card with severity bar.</item>
///   <item>Up to three small bucket cards in the row below.</item>
/// </list>
/// </summary>
/// <remarks>
/// This code-behind is a thin shell. All formatting logic lives in
/// <see cref="BucketRowViewModel"/>; all visual structure is declared
/// in the DataTemplates in <c>OverviewTab.xaml</c>. The code-behind
/// only assigns view-model instances to the named
/// <see cref="ContentControl"/> slots and updates the sync-strip
/// headline. This is the idiomatic WPF pattern: XAML owns layout,
/// code-behind owns state mapping.
/// </remarks>
public partial class OverviewTab : UserControl
{
    private readonly AppConfig _config;

    public event EventHandler? SyncRequested;
    public event EventHandler? PauseToggleRequested;

    public OverviewTab(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        Refresh(null);
    }

    private void OnSyncClicked(object sender, RoutedEventArgs e)
        => SyncRequested?.Invoke(this, EventArgs.Empty);

    private void OnPauseClicked(object sender, RoutedEventArgs e)
        => PauseToggleRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Push a fresh ping result into the view. Called by the tray host
    /// after every sync cycle and after pause toggles.
    /// </summary>
    public void UpdateFromResult(PingResult result, bool paused)
    {
        Refresh(result);
        PauseButton.Content = paused ? "Resume" : "Pause";
    }

    private void Refresh(PingResult? result)
    {
        // ── Sync strip headline ────────────────────────────────────
        var displayLabel = !string.IsNullOrEmpty(_config.AgentViewDisplayName)
            ? _config.AgentViewDisplayName
            : !string.IsNullOrEmpty(_config.AgentViewDisplayId)
                ? _config.AgentViewDisplayId
                : null;

        if (displayLabel is null)
        {
            DisplayLine.Text = "No display configured yet — open the Setup tab to publish one.";
        }
        else
        {
            DisplayLine.Text =
                $"Display {displayLabel} is online — pushing to {ExtractHost(_config.AgentViewBaseUrl)}";
        }

        if (result is null || result.Status == PingStatus.Unknown)
        {
            SetHeadline("Waiting for first sync…", BulletState.Pending);
        }
        else if (result.Status == PingStatus.Paused)
        {
            SetHeadline("Paused", BulletState.Pending);
        }
        else if (result.Status == PingStatus.Failed)
        {
            SetHeadline($"Last sync failed · {Truncate(result.Message, 80)}", BulletState.Danger);
        }
        else
        {
            SetHeadline(
                $"Synchronizing every {Math.Max(15, _config.PollIntervalSeconds)} s · last update {result.At:HH:mm}",
                BulletState.Success);
        }

        // ── Bucket grid ────────────────────────────────────────────
        var buckets = result?.Slot?.Buckets ?? new List<Bucket>();
        if (buckets.Count == 0)
        {
            EmptyCard.Visibility  = Visibility.Visible;
            BucketGrid.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyCard.Visibility  = Visibility.Collapsed;
        BucketGrid.Visibility = Visibility.Visible;

        // Assign view-model instances to the named ContentControl slots.
        // The DataTemplates in OverviewTab.xaml take it from here —
        // each template is bound to BucketRowViewModel properties so
        // every label, percentage, reset caption, and colour derives
        // from the view-model, not from C# UI construction code.
        FeaturedHost.Content  = buckets.Count >= 1
            ? new BucketRowViewModel(buckets[0], BucketCardKind.Featured)
            : null;
        SecondaryHost.Content = buckets.Count >= 2
            ? new BucketRowViewModel(buckets[1], BucketCardKind.Secondary)
            : null;
        SmallHost0.Content    = buckets.Count >= 3
            ? new BucketRowViewModel(buckets[2], BucketCardKind.Small)
            : null;
        SmallHost1.Content    = buckets.Count >= 4
            ? new BucketRowViewModel(buckets[3], BucketCardKind.Small)
            : null;
        SmallHost2.Content    = buckets.Count >= 5
            ? new BucketRowViewModel(buckets[4], BucketCardKind.Small)
            : null;
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static string ExtractHost(string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            return "agentview.de";
        }
        try
        {
            return new Uri(baseUrl).Host;
        }
        catch
        {
            return baseUrl;
        }
    }

    private enum BulletState { Pending, Success, Danger }

    private void SetHeadline(string text, BulletState state)
    {
        StatusHeadline.Text = text;
        var key = state switch
        {
            BulletState.Success => "StatusBullet.Success",
            BulletState.Danger  => "StatusBullet.Danger",
            _                   => "StatusBullet.Pending",
        };
        StatusBullet.SetResourceReference(Ellipse.StyleProperty, key);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
