# Token Counter

[![CI](https://github.com/agentview-de/agentview-examples/actions/workflows/ci.yml/badge.svg)](https://github.com/agentview-de/agentview-examples/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](../../LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

A small Windows tray app that pushes your live **Claude plan usage**
onto an agentView display, so the bars next to your desk reflect the
real "how much of my 5-hour and weekly quotas have I burnt through" —
updated every two minutes, with a Tamagotchi-style ghost mascot that
reacts to the highest usage bucket.

![Token Counter display preview](screenshot.png)

[**▶ Open the live display preview**](https://agentview-de.github.io/agentview-examples/applications/token-counter/display.html)
&nbsp;·&nbsp;
[**Download the latest .exe**](https://github.com/agentview-de/agentview-examples/releases?q=token-counter)
&nbsp;·&nbsp;
[**Source on GitHub**](https://github.com/agentview-de/agentview-examples/tree/main/applications/token-counter)

## Download + run (recommended)

1. Go to **[Releases](https://github.com/agentview-de/agentview-examples/releases?q=token-counter)** and grab the latest
   `AgentViewTokenCounter.exe`.
2. *Optional but smart:* verify the SHA-256 against the
   `AgentViewTokenCounter.exe.sha256` sidecar published alongside.
   ```powershell
   (Get-FileHash AgentViewTokenCounter.exe -Algorithm SHA256).Hash
   ```
3. Double-click the `.exe`. The setup wizard opens.

End users do **not** need .NET installed. The runtime is bundled in the
single-file binary (~65 MB). The only external requirement is the
**Microsoft Edge WebView2 runtime**, which ships preinstalled on
Windows 10 22H2 and later. Older builds get a one-click install link
from the wizard if it's missing.

## What it does

```
  ┌──────────────┐         ┌────────────────────┐         ┌────────────────┐
  │  claude.ai   │  GET    │  Token Counter     │  PUT    │   agentView    │
  │  (your own   │ ──────▶ │  Windows tray app  │ ──────▶ │   data slot    │
  │   session)   │ /usage  │  (this repo)       │ /data/  │   claude-usage │
  └──────────────┘         └────────────────────┘         └────────┬───────┘
                                                                   │ polled
                                                                   ▼
                                                          ┌────────────────┐
                                                          │  your display  │
                                                          │  (tablet/TV)   │
                                                          └────────────────┘
```

The tray app authenticates against `claude.ai` in an embedded
WebView2 (so your session cookies stay on your machine and never
touch a server), reads the **same plan-usage endpoint the official
Claude clients use**, normalises the numbers, and writes them to an
agentView data slot via the public REST API. The display polls the
slot every 120 seconds and re-renders.

## The setup wizard

The first launch opens a three-step wizard:

1. **Sign in to Claude** — embedded browser, your real `claude.ai`
   login. Cookies stay in `%APPDATA%\agentView-token-counter\WebView2`,
   never leave your machine.
2. **Sign in to agentView** — same embedded browser, different origin.
   Or skip the browser entirely: click *Use an API key instead* and
   paste a key you minted in the agentView dashboard (see
   [Authenticating with an API key](#authenticating-with-an-api-key)).
3. **Publish** — the wizard creates the data slot, mints a scoped API
   key (`slot.write` on the `claude-usage` slot only), renders the
   display HTML, and pushes it to the display you pick.
4. *Optional:* enter the 6-character pairing code shown on the actual
   screen — that binds the screen to the just-created display profile.

After that, the tray icon stays in the notification area and a sync
runs every 120 seconds. Right-click for *Sync now / Pause / Exit*,
left-click to open the Overview window.

## Authenticating with an API key

The embedded-browser login is the easy default, but the *Use an API
key instead* link on step 2 opens a modal with two flavours of
key-based auth:

| Mode | Key needs | The bridge will… |
| --- | --- | --- |
| **Full setup** | `slot.write` + `display.read` + `display.send` + `display.manage` | …run the entire wizard on the key: list / create displays, push the HTML, pair a screen. Same end-to-end flow as the browser login, just no cookies. |
| **Slot write only** | `slot.write` (scoped to one slug) | …only push data to that one slot. You set up the display + HTML in the dashboard yourself. |

Mint keys at
<https://agentview.de/dashboard.html#/settings/api-keys> —
**Settings → API Keys → + Create New Key**. The Claude side still
needs its own sign-in: the API key only covers the agentView half;
the plan-usage data is always read from *your* `claude.ai` session.

## Team setup: one display, many data feeds

A common pattern: one person owns the screen, several teammates feed
their own usage into it (or you run the bridge on a locked-down box
that should never hold an agentView login). The **Slot write only**
key makes this a clean, least-privilege handoff.

```
        ┌─────────────────────────────────────────────────────┐
        │  User A — owns the agentView account + the display   │
        │                                                       │
        │  1. Mint a key:  scope content_only · permission      │
        │     write · capability slot.write ·                   │
        │     allowedSlotSlugs ["claude-usage"]                 │
        │  2. Set up the display so its HTML reads that slot    │
        │     (install the token-counter template, or wire      │
        │      the slot read-URL into the HTML manually)        │
        │  3. Hand User B:  the avk_… key  +  the slot slug     │
        └───────────────────────────┬─────────────────────────┘
                                     │  key + slug
                                     ▼
        ┌─────────────────────────────────────────────────────┐
        │  User B — runs the bridge, no agentView account       │
        │                                                       │
        │  1. Token Counter → sign in to *their own* claude.ai  │
        │  2. Use an API key instead → "Slot write only"        │
        │  3. Paste A's key + the slot slug → done              │
        └─────────────────────────────────────────────────────┘
```

What this gets you:

- **User B never needs an agentView account.** They only sign in to
  their own Claude (the usage shown is inherently per-person) and
  paste the key.
- **Least privilege.** A `slot.write` key restricted to one slug can
  do exactly one thing: overwrite that slot. It cannot read other
  slots, touch displays, or mint more keys — so handing it out is
  low-risk. Revoke it in the dashboard at any time.
- **The slot is owned by User A.** The first `PUT` creates the slot
  under the key owner's account, so A's display can read it and A
  stays in control.

Order matters: have **User A create the slot + wire up the display
first**, then hand the key over. If B's first ping auto-creates the
slot, A still has to go back and bake the freshly-generated read-URL
into the display HTML — fiddly. Slot first, key second.

> The **Full setup** key is the wrong tool for this handoff: it would
> let User B create and rewrite displays on A's account. Use it only
> when the same person owns both sides and just prefers a key over the
> browser login.

## Build from source

If you'd rather not download a binary you didn't compile, the build
is one command:

```powershell
cd dotnet
dotnet publish src\AgentViewTokenCounter\AgentViewTokenCounter.csproj `
    -c Release -r win-x64 -o dist
.\dist\AgentViewTokenCounter.exe
```

Requires the .NET 10 SDK. The output `.exe` is byte-identical with
the one in Releases when built from the same commit.

Detailed walkthrough in [`dotnet/README.md`](dotnet/README.md).

## Make it your own

You do **not** need to understand WebView2, DPAPI, or the tray
plumbing to adapt this. The whole app is a pipe:

```
  read a number  →  shape it into JSON  →  PUT it to a slot  →  HTML renders it
   (your source)      (BucketMapper)        (plumbing)          (display.html)
```

Only the **first**, **second**, and **fourth** boxes are "yours". The
third (auth, the sync loop, config storage, the tray) is reusable
plumbing — leave it alone.

### The slot contract (the one thing that must stay consistent)

The display reads this JSON. Your source, the mapper, and
`display.html` all have to agree on it — nothing else matters:

```json
{
  "buckets": [
    { "key": "five_hour", "label": "5-Hour Limit", "usedPct": 38, "resetIso": "2026-05-19T03:00:00Z" }
  ],
  "sparkline": null,
  "status": null
}
```

`buckets` is 1–6 rows; each needs `key`, `label`, `usedPct` (0–100).
That's the entire data model. Change what the numbers *mean*, keep
the shape.

### Two channels: the HTML goes once, the data goes forever

This is the single most important thing to understand before you
touch anything — get this wrong and you ship a display that stays
blank.

```
 CHANNEL 1 — happens ONCE, during Setup → Publish
 ─────────────────────────────────────────────────
   Resources/display.html   (your template, embedded in the .exe)
        │  DisplayHtmlBuilder.Build(slotReadUrl)
        │    replaces the token  {{slot:token-usage.readUrl}}
        │    with the slot's real public URL
        ▼
   SendDisplayHtmlAsync  ──POST──▶  the display profile
        (SetupCoordinator, publish step — fires exactly once)

   ⇒ the screen now runs your HTML and will keep running it.

 CHANNEL 2 — happens EVERY ~120 s, forever
 ─────────────────────────────────────────────────
   ClaudeApiClient.FetchUsageAsync   (collect the numbers)
        │  BucketMapper.Map → SlotContent  (shape them)
        ▼
   AgentViewClient.WriteSlotAsync  ──PUT /api/v1/data/{slug}──▶  the slot
        (PingService loop — fires every cycle)

   ⇒ the slot's JSON changes. The display HTML — which has its own
     setInterval(reload, 120000) — fetches that slot URL on its next
     tick and re-renders. The app never talks to the screen again
     after Channel 1; it only ever writes the slot. The screen pulls.
```

Consequences a beginner must internalise:

- **The app pushes to the *slot*, never to the screen.** The screen
  is just a browser running your HTML, polling the slot URL itself.
- **`{{slot:token-usage.readUrl}}` in `display.html` is the wire.**
  `DisplayHtmlBuilder` swaps it for the live slot URL at publish
  time. Inside the HTML it lands in `var USAGE_URL = "…";`. If you
  delete that token, rename `USAGE_URL`, or hard-code a URL, the
  display has nothing to poll and stays permanently on the fallback
  zeros. **Keep the token, keep the variable.**
- **Restyling the HTML needs a re-publish.** Channel 1 only ran once.
  Edit `display.html`, then go to Setup → **Re-publish** so the new
  HTML reaches the screen. A data-only change (Channel 2) needs no
  re-publish — it flows automatically.
- **The display already polls every 120 s.** That is why the app's
  sync interval below ~120 s is wasted writes.

Where each piece lives:

| Piece | File |
|---|---|
| The HTML template you ship to the screen | `src/AgentViewTokenCounter/Resources/display.html` |
| Token → real URL substitution            | `Services/DisplayHtmlBuilder.cs` |
| The one-time "send HTML to display"       | `Services/SetupCoordinator.cs` (publish step) |
| The forever "collect → shape → write slot" | `Services/ClaudeApiClient.cs` → `Services/BucketMapper.cs` → `Services/PingService.cs` |
| The slot read-loop inside the screen      | `display.html` — `fetchOrFallback(USAGE_URL, …)` + `setInterval(reload, 120000)` |

### What to edit, by goal

| I want to…                                  | Edit only…                                                            | Leave alone |
|---|---|---|
| Restyle the display (colours, layout, fonts) | `Resources/display.html`                                              | everything else |
| Change bar labels / which buckets show      | `Services/BucketMapper.cs`                                            | the rest |
| Read a **different data source** (not Claude) | `Services/ClaudeApiClient.cs` + `Models/ClaudeUsageResponse.cs` + `Services/BucketMapper.cs` | plumbing |
| Point at a self-hosted agentView            | nothing — set the base URL in the **Settings** tab at runtime         | code |
| Change the slot slug                        | nothing — set it in **Settings** / the API-key modal                  | code |

The "different data source" row is the real fork. Concretely, to show
(say) your CI build-minutes instead of Claude usage:

1. **Fetch.** `ClaudeApiClient` implements `IClaudeApiClient`
   (`FetchUsageAsync`). Write your own implementation that calls your
   API, then swap the one line in `App.xaml.cs` that constructs the
   Claude client (it is the composition root — every dependency is
   wired there, by hand, on purpose).
2. **Shape.** `Models/ClaudeUsageResponse.cs` is just the source's
   JSON deserialised. Replace its fields with your API's fields.
3. **Map.** `Services/BucketMapper.cs` is a *pure function*
   (source object → `SlotContent`). Rewrite its body to put your
   numbers into `buckets`. It has unit tests — run `dotnet test`
   while you edit and you will know the moment you break the shape.
4. Done. `PingService`, the agentView clients, the tray, config
   storage and the display polling are unchanged — they only ever
   see the `SlotContent` shape from step 3.

`BucketMapper` having its own test class is deliberate: it is the
seam you are meant to modify, so it is the seam that is pinned by
tests. Start there, keep the tests green, and the rest keeps working.

### What you should NOT touch

`WebViewSession`, `AgentViewApiClient`, `AgentViewClient`,
`SetupCoordinator`, `ConfigStore`, `TrayIconHost`. These solve
problems orthogonal to your use case (keeping a browser session
alive, the dual auth transport, DPAPI at-rest encryption, the
navigation security fence, the wizard flow). They are documented for
the curious, but a forker adapting the data should never need to open
them.

## What the display looks like

The display is a single-file HTML that polls the data slot and
renders it on a 540×540 (or larger) surface. It works on every
WebView-class device agentView supports — smart TVs, dedicated
signage players, browser tabs.

Up to six buckets stack vertically. The first bucket renders as a
large primary bar, the rest sit below in equal-size rows. Severity
shading (low / mid / high / crit) is derived from `usedPct`
automatically — you never set the colour, you write the numbers.

A pixel-art ghost mascot named **Synth** lives in the bottom-right
corner and reacts to the highest bucket:

| Mood    | Trigger     | Behaviour |
| ---     | ---         | --- |
| `cool`  | `pct < 30`  | Slow drift, the occasional yawn |
| `focus` | `pct < 60`  | Tight pace, small head-bobs |
| `busy`  | `pct < 80`  | Jogging, faster cycle |
| `hot`   | `pct < 95`  | Twitchy, antenna pulsing amber |
| `crit`  | `pct >= 95` | Red eyes, alarm emote, fast cycle |

The page works offline: when the slot can't be fetched, the embedded
defaults render so the display never goes blank.

## Why this example exists

The agentView REST API is documented at
<https://agentview.de/swagger/index.html>. Writing a slot is
literally:

```http
PUT https://agentview.de/api/v1/data/claude-usage?label=Claude%20plan%20usage
X-API-Key:    avk_...
Content-Type: application/json

{"buckets":[{"key":"five_hour","label":"5-Hour Limit","usedPct":38,...}]}
```

The interesting bits this example demonstrates are everything *around*
that one HTTP call:

- How to keep a long-lived authenticated `claude.ai` session in an
  app-scoped WebView2 cookie jar (Chromium 127+ App-Bound Encryption
  killed the "read cookies from the user's Chrome" approach).
- How to run the **same REST flow under two auth transports** — a
  cookie session from an embedded browser, or an `X-API-Key` header —
  behind one client interface, so the UI doesn't care which the user
  picked.
- How to provision a display end-to-end via the public API:
  create the data slot, render the HTML, post it to the display
  profile, mint a scoped write-only API key, persist everything in
  `%APPDATA%`.
- How to design a **least-privilege handoff**: a `slot.write` key
  scoped to a single slug is safe to pass to a teammate who should
  feed data but touch nothing else.
- How to pair a physical screen by the 6-character on-screen code via
  `POST /api/v1/agent/displays/pair-by-code`.
- How to ship the whole thing as a single self-contained `.exe` your
  non-technical colleagues can install.

The patterns transfer to any "live data from a personal service to a
display" integration.

## Caveats

The `claude.ai` plan-usage endpoint is **not part of any documented
Anthropic API.** It is the same endpoint the official Claude clients
hit; Anthropic may change field names or remove it entirely without
notice. Treat the bridge as best-effort personal tooling. Both
[`DISCLAIMER.md`](../../DISCLAIMER.md) and
[`SECURITY.md`](../../SECURITY.md) at the repo root cover the scope
and the responsible-disclosure flow.

## License

[MIT](../../LICENSE) — code, the mascot graphic, and docs. No
trademark rights are claimed in any name or mark used here; see
[`../../DISCLAIMER.md`](../../DISCLAIMER.md). The published `.exe`
bundles third-party components — attribution in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
