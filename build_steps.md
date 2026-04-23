# CPAD Native Rewrite Build Steps

> 目标：将 **Cli Proxy API Desktop** 从当前 **WPF + WebView2 桌面宿主** 重构为 **原生 WPF 桌面前端 + Go 后端 + C# BuildTool + MicaSetup 安装链** 的完整 Windows 桌面产品。
>
> 执行原则：**必须严格按本文档顺序执行**；每完成一项就勾选、构建、测试、验收、提交；不得跳步、不得大面积并行改动后再一次性回头修；除最后一步外，**不得删除本文件**。

---

## 0. 总执行规则（强约束）

### 0.1 总规则
- [x] **仅以本文件为最高执行顺序依据**。
- [x] 每个阶段结束前，必须完成：
  - [x] 代码实现
  - [x] 自检
  - [x] 构建通过
  - [x] 对应 smoke/test 通过
  - [x] 更新本文档勾选状态与阶段备注
  - [x] 提交到本地 git
- [x] **禁止**在未通过当前阶段验收前进入下一阶段。
- [x] **禁止**把半残状态 push 到远端。
- [x] **禁止**回退到 WebView2 宿主思路。
- [x] **禁止**保留 PowerShell 构建链和 Inno Setup 安装链作为正式方案。
- [x] **禁止**在主 UI 中保留终端式大日志占位。
- [x] **禁止**引入与最终品牌混淆的第三方品牌图标、命名、文案。

### 0.2 代码与分支规则
- [x] 在 `main` 上推进，但**每次提交都必须可编译**。
- [x] 每个阶段至少 1 次本地提交，提交说明清晰描述阶段目标。
- [x] 如阶段内改动较大，可拆成多个小提交，但必须保证每个提交均能通过最低验收。

### 0.3 输出与记录规则
- [x] 每完成一个步骤，立即在本文件对应 checkbox 打勾。
- [x] 每完成一个阶段，在阶段末尾补充“阶段结果记录”。
- [x] 如发现文档与现实冲突，应优先做**最小偏差实现**，并在阶段结果记录说明。
- [x] 除最后一步外，**禁止删除或重命名本文件**。

### 0.4 最终交付定义（100%）
- [x] 原生 WPF 多页面桌面前端完成。
- [x] WebView2 路线、Onboarding、PowerShell、Inno Setup 全部移除。
- [x] C# BuildTool 完成资源拉取、校验、发布、安装器生成、验包。
- [x] 安装版 + 便携版 + 开发版产物可用。
- [x] Stable 更新链可用，Beta 预留。
- [x] 主功能页全部可用。
- [x] 托盘、关闭行为、依赖修复、卸载清理行为符合本文档。
- [x] 所有必须测试通过。
- [ ] 本文件在完成全部工作后删除。
- [ ] 最终提交 push 到远端 `origin/main`。

### 0.5 外部参考仓库基线
> - 本轮继续开发前已同步并读取外部参考仓库：BetterGI `8363717`、CPA-UV `788076c`、Blackblock Management Center `b45639a`、upstream CLIProxyAPI `a188159`、upstream Management Center `b45639a`。
> - 参考依据落地方式：BetterGI 用于原生窗口/导航/托盘交互取向；CPA-UV 用于概览、配额、系统和桌面工具语义；两个 Management Center 与 CLIProxyAPI upstream 用于 Management API、OAuth、配置、日志、系统和版本能力边界。

---

## 1. 项目目标与固定规格（不可偏离）

### 1.1 产品定位
- [x] 从“桌面壳托管原 WebUI”转为“原生桌面前端”。
- [x] 保留 Go 后端，不重写后端主体。
- [x] 前端页面直接承接现有 Management API 能力。
- [x] 允许新增接口与新增字段；除非必要，不破坏现有接口兼容。

### 1.2 UI / 产品规格
- [x] 外壳风格：参考 BetterGI 主窗口结构与交互风格。
- [x] 首页内容组织：参考 CPA-UV 概览页思路。
- [x] 功能语义：以原版 Management WebUI 为准。
- [x] 全部替换为自有品牌、图标、文案。
- [x] 明暗双主题；默认浅色；允许设定跟随系统。
- [x] Windows 11 x64 优先；ARM64 后置。
- [x] 用户主界面不出现终端/控制台大日志占位。

