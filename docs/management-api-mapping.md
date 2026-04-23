# Management API Mapping

## Audited Sources

- `artifacts/reference-repos/CLIProxyAPI-upstream/internal/api/server.go`
- `artifacts/reference-repos/CLIProxyAPI-upstream/internal/api/handlers/management/*.go`
- `artifacts/reference-repos/Cli-Proxy-API-Management-Center-blackblock/src/services/api/*.ts`
- `artifacts/reference-repos/Cli-Proxy-API-Management-Center-blackblock/src/types/*.ts`
- `artifacts/reference-repos/Cli-Proxy-API-Management-Center-blackblock/src/pages/DashboardPage.tsx`
- `artifacts/reference-repos/Cli-Proxy-API-Management-Center-blackblock/src/pages/SystemPage.tsx`
- `artifacts/reference-repos/CPA-UV/internal/tui/client.go`
- `artifacts/reference-repos/CPA-UV/internal/tui/dashboard.go`

## Page To Endpoint Mapping

| Native page/domain | Upstream endpoint set | Desktop service |
| --- | --- | --- |
| Overview | `/config`, `/api-keys`, `/auth-files`, `/gemini-api-key`, `/codex-api-key`, `/claude-api-key`, `/vertex-api-key`, `/openai-compatibility`, `/v1/models` | `IManagementOverviewService` |
| Accounts & Auth | `/api-keys`, `/auth-files`, `/auth-files/models`, `/auth-files/status`, `/model-definitions/:channel`, `/oauth-excluded-models`, `/oauth-model-alias`, `/{provider}-auth-url`, `/get-auth-status`, `/oauth-callback` | `IManagementAuthService` |
| Quota & Usage | `/usage`, `/usage/export`, `/usage/import` | `IManagementUsageService` |
| Configuration | `/config`, `/config.yaml`, `/debug`, `/proxy-url`, `/request-retry`, `/max-retry-interval`, `/quota-exceeded/*`, `/usage-statistics-enabled`, `/request-log`, `/logging-to-file`, `/logs-max-total-size-mb`, `/error-logs-max-files`, `/ws-auth`, `/force-model-prefix`, `/routing/strategy` | `IManagementConfigurationService` |
| Logs & Diagnostics | `/logs`, `/request-error-logs`, `/request-log-by-id/:id` | `IManagementLogsService` |
| System & Models | `/latest-version`, `/v1/models`, `/api-call` | `IManagementSystemService` |

## Desktop Data Layer Outputs

- Unified HTTP client with:
  - bearer auth for management endpoints
  - version/build header capture from `X-CPA-VERSION`, `X-CPA-COMMIT`, `X-CPA-BUILD-DATE`
  - retry for transient startup/network failures
  - cancellation and per-request timeout support
  - structured `ManagementApiException`
- DTO mapping for:
  - config snapshot
  - provider key lists
  - auth files and OAuth aliases
  - usage snapshots and import/export payloads
  - logs and request-error logs
  - model descriptors
  - overview aggregate snapshot

## Missing Fields / Interfaces

- No backend-breaking field addition was required for phase 7.
- No desktop-only management endpoint was required for phase 7.
- Direct model discovery is handled through backend `/v1/models`.
- Provider passthrough remains available through `/v0/management/api-call`.
- Version tolerance is handled in the desktop client by:
  - header-based server metadata capture
  - tolerant field-name mapping for snake_case / camelCase variants
  - retry on transient `408/502/503/504`
