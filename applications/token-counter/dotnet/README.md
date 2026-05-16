# Token Counter — .NET 10 tray app

A Windows tray bridge that streams your live Claude plan usage to an
agentView display. Single-file self-contained `.exe`, no installer,
no external dependencies beyond the Microsoft Edge **WebView2**
runtime (preinstalled on Windows 10 22H2 and later).

```
.editorconfig                       shared C# style (teaching-grade)
Directory.Build.props               nullable + analyzers + warnings-as-errors
AgentViewTokenCounter.sln
src/AgentViewTokenCounter/
├── AgentViewTokenCounter.csproj    .NET 10 / WPF / publish-single-file
├── App.xaml{,.cs}                  composition root (hand-wired, no DI container)
├── app.manifest                    DPI awareness, common-controls v6
├── Models/                         POCOs (config, slot, API DTOs)
├── Services/
│   ├── IClaudeApiClient.cs         seam — Claude usage fetch
│   ├── IAgentViewClient.cs         seam — slot writer (ping loop)
│   ├── IAgentViewApiClient.cs      seam — wizard provisioning surface
│   ├── ClaudeApiClient.cs          /api/organizations + /usage
│   ├── AgentViewApiClient.cs       wizard; cookie OR X-API-Key transport
│   ├── AgentViewClient.cs          key-auth slot writer, used by the ping loop
│   ├── SetupCoordinator.cs         all wizard orchestration (no WPF types — unit-tested)
│   ├── BucketMapper.cs             claude.ai → slot-content shape (pure)
│   ├── ConfigStore.cs              %APPDATA% + DPAPI for the API key
│   ├── DisplayHtmlBuilder.cs       embedded display.html → send-ready HTML
│   ├── ExternalLink.cs             scheme-allow-listed browser launch
│   ├── PingService.cs              one sync cycle, no state
│   └── WebViewSession.cs           shared WebView2 cookie jar + navigation fence
├── ViewModels/BucketRowViewModel.cs  bound by the Overview DataTemplates
├── Converters/                     severity-brush / number-ink value converters
├── Themes/                         design tokens + control styles
├── UI/TrayIconHost.cs              tray icon, polling timer, menu
├── Views/                          MainWindow + Overview/Setup/Settings tabs
│                                   (thin shells — logic lives in services)
└── Resources/
    ├── display.html                shipped to the display on publish
    └── token-usage.default.json    "no data yet" slot seed
tests/AgentViewTokenCounter.Tests/  xUnit; BucketMapper, ConfigStore,
                                    PingService, SetupCoordinator, …
```

The split is deliberate teaching structure: **everything testable has no
WPF dependency**. `SetupCoordinator` owns the wizard flow; the `*.xaml.cs`
files only wire events and reflect result records. The three `I*Client`
seams let `PingService` and the coordinator be exercised with hand-written
fakes (no mocking framework).

## Build

```powershell
dotnet publish src/AgentViewTokenCounter/AgentViewTokenCounter.csproj `
    -c Release -r win-x64 -o dist
