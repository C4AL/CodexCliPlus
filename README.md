<p align="center">
  <img src="./resources/icons/ico-transparent.png" alt="CPAD" width="160" />
</p>

<h1 align="center">CPAD</h1>

<p align="center">
  Cli Proxy API Desktop
  <br />
  基于 C# / .NET 10 的 Windows 原生桌面壳与本地服务
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" />
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
  <a href="./docs/CPAD-.NET10-%E7%BB%9F%E4%B8%80%E9%87%8D%E6%9E%84%E6%96%B9%E6%A1%88.md">重构方案</a>
  ·
  <a href="./docs/%E5%BC%80%E6%BA%90%E7%BB%A7%E6%89%BF%E5%A3%B0%E6%98%8E.md">开源继承声明</a>
  ·
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/issues">Issue</a>
  ·
  <a href="https://qm.qq.com/q/Q48OP64HwQ">QQ群</a>
</p>

CPAD 是一个面向 Windows 的统一桌面项目，目标是用单一 `C# / .NET 10` 重构原有混合技术栈，形成可维护的桌面端与本地服务主线。

## 功能

- 原生桌面端：`WPF + MVVM`
- 本地服务：`ASP.NET Core`
- 状态聚合接口：`/api/system/status`
- 内嵌预览位：`WebView2`
- 主线仓库结构：`apps/ + src/ + resources/ + docs/ + reference/`

## 当前进度

- 已完成根级 `.NET 10` 主结构整理
- 已落地 `CPAD.Service`
- 已落地 `CPAD.Desktop` 原生状态面板
- 已接入 `WebView2` 预览位
- 已保留迁移所需参考源码目录

## 项目结构

```text
/
  apps/
    CPAD.Desktop/
    CPAD.Service/
  src/
    CPAD.Domain/
    CPAD.Application/
    CPAD.Contracts/
    CPAD.Infrastructure/
  resources/
    icons/
  docs/
  reference/
  CPAD.sln
  Directory.Build.props
  Directory.Packages.props
  global.json
```

## 运行

```powershell
git clone https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop.git
cd Cli-Proxy-API-Desktop
dotnet build CPAD.sln
dotnet run --project apps/CPAD.Service
dotnet run --project apps/CPAD.Desktop
```

本地服务默认地址：

```text
http://127.0.0.1:17320
```

## 使用说明

1. 先启动 `CPAD.Service`。
2. 再启动 `CPAD.Desktop`。
3. 桌面端会读取本地服务状态，并在界面中展示统一状态总览。

## FAQ

- 为什么只支持 Windows？
  - 当前路线就是 `WPF + Windows Service + WebView2`。
- 现在是否已经完全重构完成？
  - 还没有，主线已统一到 `.NET 10`，但运行时控制和插件/更新执行链路仍在迁移。
- WebView2 是不是新的主前端？
  - 不是，当前主界面是原生 WPF，WebView2 只是嵌入预览位。
- 当前保留了哪些参考源码？
  - `official-backend`、`cpa-uv-overlay`、`edge-control`。

## 开发

- SDK：`10.0.203`
- 解决方案入口：`CPAD.sln`
- 桌面端入口：`apps/CPAD.Desktop`
- 服务端入口：`apps/CPAD.Service`

## 许可证

本仓库使用 `MIT License`。

- [开源继承声明](./docs/%E5%BC%80%E6%BA%90%E7%BB%A7%E6%89%BF%E5%A3%B0%E6%98%8E.md)
- [LICENSE](./LICENSE)

## 反馈

- Issue：https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/issues
- 点击链接加入群聊【CPAD(CLI Proxy API Desktop)交流群】：https://qm.qq.com/q/Q48OP64HwQ

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=Blackblock-inc/Cli-Proxy-API-Desktop&type=Date)](https://www.star-history.com/#Blackblock-inc/Cli-Proxy-API-Desktop&Date)
