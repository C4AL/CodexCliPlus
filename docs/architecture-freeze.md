# CPAD Native Architecture

## Fixed rules

- The desktop frontend must remain native WPF.
- The product must not revert to a `WebView2` host.
- The Go backend remains `CLIProxyAPI`; the desktop app hosts and manages it.
- Page semantics are derived from `CPA-UV` and the Management Center repositories.
- Window structure and tray-first interaction are derived from the current `WPF-UI + BetterGI` shell lineage.
- `BetterGI` is not the only visual source. Any open-source capability may be used when it improves CPA route fidelity, but it must be wrapped behind local CPAD controls and resource keys.
- The release path stays `CPAD.BuildTool + MicaSetup`, not `PowerShell + Inno Setup`.
- Historical `WebView2`, PowerShell, or Inno residue in generated outputs is cleanup debt, not an allowed source of truth for architecture decisions.
- Stable desktop release discovery is sourced from GitHub Releases for `Blackblock-inc/Cli-Proxy-API-Desktop`.
- Beta remains a reserved desktop channel until a real beta release line exists.
- Portable packages are a first-class package type, but they must remain manual/no-auto-update packages.
- Any future automated update flow belongs to the installer route, not to the portable route.

## Current delivery status

- As of 2026-04-24, the native desktop deliverable baseline is met:
  - all 9 primary management pages exist as native WPF pages
  - route-backed navigation remains aligned with the official CPA route tree
  - the product does not rely on a `WebView2` fallback host
- `Config` is now a field-first native page with `Advanced YAML` kept as a secondary escape hatch instead of the default workflow.
- `Logs` is now a native inspection page with a read-only log viewer, inline request log lookup, incremental refresh, full refresh, and clear actions.
- `AI Providers` and `Auth Files` remain native route-hosted experiences with secondary-route infrastructure, and are no longer expected to revert to inline dynamic editor shells.
- Repository acceptance for the desktop deliverable currently means:
  - `dotnet restore`
  - `dotnet build --no-restore`
  - `dotnet test --no-build`
  all remain green in the current repo state.
- Full-product closure is intentionally wider than desktop-deliverable closure:
  - the release and packaging architecture is fixed
  - the BuildTool command surface exists
  - end-to-end release publication, installer/update UX, and final channel operations remain follow-up work outside the desktop 100% milestone

## Current layering

- `CPAD.App`
  Native window, navigation, tray integration, theme behavior, route switching, and page orchestration.
- `CPAD.Management.DesignSystem`
  Local design tokens, wrapped controls, native editor/chart hosts, and shared management page shells.
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
- Main navigation route tree must stay aligned with official CPA routes:
  - `Dashboard`
  - `Config`
  - `AI Providers`
  - `Auth Files`
  - `OAuth`
  - `Quota`
  - `Usage`
  - `Logs`
  - `System`
- AI Providers and Auth Files must preserve official secondary-page semantics through native route shells and back-stack handling.
- Business pages may not directly use third-party UI namespaces; they must consume wrapped controls from `CPAD.Management.DesignSystem`.
- Logs, editors, and charts belong on dedicated native pages, not an embedded host console or generic dynamic grid surface.
- Closing the main window minimizes to tray by default.
- Tray menu remains:
  - Open Main Interface
  - Restart Backend
  - Check Updates
  - Exit and Stop Backend
