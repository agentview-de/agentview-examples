# Disclaimer

The code in this repository is **example / reference code** for the
public agentView REST API. It is shipped under the
[MIT license](LICENSE), "as is", without warranty of any kind.

## Affiliations

- **agentView**: this repository contains example code for the
  agentView REST API documented at
  <https://agentview.de/swagger/index.html>. It is published as-is for
  reference and carries no warranty.

- **Third-party APIs used by individual examples**: some examples
  (and especially the applications under `applications/`) integrate
  with third-party services — for example the Token Counter app
  reads plan-usage data from a public `claude.ai` endpoint that the
  official Anthropic clients also use. We are **not** affiliated with,
  endorsed by, or sponsored by any of these third parties. Their
  marks ("Claude", "Anthropic", "Claude Code", weather APIs, transit
  APIs, etc.) are property of their respective owners and used here
  only descriptively. Specifically the `claude.ai` plan-usage
  endpoint is **not** documented by Anthropic, may change at any
  time, and may be removed entirely.

## What this code is for

- Showing how to publish HTML to an agentView display via the public
  REST API.
- Showing how to write a data slot (`PUT /api/v1/data/{slug}`) so a
  display can poll fresh values without re-uploading the HTML.
- Showing how to mint a scoped `avk_...` API key, pair a physical
  screen by code, and provision a display end-to-end.
- Showing how to keep a long-lived authenticated session in an
  app-scoped WebView2 cookie jar, so a personal-use bridge can mirror
  your own data onto a display.

## What this code is NOT for

- Bypassing rate limits, plan limits, or any third-party terms of
  service. Integrations only access the caller's **own** data via
  channels their official clients already use.
- Reading other people's session cookies, displays, or organisations.
  Every API call is scoped to the user who logged in interactively.
- Running as a multi-tenant service or scraping fleet. The
  rate-limiting model assumes one personal-use installation per
  account.

## Names and marks

No trademark rights are claimed here. "agentView" is a common term
used by various unrelated parties; nothing in this repository asserts
ownership of that name, any logo, or the pixel-ghost mascot, and no
affiliation with any third party using a similar name is implied.

The mascot graphic and all other assets in this repository are
provided under the same [MIT license](LICENSE) as the code — free to
use, modify and redistribute, with no rights reserved beyond the MIT
notice.

Third-party names that appear descriptively (e.g. "Claude",
"Anthropic", "WebView2", "Windows") are the property of their
respective owners and are used only to describe what the example
integrates with.

## Trust the binary you run

If you download a prebuilt application (for example
`AgentViewTokenCounter.exe`), only trust:

- The official release artefacts on the GitHub release page of this
  repository, signed by the maintainer's GPG key.
- A binary you built yourself from this source tree with
  `dotnet publish` (or the equivalent for other applications).

A modified fork could exfiltrate session cookies or API keys. Treat
unofficial builds the same way you would treat any third-party
browser extension that asks for cookie access.
