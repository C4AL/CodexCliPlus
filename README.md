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
  <a href="./docs/architecture-freeze.md">架构设计</a>
  ·
  <a href="./docs/repository-gap-analysis.md">仓库纠偏</a>
  ·
  <a href="./docs/build-and-release.md">构建与发布</a>
  ·
  <a href="https://qm.qq.com/q/HVH9TOEaYk">QQ群聊</a>
</p>

## 项目定位

**CodexCliPlus** 是一个面向 **Windows 10/11 x64** 的 Codex CLI 一站式管理增强平台，底层基于 **CLIProxyAPI**，专注于把 Codex CLI 的代理、账号配置、插件、会话、日志、限额状态和桌面托盘体验整合到一个本地桌面应用中。

它不是 OpenAI 官方 Codex App，不是 OpenCode Desktop，也不是泛 CLIProxyAPI 管理面板。CodexCliPlus 的目标是服务 **Codex Windows CLI 用户**，为本地开发、配置切换、插件扩展和日常运行诊断提供更完整的桌面控制层。

## 核心能力

- **CLIProxyAPI 本地托管**
  - 启动、停止、重启、健康检查、端口占用检测
  - 后端日志聚合、错误提示、运行状态显示
  - 本地代理地址自动注入 Codex CLI 运行环境

- **Codex CLI 管理**
  - Codex CLI 检测、版本识别、启动入口
  - Node.js、npm、PowerShell、PATH、WSL 环境诊断
  - 常用启动参数、工作目录、项目入口与最近会话管理

- **账号配置档案**
  - Codex OAuth profile 管理
  - 多账号配置档案、本地状态查看、手动切换
  - 代理绑定、配置隔离、失败原因提示
  - 不绕过官方使用限制，不提供自动规避限额能力

- **插件与技能中心**
  - Codex plugins、skills、MCP、工作流模板的安装、启用、禁用与更新
  - 常用开发场景模板，例如审计、构建、修复、发布、README、Issue 处理
  - 插件配置可视化管理，降低手动维护配置文件的成本

- **Codex CLI 增强层**
  - 通过 wrapper / profile / config / plugin 方式增强 Codex CLI 使用体验
  - 支持配置模板、启动模板、任务模板、并发任务入口
  - 不破坏官方 CLI 的认证、安全审批和限制机制

- **配置编辑器**
  - 图形化编辑 Codex CLI 配置、profile、model、reasoning effort、sandbox、approval、status line 等常用选项
  - 支持配置备份、恢复、校验与快速切换

- **限额、日志与诊断**
  - 账号状态、请求日志、失败原因、后端日志统一查看
  - Codex CLI 常见问题诊断，例如安装失败、PATH 异常、代理失败、OAuth 失效、WSL 网络异常
  - 为 5h / weekly 等状态提供可视化提示，但不绕过使用限制

- **Windows 桌面体验**
  - WPF + WebView2 桌面宿主
  - 系统托盘菜单
  - 首次运行向导
  - 开机启动、最小化到托盘、退出时停止后端
  - 安装器、便携版与发布脚本



## 快速开始

### 1. 准备环境

- Windows 10/11 x64
- .NET SDK 10
- PowerShell 7 或 Windows PowerShell 5.1
- WebView2 Runtime（开发运行建议预装；安装器会自动检测并静默安装）
- Codex CLI（可选；未安装时仍可完成桌面宿主初始化）
- CLIProxyAPI 相关资源（由 BuildTool 拉取或通过发布资产提供）

### 2. 拉取并构建

```powershell
git clone https://github.com/Blackblock-inc/CodexCliPlus.git
cd CodexCliPlus
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- fetch-assets
dotnet build CliProxyApiDesktop.sln
dotnet test tests/CPAD.Tests/CPAD.Tests.csproj
```

> 说明：当前源码项目名、solution、测试项目可能仍保留 `CPAD.*` 历史命名。仓库产品名已切换为 CodexCliPlus，后续可逐步重构命名空间、项目文件与发布产物名称。

如需完整发布资产，请通过当前 BuildTool 链路拉取并校验资源：

```powershell
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- fetch-assets
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- verify-assets
```

### 3. 启动桌面宿主

```powershell
dotnet run --project ./src/CPAD.App/CPAD.App.csproj
```

### 4. 自动化验证

```powershell
powershell -ExecutionPolicy Bypass -File ./tests/CPAD.Tests/Smoke/SafeSmoke.ps1
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- verify-package
```

### 5. 构建安装器

```powershell
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- fetch-assets --version <version>
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- verify-assets --version <version>
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- publish --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- package-portable --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- package-dev --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- package-installer --configuration Release --runtime win-x64 --version <version>
dotnet run --project ./src/CPAD.BuildTool/CPAD.BuildTool.csproj -- verify-package --configuration Release --runtime win-x64 --version <version>
```

发布产物位于 `artifacts/buildtool`。当前历史产物名可能仍为 `CPAD.exe`；正式产品发布建议逐步切换为 `CodexCliPlus.exe`、`CodexCliPlus-Setup.exe`、`CodexCliPlus-Portable.zip`。



## 文档

- [架构设计](./docs/architecture-freeze.md)
- [仓库纠偏与差距分析](./docs/repository-gap-analysis.md)
- [构建与发布](./docs/build-and-release.md)

## 免责声明

CodexCliPlus is not affiliated with OpenAI, not an official Codex App, and not a ChatGPT Plus subscription product.

CodexCliPlus 不隶属于 OpenAI，不是 OpenAI 官方 Codex App，也不是 ChatGPT Plus / Pro 订阅产品。项目仅提供本地桌面管理、配置、诊断、插件与工作流增强能力。

## 许可证

MIT
