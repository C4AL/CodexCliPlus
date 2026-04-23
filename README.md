# Cli Proxy API Desktop

`Cli Proxy API Desktop` is a native WPF Windows desktop application for managing a local `CLIProxyAPI` backend. The active product shape is `native WPF shell + Go backend + C# BuildTool + MicaSetup installer route`; it no longer depends on the upstream web UI host at runtime.

## Current status

- Native shell is in place: header, left navigation, content surface, footer, tray menu, theme switching, and close-to-tray behavior.
- Backend hosting is in place: asset bootstrap, start/stop/restart, health checks, port fallback, logging, and diagnostics export.
- Security baseline is in place: management key plaintext is kept out of `desktop.json`, persisted through DPAPI, and written to backend config as a bcrypt hash.
- Build and packaging are now centered on `CPAD.BuildTool`: asset fetch/verify, `dotnet publish`, portable/dev packaging, installer staging, and package verification all run through the C# toolchain.
- Desktop update checks already use the repository's stable GitHub Releases feed; beta remains a reserved channel entry until a real beta release line exists.
- The old `WebView2` host and `PowerShell + Inno Setup` release path are no longer the formal architecture. Historical residue may still exist under generated outputs or cleanup targets and should not be treated as the active product shape.
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
  CPAD.BuildTool      Unified asset, publish, packaging, and package verification entry point
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

## Distribution modes

- Installed package: `package-installer` is the current formal installer route. It builds a MicaSetup-based `CPAD.Setup.<version>.exe`, writes installer metadata, and targets current-user installation under `%LocalAppData%\\Programs\\CPAD`.
- Portable package: `package-portable` emits `CPAD.Portable.<version>.<rid>.zip` with `portable-mode.json`. Writable state stays beside the app under `data/`, and portable distribution should remain manual/no-auto-update.
- Development package: `package-dev` emits `CPAD.Dev.<version>.<rid>.zip` with `app/dev-mode.json` plus `app/artifacts/dev-data` scaffolding. It is a validation/developer package, not a primary end-user release format.

## Updates And Uninstall

- Stable desktop release discovery uses `https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases/latest`.
- Beta is intentionally preserved as a reserved preference so the settings model and package metadata do not need another schema change when a beta line exists.
- The desktop currently reports stable release state, release assets, and release page links. It does not claim a finished in-app self-updater yet.
- Installed builds are the only package type aligned with future guided update flows. Portable and development packages should be treated as manual-update distributions.
- BuildTool writes installer metadata for dependency precheck, update policy, and uninstall cleanup scope. That metadata is real and package-verified, but production install/update/uninstall acceptance still depends on the dedicated later-stage validation work.

## Validation

- The old `PowerShell + Inno Setup` publishing chain is retired as a formal release path.
- `CPAD.BuildTool` is the active packaging entry point and currently exposes `fetch-assets`, `verify-assets`, `publish`, `package-portable`, `package-dev`, `package-installer`, and `verify-package`.
- Regular repository acceptance still starts with `dotnet build`, `dotnet test`, and desktop startup smoke.
- When packaging, update, or installer work changes, run the BuildTool chain and package verification as well.
- Safe smoke guidance and package-specific test notes live in `docs/testing.md`.
