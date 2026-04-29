<p align="center">
  <img src="https://raw.githubusercontent.com/Blackblock-inc/CodexCliPlus/main/resources/icons/ico-transparent.png" alt="CodexCliPlus" width="160" />
</p>

<h1 align="center">CodexCliPlus</h1>

<p align="center">
  Codex CLI for Windows, powered by CLIProxyAPI
  <br />
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

**CodexCliPlus** 是面向 **Windows 10/11 x64** 的 Codex CLI 本地管理增强平台。桌面端负责 WPF/WebView2 宿主、托盘、后端生命周期、更新与安全存储；管理界面来自仓库内置的 WebUI；后端运行时基于 CLIProxyAPI，并以 `ccp-core.exe` 作为 CodexCliPlus 托管资产名。

CodexCliPlus 不是 OpenAI 官方 Codex App，也不是 ChatGPT Plus / Pro 订阅产品。项目只提供本地桌面管理、配置、诊断、插件与工作流增强能力，不绕过官方认证、安全审批或使用限制。

## 当前能力

- **桌面宿主**：WPF + WebView2 主窗口、系统托盘、启动阻断视图、外部链接转交系统浏览器。
- **后端托管**：拉取、校验并打包 CLIProxyAPI 运行时；桌面端负责本地后端启动、停止、健康检查和管理密钥注入。
- **管理 WebUI**：从 `resources/webui/upstream/source` 构建，发布后由本地文件 `assets/webui/upstream/dist/index.html` 提供。
- **Codex 支持**：检测 Codex CLI、Node.js、npm、PowerShell、PATH、WSL 等本地依赖，并提供配置、日志、账号与用量管理入口。
- **发布链路**：`CodexCliPlus.BuildTool` 统一处理资产拉取、WebUI 构建、桌面发布、在线/离线安装器打包和包结构校验。

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
git clone https://github.com/Blackblock-inc/CodexCliPlus.git
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
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-online-installer --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-offline-installer --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-package --configuration Release --runtime win-x64 --version <version>
```

主要产物位于 `artifacts/buildtool`：

- `publish/win-x64/CodexCliPlus.exe`
- `publish/win-x64/assets/backend/windows-x64/ccp-core.exe`
- `publish/win-x64/assets/webui/upstream/dist/index.html`
- `packages/CodexCliPlus.Setup.Online.<version>.exe`
- `packages/CodexCliPlus.Setup.Offline.<version>.exe`

## 文档

- [架构约束](./docs/architecture-freeze.md)
- [构建与发布](./docs/build-and-release.md)
- [测试与验收](./docs/testing.md)
- [管理 API 映射](./docs/management-api-mapping.md)
- [项目状态与待办](./docs/repository-gap-analysis.md)

## 许可证

MIT
