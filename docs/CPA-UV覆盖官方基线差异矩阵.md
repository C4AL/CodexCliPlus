# CPA-UV 覆盖官方基线差异矩阵

本矩阵只讨论 `CPA-UV` 相对官方主程序基线 `router-for-me/CLIProxyAPI` 的覆盖差异，不包含官方管理中心仓 `router-for-me/Cli-Proxy-API-Management-Center` 的前后端拆分工作。管理中心能力仍按 CPAD 产品边界单独接入。

## 1. 分析基线

截至 `2026-04-23`，当前用于对比的提交如下：

- 官方主程序基线：`router-for-me/CLIProxyAPI` -> `a188159632429b3400d5dadd2b0322afba60de3c`
- 官方管理中心基线：`router-for-me/Cli-Proxy-API-Management-Center` -> `b45639aa0169de8441bc964fb765f2405c10ccf4`
- 本地 `CPA-UV` 源仓：`C:\Users\Reol\workspace\CPA-UV-publish` -> `788076c630035c307b6cdf675554dd7b54cc6cee`

本矩阵的目标不是把 `CPA-UV` 整仓并入 CPAD，而是回答三个问题：

1. 哪些覆盖能力必须继续保留。
2. 哪些覆盖能力必须改写为 CPAD 自己的兼容层。
3. 哪些历史遗留项不应再继承。

## 2. 差异总览

当前对比结果可归纳为：

- `CPA-UV` 独有文件：`13` 个
- 官方基线独有文件：`10` 个
- 同路径但内容不同的文件：`33` 个

结论：

- `CPA-UV` 不是“比官方多几个脚本”的轻量分支，而是已经形成一层系统性的覆盖逻辑。
- CPAD 不能直接执行“整仓覆盖回放”，必须按能力分类抽取、重写和收口。
- 官方主程序基线仍应作为后续升级来源；`CPA-UV` 更适合作为兼容行为与产品经验来源。

## 3. 覆盖分类矩阵

| 类别 | 代表文件 | `CPA-UV` 覆盖内容 | 处理结论 | 处理方式 |
| --- | --- | --- | --- | --- |
| 品牌与版本归一化 | `internal/branding/project.go` `internal/branding/versioning.go` `internal/buildinfo/buildinfo.go` `cmd/server/main.go` | 重写品牌名、版本字符串输出、原始版本与归一化版本映射 | `改写保留` | 保留版本归一化能力，去掉 `CPA-UV` 品牌硬编码，由 CPAD 统一品牌与版本输出层承接 |
| 配置结构与默认值 | `config.example.yaml` `internal/config/config.go` `sdk/config/config.go` `.env.example` | 新增 `codex-app-server-proxy`、默认仓库来源、额外 OAuth 与管理配置 | `部分保留` | 保留与 CPAD 运行时直接相关的结构扩展；默认仓库、文案、端口、目录按 CPAD 规则落地 |
| Codex App Server 代理与本地接口 | `internal/api/codexappserver/root_proxy.go` `internal/api/local_only.go` `internal/api/server.go` `internal/api/handlers/chatgpt/usage.go` | 引入 websocket -> stdio 代理、本地环回限制、ChatGPT usage 聚合接口 | `保留并模块化` | 作为 CPAD 运行时兼容模块保留；`localhost-only` 边界继续保留；账号改写和限额改写放入可替换适配器 |
| 管理更新流与旧管理页 | `assets/management.html` `internal/api/handlers/management/release_update.go` `internal/managementasset/updater.go` `internal/api/handlers/management/*.go` | 旧管理页、自更新入口、管理仓更新流程与配置改写 | `改写保留` | 更新逻辑并入 CPAD 更新中心与桌面 UI；旧 `management.html` 不作为主线界面 |
| 可比额度与调度器 | `sdk/cliproxy/auth/comparable_quota.go` `sdk/cliproxy/auth/comparable_quota_summary.go` `sdk/cliproxy/auth/conductor.go` `sdk/cliproxy/auth/scheduler.go` `sdk/cliproxy/service.go` | 可比额度汇总、池化 usage、调度打分、虚拟消耗平衡 | `保留并重构接口` | 视为关键行为层增量，保留测试并剥离品牌耦合 |
| 运行时认证、执行器与模型表调整 | `internal/auth/antigravity/auth.go` `internal/auth/gemini/gemini_auth.go` `internal/runtime/executor/*.go` `internal/registry/model_definitions.go` `internal/registry/models/models.json` | 对 provider 行为、执行器参数、模型注册表与 translator 请求细节进行覆盖 | `逐项审计` | 以官方主程序为升级面，逐文件验证后决定是否保留，不能整组直接继承 |
| SDK/API 表面差异 | `sdk/api/handlers/handlers.go` `sdk/api/handlers/handlers_request_details_test.go` `internal/api/server_test.go` | SDK 入口、请求细节测试与部分服务对外行为变化 | `待验证后收口` | 先用行为测试定边界，再决定是否保留为 CPAD 兼容层 |
| 文档与发布配置差异 | `README.md` `README_CN.md` `README_JA.md` `.goreleaser.yml` | `CPA-UV` 品牌文档、发布配置、说明文案 | `不继承` | CPAD 保持自己的产品文档和发布链路 |
| 官方基线独有能力 | `sdk/api/handlers/openai/openai_images_handlers.go` `docs/sdk-*.md` `AGENTS.md` | 官方新增 SDK 文档与 `openai images` handler | `按需补继承` | 文档只作为参考资产；`openai images` handler 是否补接取决于 CPAD 当前功能面 |

