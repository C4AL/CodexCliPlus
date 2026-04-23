# Build, Packaging, And Release

## Current workflow

Use the solution build, test project, and native app startup as the active validation path:

```powershell
dotnet build CliProxyApiDesktop.sln
dotnet test tests/CPAD.Tests/CPAD.Tests.csproj
dotnet run --project src/CPAD.App/CPAD.App.csproj
```

When changes touch assets, publish output, or release packaging, run the C# BuildTool chain instead of reviving the retired PowerShell/Inno flow:

```powershell
dotnet run --project src/CPAD.BuildTool -- fetch-assets
dotnet run --project src/CPAD.BuildTool -- verify-assets
dotnet run --project src/CPAD.BuildTool -- publish
dotnet run --project src/CPAD.BuildTool -- package-portable
dotnet run --project src/CPAD.BuildTool -- package-dev
dotnet run --project src/CPAD.BuildTool -- package-installer
dotnet run --project src/CPAD.BuildTool -- verify-package
```

Default BuildTool output roots are:

- Publish: `artifacts/buildtool/publish/<rid>/`
- Packages: `artifacts/buildtool/packages/`
- Installer staging: `artifacts/buildtool/installer/stage/`

## Minimum smoke checks

- The app starts into the native WPF shell.
- Tray icon and tray actions are available.
- The desktop app can start the managed backend.
- `/healthz` responds successfully.
- The automated test suite passes.

## Current release topology

- `PowerShell + Inno Setup` is no longer a formal release chain for this repository.
- `CPAD.BuildTool` is the active build/publish/package entry point.
- `package-portable` emits a portable zip with `portable-mode.json` and package-local data intent.
- `package-dev` emits a development zip with `app/dev-mode.json` plus dev-data scaffolding for validation scenarios.
- `package-installer` emits a MicaSetup-based installer executable plus a staging zip that includes `mica-setup.json`, the app payload, and packaging metadata.
- `verify-package` validates the produced package set and keeps package structure checks in the BuildTool lane.

## Package types

- Installed package:
  Produced by `package-installer`.
  Emits `CPAD.Setup.<version>.exe` and a staging zip.
  Uses current-user install intent and points at `%LocalAppData%\\Programs\\CPAD`.
- Portable package:
  Produced by `package-portable`.
  Emits `CPAD.Portable.<version>.<rid>.zip`.
  Intended to keep writable state beside the app and remain a manual distribution path.
- Development package:
  Produced by `package-dev`.
  Emits `CPAD.Dev.<version>.<rid>.zip`.
  Includes `app/dev-mode.json` plus `app/artifacts/dev-data` scaffolding and is intended for developer/testing distribution rather than the primary end-user channel.

## Update channel direction

- Stable desktop update checks already query `https://api.github.com/repos/Blackblock-inc/Cli-Proxy-API-Desktop/releases/latest`.
- Beta is intentionally preserved as a reserved configuration entry, but stable GitHub Releases remain the only active desktop update source in the current phase.
- The desktop currently reports release state, release assets, release page links, and whether a stable release exposes an installer-shaped asset. It does not yet claim a fully finished end-to-end self-updater.
- Portable and development packages should remain manual/no-auto-update packages. If installer-driven update automation is added later, it should stay on the installer route rather than mutating non-installed deployments in place.

## Installer And Uninstall Metadata

- BuildTool writes `dependency-precheck.json`, `update-policy.json`, and `uninstall-cleanup.json` into installer packaging metadata.
- Current metadata already records:
  bundled-first dependency policy,
  stable release source,
  beta reserved status,
  keep-user-data uninstall behavior,
  and bounded cleanup roots for config/cache/logs/backend/diagnostics/runtime and related CPAD-owned entries.
- `verify-package` requires installer metadata to be present in the staging zip, but that is still package validation, not a substitute for later isolated install/uninstall acceptance.

## Current expectation

Repository acceptance still starts with successful build, successful tests, and a working desktop startup smoke run. Packaging work should additionally pass the relevant `CPAD.BuildTool` commands and `verify-package`.
