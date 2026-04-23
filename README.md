<p align="center">
  <img src="https://raw.githubusercontent.com/Blackblock-inc/Cli-Proxy-API-Desktop/main/resources/icons/ico-transparent.png" alt="CPAD" width="160" />
</p>

<h1 align="center">CPAD</h1>

<p align="center">
  Cli Proxy API Desktop
  <br />
  基于 C# / .NET 10 + WebView2 的 Windows 原生桌面宿主
  <br />
  保留原 CLIProxyAPI Go 后端与原版 WebUI
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" />
  <img alt="Backend" src="https://img.shields.io/badge/backend-Go-00ADD8?style=flat-square" />
  <img alt="WebView2" src="https://img.shields.io/badge/WebView2-Enabled-0F6CBD?style=flat-square" />
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases">
    <img alt="Release" src="https://img.shields.io/github/v/release/Blackblock-inc/Cli-Proxy-API-Desktop?style=flat-square" />
  </a>
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/blob/main/LICENSE">
    <img alt="License" src="https://img.shields.io/github/license/Blackblock-inc/Cli-Proxy-API-Desktop?style=flat-square" />
  </a>
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/stargazers">
    <img alt="Stars" src="https://img.shields.io/github/stars/Blackblock-inc/Cli-Proxy-API-Desktop?style=flat-square" />
  </a>
</p>

<p align="center">
  <a href="./docs/CPAD-.NET10-%E6%A1%8C%E9%9D%A2%E5%AE%BF%E4%B8%BB%E6%96%B9%E6%A1%88.md">桌面宿主方案</a>
  ·
  <a href="./docs/%E5%BC%80%E6%BA%90%E7%BB%A7%E6%89%BF%E5%A3%B0%E6%98%8E.md">开源继承声明</a>
  ·
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/issues">Issue</a>
  ·
  <a href="https://qm.qq.com/q/Q48OP64HwQ">QQ群</a>
</p>

CPAD 是一个面向 Windows 的桌面化集成项目。

它不会重写或替代原 CLIProxyAPI 的核心后端能力，而是在保留原 **Go 后端** 与原版 **WebUI** 的前提下，使用 **C# / .NET 10 + WebView2** 提供更适合 Windows 用户的原生桌面宿主体验，包括：

- 原版 WebUI 内嵌展示
- 本地 CLIProxyAPI 一键启动 / 停止
- Windows 安装器与桌面快捷方式
- 托盘常驻与日志入口
- 中文化桌面交互
- Codex 配置管理
- Codex 源切换（CPA / 官方）


## 技术栈

- **Desktop Host**: C# / .NET 10 / WPF
- **Embedded UI**: WebView2
- **Backend**: CLIProxyAPI (Go)
- **Frontend**: 原版 Management WebUI
- **Target Platform**: Windows


## 运行

### 1. 克隆仓库
```powershell
git clone https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop.git
cd Cli-Proxy-API-Desktop