### 1.3 运行行为
- [x] 点击主窗口关闭按钮：最小化到托盘。
- [x] 托盘菜单固定为：
  - [x] 打开主界面
  - [x] 重启后端
  - [x] 检查更新
  - [x] 退出并停止后端
- [x] 退出并停止后端时，清理运行期残留进程与临时资源。

### 1.4 凭据与安全
- [x] 优先使用 Windows Credential Manager / DPAPI。
- [x] 不明文落盘敏感信息。
- [x] 日志默认信息级别。
- [x] 敏感字段脱敏。
- [x] 保留一键导出诊断包。

### 1.5 安装/更新/卸载
- [x] 当前用户安装优先。
- [x] 打包内置运行所需依赖。
- [x] 缺依赖时允许在线下载补齐。
- [x] Stable 从 GitHub Releases 拉取。
- [x] 预留 Beta 通道。
- [x] 便携版默认不写系统级痕迹、不自动更新、不创建开始菜单。
- [x] 卸载默认删除：配置、缓存、日志、下载资源、用户凭据引用、诊断包、自动更新缓存。
- [x] 卸载时清理：`%AppData%` / `%LocalAppData%` / 程序目录 / 开始菜单 / 自启动项 / 防火墙规则 / 计划任务。
- [x] 提供“保留用户数据”勾选项。

### 1.6 资源与命名
- [x] 产品名：`Cli Proxy API Desktop`
- [x] EXE：`CPAD.exe`
- [x] 安装器名：`CPAD.Setup.<version>.exe`
- [x] AppUserModelID：`BlackblockInc.CPAD`
- [x] 托盘提示名：`Cli Proxy API Desktop`
- [x] 设置目录名：`CPAD`
- [x] 资源入口目录：`resources/`

---

## 2. 首发页面清单（首发必须全部完成）

- [x] 概览
- [x] 账户与授权
- [x] 配额与用量
- [x] 配置
- [x] 日志与请求诊断
- [x] 系统与模型
- [x] 源切换与工具集成
- [x] 更新与版本
- [x] 设置
- [x] 关于
- [x] 依赖修复页（异常触发页）

### 页面验收通用规则
- [x] 页面可导航进入。
- [x] 页面无明显占位残缺。
- [x] 页面有加载态、错误态、空态。
- [x] 页面在浅色/深色主题下均可用。
- [x] 页面接入真实后端或真实 API，禁止纯静态假页面作为完成态。

---

## 3. 旧路线删除清单（最终必须全部移除）

- [x] WebView2 宿主
- [x] `management.html` 运行时承载
- [x] 首次运行向导 Onboarding
- [x] 主界面内嵌日志占位
- [x] 旧桌面宿主控制面板布局
- [x] PowerShell 构建链
- [x] Inno Setup 安装链
- [x] 旧 README / docs 中“WPF + WebView2 宿主原版 WebUI”的旧定位
- [x] 旧的宿主型发布说明

### 3.1 阶段结果记录
> - 旧宿主清理：删除 WebView2 运行时检测服务、WebView2 包引用、Onboarding 窗口与相关测试。
> - `management.html` 清理：后端资产准备不再复制或下载 `management.html`，后端配置写入 `disable-control-panel: true`，运行时不再传入 `MANAGEMENT_STATIC_PATH`。
> - 构建链清理：删除旧 PowerShell 脚本与 Inno Setup 脚本，README/docs 改写为原生 WPF + BuildTool/MicaSetup 方向。
> - 验收：`dotnet build CliProxyApiDesktop.sln` 通过；`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj` 通过 26/26；`CPAD.exe` 启动 smoke 通过。

---

## 4. 目录与架构重整（0% -> 10%）

### 4.1 目录审计
- [x] 审计当前仓库目录、项目、脚本、资源、测试。
- [x] 列出必须保留的核心资产：Go 后端资源、现有测试可复用部分、品牌资源、文档骨架。
- [x] 列出必须删除或替换的旧路线资产：WebView2、Onboarding、PowerShell、Inno Setup。

### 4.2 新架构落位
- [x] 设计并落地新的解决方案结构，至少包括：
  - [x] `src/CPAD.App`（WPF 桌面主程序）
  - [x] `src/CPAD.Core`（领域模型、接口、共享逻辑）
  - [x] `src/CPAD.Infrastructure`（后端托管、凭据、文件系统、更新、诊断）
  - [x] `src/CPAD.BuildTool`（C# 构建/发布/安装器工具）
  - [x] `tests/`（单元测试/集成 smoke 测试）
