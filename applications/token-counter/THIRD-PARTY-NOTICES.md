# Third-party notices — Token Counter

The Token Counter source is MIT (see [`../../LICENSE`](../../LICENSE)).
The **`.exe` published in GitHub Releases** is a single-file,
self-contained build that bundles the components below. They remain
under their own licenses; this file is the attribution that
distributing a bundled binary requires.

| Component | Version | License | Copyright |
|---|---|---|---|
| .NET runtime + WPF (bundled by `SelfContained=true`) | .NET 10 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | © .NET Foundation and contributors |
| [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) | 2.3.1 | [MIT](https://github.com/HavenDV/H.NotifyIcon/blob/master/LICENSE.md) | © Hardcodet / HavenDV |
| [Microsoft.Web.WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) | 1.0.2792.45 | [Microsoft Software License Terms](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.2792.45/License) (redistributable) | © Microsoft Corporation |

The Microsoft Edge **WebView2 Runtime** itself is *not* bundled — it
is a separate Microsoft component the end user already has (Windows 10
22H2+) or installs from Microsoft. Only the managed WebView2 wrapper
assemblies are part of the binary.

If you build from source instead of downloading the release, the same
components are restored by `dotnet` from nuget.org under the licenses
above.

To regenerate / verify this list:

```powershell
cd dotnet
dotnet list AgentViewTokenCounter.sln package --include-transitive
```