## 4. 文件桶清单

### 4.1 `CPA-UV` 独有文件

- `assets/management.html`
- `internal/api/codexappserver/root_proxy.go`
- `internal/api/handlers/chatgpt/usage.go`
- `internal/api/handlers/chatgpt/usage_test.go`
- `internal/api/handlers/management/release_update.go`
- `internal/api/handlers/management/release_update_test.go`
- `internal/api/local_only.go`
- `internal/branding/project.go`
- `internal/branding/versioning.go`
- `internal/branding/versioning_test.go`
- `sdk/cliproxy/auth/comparable_quota.go`
- `sdk/cliproxy/auth/comparable_quota_summary.go`
- `sdk/cliproxy/auth/comparable_quota_summary_test.go`

### 4.2 官方主程序基线独有文件

- `AGENTS.md`
- `docs/sdk-access.md`
- `docs/sdk-access_CN.md`
- `docs/sdk-advanced.md`
- `docs/sdk-advanced_CN.md`
- `docs/sdk-usage.md`
- `docs/sdk-usage_CN.md`
- `docs/sdk-watcher.md`
- `docs/sdk-watcher_CN.md`
- `sdk/api/handlers/openai/openai_images_handlers.go`

### 4.3 同路径差异文件

- `.env.example`
- `.goreleaser.yml`
- `cmd/server/main.go`
- `config.example.yaml`
- `internal/api/handlers/management/api_tools.go`
- `internal/api/handlers/management/auth_files.go`
- `internal/api/handlers/management/config_basic.go`
- `internal/api/handlers/management/handler.go`
- `internal/api/server.go`
- `internal/api/server_test.go`
- `internal/auth/antigravity/auth.go`
- `internal/auth/antigravity/constants.go`
- `internal/auth/gemini/gemini_auth.go`
- `internal/buildinfo/buildinfo.go`
- `internal/config/config.go`
- `internal/managementasset/updater.go`
- `internal/registry/model_definitions.go`
- `internal/registry/models/models.json`
- `internal/runtime/executor/antigravity_executor.go`
- `internal/runtime/executor/gemini_cli_executor.go`
- `internal/translator/codex/openai/responses/codex_openai-responses_request.go`
- `internal/translator/codex/openai/responses/codex_openai-responses_request_test.go`
- `README.md`
- `README_CN.md`
- `README_JA.md`
- `sdk/api/handlers/handlers.go`
- `sdk/api/handlers/handlers_request_details_test.go`
- `sdk/cliproxy/auth/conductor.go`
- `sdk/cliproxy/auth/scheduler.go`
- `sdk/cliproxy/auth/scheduler_test.go`
- `sdk/cliproxy/auth/types.go`
- `sdk/cliproxy/service.go`
- `sdk/config/config.go`

## 5. 当前结论

### 5.1 必须保留的核心覆盖能力

- `Codex App Server` 根代理与 `localhost-only` 安全边界
- 可比额度、池化 usage 与调度器行为
- 配置层中与 CPAD 受控运行时直接相关的结构扩展
- 版本归一化能力中与兼容显示、更新判断相关的部分

### 5.2 必须改写后再保留的部分

- 所有 `CPA-UV` 品牌、仓库地址、文案与发布脚本
- 旧管理页 `assets/management.html` 对应的 UI 继承路径
- 自更新与管理更新逻辑中的旧目录假设、旧仓结构假设与旧产品入口

### 5.3 当前不应继续继承的部分

- 旧品牌 README 和 `.goreleaser` 配置
- 仅服务于旧仓结构的壳层说明与历史脚本入口
- 没有行为测试支撑、但会扩大维护面的散装补丁
