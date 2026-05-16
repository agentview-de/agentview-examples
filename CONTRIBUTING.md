# Contributing

Thanks for improving the agentView examples. This repo is **reference
code people copy** — the bar is "would I be happy to see this pattern
cloned a thousand times?" Keep contributions small, readable, and
self-contained.

## Two kinds of contribution

| | `examples/` (visual templates) | `applications/` (integrations) |
|---|---|---|
| Form | one `display.html`, optional `assets/`, a `config.json`, a screenshot, a `README.md` | a buildable project + `README.md` + screenshot |
| Build step | none — must open straight in a browser | allowed; must be one documented command |
| Data | bundled sample data, no live API key required to preview | a real source, but no secret committed |
| Goal | one screen, useful and readable at first paint | one data source → one display, set up in < 10 min |

## Ground rules

- **No secrets, ever.** No API keys, cookies, tokens, `.env` files,
  or real account data. The `.gitignore` blocks the obvious paths;
  do not work around it.
- **No build artefacts.** `bin/`, `obj/`, `dist/`, `node_modules/`
  stay out (already git-ignored). A clean `git status` after a build
  is mandatory.
- **Pin your dependencies.** Declare exact package versions; an
  example that drifts is a liability for the people who copied it.
- **Document the simplest path.** A reader should preview or run your
  example without reverse-engineering it.
- **Keep secrets out of screenshots too** — use demo data.

## Working on the .NET application

```powershell
cd applications/token-counter/dotnet
dotnet build AgentViewTokenCounter.sln -c Release   # must be 0 warnings
dotnet test  AgentViewTokenCounter.sln -c Release   # must be all green
```

The build runs under **strict analysis** (`Directory.Build.props`:
nullable, `TreatWarningsAsErrors`, `latest-recommended` analyzers). A
new warning fails the build — that is intentional. CI runs the exact
same two commands on every push and pull request; a red CI badge means
the change is not mergeable.

If you change the data shape, change `BucketMapper` and keep
`BucketMapperTests` green — that test class pins the slot contract the
display depends on. See the "Make it your own" section of
[`applications/token-counter/README.md`](applications/token-counter/README.md)
for the architecture and the extension points.

## Pull requests

- One example / one fix per PR. Small PRs get reviewed.
- Update the relevant `README.md` and, for a UI change, the
  screenshot.
- Describe what you changed and why in the PR body.
- By contributing you agree your work is licensed under the repo's
  [MIT license](LICENSE).

## Scope & security

- Read [`DISCLAIMER.md`](DISCLAIMER.md) — scope, names, "as is" terms.
- Never open a public issue for a security problem; follow
  [`SECURITY.md`](SECURITY.md).
- Be respectful — see [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).
