# CPAD Native Architecture

## Fixed rules

- The desktop frontend must remain native WPF.
- The product must not revert to a `WebView2` host.
- The Go backend remains `CLIProxyAPI`; the desktop app hosts and manages it.
- Page semantics are derived from `CPA-UV` and the Management Center repositories.
- Window structure and tray-first interaction are derived from `BetterGI`.
- The release path stays `CPAD.BuildTool + MicaSetup`, not `PowerShell + Inno Setup`.
- Historical `WebView2`, PowerShell, or Inno residue in generated outputs is cleanup debt, not an allowed source of truth for architecture decisions.
- Stable desktop release discovery is sourced from GitHub Releases for `Blackblock-inc/Cli-Proxy-API-Desktop`.
- Beta remains a reserved desktop channel until a real beta release line exists.
- Portable packages are a first-class package type, but they must remain manual/no-auto-update packages.
- Any future automated update flow belongs to the installer route, not to the portable route.

## Current layering

- `CPAD.App`
  Native window, navigation, tray integration, and theme behavior.
- `CPAD.Core`
  Shared contracts, enums, constants, and domain models.
- `CPAD.Infrastructure`
  Backend asset preparation, config writing, process hosting, logging, diagnostics, and secure storage.
- `CPAD.BuildTool`
  Unified asset fetch/verify, publish, package, and package verification toolchain.

## Backend hosting constraints

- Backend binds to local loopback only.
- Desktop pages talk directly to management APIs.
- Backend config written by the desktop app disables the control panel:
  `disable-control-panel: true`
- Management secrets are not stored in plaintext desktop config.

## Packaging constraints

- Portable packages are marked by `portable-mode.json` and keep their writable state under a package-local `data/` root.
- Development packages are marked by `dev-mode.json` and are intended for validation/developer use with isolated `app/artifacts/dev-data` scaffolding, not as the primary end-user channel.
- Installed packages default to installed mode and should continue using the normal user data root under `%LocalAppData%`.
- Installer artifacts are shaped around MicaSetup metadata, a current-user-first install model, and a concrete `CPAD.Setup.<version>.exe` output, even where the full 10.x installer experience is still unfinished.
- Package validation stays inside `CPAD.BuildTool`; release packaging should not reintroduce ad-hoc PowerShell orchestration.

## Update constraints

- The desktop may check stable GitHub Releases and surface release metadata, assets, and release links.
- The desktop must not fabricate updates when the repository has no stable release; the correct state is `No stable release`.
- Installed builds are the only valid future target for guided update/install flows.
- Portable and development modes should not rely on automatic update application.
- Backend config continues to disable the upstream auto-update panel so desktop-controlled update policy remains authoritative.

## Uninstall constraints

- Uninstall cleanup must stay bounded to CPAD-owned roots, start menu entries, registry values, firewall rules, and scheduled tasks.
- The installer route must preserve a `keep user data` option instead of forcing unconditional deletion.
- Uninstall metadata is part of the package contract and must remain verifiable from BuildTool output.

## Verification constraints

- `dotnet build`, `dotnet test`, isolated smoke, and `verify-package` are the canonical acceptance layers.
- Package verification must cover portable, development, and installer outputs, not only the desktop executable itself.
- Installer and uninstall behavior require isolated validation; they must not be inferred only from the existence of an `.exe` artifact.

## UI constraints

- Main window is a native navigation shell.
- Logs and diagnostics belong on dedicated pages, not an embedded host console.
- Closing the main window minimizes to tray by default.
- Tray menu remains:
  - Open Main Interface
  - Restart Backend
  - Check Updates
  - Exit and Stop Backend
