using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Owns a single app-scoped <see cref="WebView2"/> that hosts
/// long-lived authenticated sessions to one or more web origins
/// (currently <c>claude.ai</c> and <c>agentview.de</c>).
/// </summary>
/// <remarks>
/// <para>
/// The WebView lives on an invisible host <see cref="Window"/> for the
/// lifetime of the app. The login dialog re-parents the same WebView2
/// control temporarily so the user can interact with it, then hands
/// it back when the dialog closes — that way the cookie jar persists
/// across the entire app lifecycle.
/// </para>
/// <para>
/// All HTTP traffic to authenticated origins (claude.ai, agentview.de)
/// is routed through the WebView's JavaScript context via
/// <c>fetch(..., { credentials: 'include' })</c>. Cookies, redirects,
/// HTTP/2 upgrades and CSRF therefore behave exactly like a normal
/// logged-in browser tab. Critically, this sidesteps the Chromium
/// 127+ App-Bound Encryption problem that blocks reading cookies
/// from the user's system Chrome / Edge.
/// </para>
/// <para>
/// Cookies and other browser state are persisted under
/// <c>%APPDATA%\agentView-token-counter\WebView2</c>, isolated from
/// the user's system browser.
/// </para>
/// </remarks>
public sealed class WebViewSession : IDisposable
{
    private readonly DiagnosticsLog _log;
    private readonly string         _userDataFolder;
    private readonly Window         _hostWindow;
    private readonly WebView2       _webView;

    private CoreWebView2Environment? _environment;
    private bool                     _ready;
    private bool                     _disposed;

