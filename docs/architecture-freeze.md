# CPAD Native Architecture

## Fixed rules

- The desktop frontend must remain native WPF.
- The product must not revert to a `WebView2` host.
- The Go backend remains `CLIProxyAPI`; the desktop app hosts and manages it.
- Page semantics are derived from `CPA-UV` and the Management Center repositories.
- Window structure and tray-first interaction are derived from `BetterGI`.
- The long-term release path is `CPAD.BuildTool + MicaSetup`, not `PowerShell + Inno Setup`.

## Current layering

- `CPAD.App`
  Native window, navigation, tray integration, and theme behavior.
- `CPAD.Core`
  Shared contracts, enums, constants, and domain models.
- `CPAD.Infrastructure`
  Backend asset preparation, config writing, process hosting, logging, diagnostics, and secure storage.
- `CPAD.BuildTool`
  Placeholder for the future unified build and packaging toolchain.

## Backend hosting constraints

- Backend binds to local loopback only.
- Desktop pages talk directly to management APIs.
- Backend config written by the desktop app disables the control panel:
  `disable-control-panel: true`
- Management secrets are not stored in plaintext desktop config.

## UI constraints

- Main window is a native navigation shell.
- Logs and diagnostics belong on dedicated pages, not an embedded host console.
- Closing the main window minimizes to tray by default.
- Tray menu remains:
  - Open Main Interface
  - Restart Backend
  - Check Updates
  - Exit and Stop Backend
