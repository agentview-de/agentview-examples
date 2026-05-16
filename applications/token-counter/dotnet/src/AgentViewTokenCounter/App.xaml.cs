using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using AgentView.TokenCounter.Services;
using AgentView.TokenCounter.UI;

namespace AgentView.TokenCounter;

/// <summary>
/// WPF entry point — the composition root.
/// </summary>
/// <remarks>
/// <para>
/// All long-lived services (singletons for the lifetime of the process)
/// are constructed here by hand. There is deliberately no DI container:
/// the dependency graph fits on one screen, and explicit wiring is
/// easier for new contributors to follow than a framework registration
/// dance.
/// </para>
/// <para>
/// Ownership and disposal order on exit (reverse construction):
/// <c>TrayIconHost → WebViewSession → HttpClient → Mutex</c>.
/// </para>
/// </remarks>
// CA1001: Disposal of _http, _session, _tray happens in OnExit (WPF
// Application does not implement IDisposable; OnExit is the correct hook).
#pragma warning disable CA1001
public partial class App : Application
#pragma warning restore CA1001
{
    // Held as fields so OnExit can dispose in a deterministic order.
    private System.Threading.Mutex? _singleInstanceMutex;
    private HttpClient?             _http;
    private WebViewSession?         _session;
    private TrayIconHost?           _tray;
    private DiagnosticsLog?         _diagnostics;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Unhandled exception sinks ──────────────────────────────
        // Surface ANY unhandled exception to the diagnostics log so a
        // crash from a tray menu click doesn't silently kill the
        // process. We attach all three sinks WPF + .NET can route
        // exceptions through.
        DispatcherUnhandledException              += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException      += OnUnobservedTaskException;

        // ── Single-instance guard ──────────────────────────────────
        // Re-launching the .exe while one is already running should not
        // spawn a second tray icon racing the first.
        _singleInstanceMutex = new System.Threading.Mutex(
            initiallyOwned: true,
            name:           "Global\\agentView.TokenCounter.SingleInstance",
            createdNew:     out var owns);
        if (!owns)
        {
            Shutdown();
            return;
        }

        // ── Singletons (created once, live for the app lifetime) ───

        // Shared HttpClient for the long-running agentView PUT loop.
        // The setup wizard talks to agentView through the WebView
        // (cookies); only the background ping loop uses this client
        // directly (with the avk_ API key).
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // Append-only diagnostics log at %APPDATA%\agentView-token-counter\diag.log.
        _diagnostics = new DiagnosticsLog();
        _diagnostics.Info("==== agentView Token Counter starting (WPF build) ====");

        // Config persistence (JSON + DPAPI for the API key).
        var configStore   = new ConfigStore();
        var initialConfig = configStore.Load();

        // WebView2 session: owns the embedded browser + cookie jar.
        // Lives for the entire app — keeps both claude.ai and agentView
        // sessions alive without re-authentication.
        _session = new WebViewSession(_diagnostics);

        // claude.ai / anthropic.com / agentview.de are trusted by
        // default; register the configured agentView host too so a
        // custom or self-hosted base URL passes the navigation fence.
        try
        {
            _session.TrustHost(new Uri(initialConfig.AgentViewBaseUrl).Host);
        }
        catch (UriFormatException)
        {
            // Malformed base URL: leave it to the Settings tab to fix;
            // the default trusted hosts still cover agentview.de.
        }

        // ── Service layer ──────────────────────────────────────────

        // Claude API client — routes calls through the WebView session.
        var claude = new ClaudeApiClient(_session, _diagnostics);

        // agentView wizard client — dual-transport (cookie via WebView,
        // or X-API-Key via HttpClient). Used only by SetupCoordinator.
        // See class-level doc on AgentViewApiClient vs AgentViewClient
        // for the rationale behind having two separate clients.
        var agentViewApiClient = new AgentViewApiClient(
            _session, _http, _diagnostics, initialConfig.AgentViewBaseUrl);

        // Setup coordinator — orchestrates the 3-step wizard without
        // holding any WPF types; keeps SetupTab.xaml.cs a thin shell.
        var coordinator = new SetupCoordinator(
            configStore, initialConfig, claude, agentViewApiClient,
            _diagnostics);

        // agentView slot writer — plain HttpClient + avk_ key. Used
        // exclusively by PingService for the background sync loop.
        var agentViewClient = new AgentViewClient(_http);

        // Ping service — stateless; called by the tray timer every
        // PollIntervalSeconds seconds.
        var pingService = new PingService(claude, agentViewClient, _diagnostics);

        // ── Tray host (owns the window and the timer) ──────────────
        _tray = new TrayIconHost(
            configStore, pingService, _session, coordinator, _diagnostics, this);
        _tray.Start();
    }

    // Re-entrancy / flood guard for the error dialog. A repeating
    // exception (e.g. a bad data binding firing once per item per
    // render) must never be able to bury the user under a stack of
    // modal dialogs. We show at most ONE dialog at a time and collapse
    // an identical message that recurs within a short window — every
    // occurrence is still written to the diagnostics log in full.
    private bool   _errorDialogOpen;
    private string _lastErrorMessage = "";
    private DateTime _lastErrorShownUtc = DateTime.MinValue;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Full exception (incl. stack trace) ALWAYS goes to the
        // diagnostics log. The user-facing dialog shows only the
        // message: a stack trace in a MessageBox is reconnaissance for
        // an attacker and noise for everyone else. "Open config + logs
        // folder" in Settings surfaces the full detail when needed.
        _diagnostics?.Error("UI dispatcher exception", e.Exception);

        // Mark handled regardless: a single bad click / binding glitch
        // must not tear down an otherwise healthy background sync loop.
        e.Handled = true;

        var message = e.Exception.Message;
        var now     = DateTime.UtcNow;

        // Suppress if a dialog is already up, or if this is the same
        // message we just showed within the last 5 seconds. Either way
        // it is already in the log.
        if (_errorDialogOpen ||
            (message == _lastErrorMessage &&
             (now - _lastErrorShownUtc) < TimeSpan.FromSeconds(5)))
        {
            return;
        }

        _errorDialogOpen   = true;
        _lastErrorMessage  = message;
        _lastErrorShownUtc = now;
        try
        {
            MessageBox.Show(
                message,
                "agentView Token Counter — Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* swallow — a failing MessageBox must not recurse */ }
        finally
        {
            _errorDialogOpen = false;
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _diagnostics?.Error("AppDomain unhandled exception", ex);
        }
        else
        {
            _diagnostics?.Warn("AppDomain unhandled exception (non-Exception object): " + e.ExceptionObject);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _diagnostics?.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose in reverse construction order so no service tries to
        // use a dependency that has already been torn down.
        try { _tray?.Dispose();    } catch { /* shutdown */ }
        try { _session?.Dispose(); } catch { /* shutdown */ }
        try { _http?.Dispose();    } catch { /* shutdown */ }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* not owned */ }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
