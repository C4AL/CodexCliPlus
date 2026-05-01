<p align="center">
  <img src="https://raw.githubusercontent.com/C4AL/CodexCliPlus/main/resources/icons/ico-transparent.png" alt="CodexCliPlus" width="160" />
</p>

<h1 align="center">CodexCliPlus</h1>

<p align="center">
  基于 CLIProxyAPI 的 Codex Windows CLI 一站式管理增强平台
  <br />
  C# / WPF / WebView2 / .NET 10 + CLIProxyAPI Go 后端 + Codex CLI 增强层
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6?style=flat-square" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" />
  <img alt="Host" src="https://img.shields.io/badge/Host-WPF%20%2B%20WebView2-0F6CBD?style=flat-square" />
  <img alt="Backend" src="https://img.shields.io/badge/Backend-CLIProxyAPI%20Go-00ADD8?style=flat-square" />
  <img alt="Codex" src="https://img.shields.io/badge/Codex-CLI%20Only-111111?style=flat-square" />
  <img alt="Status" src="https://img.shields.io/badge/status-in%20development-orange?style=flat-square" />
</p>

<p align="center">
  <a href="./docs/architecture-freeze.md">架构约束</a>
  ·
  <a href="./docs/build-and-release.md">构建与发布</a>
  ·
  <a href="./docs/testing.md">测试与验收</a>
  ·
  <a href="./docs/management-api-mapping.md">管理 API 映射</a>
  ·
  <a href="https://qm.qq.com/q/HVH9TOEaYk">QQ群聊</a>
</p>

## 项目定位

**CodexCliPlus** 是面向 **Windows 10/11 x64** 的 Codex CLI 本地管理增强平台，服务于中文、Windows、本地优先、低配置成本、只使用 Codex 的用户。桌面端负责 WPF/WebView2 宿主、托盘、后端生命周期、更新与安全存储；管理界面来自仓库内置的 WebUI；后端运行时基于 CLIProxyAPI，并以 `ccp-core.exe` 作为 CodexCliPlus 托管资产名。

CodexCliPlus 不是 OpenAI 官方 Codex App，也不是 ChatGPT Plus / Pro 订阅产品。项目只提供本地桌面管理、配置、诊断、插件与工作流增强能力，不绕过官方认证、安全审批或使用限制。

## 目标人群

- **中文用户**：界面、文档和默认工作流优先面向中文使用场景。
- **Windows 用户**：聚焦 Windows 10/11 x64 本地桌面体验，不追求跨平台泛化。
- **本地用户**：优先把账号、配置、日志、插件与运行状态留在本机管理。
- **懒人配置用户**：减少手动编辑环境变量、认证文件和 `.codex` 配置的步骤。
- **仅 Codex 用户**：围绕 Codex CLI 做管理增强，不扩展成通用多模型客户端。

## 当前特性

相对直接运行原版 CLIProxyAPI（CPA），CodexCliPlus 当前提供以下桌面化、本地化和 Codex 专向增强：

