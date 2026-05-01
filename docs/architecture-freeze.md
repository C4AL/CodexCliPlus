# CodexCliPlus 桌面架构约束

## 固定规则

- 运行时前端基线是仓库内置 WebUI，不回退到完整原生 WPF 管理页。
- `CodexCliPlus.App` 只负责桌面宿主职责：窗口、托盘、WebView2、后端生命周期、安全存储、更新状态和启动阻断视图。
- WebUI 必须从本地打包文件加载，入口为 `assets/webui/upstream/dist/index.html`，不能改为远程 URL。
- 后端仍基于 CLIProxyAPI；CodexCliPlus 托管运行时资产名固定为 `ccp-core.exe`。上游压缩包内的 `cli-proxy-api.exe` 只作为拉取输入名。
- 桌面端是管理密钥来源。WebView2 bootstrap 可以注入 `desktopMode`、`apiBase`、`managementKey`，但不能把 `managementKey` 持久化到浏览器存储。
- 发布链路固定为 `CodexCliPlus.BuildTool + MicaSetup`，旧 PowerShell/Inno 发布链不再作为正式路径。
- 稳定更新检查指向 `C4AL/CodexCliPlus` 的 GitHub Releases；Beta 渠道保留配置位，但还不是实际发布线。

## 当前分层

- `src/CodexCliPlus.App`
  WPF shell、WebView2 host、托盘、通知、导航状态、启动阻断视图。
- `src/CodexCliPlus.Core`
  常量、共享模型、枚举、管理 API 契约和桌面 bootstrap payload。
- `src/CodexCliPlus.Infrastructure`
  后端资产准备、配置写入、进程托管、日志、诊断、安全存储、管理 API 客户端。
- `src/CodexCliPlus.BuildTool`
  资产拉取/校验、WebUI 构建、桌面 publish、安装器打包和包结构校验。
- `resources/webui/upstream/source`
  内置 WebUI 源码。
- `artifacts/buildtool/assets/webui/upstream/dist`
  BuildTool 生成并复制进桌面发布目录的 WebUI 构建产物。
- `resources/webui/modules/cpa-uv-overlay`
  保留上游/覆盖层页面、路由和行为来源追踪，供同步与差异审计使用。

## 打包约束

发布输出必须至少包含：

- `CodexCliPlus.exe`
- `assets/backend/windows-x64/ccp-core.exe`
- `assets/webui/upstream/dist/index.html`
- `assets/webui/upstream/sync.json`
- `publish-manifest.json`

安装器包必须校验：

- `app-package/CodexCliPlus.exe`
- `app-package/assets/webui/upstream/dist/index.html`
- `app-package/assets/webui/upstream/sync.json`
- `app-package/packaging/dependency-precheck.json`
- `app-package/packaging/update-policy.json`
- `app-package/packaging/uninstall-cleanup.json`

## 宿主行为约束

- 主窗口是单一 WebView2 宿主，不恢复旧运行时原生导航壳。
- 缺少 WebView2 Runtime、缺少 WebUI 资产或后端启动失败时，必须显示原生阻断视图，不能留下空白 WebView2。
- 外部链接必须交给系统浏览器。
- 关闭主窗口时，托盘模式开启则默认最小化到托盘。
- 托盘必须保留打开、重启后端、检查更新、退出等核心操作。

## 验收基线

常规仓库验收从以下命令开始：

```powershell
dotnet restore CodexCliPlus.sln --locked-mode
dotnet build CodexCliPlus.sln --configuration Release --no-restore
dotnet test tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj --configuration Release --no-build
```

涉及 WebUI、发布或打包时，追加对应的 npm 验证和 `CodexCliPlus.BuildTool` 发布/打包/校验命令。
