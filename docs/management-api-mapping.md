# 管理 API 映射

## 审计来源

- `src/CodexCliPlus.Infrastructure/Management/*.cs`
- `src/CodexCliPlus.Infrastructure/Backend/*.cs`
- `src/CodexCliPlus.Core/Abstractions/Management/*.cs`
- `src/CodexCliPlus.Core/Models/Management/*.cs`
- `resources/webui/upstream/source/src/services/api/*.ts`
- `resources/webui/upstream/source/src/types/*.ts`
- `resources/webui/modules/cpa-uv-overlay/module.json`
- `resources/webui/modules/cpa-uv-overlay/traceability.json`
- `resources/webui/modules/cpa-uv-overlay/source/src/router/MainRoutes.tsx`
- `resources/webui/modules/cpa-uv-overlay/source/src/pages/*.tsx`

## 页面到端点

| 页面/领域 | 上游端点集合 | 桌面服务 |
| --- | --- | --- |
| 运行概览 | `/config`, `/auth-files`, `/usage`, `/logs`, optional local `wham/usage` bridge via `/api-call` | WebUI 直连管理 API |
| 桌面外壳状态/设置摘要 | `/config`, `/api-keys`, `/auth-files` | `IManagementOverviewService` |
| 账号与认证 | `/api-keys`, `/auth-files`, `/auth-files/models`, `/auth-files/status`, `/model-definitions/:channel`, `/oauth-excluded-models`, `/oauth-model-alias`, `/{provider}-auth-url`, `/get-auth-status`, `/oauth-callback` | `IManagementAuthService` |
| 限额与用量 | `/usage`, `/usage/export`, `/usage/import` | `IManagementUsageService` |
| 配置 | `/config`, `/config.yaml`, `/debug`, `/proxy-url`, `/request-retry`, `/max-retry-interval`, `/quota-exceeded/*`, `/usage-statistics-enabled`, `/request-log`, `/logging-to-file`, `/logs-max-total-size-mb`, `/error-logs-max-files`, `/ws-auth`, `/force-model-prefix`, `/routing/strategy` | `IManagementConfigurationService` |
| 日志与诊断 | `/logs`, `/request-error-logs`, `/request-log-by-id/:id` | `IManagementLogsService` |
| 系统与模型 | `/latest-version`, optional `/install-update`, `/v1/models`, `/api-call` | `IManagementSystemService` |

## 桌面数据层输出

- 统一 HTTP client：
  - 管理端点 bearer auth
  - `X-CPA-VERSION`、`X-CPA-COMMIT`、`X-CPA-BUILD-DATE` 响应头捕获
  - 启动期和瞬时网络失败重试
  - cancellation 和 per-request timeout
  - 结构化 `ManagementApiException`
- DTO 映射：
  - config snapshot
  - provider key lists
  - auth files and OAuth aliases
  - usage snapshots and import/export payloads
  - logs and request-error logs
  - model descriptors
  - shell status and settings summary snapshots

## 兼容约束

- 本轮清理没有新增或破坏后端管理端点。
- WebView2 bridge payload 继续使用现有 bootstrap 字段。
- 模型发现仍通过后端 `/v1/models`。
- Provider passthrough 仍通过 `/v0/management/api-call`。
- 桌面客户端通过 header metadata、snake_case/camelCase 容错和 `408/502/503/504` 重试处理后端版本差异。
- Overlay-only WebUI 提取继续保留在 `resources/webui/modules/cpa-uv-overlay/*`，用于证明路由、页面和行为来源。
