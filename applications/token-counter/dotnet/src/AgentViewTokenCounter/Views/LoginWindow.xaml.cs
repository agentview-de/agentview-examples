using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AgentView.TokenCounter.Services;
using Microsoft.Web.WebView2.Wpf;

namespace AgentView.TokenCounter.Views;

/// <summary>
/// Dialog that temporarily hosts the shared <see cref="WebViewSession"/>
/// WebView2 control so the user can sign in to a web origin
/// (claude.ai or agentview.de). The dialog auto-closes as soon as the
/// supplied <c>isLoggedIn</c> probe returns <c>true</c>.
/// </summary>
/// <remarks>
/// <para>
/// The WebView2 control is re-parented from the session's hidden host
/// window onto this dialog for the duration of the sign-in, then
/// handed back when the dialog closes. The same browser instance —
/// and therefore the same cookie store — is used for the login UI and
/// for the background fetches that follow.
/// </para>
/// <para>
/// This window is intentionally service-agnostic: it does not know
/// about claude.ai or agentview.de. The caller wires it up by passing
/// a navigation URL and a probe that returns true once the session is
/// authenticated.
/// </para>
/// </remarks>
public partial class LoginWindow : Window
{
    private readonly WebViewSession      _session;
    private readonly WebView2            _webView;
    private readonly DependencyObject?   _previousParent;
    private readonly Func<Task<bool>>    _isLoggedIn;
    private readonly string              _loginUrl;
    private readonly DispatcherTimer     _loginPoll;

    public bool LoginSucceeded { get; private set; }

    /// <param name="session">Shared WebView session.</param>
    /// <param name="title">Window title shown to the user.</param>
    /// <param name="header">Short headline shown in the header strip.</param>
    /// <param name="subtitle">Secondary line under the header.</param>
    /// <param name="loginUrl">URL the WebView is navigated to on open.</param>
    /// <param name="isLoggedIn">Probe that returns true when the session is authenticated.</param>
    public LoginWindow(
        WebViewSession session,
        string title,
        string header,
        string subtitle,
        string loginUrl,
        Func<Task<bool>> isLoggedIn)
    {
        InitializeComponent();

        _session        = session;
        _webView        = session.Control;
        _previousParent = LogicalTreeHelper.GetParent(_webView);
        _isLoggedIn     = isLoggedIn;
        _loginUrl       = loginUrl;

        Title              = title;
        HeaderTitle.Text   = header;
        HeaderSubtitle.Text = subtitle;

        // Re-parent the shared WebView2 onto this dialog. If it was
        // already hosted somewhere (in our hidden background Window),
        // detach it first — WPF only allows one logical parent.
        DetachFromCurrentParent();
        WebViewHost.Child = _webView;

        _loginPoll = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500),
        };
        _loginPoll.Tick += OnPollTick;

        Loaded  += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Relax the navigation fence for the duration of the visible
        // sign-in: federated IdPs (Google / Apple / Microsoft) redirect
        // the browser off claude.ai, which the background allowlist
        // would otherwise block. Re-armed in OnClosing.
        _session.InteractiveLoginActive = true;
        _session.Navigate(_loginUrl);
        _loginPoll.Start();
    }

    private async void OnPollTick(object? sender, EventArgs e)
    {
        _loginPoll.Stop();
        try
        {
            if (await _isLoggedIn().ConfigureAwait(true))
            {
                LoginSucceeded = true;
                DialogResult   = true;
                Close();
                return;
            }
        }
        catch
        {
            // Probe failure during interactive login is normal — the
            // user might still be mid-flow. Keep polling.
        }
        finally
        {
            if (IsLoaded)
            {
                _loginPoll.Start();
            }
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _loginPoll.Stop();

        // Re-arm the strict background navigation fence now that the
        // user is done signing in.
        _session.InteractiveLoginActive = false;

        // Hand the WebView2 control back to the hidden host. If it had
        // no prior parent (very first login of the app), simply detach
        // — the session.InitialiseAsync() flow will re-host as needed.
        WebViewHost.Child = null;
        if (_previousParent is ContentControl cc)
        {
            cc.Content = _webView;
        }
        else if (_previousParent is Decorator dec)
        {
            dec.Child = _webView;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DetachFromCurrentParent()
    {
        var parent = LogicalTreeHelper.GetParent(_webView);
        switch (parent)
        {
            case ContentControl cc when ReferenceEquals(cc.Content, _webView):
                cc.Content = null;
                break;
            case Decorator dec when ReferenceEquals(dec.Child, _webView):
                dec.Child = null;
                break;
            case Window w when ReferenceEquals(w.Content, _webView):
                w.Content = null;
                break;
        }
    }
}
