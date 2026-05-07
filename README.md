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

**CodexCliPlus** 是面向 **Windows 10/11 x64** 的 Codex CLI 本地管理增强平台。它把原本需要手动处理的 CLIProxyAPI 后端、Codex 路由、认证文件、本地环境、日志、用量和更新流程收进一个桌面应用，并固定使用 `ccp-core.exe` 作为托管后端资产名。

## 目标人群

- 中文 Windows Codex CLI 用户
- 想本机管理账号、配置、日志和用量的用户
- 不想手动折腾环境和认证文件的用户
- 需要官方 Codex 与 CPA 路由切换的用户
- 只关注 GPT-API 、不需要其他API接入口的用户

## 当前特性

- **Windows 桌面宿主**：提供主窗口、托盘、桌面通知和启动阻断页。
- **托管魔改 CPA 后端**：后端由 CodexCliPlus 管理，固定资产名为 `ccp-core.exe`。
- **内置 WebUI 与桌面桥接**：管理界面随应用发布，管理密钥不落浏览器存储。
- **Codex 路由检测与切换**：识别并切换官方 Codex、CodexCliPlus CPA 和第三方 CPA 路由。
- **本地环境检测与修复**：覆盖 Node.js、npm、PowerShell、PATH、Codex CLI、WSL 等依赖。
- **Codex 配置集中管理**：管理 Codex API、OAuth、认证文件、模型别名、模型禁用和额度信息。
- **OpenAI 兼容上游**：支持多 Key、自定义 Header、模型池、代理和流式透传。
- **本机凭据保护**：使用 DPAPI、`ccp-secret://` 引用和 `.sac v2` 加密导入导出。
- **运行记录持久化**：提供 SQLite 用量持久化、日志、请求日志、更新器和安装包链路。

## 开发路线

- **完善 `.codex` 配置页**：补齐可视化编辑、项目配置查看和更完整校验。
- **增强 Codex 任务入口**：整合启动、常用命令、日志和任务结果。
- **手机通知**：把完成结果、阶段性结论和关键失败状态转发到手机端。

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
