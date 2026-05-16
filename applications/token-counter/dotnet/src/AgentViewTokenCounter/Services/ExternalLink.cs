using System.Diagnostics;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// One vetted place to launch a web link in the user's default
/// browser. <c>Process.Start(UseShellExecute=true)</c> will happily
/// dispatch <c>file://</c>, <c>ms-settings:</c>, custom protocol
/// handlers and other shell verbs — so anything that could ever carry
/// an untrusted string must pass through here, where the scheme is
/// allow-listed to <c>http</c>/<c>https</c> first.
/// </summary>
/// <remarks>
/// All current call sites pass hard-coded developer strings, so this
/// is defence-in-depth rather than a live-injection fix. It exists
/// mainly so the pattern a thousand copiers will lift from this
/// reference repo is the safe one: validate the scheme, then launch.
/// Opening a local folder is a deliberately different operation and
/// does not go through here.
/// </remarks>
public static class ExternalLink
{
    /// <summary>
    /// Opens <paramref name="url"/> in the default browser iff it is a
    /// well-formed absolute <c>http</c>/<c>https</c> URL. Anything else
    /// is refused (best-effort: returns false, never throws).
    /// </summary>
    public static bool OpenInBrowser(string? url, DiagnosticsLog? log = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            log?.Warn($"Refused to open non-http(s) link: {url}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = uri.AbsoluteUri,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            // Best-effort: a missing default browser must not crash the
            // app. The caller's UI still shows the URL as text.
            log?.Warn($"Could not launch browser for {uri.AbsoluteUri}: {ex.Message}");
            return false;
        }
    }
}
