# Repository Gap Analysis

## Already completed

- `CPAD` solution and project layout are established.
- Native WPF shell, tray behavior, and theme persistence are established.
- Go backend hosting, path strategy, diagnostics, and secure credential handling are established.
- Legacy host artifacts are removed:
  - `WebView2`
  - `management.html` hosting
  - onboarding window
  - PowerShell publishing chain
  - Inno Setup packaging chain

## Still pending

### API and data layer

- Unified management API client
- Real data mapping for overview, quota, system, logs, config, and auth flows
- Compatibility coverage for desktop-specific error handling and retries

### Native pages

- Overview page driven by live backend data
- Accounts and auth flows mapped to audited management semantics
- Quota, system, configuration, and diagnostics pages driven by real APIs

### Build and packaging

- Expand `CPAD.BuildTool`
- Add asset verification and package verification
- Add installer, portable, and dev package generation
- Add stable update flow and beta channel reservation