    // ── Navigation guard ───────────────────────────────────────────
    //
    // The embedded browser must never be steered to an origin we did
    // not intend. Two threat models, handled differently:
    //
    //   * BACKGROUND mode (default): the WebView is off-screen and
    //     driven only by our own code (origin hops + JS fetch). There
    //     is no legitimate reason to ever land anywhere except a
    //     trusted first-party host over HTTPS, so anything else is a
    //     hostile redirect and is cancelled.
    //
    //   * INTERACTIVE-LOGIN mode: the user is deliberately signing in
    //     and the dialog is visible. Federated sign-in ("Continue with
    //     Google / Apple / Microsoft", magic-link domains, …) legiti-
    //     mately redirects off claude.ai, so a hard allowlist here
    //     would break login. We log but allow navigation; popups are
    //     still suppressed unconditionally.
    //
    // Trusted hosts are matched by suffix so "claude.ai" also covers
    // "auth.claude.ai" etc. The configured agentView host is added at
    // startup via TrustHost so custom / self-hosted instances work.
    private readonly HashSet<string> _trustedHostSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude.ai",
        "anthropic.com",
        "agentview.de",
    };

    /// <summary>
    /// When true the navigation guard permits any navigation (the user
    /// is interactively signing in and federated-IdP redirects are
    /// expected). Toggled by <see cref="Views.LoginWindow"/> around the
    /// sign-in dialog. Popups are blocked in both modes.
    /// </summary>
    internal bool InteractiveLoginActive { get; set; }

    // Serializes all WebView operations. There is only ONE WebView2
    // control in the app, but several callers now use it in parallel
    // — the background poll timer, the LoginWindow's IsLoggedIn probe
    // that runs every 1.5 s during sign-in, and the Setup tab's
    // refresh-state path. Without serialization they race on
    // navigation: a fetch starts a navigation to claude.ai, another
    // caller yanks it back to agentview.de mid-flight, and the first
    // fetch times out with "Failed to fetch".
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Bumped on every real top-level navigation (see OnNavigationStarting).
    // FetchUnlockedAsync snapshots it right after injecting the fetch:
    // if it changes while we are still polling, the document that holds
    // our `window.__avFetch__` promise has been replaced (classic case:
    // the user completes an interactive login and the page redirects
    // login.html → dashboard.html). Without this the poll loop would
    // spin the full 20 s against a dead global before timing out — a
    // visible stall during sign-in. Comparing the WebView's Source
    // string instead would be fragile: SPA hash changes
    // (dashboard.html → dashboard.html#/) are NOT document loads and
    // must NOT count, whereas NavigationStarting fires only for real
    // document-replacing navigations — exactly the signal we want.
    private volatile int _navGeneration;

    public WebViewSession(DiagnosticsLog log)
    {
        _log = log;
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "agentView-token-counter",
            "WebView2");
        Directory.CreateDirectory(_userDataFolder);

        // Hidden host window keeps a handle around so the WebView2
        // control can render off-screen for background fetches. We
        // explicitly avoid Visibility="Hidden" (which would prevent the
        // handle from being created) — instead we move the window off
        // the available virtual screen so it never paints.
        _hostWindow = new Window
        {
            Title                 = "agentView Token Counter (WebView host)",
            WindowStyle           = WindowStyle.None,
            ShowInTaskbar         = false,
            Width                 = 1,
            Height                = 1,
            Left                  = -32000,
            Top                   = -32000,
            ShowActivated         = false,
            AllowsTransparency    = false,
            Topmost               = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        _webView = new WebView2();
        _hostWindow.Content = _webView;
    }

    /// <summary>
    /// The WebView2 control. The login window parents it temporarily
    /// to make it visible during the login step.
    /// </summary>
    public WebView2 Control => _webView;

    /// <summary>Pre-initialises the WebView environment.</summary>
    public async Task InitialiseAsync()
    {
        if (_ready) return;

        // We must show the host window briefly so the WebView2 control
        // gets a real handle. Show -> Hide is the canonical pattern.
        if (!_hostWindow.IsVisible)
        {
            _hostWindow.Show();
            _hostWindow.Hide();
        }

        try
        {
            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder:          _userDataFolder,
                options:                 new CoreWebView2EnvironmentOptions
                {
                    Language = "en-US",
                }).ConfigureAwait(true);

            await _webView.EnsureCoreWebView2Async(_environment).ConfigureAwait(true);

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled            = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled            = false;

            // Fence the embedded browser: no hostile redirect in the
            // background, no uncontrolled popups ever. See the field
            // comments on _trustedHostSuffixes / InteractiveLoginActive.
            _webView.CoreWebView2.NavigationStarting  += OnNavigationStarting;
            _webView.CoreWebView2.NewWindowRequested  += OnNewWindowRequested;

            _ready = true;
            _log.Info($"WebView2 ready, user-data folder: {_userDataFolder}");
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            _log.Error("WebView2 Runtime is not installed.", ex);
            throw new WebView2RuntimeMissingException(
                "The Microsoft Edge WebView2 Runtime is not installed. Download the Evergreen Bootstrapper from https://developer.microsoft.com/microsoft-edge/webview2/ and run it.", ex);
        }
        catch (Exception ex)
        {
            _log.Error("WebView2 initialisation failed.", ex);
            throw;
        }
    }

    /// <summary>
    /// Adds a host (e.g. a custom / self-hosted agentView instance) to
    /// the navigation allowlist. Matched by suffix, so passing
    /// <c>"display.example.com"</c> trusts that host and its subdomains.
    /// Call before the first background navigation.
    /// </summary>
    public void TrustHost(string host)
    {
        if (!string.IsNullOrWhiteSpace(host))
        {
            _trustedHostSuffixes.Add(host.Trim());
        }
    }

    private bool IsTrustedHost(string host) =>
        _trustedHostSuffixes.Any(suffix =>
            host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Navigation fence. In background mode only HTTPS navigations to a
    /// trusted host are allowed; everything else is cancelled and
    /// logged. In interactive-login mode the user drives the browser
    /// (federated IdP redirects expected) so navigation is permitted
    /// but still logged.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Internal scheme used by the control itself between hops.
        if (string.IsNullOrEmpty(e.Uri) ||
            e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A real document-replacing navigation is starting. Bump the
        // generation BEFORE the fence's mode branches return, so an
        // in-flight FetchUnlockedAsync abandons its now-dead promise
        // fast — this must count interactive-login redirects too, which
        // are exactly the ones that orphaned the fetch.
        unchecked { _navGeneration++; }

        if (InteractiveLoginActive)
        {
            // User-driven sign-in: SSO bounces off-origin legitimately.
            return;
        }

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsTrustedHost(uri.Host))
        {
            _log.Warn($"Navigation BLOCKED (background mode): {e.Uri}");
            e.Cancel = true;
        }
    }

    /// <summary>
    /// Popups are never legitimate here — the app drives a single
    /// control. Suppress unconditionally; if the popup targeted a
    /// trusted host (e.g. a "open in new tab" login link) redirect the
    /// main control there instead so interactive sign-in still works.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (InteractiveLoginActive &&
            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            IsTrustedHost(uri.Host))
        {
            _webView.CoreWebView2.Navigate(e.Uri);
        }
        else
        {
            _log.Warn($"Popup BLOCKED: {e.Uri}");
        }
    }

    /// <summary>Navigates the WebView to <paramref name="url"/>.</summary>
    public void Navigate(string url)
    {
        if (!_ready) throw new InvalidOperationException("Call InitialiseAsync() first.");
        _webView.CoreWebView2.Navigate(url);
    }

    /// <summary>Returns the current URL the WebView is on.</summary>
    public string CurrentUrl => _webView.CoreWebView2?.Source ?? "";

    /// <summary>
    /// Logs the session out of a single origin by deleting every cookie
    /// the WebView would send to it (this includes parent-domain cookies
    /// such as <c>.claude.ai</c>). Per-origin on purpose: signing out of
    /// Claude must not also drop the agentView session, and vice versa.
    /// Gated by the same semaphore as fetch/navigation so it can't race
    /// an in-flight request.
    /// </summary>
    /// <param name="origin">e.g. <c>https://claude.ai</c>.</param>
    public async Task ClearCookiesForOriginAsync(string origin)
    {
        if (!_ready)
        {
            // Nothing to clear if the browser was never initialised.
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            var mgr     = _webView.CoreWebView2.CookieManager;
            var cookies = await mgr.GetCookiesAsync(origin).ConfigureAwait(true);
            foreach (var c in cookies)
            {
                mgr.DeleteCookie(c);
            }
            _log.Info($"Cleared {cookies.Count} cookie(s) for {origin}");
        }
        catch (Exception ex)
        {
            // Best-effort: a logout that fails must not crash the app.
            _log.Warn($"Cookie clear for {origin} failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs an arbitrary <c>fetch(url, options)</c> inside the WebView
    /// and returns the response body as a string. The fetch carries
    /// the WebView's cookies for the target origin (via
    /// <c>credentials: 'include'</c>).
    /// </summary>
    /// <param name="absoluteUrl">Full URL to fetch.</param>
    /// <param name="method">HTTP method (GET, POST, PUT, ...).</param>
    /// <param name="bodyJson">Optional JSON body for POST/PUT.</param>
    /// <param name="acceptOrigin">If set, ensure the WebView is on
    /// this origin before fetching. Required when the target API
    /// needs same-site cookies and the WebView is currently somewhere
    /// else (e.g. login redirect).</param>
    public async Task<HttpFetchResult> FetchAsync(
        string absoluteUrl,
        string method            = "GET",
        string? bodyJson         = null,
        string? acceptOrigin     = null)
    {
        // Single gate covers the entire fetch — including any
        // navigation triggered by EnsureOrigin — so two concurrent
        // callers don't race each other to a different origin.
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!_ready) await InitialiseAsync().ConfigureAwait(true);

            if (!string.IsNullOrEmpty(acceptOrigin))
            {
                await EnsureOriginUnlockedAsync(acceptOrigin).ConfigureAwait(true);
            }

            return await FetchUnlockedAsync(absoluteUrl, method, bodyJson).ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Fetch helper that assumes the caller already owns the gate.
    /// Splitting this out keeps FetchAsync's lock-grab in one place
    /// and lets EnsureOriginAsync stay independently lockable.
    /// </summary>
    private async Task<HttpFetchResult> FetchUnlockedAsync(
        string absoluteUrl, string method, string? bodyJson)
    {
        var current = _webView.CoreWebView2.Source ?? "(none)";
        _log.Info($"Fetch [{method}] {absoluteUrl} (WebView on: {current})");

        // SECURITY NOTES for anyone copying this fetch-bridge pattern:
        //
        //  1. Injection: every value spliced into the script below
        //     (method, url, body) goes through JsonSerializer.Serialize
        //     first, so it lands as a proper JS string literal and
        //     cannot break out into executable code. Never string-
        //     concatenate raw values into ExecuteScriptAsync.
        //
        //  2. The response is parked on window.__avFetch__ for up to
        //     ~20 s and is readable by any other script on the page.
        //     That is acceptable HERE because: the page is a trusted
        //     first-party origin (navigation fence above), DevTools and
        //     the context menu are disabled, and we inject no user/
        //     extension scripts. If you enable extensions, user
        //     scripts, or untrusted iframes you MUST switch to a
        //     non-guessable property name + a postMessage channel, or
        //     this becomes a same-page exfiltration sink.
        var initObjectLiteral = bodyJson is null
            ? $"{{ method: {JsonSerializer.Serialize(method)}, credentials: 'include', headers: {{ 'Accept': 'application/json' }} }}"
            : $"{{ method: {JsonSerializer.Serialize(method)}, credentials: 'include', headers: {{ 'Accept': 'application/json', 'Content-Type': 'application/json' }}, body: {JsonSerializer.Serialize(bodyJson)} }}";

        var kickoff = $@"
(() => {{
  window.__avFetch__ = null;
  fetch({JsonSerializer.Serialize(absoluteUrl)}, {initObjectLiteral})
  .then(async r => {{
    const text = await r.text();
    window.__avFetch__ = JSON.stringify({{ ok: r.ok, status: r.status, body: text }});
  }})
  .catch(e => {{
    window.__avFetch__ = JSON.stringify({{ ok: false, status: -1, body: String(e) }});
  }});
  return 'kicked';
}})();
";
        await _webView.CoreWebView2.ExecuteScriptAsync(kickoff).ConfigureAwait(true);

        // Snapshot the navigation generation now that the fetch promise
        // lives in *this* document. If it changes mid-poll the document
        // (and our window.__avFetch__) is gone — see field comment.
        var genAtKickoff = _navGeneration;

        // Poll. The Promise-await behaviour of ExecuteScriptWithResultAsync
        // depends on the runtime version; the polling pattern works on
        // every WebView2 version we ship for.
        for (int i = 0; i < 200; i++)
        {
            await Task.Delay(100).ConfigureAwait(true);
            var probe = await _webView.CoreWebView2
                .ExecuteScriptAsync("window.__avFetch__")
                .ConfigureAwait(true);

            if (probe == "null")
            {
                // No result yet. If the document was replaced under us
                // the promise can never resolve here — fail fast so the
                // caller (e.g. the IsLoggedIn probe) retries on the new
                // page within its normal cadence instead of stalling the
                // full 20 s.
                if (_navGeneration != genAtKickoff)
                {
                    _log.Info("  → fetch abandoned: document navigated away mid-request");
                    return new HttpFetchResult(false, -1, "navigated away during fetch");
                }
                continue;
            }

            string inner;
            try
            {
                inner = JsonSerializer.Deserialize<string>(probe) ?? "";
            }
            catch
            {
                continue;
            }
            if (string.IsNullOrEmpty(inner)) continue;

            using var doc = JsonDocument.Parse(inner);
            var ok     = doc.RootElement.GetProperty("ok").GetBoolean();
            var status = doc.RootElement.GetProperty("status").GetInt32();
            var body   = doc.RootElement.GetProperty("body").GetString() ?? "";
            _log.Info($"  → status={status}, ok={ok}, body_len={body.Length}");
            return new HttpFetchResult(ok, status, body);
        }
        throw new TimeoutException($"WebView fetch polling timed out after 20 s for {absoluteUrl}.");
    }

    /// <summary>
    /// Navigates the WebView to <paramref name="origin"/>/ if it is
    /// currently somewhere else. Required so the document origin
    /// matches the target of a same-site <c>fetch</c>.
    /// </summary>
    public async Task EnsureOriginAsync(string origin)
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try { await EnsureOriginUnlockedAsync(origin).ConfigureAwait(true); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Gate-less helper used by <see cref="FetchAsync"/>, which
    /// already owns the gate.
    /// </summary>
    private async Task EnsureOriginUnlockedAsync(string origin)
    {
        if (!_ready) await InitialiseAsync().ConfigureAwait(true);

        var current = _webView.CoreWebView2.Source ?? "";
        if (current.StartsWith(origin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _log.Info($"EnsureOrigin: WebView on \"{current}\", navigating to {origin}/...");

        var navTcs = new TaskCompletionSource<bool>();
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, e) =>
        {
            _webView.CoreWebView2.NavigationCompleted -= handler;
            navTcs.TrySetResult(e.IsSuccess);
        };
        _webView.CoreWebView2.NavigationCompleted += handler;
        _webView.CoreWebView2.Navigate(origin + "/");

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
        var completed   = await Task.WhenAny(navTcs.Task, timeoutTask).ConfigureAwait(true);
        if (completed == timeoutTask)
        {
            _webView.CoreWebView2.NavigationCompleted -= handler;
            throw new TimeoutException($"Navigation to {origin} timed out.");
        }
        _log.Info($"  navigated to: {_webView.CoreWebView2.Source}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _webView.Dispose();   } catch { }
        try { _hostWindow.Close();  } catch { }
    }
}

/// <summary>Result of a <see cref="WebViewSession.FetchAsync"/> call.</summary>
/// <param name="Ok">True when HTTP status is 2xx.</param>
/// <param name="Status">HTTP status code, or -1 on network error.</param>
/// <param name="Body">Response body as a string.</param>
public sealed record HttpFetchResult(bool Ok, int Status, string Body);

public sealed class WebView2RuntimeMissingException : Exception
{
    public WebView2RuntimeMissingException(string message, Exception inner)
        : base(message, inner) { }
}
