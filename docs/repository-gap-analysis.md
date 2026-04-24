# Repository Gap Analysis

## Snapshot

- Date: 2026-04-24
- Active desktop frontend baseline: vendored official upstream WebUI hosted in WebView2
- Full product closure status: not complete yet

## Already completed

- `CPAD` solution and project layout are established.
- Minimal WPF shell, tray behavior, backend hosting, diagnostics, and secure credential handling are established.
- Vendored official WebUI source now lives under `resources/webui/upstream/source`.
- Vendored WebUI build metadata is pinned by `resources/webui/upstream/sync.json`.
- Desktop bootstrap flow is established for:
  - `desktopMode`
  - `apiBase`
  - `managementKey`
- Desktop-mode auth recovery is wired to consume bootstrap first and to avoid persisting `managementKey` into browser storage.
- `CPAD.BuildTool` owns the repository command surface for:
  - `fetch-assets`
  - `verify-assets`
  - `publish`
  - `package-portable`
  - `package-dev`
  - `package-installer`
  - `verify-package`
- Automated verification is established across:
  - `dotnet restore`
  - `dotnet build`
  - `dotnet test`
  - isolated smoke coverage
  - BuildTool packaging and verification tests

## Still pending

### Frontend parity and acceptance

- Run the fixed-viewport screenshot acceptance pass against the vendored upstream commit for the 9 primary pages and the defined secondary flows.
- Continue upstream sync hygiene so future frontend changes are commit-based syncs rather than ad hoc desktop rewrites.

### Build and packaging

- Run the full BuildTool publish/package/verify flow end to end against real release inputs and owned toolchain assets, not only repository tests.
- Complete the installer/update experience for installed builds as the future guided update path.
- Publish and validate a real stable GitHub Releases line for `Blackblock-inc/Cli-Proxy-API-Desktop`.
- Keep beta reserved until an actual beta release line exists.

### Known non-blocking debt

- `NU1701` compatibility warnings remain accepted technical debt for the current milestone.
- Legacy native management page code still exists in the tree for compile compatibility, but it is no longer the runtime product path.
- Code-analysis warnings remain cleanup work, but they do not block the current desktop acceptance baseline.
