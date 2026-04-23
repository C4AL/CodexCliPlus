# Repository Gap Analysis

## Snapshot

- Date: 2026-04-24
- Deliverable desktop status: complete
- Full product closure status: not complete yet

## Already completed

- `CPAD` solution and project layout are established.
- Native WPF shell, tray behavior, and theme persistence are established.
- Native management route catalog is established for primary pages and CPA-derived secondary routes.
- The unified management API client and service layer are established for overview, config, providers, auth files, OAuth, quota, usage, logs, and system flows.
- The 9 primary native management pages are established:
  - `Dashboard`
  - `Config`
  - `AI Providers`
  - `Auth Files`
  - `OAuth`
  - `Quota`
  - `Usage`
  - `Logs`
  - `System`
- `Config` is productized as a field-first native page, with field-level saves through audited management endpoints and `Advanced YAML` retained only as a secondary path.
- `Logs` is productized as a native inspection page, with read-only log viewing, incremental/full refresh, clear, error-log inventory, and inline request-log lookup.
- `AI Providers` and `Auth Files` preserve CPA-derived secondary-page semantics through native secondary-route hosts instead of the removed dynamic section shell.
- Local `CPAD.Management.DesignSystem` is established as the only allowed page-facing UI layer for wrapped editors, charts, badges, action bars, tabs, and secondary shells.
- Go backend hosting, path strategy, diagnostics, and secure credential handling are established.
- `CPAD.BuildTool` now exposes the repository-owned command surface for:
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
- Legacy host artifacts are removed:
  - `WebView2`
  - `management.html` hosting
  - onboarding window
  - PowerShell publishing chain
  - Inno Setup packaging chain

## Still pending

### Native pages

- Continue tightening visual parity with the audited CPA WebUI for spacing, motion, density, and empty/error states.
- Continue deepening AI Providers/Auth Files secondary-route parity where a richer 1:1 workflow is still desirable.
- Continue reducing broad analyzer-warning debt and remaining formatting consistency issues in touched surfaces.

### Build and packaging

- Run the BuildTool publish/package/verify flow end to end against real release inputs and owned toolchain assets, not only repository tests.
- Complete the installer/update experience for installed builds as the future guided update path.
- Publish and validate a real stable GitHub Releases line for `Blackblock-inc/Cli-Proxy-API-Desktop`.
- Keep beta reserved until an actual beta release line exists.

### Known non-blocking debt

- `NU1701` compatibility warnings remain accepted technical debt for the current desktop-deliverable milestone.
- Code-analysis warnings remain cleanup work, but they do not block the repository's current desktop acceptance baseline.
