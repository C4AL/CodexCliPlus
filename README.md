# Cli Proxy API Desktop

`Cli Proxy API Desktop` is a native WPF Windows desktop application for managing a local `CLIProxyAPI` backend. It no longer hosts the upstream web UI through `WebView2`, and it no longer depends on `management.html` at runtime.

## Current status

- Native shell is in place: header, left navigation, content surface, footer, tray menu, theme switching, and close-to-tray behavior.
- Backend hosting is in place: asset bootstrap, start/stop/restart, health checks, port fallback, logging, and diagnostics export.
- Security baseline is in place: management key plaintext is kept out of `desktop.json`, persisted through DPAPI, and written to backend config as a bcrypt hash.
- Legacy host assets are removed: `WebView2`, `management.html` hosting, onboarding window, PowerShell release scripts, and Inno Setup packaging.
- Native page and API work is still ongoing and continues under `build_steps.md`.

## Execution basis

Implementation work must continue against these audited references:

- `artifacts/reference-repos/better-genshin-impact`
- `artifacts/reference-repos/CPA-UV`
- `artifacts/reference-repos/Cli-Proxy-API-Management-Center-blackblock`
- `artifacts/reference-repos/CLIProxyAPI-upstream`
- `artifacts/reference-repos/Cli-Proxy-API-Management-Center-upstream`

Shell structure follows `BetterGI`. Overview, quota, system, and management semantics follow `CPA-UV` plus the two Management Center repositories. Management API behavior follows `CLIProxyAPI-upstream` and `CPA-UV`.

## Repository layout

```text
src/
  CPAD.App            Native WPF desktop application
  CPAD.Core           Shared abstractions, enums, constants, models
  CPAD.Infrastructure Backend hosting, paths, logging, security, diagnostics
  CPAD.BuildTool      Future build and packaging entry point
tests/
  CPAD.Tests          Unit tests and smoke-oriented integration coverage
resources/
  backend/            Backend binary and related upstream files
artifacts/
  reference-repos/    Local mirrors of required reference repositories
```

## Local development

```powershell
dotnet build CliProxyApiDesktop.sln
dotnet test tests/CPAD.Tests/CPAD.Tests.csproj
dotnet run --project src/CPAD.App/CPAD.App.csproj
```

You can also start the built app directly:

```powershell
src\CPAD.App\bin\Debug\net10.0-windows\CPAD.exe
```

## Packaging note

- The old `PowerShell + Inno Setup` publishing chain is retired and removed from this repository.
- `CPAD.BuildTool` and the future `MicaSetup` installer flow are still planned work.
- Until that lands, the supported validation loop is `dotnet build`, `dotnet test`, and desktop startup smoke.
