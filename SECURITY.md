# Security policy

We take the security of the **agentview-examples** repository
seriously even though it ships reference code, not a production
agentView component.

## Scope

This policy covers:

- The visual templates under `examples/` and the applications under
  `applications/` in this repository.
- The way those examples and applications interact with the **public**
  agentView REST API and any third-party services they bridge to
  (e.g. the undocumented but widely-used `claude.ai` plan-usage
  endpoint).

Out of scope:

- The agentView platform itself (server, database, admin tooling).
  Report those at **security@agentview.de** instead, against the
  platform rather than this repository.
- Third-party services we bridge to. Report those via the third
  party's official channels — e.g. Anthropic at
  <https://www.anthropic.com/security>.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security problems.

Email **security@agentview.de** with:

- A description of the issue (what the example does wrong, what
  the attacker gains).
- Step-by-step reproduction.
- Affected version (the version string is in the .exe properties or
  the commit SHA you are looking at).
- Optional: a suggested fix.

You will get an acknowledgement within five business days. We aim
to ship a fix or mitigation within 30 days of the initial report,
sooner for credential-leak or arbitrary-code-execution issues. We
will credit you in the release notes unless you ask to stay
anonymous.

## What we consider in-scope vulnerabilities

- Credential leakage from the local config / DPAPI store.
- An example or application sending session data to a third-party
  host that isn't called out in the documentation.
- The WebView2 component accepting navigation to unintended origins
  (so a hostile page could phish the user mid-session).
- Code that parses untrusted server responses with insufficient
  bounds checking (panic / crash / RCE).
- Anything that turns an application into a relay for somebody
  else's agentView account.
- A visual template (under `examples/`) that loads a malicious
  external resource by default.

## What we explicitly do NOT consider a vulnerability

- The fact that the Token Counter app calls an undocumented
  `claude.ai` endpoint. That endpoint is the same one the official
  Anthropic clients hit; Anthropic may change it without notice and
  we treat that as a normal breaking-change, not a security bug.
- The fact that a user with shell access to the same Windows account
  can read the WebView2 cookie store. DPAPI binds the secret to the
  Windows user — this is the same threat model every browser uses.
- Pairing-code or magic-link spam against agentView. Rate-limiting is
  the responsibility of the agentView platform itself. Report that
  via security@agentview.de against the platform, not this repo.

## Coordinated disclosure

If your finding affects the agentView platform OR a third party we
bridge to, we will forward your report (with credit) to the relevant
team and coordinate the timing of the public disclosure with them.