- **保留 CPA 后端语义**：继续使用 CLIProxyAPI 的本地代理和管理 API 能力，CodexCliPlus 负责托管、打包和桌面集成；运行时资产统一命名为 `ccp-core.exe`。
- **Windows 桌面入口**：提供 WPF + WebView2 主窗口、系统托盘、托盘模式下关闭最小化、桌面通知、外部链接转交系统浏览器，以及 WebView2、WebUI 或后端不可用时的原生启动阻断视图。
- **一体化后端托管**：自动准备后端资产、写入运行时配置、启动/停止/重启本地后端、注入管理密钥、跟踪健康状态，并把后端生命周期纳入桌面端管理。
- **内置本地 WebUI**：从仓库内置 `resources/webui/upstream/source` 构建管理界面，发布后通过本地 `assets/webui/upstream/dist/index.html` 加载，不依赖远程管理页面。
- **Codex 环境管理**：围绕 Codex CLI 检测 Node.js、npm、PowerShell、PATH、WSL 等本地依赖，提供依赖状态、修复入口、配置、日志、账号和用量管理入口。
- **账号与认证管理**：通过桌面桥接管理 API Key、OAuth 认证文件、模型排除列表、模型别名、Provider 配置和账号配置导入导出，减少直接手写后端 YAML/JSON 的操作。
- **安全与账号保护**：首次启动生成本地安全密钥；管理密钥和敏感值通过 Windows DPAPI 保存到本机凭据/密钥库；账号配置、认证文件和管理接口写入会把 API Key、OAuth Token、Authorization、Cookie 等敏感字段迁移为 `ccp-secret://` 引用；账号配置导出仅支持 `.sac v2` 加密安全包，使用 Argon2id 派生密钥和 AES-256-GCM 加密，含凭据内容禁止明文导出。
- **用量持久化与汇总**：在 CPA 用量接口基础上增加本地 SQLite 持久化、事件去重、水位过滤、导入导出和清理能力，便于保留跨会话用量记录。
- **日志与诊断**：聚合后端日志、请求错误日志、请求详情和桌面诊断信息，便于从同一个桌面入口排查运行、认证、代理和配置问题。
- **更新与安装包**：支持从 GitHub Releases 检查稳定版更新、下载并校验更新包、调用随包更新器安装；发布链路生成离线安装器、更新包、校验和、签名或未签名侧车元数据。
- **构建与发布自动化**：`CodexCliPlus.BuildTool` 统一处理 CPA 资产拉取与校验、WebUI 构建、桌面发布、离线安装器打包、更新包生成、包结构校验和校验和写入。
- **中文与本地优先体验**：桌面界面、文档和默认工作流面向中文 Windows 用户；账号、配置、日志、诊断、用量和运行状态优先留在本机管理。

## 开发路线

- **集成 Codex 工作流**：围绕 Codex CLI 做安装检测、更新、启动、日志、账号、用量和常用任务入口整合。
- **可视化编辑 `.codex`**：提供图形化配置编辑、一键配置和配置校验，减少手写 TOML/JSON/YAML 的错误成本。
- **官方模式 / CPA 模式自由切换**：支持在官方 Codex 路由和 CPA（CLIProxyAPI）路由之间按需切换，并尽量保留一致的本地管理体验。
- **邮件转发到手机**：将 Codex 阶段性结论、任务完成结果和关键失败状态通过邮件系统转发给用户，方便在手机上接收进度。
- **更灵活的路由策略**：支持按账号、任务、模型、网络环境、失败回退和优先级配置不同路由，降低手动切换成本。

## 快速开始

### 1. 准备环境

- Windows 10/11 x64
- .NET SDK 10
- Node.js 与 npm
- PowerShell 7 或 Windows PowerShell 5.1
- Microsoft Edge WebView2 Runtime
- Codex CLI（可选；未安装时仍可启动桌面宿主）

### 2. 拉取、还原并构建

```powershell
git clone https://github.com/C4AL/CodexCliPlus.git
cd CodexCliPlus

dotnet tool restore
dotnet restore CodexCliPlus.sln
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- fetch-assets
dotnet build CodexCliPlus.sln --configuration Release
dotnet test ./tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj --configuration Release
```

### 3. 启动桌面宿主

```powershell
dotnet run --project ./src/CodexCliPlus.App/CodexCliPlus.App.csproj
```

### 4. 验证 WebUI

```powershell
Push-Location ./resources/webui/upstream/source
npm ci
npm run lint
npm run type-check
npm run test
npm run build
Pop-Location
```

### 5. 发布与打包

```powershell
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-assets --version <version>
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- publish --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-offline-installer --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-update --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-package --configuration Release --runtime win-x64 --version <version>
```

主要产物位于 `artifacts/buildtool`：

- `publish/win-x64/CodexCliPlus.exe`
- `publish/win-x64/assets/backend/windows-x64/ccp-core.exe`
- `publish/win-x64/assets/webui/upstream/dist/index.html`
- `packages/CodexCliPlus.Setup.Online.<version>.exe`
- `packages/CodexCliPlus.Setup.Offline.<version>.exe`


## 许可证

MIT