.\dist\AgentViewTokenCounter.exe
```

That produces a ~65 MB single-file `.exe`. The .NET 10 runtime is
bundled (`<SelfContained>true</SelfContained>`), the embedded display
HTML and the all-zeros seed JSON are linked in as managed resources.
The only thing the target machine needs is the **WebView2 runtime** —
already present on Windows 10 22H2+, otherwise free from
<https://developer.microsoft.com/microsoft-edge/webview2/>.

The whole solution builds **0-warning under strict analysis**
(`Directory.Build.props` sets `Nullable=enable`,
`TreatWarningsAsErrors=true`, `EnableNETAnalyzers=true`,
`AnalysisLevel=latest-recommended`). If a contribution introduces a
warning, the build fails — by design.

## Tests

```powershell
dotnet test AgentViewTokenCounter.sln -c Release
```

`tests/AgentViewTokenCounter.Tests/` is xUnit, no mocking framework
(hand-written fakes — easier to read as example code). The line is
drawn deliberately: everything with real logic and no WPF/WebView2
dependency is unit-tested; the UI shells are not. Coverage focuses on

- `BucketMapper` — the claude.ai → slot transform (rounding, clamping,
  ordering, model-split toggle, extra-credits + mood),
- `ConfigStore` — DPAPI round-trip, plaintext migration, atomic write
  (real filesystem, temp dirs, never `%APPDATA%`),
- `PingService` — the sync cycle incl. the missing-label retry branch,
- `SetupCoordinator` — wizard orchestration incl. the API-key
  FullSetup-vs-SlotOnly branching,
- `DisplayHtmlBuilder`, `PingResult`, `AgentViewClient` URL building.

`ConfigStore` tests carry `[Trait("Category","Integration")]` (real
DPAPI + disk); filter with `--filter Category!=Integration` on a
non-Windows runner. DPAPI works on Windows-hosted GitHub Actions.

## Runtime layout

Per-user state lives in `%APPDATA%\agentView-token-counter\`:

```
config.json                          plain JSON; AgentViewApiKey is DPAPI-wrapped
diagnostics.log                      rolling log of fetches + errors
WebView2/                            Edge WebView2 user-data folder
└── (claude.ai / agentview.de cookies, isolated from system browser)
```

DPAPI binds the encrypted blob to the current Windows user, so the
config is portable between machines for the *same* Windows account
and unreadable on any other. The WebView2 cookie store is also
encrypted by Edge itself — same threat model as a normal browser
profile.

## Architecture in one paragraph

`App.xaml.cs` owns the singletons: one shared `HttpClient`, one
shared `WebViewSession`, one `ConfigStore`. The
`WebViewSession` hosts a single off-screen WebView2 control whose
cookie jar persists across two web origins (`claude.ai` and
`agentview.de`). The setup wizard re-parents that same WebView2
control onto the visible `LoginWindow` so the user can sign in
interactively, then hands it back.

`AgentViewApiClient` runs every setup-wizard call (`/auth/me`,
list / create displays, slot PUT, send HTML, pair) through a single
internal `FetchAsync` helper that picks the transport: the WebView
cookie session by default, or a plain-`HttpClient` `X-API-Key`
request once `UseApiKey()` is called from the "Use an API key
instead" modal. The wizard UI is identical either way. The
long-running sync loop in `PingService` always uses the plain
`HttpClient` with a scoped `X-API-Key: avk_…` header via
`AgentViewClient` — it never needs the cookie session again.

`TrayIconHost` owns the polling timer, draws the pixel-ghost tray
icon at four palettes (idle / working / failed / paused), and opens
the single reusable `MainWindow` with three tabs:

* **Overview** — last-sync headline, bucket bars, "Sync now" /
  "Pause".
* **Setup** — three-step wizard (claude.ai login, agentView login or
  paste-an-API-key, publish). Persists across restarts: an
  already-configured app shows "Re-publish" and the status flips green
  again after the first successful sync proves the pipeline still
  works.
* **Settings** — base URL, slot slug, group ID, API key, poll
  interval, model-split toggle, auto-start.

## Two agentView clients — why there are two

The project ships two classes that both talk to agentView:

| Class | Used by | Transport | Scope |
|---|---|---|---|
| `AgentViewApiClient` | `SetupCoordinator` (wizard) | Cookie via WebView **or** `X-API-Key` | Broad: list/create displays, PUT slot, send HTML, mint key, pair |
| `AgentViewClient` | `PingService` (ping loop) | `X-API-Key` only | Narrow: PUT slot |

The split is deliberate and load-bearing:

- The **wizard** needs dual transport because the user authenticates in
  an embedded browser (cookie) but may alternatively paste a broad API
  key. Either way the provisioning flow is identical from the wizard's
  perspective.
- The **ping loop** needs none of that. It only ever writes a slot.
  Giving it a minimal `IAgentViewClient` seam means the loop is
  independently testable and has no dependency on WebView2 or on the
  broad dashboard session.
- Merging the two would force `PingService` to carry a dependency on
  `WebViewSession` (a WPF type) or on the broad API-key session — both
  of which are wrong for a stateless background worker.

## The two HTTP exchanges that matter

```
GET https://claude.ai/api/organizations/{uuid}/usage
Cookie: …whatever the user logged in with…

200 OK
{
  "five_hour":         { "utilization": 38, "resets_at": "2026-05-15T18:50:00Z" },
  "seven_day":         { "utilization": 64, "resets_at": "2026-05-19T03:00:00Z" },
  "seven_day_opus":    { "utilization": 21, "resets_at": "2026-05-19T03:00:00Z" },
  "seven_day_sonnet":  { "utilization": 12, "resets_at": "2026-05-19T03:00:00Z" },
  "seven_day_omelette":{ "utilization": 29, "resets_at": "2026-05-19T03:00:00Z" },
  "extra_usage":       { "is_enabled": false }
}
```

```
PUT https://agentview.de/api/v1/data/claude-usage?label=Claude%20plan%20usage
X-API-Key:    avk_…
Content-Type: application/json

{
  "buckets": [
    { "key": "five_hour", "label": "5-Hour Limit", "usedPct": 38, "resetIso": "2026-05-15T18:50:00Z" },
    …
  ],
  "sparkline": null,
  "status": null
}
```

That's the entire bridge. Everything else is plumbing.

## Versioning + Releases

The `<Version>` in the csproj and the `assemblyIdentity/@version` in
`app.manifest` are kept in lockstep. Bump both when shipping a new
release. Releases are tagged in the parent repo, not from inside this
folder.

## License

[MIT](../../../LICENSE) — including the mascot graphic. No trademark
rights are claimed in any name or mark used here; see
[`../../../DISCLAIMER.md`](../../../DISCLAIMER.md).