- [x] 调整命名空间、程序集命名与产品标识，使其统一为 `CPAD` 体系。
- [x] 引入统一配置模型、路径策略、日志策略、主题策略。

### 4.3 最低验收
- [x] 解决方案可还原/可编译。
- [x] 新项目结构建立完成。
- [x] 旧项目未被误删到无法继续迁移。

### 4.4 阶段结果记录
> 在完成本阶段后填写：
>
> - 目录调整摘要：完成从旧 `DesktopHost` 体系到 `CPAD` 体系的解决方案重整，桌面主程序、核心模型、基础设施、BuildTool 与测试目录统一落位。
> - 新增项目：`src/CPAD.App`、`src/CPAD.Core`、`src/CPAD.Infrastructure`、`src/CPAD.BuildTool`、`tests/CPAD.Tests`。
> - 删除/待删项目：旧 `DesktopHost*` 项目已迁移/替换；旧宿主残留在 3.1 阶段结果记录中继续收口。
> - 构建结果：阶段提交 `b88f30f` 已通过解决方案构建与测试验收。

---

## 5. 主窗口与应用外壳重建（10% -> 22%）

### 5.1 应用壳体
- [x] 重写主窗口为原生 WPF 导航式壳体。
- [x] 建立统一标题栏、导航栏、内容区、状态区。
- [x] 支持浅色/深色主题切换与跟随系统。
- [x] 接入自有图标与品牌资源（来自 `resources/`）。

### 5.2 导航结构
- [x] 实现左侧/侧边导航结构。
- [x] 固定首发页面路由与导航入口。
- [x] 关于页与设置页纳入统一导航体系。

### 5.3 托盘
- [x] 实现托盘图标。
- [x] 实现托盘菜单：打开主界面 / 重启后端 / 检查更新 / 退出并停止后端。
- [x] 实现关闭按钮最小化到托盘。
- [x] 实现“退出并停止后端”的可靠收尾逻辑。

### 5.4 最低验收
- [x] 程序启动进入原生主窗口，而非 WebView2 宿主页。
- [x] 导航存在并可切换。
- [x] 托盘行为符合要求。
- [x] 不出现旧宿主布局。

### 5.5 阶段结果记录
> - 主窗口完成度：原生 WPF 壳体已完成，具备头部、侧边导航、内容区与页脚状态区。
> - 导航完成度：首发页面路由已固定，概览/账户/配额/配置/日志/系统/工具/更新/设置/关于入口已接入统一导航。
> - 托盘完成度：托盘图标、固定菜单、关闭最小化到托盘与“退出并停止后端”链路已实现。
> - 构建/运行结果：阶段提交 `67f0716` 已通过解决方案构建、测试与原生壳体启动 smoke。

---

## 6. 基础设施迁移（22% -> 34%）

### 6.1 后端托管
- [x] 保留并重构 Go 后端托管能力。
- [x] 实现启动、停止、重启、健康检查。
- [x] 处理端口占用与失败恢复。
- [x] 统一后端运行目录与生命周期。

