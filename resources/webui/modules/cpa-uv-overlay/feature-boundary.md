# CPA-UV Overlay Feature Boundary

- Route keep: `/dashboard` remains the upstream dashboard.
- Route add: `/dashboard/overview` is injected through the overlay only.
- Route compatibility: `/console` redirects to `/dashboard/overview`.
- Overview keep: 24-hour Codex account summary, quota bridge rendering, recent request events table.
- System keep: dual-version awareness, latest-version check, optional install-update action.
- System remove: CPA-UV branding, external repository links, release/source links.
- Dependency keep: local `wham/usage` bridge path only.
- Dependency remove: `codex-app-server-proxy` and related config fields.
