# CPAD Desktop Architecture

## Fixed rules

- The runtime frontend baseline is the vendored official upstream Management Center WebUI, not a native WPF reimplementation.
- `CPAD.App` is a minimal native shell: windowing, tray, backend lifecycle, secure storage, updates, and host-level blocker UI.
- The official WebUI is served from local packaged files through `WebView2`, not from a remote URL.
- Backend remains `CLIProxyAPI`; the desktop app is still the source of truth for backend startup and management credentials.
- Desktop bootstrap is the first login path in desktop mode and must not persist `managementKey` into browser storage.
- Release packaging remains `CPAD.BuildTool + MicaSetup`; old PowerShell/Inno chains stay retired.
- Stable desktop release discovery is sourced from GitHub Releases for `Blackblock-inc/Cli-Proxy-API-Desktop`.
- Beta remains a reserved channel until a real beta release line exists.
- Portable packages stay manual/no-auto-update packages.

## Current delivery status

- As of 2026-04-24, the active desktop path is the single `WebView2` host window in `CPAD.App`.
- Native management pages and native navigation shells are no longer the product frontend target.
- Repository acceptance baseline remains:
  - `dotnet restore`
  - `dotnet build --no-restore`
  - `dotnet test --no-build`
- Packaging acceptance additionally requires BuildTool publish/package output plus `verify-package`.

## Current layering

- `CPAD.App`
  Minimal WPF shell and WebView2 host, tray integration, blocker UI, and desktop bootstrap injection.
- `resources/webui/upstream/source`
  Vendored official upstream WebUI source tree pinned by `resources/webui/upstream/sync.json`.
- `resources/webui/upstream/dist`
  Vendored built WebUI assets copied into desktop publish/package outputs.
- `CPAD.Core`
  Shared contracts, enums, constants, about metadata, and desktop bootstrap payload types.
- `CPAD.Infrastructure`
  Backend asset preparation, config writing, process hosting, logging, diagnostics, and secure storage.
- `CPAD.BuildTool`
  Unified asset fetch/verify, vendored WebUI build, publish, package, and package verification toolchain.

## Backend and authentication constraints

- Backend binds to local loopback only.
- Backend config written by the desktop app keeps the upstream control panel disabled:
  `disable-control-panel: true`
- The desktop shell remains the source of truth for the management key through secure storage.
- Desktop bootstrap must provide:
  - `desktopMode`
  - `apiBase`
  - `managementKey`
- Desktop mode may persist non-sensitive UI state such as theme, language, and cached layout state, but not `managementKey`.

## Packaging constraints

- Publish output must contain:
  - `CPAD.exe`
  - `assets/webui/upstream/dist/index.html`
  - `assets/webui/upstream/sync.json`
- Portable packages are marked by `portable-mode.json` and keep writable state under a package-local `data/` root.
- Development packages are marked by `dev-mode.json` and are intended for validation/developer use with isolated `app/artifacts/dev-data` scaffolding.
- Installed packages default to installed mode and keep using the normal user data root under `%LocalAppData%`.
- Installer metadata must describe both packaged WebUI assets and the external WebView2 runtime requirement.

## Host behavior constraints

- Main window is a single WebView2 host, not a native page router.
- Closing the main window minimizes to tray by default when tray mode is enabled.
- Tray menu remains:
  - Open
  - Restart Backend
  - Check Updates
  - Exit
- External links from the hosted WebUI must open in the system browser.
- Missing WebView2 runtime, missing packaged WebUI assets, or backend startup failure must show a native blocker view instead of a blank host.
