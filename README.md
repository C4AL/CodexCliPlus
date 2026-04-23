<p align="center">
  <img src="https://raw.githubusercontent.com/Blackblock-inc/Cli-Proxy-API-Desktop/main/resources/icons/ico-transparent.png" alt="CPAD" width="160" />
</p>

<h1 align="center">CPAD</h1>

<p align="center">
  Cli Proxy API Desktop
  <br />
  面向 Windows x64 的 CLIProxyAPI 原生桌面宿主
  <br />
  C# / WPF / WebView2 / .NET 10 + 原版 CLIProxyAPI Go 后端 + 原版 Management WebUI
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6?style=flat-square" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" />
  <img alt="Host" src="https://img.shields.io/badge/Host-WPF%20%2B%20WebView2-0F6CBD?style=flat-square" />
  <img alt="Backend" src="https://img.shields.io/badge/Backend-CLIProxyAPI%20Go-00ADD8?style=flat-square" />
  <img alt="Codex" src="https://img.shields.io/badge/Codex-CLI%20Only-111111?style=flat-square" />
</p>

<p align="center">
  <a href="./docs/architecture-freeze.md">架构冻结</a>
  ·
  <a href="./docs/repository-gap-analysis.md">仓库纠偏</a>
  ·
  <a href="./docs/build-and-release.md">构建与发布</a>
  ·
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/issues">Issue</a>
</p>

## 项目定位

CPAD 是一个只面向 **Windows 10/11 x64** 的桌面化托管项目。

它不重写 CLIProxyAPI 的 Go 后端，也不重写原版 Management WebUI，而是在桌面宿主层补齐这些能力：

- 原版 `/management.html` 内嵌展示
- 本地后端一键启动、停止、重启、健康检查
- 桌面端日志、诊断、托盘、首次运行向导
- 中文化桌面交互
- Codex CLI 检测、配置管理与启动入口
- 仅支持 `official` / `cpa` 两种 Codex 源切换
- Windows 安装器与发布脚本


## 快速开始

### 1. 准备环境

- Windows 10/11 x64
- .NET SDK 10
- PowerShell 7 或 Windows PowerShell 5.1
- WebView2 Runtime（开发运行建议预装；安装器会自动检测并静默安装）
- Codex CLI（可选；未安装时仍可完成桌面宿主初始化）

### 2. 拉取并构建

```powershell
git clone https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop.git
cd Cli-Proxy-API-Desktop
pwsh ./build/scripts/fetch-assets.ps1
dotnet build CliProxyApiDesktop.sln
dotnet test src/DesktopHost.Tests/DesktopHost.Tests.csproj
```

### 3. 启动桌面宿主

```powershell
dotnet run --project ./src/DesktopHost/DesktopHost.csproj
```

### 4. 自动化验证

```powershell
dotnet run --project ./src/DesktopHost/DesktopHost.csproj -- --smoke
dotnet run --project ./src/DesktopHost/DesktopHost.csproj -- --verify-onboarding
dotnet run --project ./src/DesktopHost/DesktopHost.csproj -- --verify-hosting
```

### 5. 构建安装器

```powershell
pwsh ./build/scripts/publish.ps1
pwsh ./build/scripts/build-installer.ps1
pwsh ./build/scripts/verify-installer.ps1
```

安装器输出位于 `artifacts/installer/CPAD-Setup-<version>.exe`。


## 文档

- [架构冻结](./docs/architecture-freeze.md)
- [仓库纠偏与差距分析](./docs/repository-gap-analysis.md)
- [构建与发布](./docs/build-and-release.md)

## 许可证