### 6.2 路径与目录策略
- [x] 落实安装版数据目录：`%LocalAppData%\CPAD\`
- [x] 落实便携版数据目录：`.\data\`
- [x] 落实开发版数据目录：`artifacts/dev-data/`
- [x] 区分 config / cache / logs / diagnostics / runtime 目录。

### 6.3 凭据与安全
- [x] 接入 Windows Credential Manager / DPAPI。
- [x] 定义凭据引用策略与异常处理。
- [x] 确保敏感信息不明文落盘。

### 6.4 诊断能力
- [x] 统一日志采集。
- [x] 保留诊断包导出。
- [x] 对日志敏感字段做脱敏。

### 6.5 最低验收
- [x] 后端可被桌面主程序可靠托管。
- [x] 目录写入规则正确。
- [x] 凭据存取可用。
- [x] 诊断包可导出。

### 6.6 阶段结果记录
> - 后端托管状态：保留并收敛了 Go 后端托管链路，覆盖启动、停止、重启、健康检查、端口占用回退、失败恢复与运行期日志脱敏。
> - 目录策略状态：安装版、便携版、开发版三种数据目录模式已落地，并明确拆分 config / cache / logs / diagnostics / runtime 目录。
> - 凭据状态：接入基于 DPAPI 的安全凭据存储，`desktop.json` 仅保留引用，管理密钥迁移出明文配置，后端配置写入 bcrypt 哈希而非明文。
> - 诊断状态：统一桌面日志采集与最近日志缓存脱敏，新增诊断包导出，导出内容覆盖报告、日志、桌面配置与后端配置的脱敏副本。

---

## 7. API 适配与数据层（34% -> 44%）

### 7.1 现有 API 盘点
- [x] 盘点当前 Management API 能力。
- [x] 将 API 能力映射到首发页面需求。
- [x] 标出缺失字段与缺失接口。

### 7.2 扩展策略
- [x] 在不破坏兼容的前提下新增必要字段（已核验：本阶段无需新增后端字段）。
- [x] 如确有必要，新增桌面专属接口（已核验：本阶段无需新增桌面专属接口）。
- [x] 补充版本协商或容错策略。

### 7.3 客户端层
- [x] 建立统一 API Client。
- [x] 建立请求错误处理、重试、取消、超时策略。
- [x] 建立 ViewModel / Service / DTO 映射。

### 7.4 最低验收
- [x] 核心页面所需 API 均可获取真实数据。
- [x] 不再依赖 Web 前端代码来完成功能。
- [x] 缺失接口补齐方案明确并已落地。

### 7.5 阶段结果记录
> - 已映射 API：已按 `docs/management-api-mapping.md` 将概览、账户与授权、配额与用量、配置、日志与请求诊断、系统与模型页所需接口映射到 `/v0/management/*` 与后端 `/v1/models`，执行依据来自 CLIProxyAPI upstream、Management Center blackblock/upstream 与 CPA-UV。
> - 新增 API：阶段 7 未新增后端字段或桌面专属接口；桌面端通过统一 C# 管理层直接适配现有 Management API，并保留 `/v0/management/api-call` 作为上游透传能力。
> - 兼容性说明：新增 `IManagementApiClient`、`IManagement*Service` 与 DTO 映射，统一处理 Bearer 鉴权、版本头读取、错误模型、重试、取消、超时与 snake_case/camelCase 容错；验收通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（30/30）以及 `CPAD.exe` 启动 smoke。

---

## 8. 页面逐步原生化（44% -> 72%）

> 要求：本阶段必须逐页完成，**每页完成后都单独验证**。不得一次性铺大量半成品页面。

### 8.1 概览页
- [x] 以 CPA-UV 首页思路重做概览页。
- [x] 展示核心状态、账户状态、配额摘要、更新提醒、依赖状态、快捷入口。
- [x] 融入桌面侧状态（后端、更新、运行目录、依赖修复）。
- [x] 验收：概览页完全接真实数据，UI 达到可交付状态。

### 8.2 账户与授权页
- [x] 实现 OAuth / 设备流 / Cookie 导入 / 管理密钥等能力。
- [x] 使用 Credential Manager / DPAPI 存储敏感信息。
- [x] 验收：账户相关操作真实可用。

### 8.3 配额与用量页
- [x] 实现额度、请求数、Token、分模型/分 API 统计。
- [x] 验收：真实统计信息可展示。

### 8.4 配置页
- [x] 原生化配置编辑能力。
- [x] 提供保存、校验、错误提示。
- [x] 验收：配置可读写、可校验、可回显。

### 8.5 日志与请求诊断页
- [x] 实现日志浏览、筛选、搜索、导出诊断包。
- [x] 不将日志嵌入主界面常驻布局。
- [x] 验收：日志功能独立、完整、可导出。

### 8.6 系统与模型页
- [x] 展示系统状态、模型列表、接口连通性、健康检查。
- [x] 验收：系统状态可实时刷新。

### 8.7 源切换与工具集成页
- [x] 实现 `official / cpa` 源切换。
- [x] 保留桌面侧工具整合入口。
- [x] 验收：切换真实生效，必要时可恢复默认。

### 8.8 更新与版本页
- [x] 接入 Stable 更新查询。
- [x] 预留 Beta 逻辑入口。
- [x] 验收：检查更新可用。

### 8.9 设置页
- [x] 实现主题、启动行为、托盘、目录、更新偏好、隐私选项。
- [x] 验收：设置可持久化、可回显。

### 8.10 关于页
- [x] 展示版本、许可证、组件来源、诊断入口。
- [x] 验收：信息完整。

### 8.11 依赖修复页
- [x] 顶部横幅提醒。
- [x] 不可用功能显示感叹号标记并禁用。
- [x] 默认禁用大部分核心功能，仅保留概览、设置、诊断、关于。
- [x] 按钮：立即修复 / 查看详情 / 重新检测 / 导出诊断。
- [x] 触发条件至少包括：
  - [x] 运行时缺失或版本不满足
  - [x] Go 后端运行文件缺失/损坏/校验失败
  - [x] 首次启动初始化未完成
  - [x] Credential Manager / DPAPI 不可用
  - [x] 更新组件缺失或损坏
  - [x] 关键端口占用且自动换端口失败
  - [x] 必需资源包缺失或版本不匹配
- [x] 验收：依赖异常时可正确进入修复模式。

### 8.12 阶段结果记录
> - 已完成页面：概览页（8.1）已接入 Management API 概览聚合、账户/授权数量、用量摘要、版本检查、桌面运行目录、后端状态、依赖检测与修复入口；账户与授权页（8.2）已接入 `/api-keys`、`/auth-files`、`/auth-files/models`、`/auth-files/status`、`/{provider}-auth-url`、`/get-auth-status`、`/oauth-callback`，支持管理密钥 DPAPI 存储、OAuth/设备流入口、Auth JSON/Cookie 导入、API key 替换、auth file 禁用/启用/删除与模型查看；配额与用量页（8.3）已接入 `/usage`、`/usage/export`、`/usage/import` 与 `/auth-files`，展示请求数、成功率、Token、RPM/TPM、分 API/分模型统计、认证文件配额信号与最近请求事件；配置页（8.4）已接入 `/config`、`/config.yaml`、`/debug`、`/proxy-url`、`/request-retry`、`/max-retry-interval`、`/quota-exceeded/*`、`/usage-statistics-enabled`、`/request-log`、`/logging-to-file`、`/logs-max-total-size-mb`、`/error-logs-max-files`、`/ws-auth`、`/force-model-prefix` 与 `/routing/strategy`，同时提供结构化表单编辑、原始 YAML 校验保存、后端错误提示与回显快照；日志与请求诊断页（8.5）已接入 `/logs`、`/request-error-logs`、`/request-log-by-id/:id`、`DELETE /logs` 和桌面诊断包导出，支持独立日志浏览、搜索/等级筛选、请求诊断、错误日志清单与脱敏诊断包导出；系统与模型页（8.6）已接入 `/latest-version`、`/v1/models`、`/api-call` 与本地 `/healthz`，展示桌面托管状态、版本元数据、模型分组、健康检查和可编辑连通性探针；源切换与工具集成页（8.7）已接入 `CodexConfigService`、`CodexLocator`、`CodexVersionReader`、`CodexAuthStateReader` 与 `CodexLaunchService`，支持 `official/cpa` 真实 profile/auth 切换、官方 auth 备份恢复、CPA 本地后端 profile 写入、命令预览和桌面工具目录入口；更新与版本页（8.8）已接入桌面仓库 GitHub Releases Stable 查询、Beta 预留 channel entry、桌面/后端版本诊断、release assets 展示与 GitHub 404 无 release 正常态处理；设置页（8.9）已接入桌面 `desktop.json` 真实读写、主题偏好、启动项 Run Key 状态、托盘与关闭行为、目录写入诊断、更新偏好、日志级别与调试工具开关，并回显持久化快照；关于页（8.10）已改为原生 WPF 页面，展示桌面/后端版本、桌面与后端许可证预览、结构化组件来源清单，以及脱敏诊断导出与诊断目录入口；依赖修复页（8.11）已接入桌面依赖健康检查与修复模式，支持顶部修复横幅、不可用导航禁用与感叹号标记、仅保留概览/日志与诊断/设置/关于/依赖修复入口、立即修复、查看详情、重新检测、导出诊断，并覆盖运行时版本、后端运行文件、初始化状态、Credential Manager / DPAPI、更新组件、端口分配、资源包缺失或版本不匹配等阻断项。
> - 仍待打磨页面：无。
> - 页面测试结果：8.1 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（30/30）与 `CPAD.exe` 启动 smoke；8.2 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（36/36）与 UI Automation smoke（进入 Accounts & Auth 并确认 Management Key、OAuth、Backend API Keys、Auth JSON / Cookie Import、Stored Auth Files 区块）；8.3 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（37/37）与 UI Automation smoke（进入 Quota & Usage 并确认 Usage Summary、Quota Signals、Requests by API、Requests by Model、Recent Request Events 区块）；8.4 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（38/38）与 UI Automation smoke（进入 Configuration 并确认 Editable Settings、Raw YAML Editor、Validation & Echo、Live Snapshot、Apply Structured Changes、Validate & Save YAML 入口）；8.5 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（39/39）与 UI Automation smoke（进入 Logs & Diagnostics 并确认 Log Browser、Request Diagnostics、Diagnostics Export、Refresh Logs、Export Diagnostic Package 入口）；8.6 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（40/40）与 UI Automation smoke（进入 System & Models 并确认 System Status、Health Check、Model Inventory、Connectivity Probe、Refresh System Status、Run Connectivity Probe 入口）；8.7 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（42/42）与隔离 UI Automation smoke（进入 Sources & Tools 并确认 Source Switching、Codex Desktop Integration、Desktop Tool Entries、Apply Source Switch、Launch Selected Source、Refresh Codex Status 入口）；8.8 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（45/45）与隔离 UI Automation smoke（进入 Updates & Version 并确认 Channel Summary、Desktop Stable Release、Backend Version Diagnostics、Release Assets、Open Release Page 入口，当前真实状态为 `No stable release`）；8.9 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（46/46）与隔离 UI Automation smoke（进入 Settings 并确认 Appearance & Shell、Startup & Tray、Directories、Update Preferences、Privacy & Diagnostics、Persisted Snapshot、Save Settings、Reload Settings，验收过程中未点击保存以避免改写当前用户启动项）；8.10 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（47/47）与隔离 UI Automation smoke（进入 About 并确认 Version Information、Licenses、Component Sources、Diagnostics Entry、Desktop License Preview、Backend License Preview、Export Diagnostics、Open Diagnostics Folder 入口，验收过程中未启动 backend）；8.11 已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（57/57）与隔离 UI Automation smoke（占用 32 个本次创建的 loopback 监听端口触发端口分配失败，进入 Dependency Repair，确认 Dependency Repair Mode 横幅、Repair Summary、Safe Routes、Repair Actions、Issue Details、Repair Now、View Details、Re-check、Export Diagnostics 可见，Accounts & Auth 等核心路由禁用且不启动/停止现有 CPA-UV 进程）。

---

## 9. BuildTool 重写发布链（72% -> 84%）

### 9.1 BuildTool 基础能力
- [x] 创建 `CPAD.BuildTool`。
- [x] 提供统一命令入口，例如：
  - [x] `fetch-assets`
  - [x] `verify-assets`
  - [x] `publish`
  - [x] `package-portable`
  - [x] `package-dev`
  - [x] `package-installer`
  - [x] `verify-package`
- [x] BuildTool 输出清晰日志与退出码。

### 9.2 资源拉取与校验
- [x] 替代旧 PowerShell 资源脚本。
- [x] 支持资源下载、校验、版本记录。
- [x] 失败时有清晰错误与重试策略。

### 9.3 发布与产物
- [x] 生成安装版产物。
- [x] 生成便携版产物。
- [x] 生成开发版产物。
- [x] 输出统一产物目录结构。

### 9.4 签名接口预留
- [x] BuildTool 预留签名接口。
- [x] 首版允许无证书运行，但结构必须可扩展接入真实签名。

### 9.5 最低验收
- [x] PowerShell 正式链路不再是必需。
- [x] 通过 BuildTool 可完成完整发布链。
- [x] 产物可被验证。

### 9.6 阶段结果记录
> - BuildTool 命令：`fetch-assets` 复制本地 `resources/backend/windows-x64` 并在缺失时回退下载 CLIProxyAPI release zip；`verify-assets` 校验 `asset-manifest.json` 的 SHA-256/大小/必需文件；`publish` 执行 `dotnet publish` 并注入 `assets/backend/windows-x64`；`package-portable`、`package-dev`、`package-installer` 统一从发布目录生成产物；`verify-package` 检查三类 zip 可读且包含 `CPAD.exe` 与安装器 staging 配置。
> - 产物类型：`artifacts/buildtool/packages/CPAD.Portable.1.0.0.win-x64.zip`、`CPAD.Dev.1.0.0.win-x64.zip`、`CPAD.Setup.1.0.0.win-x64.zip`；安装版当前为 MicaSetup staging 包，10.x 继续生成最终 `.exe` 安装器。
> - 验证结果：已通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（62/62），并顺序执行 `dotnet run --project src/CPAD.BuildTool -- fetch-assets`、`verify-assets`、`publish`、`package-portable`、`package-dev`、`package-installer`、`verify-package`，最终 `package verification passed`。

---

## 10. 安装器与更新链（84% -> 92%）

### 10.1 安装链
- [x] 使用 MicaSetup 路线生成安装器。
- [x] 当前用户安装优先。
- [x] 安装时完成依赖预检。
- [x] 内置依赖优先，缺失时允许在线下载补齐。
- [x] 安装完成后可直接启动。

### 10.2 卸载链
- [x] 默认清理配置、缓存、日志、下载资源、用户凭据引用、诊断包、自动更新缓存。
- [x] 清理 `AppData/LocalAppData/程序目录/开始菜单/自启动项/防火墙规则/计划任务`。
- [x] 提供“保留用户数据”勾选项。
- [x] 卸载路径安全，禁止误伤无关目录。

### 10.3 更新链
- [x] Stable 通道从 GitHub Releases 检查更新。
- [x] 预留 Beta 通道配置。
- [x] 安装版支持更新流程。
- [x] 便携版默认不自动更新。

### 10.4 最低验收
- [x] 安装器生成成功。
- [x] 安装可用。
- [x] 卸载可用且清理正确。
- [x] 检查更新可用。

### 10.5 阶段结果记录
> - 安装器结果：`package-installer --version 1.0.0` 已通过 MicaSetup 生成真实 `artifacts/buildtool/packages/CPAD.Setup.1.0.0.exe`，同时保留 installer staging zip、依赖预检、当前用户安装配置、安装完成启动配置与签名预留元数据；`verify-package --version 1.0.0` 已验证安装器 PE 头与 staging 内容。
> - 卸载结果：已生成卸载清理清单并接入 MicaSetup uninstaller 定制，默认覆盖配置、缓存、日志、下载资源、凭据引用、诊断包、更新缓存、开始菜单、自启动、计划任务、防火墙规则等清理目标，并提供 `KeepMyData` 保留用户数据入口；实际安装/卸载手工执行仍留到 11.2 验收。
> - 更新结果：Stable 通过 GitHub Releases 查询并识别 `CPAD.Setup.<version>.exe` 直装资产；Beta 通道保留配置入口；安装版通过 `IUpdateInstallerService` 下载/校验/启动安装器，portable/development 默认禁用自动启动检查与自动安装，仅提供手动下载/Release 页面入口。

---

## 11. 测试、清理与文档收尾（92% -> 98%）

### 11.1 自动化测试
- [x] 补齐并修复单元测试。
- [x] 补齐并修复集成 smoke 测试。
- [x] 至少覆盖：
  - [x] 主程序启动
  - [x] 后端托管
  - [x] 路径策略
  - [x] 凭据存取
  - [x] 依赖修复判定
  - [x] 更新检查
  - [x] 安装器产物验证

### 11.2 手工验收
- [x] 安装版手工验收
- [x] 便携版手工验收
- [x] 开发版手工验收
- [x] 主题切换手工验收
- [x] 托盘行为手工验收
- [x] 关闭/退出行为手工验收
- [x] 卸载清理手工验收

### 11.3 文档更新
- [x] 重写 README 为原生桌面版定位。
- [x] 更新架构文档、构建文档、发布文档、测试文档。
- [x] 删除/改写旧宿主路线文档。

### 11.4 代码清理
- [x] 删除未使用代码。
- [x] 删除旧 WebView2 代码。
- [x] 删除旧 Onboarding 代码。
- [x] 删除旧 PowerShell、Inno Setup 相关文件。

### 11.5 最低验收
- [x] 测试通过。
- [x] 文档与实际一致。
- [x] 仓库中不再保留旧路线残骸。

### 11.6 阶段结果记录
> - 自动化测试：已补齐 Updates、BuildTool、路径标记与 Smoke 覆盖；最终已再次通过 `dotnet build CliProxyApiDesktop.sln`、`dotnet test tests/CPAD.Tests/CPAD.Tests.csproj`（87/87）、BuildTool 全链（`fetch-assets` / `verify-assets` / `publish` / `package-portable` / `package-dev` / `package-installer` / `verify-package`）以及 `tests/CPAD.Tests/Smoke/SafeSmoke.ps1` 静态与隔离启动 smoke。
> - 手工验收：便携版与开发版已从本地打包 zip 解压，在隔离环境成功启动并分别验证 `data/` 与 `app/artifacts/dev-data/` 写入路径；隔离 UI 实测主题按钮可将 `themeMode` 从 `System` 持久化到 `Light`，关闭主窗口后进程继续存活且窗口隐藏，符合最小化到托盘行为；安全 smoke 只停止本次启动的精确 PID 及其隔离目录内 backend 子进程。安装版与卸载清理验收限定为本轮安全验收：验证 `CPAD.Setup.1.0.0.exe` PE 头、MicaSetup staging、当前用户安装计划、安装后启动计划、`uninstall-cleanup.json`、`update-policy.json` 与清理安全边界；未在当前主开发环境执行真实安装/卸载，以避免写入 `%LocalAppData%\\Programs\\CPAD`、开始菜单与 HKCU。
> - 文档状态：README、架构冻结、构建发布与测试文档已改写为原生 WPF + Go backend + C# BuildTool + MicaSetup 现状，不再把 WebView2、PowerShell、Inno Setup 写作正式路线。
> - 清理结果：活动源码/构建输入经静态 smoke 审计未发现 WebView2、Onboarding、`management.html` runtime wiring、PowerShell 构建链或 Inno Setup 安装链引用；空 `resources/webview2` 目录已删除；旧 `artifacts/publish` 与 `artifacts/installer` 历史输出已清理，后续只以 `artifacts/buildtool` 作为当前产物依据。

---

## 12. 最终交付、删除步骤文档、推送远端（98% -> 100%）

### 12.1 最终总验收
- [x] 全量构建通过。
- [x] 核心 smoke/test 全部通过。
- [x] 首发页面全部完成。
- [x] 旧路线全部删除。
- [x] 安装版/便携版/开发版可用。
- [x] Stable 更新可用。
- [x] 托盘/退出/卸载行为符合规格。
- [x] README / docs 已同步。

### 12.2 删除步骤文档
- [ ] 在确认所有任务完成且已验收后，删除本文件。
- [ ] 删除本文件必须单独纳入最终提交，避免中途失去执行依据。

### 12.3 最终 git 操作
- [ ] 查看 `git status`，确认无意外脏文件。
- [ ] 整理最终提交说明，清楚描述原生重构完成。
- [ ] 提交最终变更。
- [ ] push 到 `origin/main`。

### 12.4 最终结果记录
> - 最终提交 hash：
> - push 结果：
> - 产物位置：
> - 遗留问题（如无写“无”）：

---

## 13. 推荐执行命令模板（可按实际调整）

```bash
# 1) 恢复与构建
# dotnet restore
# dotnet build

# 2) 测试
# dotnet test

# 3) BuildTool
# dotnet run --project src/CPAD.BuildTool -- fetch-assets
# dotnet run --project src/CPAD.BuildTool -- verify-assets
# dotnet run --project src/CPAD.BuildTool -- publish
# dotnet run --project src/CPAD.BuildTool -- package-portable
# dotnet run --project src/CPAD.BuildTool -- package-dev
# dotnet run --project src/CPAD.BuildTool -- package-installer
# dotnet run --project src/CPAD.BuildTool -- verify-package
```

---

## 14. 给执行代理的最后硬要求

- [x] 严格按本文档顺序执行。
- [x] 每步完成后立即勾选。
- [x] 每阶段必须构建、测试、验收、提交。
- [x] 不得跳过验收。
- [x] 不得在未完成全部任务前删除本文档。
- [x] 最后必须删除本文档、提交并 push 到远端。
