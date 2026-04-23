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

## 冻结范围

V1 已冻结为以下范围：

- 平台：Windows 10/11 x64
- 宿主：C# + WPF + WebView2 + .NET 10
- 后端：保留原 CLIProxyAPI Go 后端
- 管理界面：保留原版 Management WebUI
- Codex：只面向 Codex CLI
- 源切换：只做 `official` / `cpa`

以下内容不在 V1：

- Electron
- 插件中心
- 多平台发行
- Codex IDE / App 专门适配
- 对原 WebUI 的大改版或重画

## 仓库结构

```text
/
├─ src/
│  ├─ DesktopHost/
│  ├─ DesktopHost.Core/
│  ├─ DesktopHost.Infrastructure/
│  └─ DesktopHost.Tests/
├─ resources/
│  ├─ backend/
│  ├─ icons/
│  └─ webview2/
├─ build/
│  ├─ installer/
│  └─ scripts/
└─ docs/
```

## 上游继承策略

CPAD 通过桌面壳整合上游项目，不在本仓库内 fork 重写：

- CLIProxyAPI Go 后端：使用官方发布资源接入
- Management WebUI：使用官方发布的 `management.html`
- Codex CLI：遵循官方 `config.toml`、`profile` 与认证路径

桌面宿主只负责：

- 下载/校验/打包上游资源
- 写入桌面专用后端配置
- 在 WebView2 中承载原版管理页
- 对 Codex CLI 做本地化托管和切换

## 开发要求

- 只支持中文桌面体验
- 不改写原 WebUI 主业务
- 不改写原 Go 后端核心逻辑
- 仅托管桌面产品拥有的配置字段
- 每个阶段必须经过实际构建与验证

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

## 安装与使用

### 安装

1. 运行 `artifacts/installer/CPAD-Setup-<version>.exe`
2. 安装器会复制桌面宿主、CLIProxyAPI、`management.html`
3. 如本机缺少 WebView2 Runtime，安装器会自动调用 `MicrosoftEdgeWebView2Setup.exe`
4. 安装完成后可从桌面快捷方式或开始菜单启动 `CPAD`

### `official` / `cpa` 切换

主窗口右侧 `Codex CLI` 卡片内提供完整切换入口：

- `设为 official`：把默认源切到 `official`
- `设为 cpa`：把默认源切到 `cpa`
- `恢复官方默认`：把默认源和认证恢复到 `official`
- `复制命令`：复制当前仓库对应的 `codex --profile ...` 启动命令
- `用当前源启动 / 以 official 启动 / 以 cpa 启动`：直接拉起终端并注入对应 profile

切换过程中 CPAD 会维护 `~/.codex/config.toml` 里的托管块，并在 `official` / `cpa` 间切换 `auth.json`。

## 文档

- [架构冻结](./docs/architecture-freeze.md)
- [仓库纠偏与差距分析](./docs/repository-gap-analysis.md)
- [构建与发布](./docs/build-and-release.md)

## 许可证

当前仓库自身文件按仓库声明发布；上游 CLIProxyAPI 与 Management WebUI 继续遵循各自项目许可证。
