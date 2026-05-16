using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;
using AgentView.TokenCounter.Views;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace AgentView.TokenCounter.UI;

/// <summary>
/// Owns the tray icon (H.NotifyIcon.Wpf), the polling timer, and a
/// single reusable <see cref="MainWindow"/>. Left-clicking the tray
/// icon opens the Overview tab; right-click exposes the quick actions.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly ConfigStore        _configStore;
    private readonly PingService        _pingService;
    private readonly WebViewSession     _session;
    private readonly SetupCoordinator   _coordinator;
    private readonly DiagnosticsLog     _log;
    private readonly Application        _app;

    private readonly TaskbarIcon         _icon;
    private readonly System.Windows.Controls.ContextMenu _menu;
    private readonly Window              _iconHost;
    private readonly DispatcherTimer     _scheduleTimer;

    private System.Windows.Controls.MenuItem _statusItem    = null!;
    private System.Windows.Controls.MenuItem _pauseItem     = null!;
    private CancellationTokenSource          _runCts        = new();

    private MainWindow? _mainWindow;
    private AppConfig   _config;
    private PingResult  _last = new()
    {
        At      = DateTimeOffset.MinValue,
        Status  = PingStatus.Unknown,
        Message = "Never run.",
    };

    public TrayIconHost(
        ConfigStore configStore,
        AppConfig config,
        PingService pingService,
        WebViewSession session,
        SetupCoordinator coordinator,
        DiagnosticsLog log,
        Application app)
    {
        _configStore = configStore;
        _pingService = pingService;
        _session     = session;
        _coordinator = coordinator;
        _log         = log;
        _app         = app;

        // The single, app-wide AppConfig instance, created once at the
        // composition root and shared with the SetupCoordinator and
        // every tab. Never replace this reference (see the Saved /
        // InstallCompleted handlers) — mutate it in place so all
        // holders observe the same state.
        _config      = config;

        _menu = BuildMenu();
        _icon = new TaskbarIcon
        {
            ToolTipText        = "agentView Token Counter",
            Icon               = MakeIcon(IconState.Idle),
            ContextMenu        = _menu,
            NoLeftClickDelay   = true,
            Visibility         = Visibility.Visible,
        };
        _icon.LeftClickCommand    = new RelayCommand(_ => OpenMainWindow("Overview"));
        _icon.DoubleClickCommand  = new RelayCommand(_ => OpenMainWindow("Overview"));

        // The TaskbarIcon needs a visual tree before Loaded fires and
        // it registers with Shell_NotifyIcon. Park it inside an
        // off-screen invisible Window — show-then-hide gives us the
        // Loaded event without ever painting anything to the user.
        _iconHost = new Window
        {
            Title                 = "agentView Token Counter (tray host)",
            WindowStyle           = WindowStyle.None,
            ShowInTaskbar         = false,
            ShowActivated         = false,
            Width                 = 1,
            Height                = 1,
            Left                  = -32000,
            Top                   = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            AllowsTransparency    = true,
            Background            = System.Windows.Media.Brushes.Transparent,
            Content               = _icon,
        };
        _iconHost.Show();
        _iconHost.Hide();

        _scheduleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _scheduleTimer.Tick += OnTimerTick;
    }

    // ─── Lifecycle ──────────────────────────────────────────────────

    public void Start()
    {
        if (!_config.SetupComplete)
        {
            OpenMainWindow("Setup");
        }
        else
        {
            _ = WarmUpWebViewAsync();
        }
        _scheduleTimer.Start();
    }

    private async Task WarmUpWebViewAsync()
    {
        try { await _session.InitialiseAsync().ConfigureAwait(true); }
        catch (WebView2RuntimeMissingException ex)
        {
            _log.Error("WebView2 runtime missing on startup", ex);
            _icon.ShowNotification("agentView Token Counter",
                "Microsoft Edge WebView2 Runtime is not installed. Install the Evergreen Bootstrapper from https://developer.microsoft.com/microsoft-edge/webview2/.",
                NotificationIcon.Warning);
        }
        catch (Exception ex)
        {
            _log.Error("WebView2 warm-up failed", ex);
        }
    }

    public void Dispose()
    {
        _scheduleTimer.Stop();
        _runCts.Cancel();
        _runCts.Dispose();
        try { _mainWindow?.Close(); } catch { }
        try { _icon.Dispose();      } catch { }
        try { _iconHost.Close();    } catch { }
    }

    // ─── Main window plumbing ──────────────────────────────────────

    private void OpenMainWindow(string tab)
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(
                _configStore, _config, _session, _coordinator, _log);
            _mainWindow.Closed += (_, _) =>
            {
                _mainWindow = null;
            };
            // SettingsTab.OnSave/OnReset and SetupCoordinator.PublishAsync
            // mutate the shared _config in place before raising these
            // events, so it is already current — re-reading disk here
            // would only fork the graph again (the bug this design
            // avoids). Just kick a fresh sync with the new settings.
            _mainWindow.Settings.Saved += (_, _) => _ = SyncNowAsync();
            _mainWindow.Setup.InstallCompleted += (_, _) => _ = SyncNowAsync();
            _mainWindow.Overview.SyncRequested        += async (_, _) => await SyncNowAsync();
            _mainWindow.Overview.PauseToggleRequested += (_, _) => TogglePaused();
        }
        _mainWindow.ShowTab(tab);
        PushLastIntoTabs();
    }

    /// <summary>
    /// Hands the most recent ping result to every tab in the main
    /// window that cares. Setup uses it to derive the "published"
    /// indicator (a successful sync proves the pipeline works);
    /// Overview uses it to render the live bucket bars; the MainWindow
    /// title band uses it for the global status pill that mirrors the
    /// tray icon.
    /// </summary>
    private void PushLastIntoTabs()
    {
        if (_mainWindow is null) return;
        _mainWindow.SetGlobalStatus(_last, _config.Paused);
        _mainWindow.Overview.UpdateFromResult(_last, _config.Paused);
        _mainWindow.Setup.UpdateFromSyncResult(_last);
    }

    // ─── Tray context menu (right-click) ───────────────────────────

    private System.Windows.Controls.ContextMenu BuildMenu()
    {
        var m = new System.Windows.Controls.ContextMenu();

        _statusItem = new System.Windows.Controls.MenuItem
        {
            Header     = "Waiting for first sync…",
            IsEnabled  = false,
            FontWeight = FontWeights.SemiBold,
        };
        m.Items.Add(_statusItem);
        m.Items.Add(new Separator());

        var open = new System.Windows.Controls.MenuItem { Header = "Open Token Counter" };
        open.Click += (_, _) => OpenMainWindow("Overview");
        m.Items.Add(open);

        var sync = new System.Windows.Controls.MenuItem { Header = "Sync now" };
        sync.Click += async (_, _) => await SyncNowAsync();
        m.Items.Add(sync);

        _pauseItem = new System.Windows.Controls.MenuItem { Header = "Pause" };
        _pauseItem.Click += (_, _) => TogglePaused();
        m.Items.Add(_pauseItem);

        m.Items.Add(new Separator());

        var exit = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exit.Click += (_, _) => _app.Shutdown();
        m.Items.Add(exit);

        m.Opened += (_, _) => RefreshMenu();
        return m;
    }

    private void RefreshMenu()
    {
        _statusItem.Header = StatusLine();
        _pauseItem.Header  = _config.Paused ? "Resume" : "Pause";
    }

    private string StatusLine() => _last.Status switch
    {
        PingStatus.Ok      => $"Last sync OK · {_last.At:HH:mm:ss} · {_last.BucketsPosted} bucket(s)",
        PingStatus.Failed  => $"Last sync FAILED · {Truncate(_last.Message, 60)}",
        PingStatus.Paused  => "Paused",
        _                  => "Waiting for first sync…",
    };

    // ─── Timer + ping cycle ────────────────────────────────────────

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        _scheduleTimer.Stop();
        try { await SyncNowAsync().ConfigureAwait(true); }
        finally
        {
            _scheduleTimer.Interval = TimeSpan.FromSeconds(Math.Max(15, _config.PollIntervalSeconds));
            _scheduleTimer.Start();
        }
    }

    private async Task SyncNowAsync()
    {
        _runCts.Cancel();
        _runCts.Dispose();
        _runCts = new CancellationTokenSource();

        SetIconState(IconState.Working);
        try { _last = await _pingService.RunOnceAsync(_config, _runCts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { return; }
        ApplyResult(_last);
    }

    private void ApplyResult(PingResult result)
    {
        SetIconState(result.Status switch
        {
            PingStatus.Ok     => IconState.Idle,
            PingStatus.Paused => IconState.Paused,
            _                 => IconState.Failed,
        });
        _icon.ToolTipText = TooltipText();
        PushLastIntoTabs();
    }

    private string TooltipText()
    {
        var statusShort = _last.Status switch
        {
            PingStatus.Ok     => $"OK {_last.At:HH:mm}",
            PingStatus.Failed => "FAILED",
            PingStatus.Paused => "Paused",
            _                 => "Waiting",
        };
        var buckets = _last.Slot?.Buckets;
        if (buckets is { Count: > 0 })
        {
            var pieces = buckets.Select(b => $"{ShortBucketLabel(b.Key, b.Label)} {b.UsedPct}%");
            return Truncate($"Token Counter · {statusShort} · {string.Join(" / ", pieces)}", 127);
        }
        return Truncate($"Token Counter · {StatusLine()}", 127);
    }

    private static string ShortBucketLabel(string key, string label) => key switch
    {
        "five_hour"        => "5h",
        "seven_day"        => "Wk",
        "seven_day_design" => "Dsg",
        "seven_day_opus"   => "Opus",
        "seven_day_sonnet" => "Sn",
        _                  => label.Length > 6 ? label[..6] : label,
    };

    // ─── Actions ────────────────────────────────────────────────────

    private void TogglePaused()
    {
        _config.Paused = !_config.Paused;
        _configStore.Save(_config);
        if (_config.Paused)
        {
            _last = PingResult.Paused();
            ApplyResult(_last);
        }
        else
        {
            _ = SyncNowAsync();
        }
    }

    // ─── Icon drawing ──────────────────────────────────────────────
    //
    // We draw the Synth pixel mascot from the display.html SVG at
    // 2× scale (16×14 source → 32×28, centred in a 32-square canvas
    // with 2 px of vertical padding). Same coordinates as the SVG so
    // the tray ghost reads as the same character. State is encoded
    // through palette swaps — antenna colour and (in the failed
    // state) red pupils — rather than completely different icons.

    private enum IconState { Idle, Working, Failed, Paused }

    private enum Px { Outline, Body, Highlight, EyeWhite, Pupil, Antenna }

    private readonly record struct GhostPalette(
        Color Outline, Color Body, Color Highlight,
        Color EyeWhite, Color Pupil, Color Antenna);

    // (x, y, w, h) coordinates lifted from the SVG mascot in
    // Resources/display.html — keep them in sync if you tweak the art.
    // Drawing order matches the SVG so later rects (highlights, eyes,
    // mouth, skirt) layer correctly on top of the body fill.
    private static readonly (int x, int y, int w, int h, Px color)[] GhostPixels =
    {
        // Antenna tip — recoloured per state.
        (8, 0, 1, 1, Px.Antenna),

        // Head outline / ears (drawn before body so the body fill
        // covers their interior portion the same way the SVG does).
        (8,  1, 1, 1, Px.Outline),
        (5,  2, 6, 1, Px.Outline),
        (3,  3, 2, 1, Px.Outline),
        (11, 3, 2, 1, Px.Outline),
        (2,  4, 1, 1, Px.Outline),
        (13, 4, 1, 1, Px.Outline),
        (1,  5, 1, 1, Px.Outline),
        (14, 5, 1, 1, Px.Outline),
        (1,  6, 1, 6, Px.Outline),
        (14, 6, 1, 6, Px.Outline),

        // Body fill (cyan).
        (5, 3, 6,  1, Px.Body),
        (4, 4, 8,  1, Px.Body),
        (3, 5, 10, 1, Px.Body),
        (2, 6, 12, 6, Px.Body),

        // Body highlights (lighter cyan).
        (3, 5, 1, 1, Px.Highlight),
        (2, 6, 1, 1, Px.Highlight),
        (3, 6, 1, 1, Px.Highlight),

        // Eyes (white sclera + dark pupils).
        (3,  6, 3, 3, Px.EyeWhite),
        (10, 6, 3, 3, Px.EyeWhite),
        (4,  7, 1, 1, Px.Pupil),
        (11, 7, 1, 1, Px.Pupil),

        // Mouth.
        (7, 10, 2, 1, Px.Outline),
        (6,  9, 1, 1, Px.Outline),
        (9,  9, 1, 1, Px.Outline),

        // Skirt — three fringes with outlines.
        (2,  12, 3, 1, Px.Body),
        (2,  13, 3, 1, Px.Outline),
        (5,  12, 1, 1, Px.Outline),
        (6,  12, 4, 1, Px.Body),
        (6,  13, 4, 1, Px.Outline),
        (10, 12, 1, 1, Px.Outline),
        (11, 12, 3, 1, Px.Body),
        (11, 13, 3, 1, Px.Outline),
    };

    private static GhostPalette PaletteFor(IconState state) => state switch
    {
        // Idle: the "happy" plum-pixel default. Lime antenna says alive.
        IconState.Idle => new GhostPalette(
            Outline:   Color.FromArgb(0x2F, 0x5A, 0x75),
            Body:      Color.FromArgb(0x7D, 0xD6, 0xFF),
            Highlight: Color.FromArgb(0xB5, 0xEB, 0xFF),
            EyeWhite:  Color.FromArgb(0xF4, 0xEF, 0xE6),
            Pupil:     Color.FromArgb(0x1A, 0x13, 0x20),
            Antenna:   Color.FromArgb(0xC9, 0xD6, 0x67)),

        // Working: amber antenna pulses while a sync is in flight.
        IconState.Working => new GhostPalette(
            Outline:   Color.FromArgb(0x2F, 0x5A, 0x75),
            Body:      Color.FromArgb(0x7D, 0xD6, 0xFF),
            Highlight: Color.FromArgb(0xB5, 0xEB, 0xFF),
            EyeWhite:  Color.FromArgb(0xF4, 0xEF, 0xE6),
            Pupil:     Color.FromArgb(0x1A, 0x13, 0x20),
            Antenna:   Color.FromArgb(0xFF, 0xC7, 0x4A)),

        // Failed: red pupils + red antenna — visible "something is off".
        IconState.Failed => new GhostPalette(
            Outline:   Color.FromArgb(0x2F, 0x5A, 0x75),
            Body:      Color.FromArgb(0x7D, 0xD6, 0xFF),
            Highlight: Color.FromArgb(0xB5, 0xEB, 0xFF),
            EyeWhite:  Color.FromArgb(0xFF, 0xD7, 0xDC),
            Pupil:     Color.FromArgb(0xFF, 0x3D, 0x5A),
            Antenna:   Color.FromArgb(0xFF, 0x3D, 0x5A)),

        // Paused: full desaturation — clearly "off duty".
        IconState.Paused => new GhostPalette(
            Outline:   Color.FromArgb(0x55, 0x55, 0x55),
            Body:      Color.FromArgb(0xA0, 0xA0, 0xA0),
            Highlight: Color.FromArgb(0xC8, 0xC8, 0xC8),
            EyeWhite:  Color.FromArgb(0xE0, 0xE0, 0xE0),
            Pupil:     Color.FromArgb(0x20, 0x20, 0x20),
            Antenna:   Color.FromArgb(0x70, 0x70, 0x70)),

        _ => new GhostPalette(
            Color.DarkGray, Color.LightGray, Color.WhiteSmoke,
            Color.White,    Color.Black,     Color.Gray),
    };

    private void SetIconState(IconState state) => _icon.Icon = MakeIcon(state);

    private static Icon MakeIcon(IconState state)
    {
        var palette = PaletteFor(state);
        var bmp     = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            // Pixel-art look: no smoothing, snap to whole pixels.
            g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.None;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.Clear(Color.Transparent);

            const int scale   = 2;   // 16×14 source → 32×28 rendered
            const int yOffset = 2;   // centre vertically in 32-tall icon

            foreach (var (x, y, w, h, key) in GhostPixels)
            {
                var color = key switch
                {
                    Px.Outline   => palette.Outline,
                    Px.Body      => palette.Body,
                    Px.Highlight => palette.Highlight,
                    Px.EyeWhite  => palette.EyeWhite,
                    Px.Pupil     => palette.Pupil,
                    Px.Antenna   => palette.Antenna,
                    _            => Color.Magenta,
                };
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, x * scale, y * scale + yOffset, w * scale, h * scale);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}

internal sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _exec;
    public RelayCommand(Action<object?> exec) { _exec = exec; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)     => _exec(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
