# Cutting a release

This repo publishes prebuilt application binaries through GitHub
Releases. Each application owns its own tag namespace so independent
applications can ship at independent cadences.

## Token Counter (`applications/token-counter`)

The `.github/workflows/release-token-counter.yml` workflow builds and
uploads a single-file Windows `.exe` whenever a tag of the form
`token-counter/v<X>.<Y>.<Z>` is pushed.

### Cut a new release

1. Land the change you want shipped on `main` and make sure
   `dotnet build` is clean locally.
2. Bump the `<Version>` in
   `applications/token-counter/dotnet/src/AgentViewTokenCounter/AgentViewTokenCounter.csproj`
   and the `assemblyIdentity/@version` in the matching `app.manifest`.
   Both should always agree.
3. Commit the version bump on `main`.
4. Tag + push:
   ```bash
   git tag -a token-counter/v1.2.3 -m "Token Counter v1.2.3"
   git push origin token-counter/v1.2.3
   ```
5. GitHub Actions builds the `.exe` on `windows-latest`, computes the
   SHA-256 sidecar, and creates the GitHub Release. The whole run
   takes ~3-5 minutes.
6. Edit the auto-created release page and paste hand-written release
   notes above the auto-generated checksum / verification block.

### Pre-releases

Tags with a hyphen-suffix are auto-marked as pre-release on the GitHub
side. Example:

```bash
git tag -a token-counter/v1.3.0-rc1 -m "Token Counter v1.3.0-rc1"
git push origin token-counter/v1.3.0-rc1
```

### Local dry-run

Reproduce the artefact the workflow will publish:

```powershell
cd applications/token-counter/dotnet
dotnet publish src/AgentViewTokenCounter/AgentViewTokenCounter.csproj `
    -c Release -r win-x64 -o dist
(Get-FileHash dist/AgentViewTokenCounter.exe -Algorithm SHA256).Hash
```

The hash should match the one the workflow attaches to the release.
If it doesn't, somebody changed the source between your tag and the
runner's checkout — usually that just means a force-push or a stale
local working copy.

## Adding a new application

When you add the second application under `applications/`, copy
`release-token-counter.yml` to `release-<your-app>.yml`, change the
trigger tag pattern + the working directories + the asset paths, and
the same flow works for the new application without touching the
token-counter one.
