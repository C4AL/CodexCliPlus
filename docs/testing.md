# Testing And Safe Verification

## Purpose

Use `tests/CPAD.Tests/Smoke/SafeSmoke.ps1` as the default acceptance entry point for desktop smoke checks when a local CPA-UV or other backend may already be running on the machine.

Default mode is static-only and does not launch `CPAD.exe`.

`-Launch` mode starts `CPAD.exe` with an isolated data/profile/home layout and only stops the exact PID started by the script plus any `cli-proxy-api.exe` child process rooted inside that isolated smoke directory.

This document also defines how to validate BuildTool packaging, update behavior, and installer/uninstall metadata without treating unfinished later-stage work as already accepted.

## Default acceptance loop

For normal repository work, use this order:

```powershell
dotnet build CliProxyApiDesktop.sln
dotnet test tests\CPAD.Tests\CPAD.Tests.csproj
powershell -ExecutionPolicy Bypass -File tests\CPAD.Tests\Smoke\SafeSmoke.ps1
powershell -ExecutionPolicy Bypass -File tests\CPAD.Tests\Smoke\SafeSmoke.ps1 `
  -Launch `
  -AppPath "$PWD\artifacts\buildtool\publish\win-x64\CPAD.exe" `
  -RemoveSmokeRoot
```

When packaging or release behavior changes, append:

```powershell
dotnet run --project src\CPAD.BuildTool -- fetch-assets
dotnet run --project src\CPAD.BuildTool -- verify-assets
dotnet run --project src\CPAD.BuildTool -- publish
dotnet run --project src\CPAD.BuildTool -- package-portable
dotnet run --project src\CPAD.BuildTool -- package-dev
dotnet run --project src\CPAD.BuildTool -- package-installer
dotnet run --project src\CPAD.BuildTool -- verify-package
```

## Automated coverage map

- `tests/CPAD.Tests/BuildTool/BuildToolCommandTests.cs` covers BuildTool help/command parsing, asset fetch/verify, and package verification expectations for portable, development, and installer outputs.
- `tests/CPAD.Tests/Paths/AppPathServiceTests.cs` covers installed, portable, and development data-mode resolution and managed directory creation.
- `tests/CPAD.Tests/Updates/GitHubReleaseUpdateServiceTests.cs` covers stable release discovery, the `No stable release` state, beta reservation, and installer-asset versus portable-asset detection.
- `tests/CPAD.Tests/Dependencies/DependencyHealthServiceTests.cs` covers dependency repair triggers and repair-mode entry signals.
- `tests/CPAD.Tests/Diagnostics/DiagnosticsServiceTests.cs` and `tests/CPAD.Tests/Backend/BackendProcessManagerIntegrationTests.cs` cover diagnostic export and backend lifecycle behavior.

## Safety Rules

- Always isolate `CPAD_APP_ROOT`, `USERPROFILE`, `HOME`, and `CODEX_HOME` for UI smoke. The script also isolates `TEMP` and `TMP`.
- Never use name-based cleanup such as `taskkill /IM cli-proxy-api.exe /F`, `Stop-Process -Name cli-proxy-api`, or any broad `Get-Process` kill loop.
- Never point launch smoke at a legacy `artifacts/publish/win-x64` tree if one reappears from old local tooling or leftover outputs.
- Prefer an explicit `-AppPath` from the current CPAD build output, typically `artifacts/buildtool/publish/win-x64/CPAD.exe`.
- Do not run smoke against the real user profile or real `CODEX_HOME`.

## Manual UI Constraints

These actions are not safe for routine local smoke unless you deliberately want their side effects:

- `Settings -> Save Settings`: touches the real current-user Run key through `StartupRegistrationService`, which is not isolated by `CPAD_APP_ROOT`.
- `Tools -> Launch Selected Source`: spawns a separate PowerShell session and is unnecessary for acceptance.
- `Updates -> Open Release Page`: opens the external browser and is not useful for smoke.

These actions are only safe when the smoke session is isolated and you explicitly want the behavior:

- `Tools -> Apply Source Switch`: rewrites `CODEX_HOME`, so only use it when `CODEX_HOME` is isolated.
- `System -> Start Backend` and `System -> Run Connectivity Probe`: safe only inside the isolated smoke root.
- `About -> Export Diagnostics` and `Dependency Repair -> Export Diagnostics`: safe when `CPAD_APP_ROOT` points at the smoke root because diagnostics stay under that tree.

## Package-specific verification

- Installed package:
  Verify `package-installer` produced both `CPAD.Setup.<version>.exe` and `CPAD.Setup.<version>.<rid>.zip`.
  Treat `verify-package` plus metadata inspection as the default automated gate.
  Do manual install/uninstall checks only in an isolated test user, VM, or disposable environment.
- Portable package:
  Verify the zip contains `CPAD.exe` and `portable-mode.json`.
  Treat it as a manual/no-auto-update distribution.
  Launch it only with isolated writable state when doing smoke.
- Development package:
  Verify the zip contains `app/CPAD.exe`, `app/dev-mode.json`, and `app/artifacts/dev-data/.gitkeep`.
  Treat it as a developer validation artifact, not a production distribution.

## Update verification

- Stable release checks are correct when they query the GitHub Releases latest endpoint for this repository.
- `No stable release` is a valid passing state when the repository has no published stable release.
- Beta is expected to remain reserved even if the preference exists in settings and packaging metadata.
- A stable release asset list may contain installer, portable, or other assets; only installer-shaped assets should be treated as candidates for a future guided install/update path.

## Uninstall verification

- Current automated proof is limited to installer metadata and package verification, especially the presence of `uninstall-cleanup.json`.
- Cleanup must stay bounded to CPAD-owned roots, CPAD-owned start menu entries, CPAD registry values, and CPAD firewall/task names.
- The `keep user data` option is part of the uninstall contract and should remain documented in metadata even before full installer acceptance is closed.
- Do not perform uninstall acceptance on the main development environment. Use an isolated machine, VM, or disposable user profile when that phase is being actively verified.

## Current Acceptance Risks

- Active source is clean of live `WebView2` namespace/package usage, and `resources/webview2/` has been removed from the active tree.
- Historical `artifacts/publish` and `artifacts/installer` trees were removed during cleanup. If an old local publish tree reappears later, do not treat it as proof of the current architecture.
- Generated residue also exists under `src/**/bin`, `src/**/obj`, `tests/**/bin`, and `tests/**/obj`. Acceptance scans must separate generated files from active source.
- A generated `CPAD.Setup.*.exe` or staging zip is necessary packaging evidence, but it does not by itself prove full installer/update/uninstall acceptance is complete.
- `resources/backend/windows-x64/README.md` and `README_CN.md` are upstream backend docs. They describe CLIProxyAPI behavior, not the desktop app's final UI or installer guarantees.
- `Dependency Repair` only repairs bundled backend/runtime assets. It does not close all 10.x gaps such as uninstall cleanup, runtime installation policy, or update-chain completion.

## Recommended Commands

Static-only residue and safety audit:

```powershell
powershell -ExecutionPolicy Bypass -File tests\CPAD.Tests\Smoke\SafeSmoke.ps1
```

Isolated launch smoke against the current BuildTool publish output:

```powershell
powershell -ExecutionPolicy Bypass -File tests\CPAD.Tests\Smoke\SafeSmoke.ps1 `
  -Launch `
  -AppPath "$PWD\artifacts\buildtool\publish\win-x64\CPAD.exe" `
  -RemoveSmokeRoot
```

If you are verifying a direct `dotnet publish` output instead of BuildTool output, keep the same script and only change `-AppPath` to the actual `CPAD.exe` path.

Recommended full validation sequence when packaging and smoke both matter:

```powershell
dotnet build CliProxyApiDesktop.sln
dotnet test tests\CPAD.Tests\CPAD.Tests.csproj
powershell -ExecutionPolicy Bypass -File tests\CPAD.Tests\Smoke\SafeSmoke.ps1
powershell -ExecutionPolicy Bypass -File tests\CPAD.Tests\Smoke\SafeSmoke.ps1 `
  -Launch `
  -AppPath "$PWD\artifacts\buildtool\publish\win-x64\CPAD.exe" `
  -RemoveSmokeRoot
dotnet run --project src\CPAD.BuildTool -- verify-package
```

## Notes

- `SafeSmoke.ps1` defaults to reporting warnings for generated residue and upstream bundled docs, because those are useful acceptance signals but not all of them are active-source regressions.
- The script returns a non-zero exit code only when it finds active legacy source/build references that should not still be present.
- This document does not change `build_steps.md`; it only defines a safe way to execute the existing smoke and verification work.
