# Cli Proxy API Desktop

## 简介

Cli Proxy API Desktop 是一个面向 Windows 的中文桌面一站式 Hub，目标是把当前分散的 CPA、Codex、本地模式切换、插件市场、更新中心和持久化统计统一成一个可安装、可更新、可卸载、可持续演进的桌面产品。

本项目当前处于建库与架构冻结阶段。首版目标不是继续维护多个分散仓库，而是以 `Cli Proxy API Desktop` 为唯一主仓库，逐步吸收原 `CPA-UV` 及其相关桥接项目、管理中心项目和自有插件集合。

相关文档：

- [完整开发计划](docs/开发计划.md)
- [已确认条目](docs/已确认事项.md)
- [待整合资产清单](docs/待整合资产清单.md)
- [Codex 仓库协作约束](AGENTS.md)

## 相对官方 CPA 的新特性

- 只支持 `Windows + 中文`，不再为其他平台和多语言分散工程精力。
- 提供基于 Chromium 的桌面前端，统一承载安装、设置、状态、日志、更新和插件市场。
- 提供统一安装目录 `C:\Users\<主用户>\Cli Proxy API Desktop\`，实现更直观的目录管理与完整卸载。
- 将原先分散在脚本、配置文件、桌面快捷方式和本地桥接中的 Codex 切换逻辑收敛为产品内受控能力。
- 通过系统服务实现开机静默常驻，在未登录时也能维持后台服务运行。
- 统一管理 CPA、Codex CLI、插件市场、更新中心和持久化统计，不再依赖用户手工拼装环境。
- 仅支持自有插件集合，并在桌面端内置插件市场入口。
- 统一更新入口，桌面端负责兼容性策略，Codex CLI 不再建议通过 `npm` 直接升级。
- 将统计、状态、日志和本地数据库持久化到安装目录内，替代“后端重启后易丢失”的临时态体验。
- 通过最小桥接兼容旧路径，同时把真实数据统一收敛到安装目录。

## 依赖

运行与开发基线按当前规划限定为必要依赖：

- Windows 10/11 x64
- Git
- Node.js LTS
- Electron
- SQLite
- Go
- 官方 Codex CLI Windows 版本

说明：

- 不支持 WSL 作为默认运行前置。
- 不纳入与 CPAD/Codex 主链路无关的第三方依赖集合。
- Codex CLI 由桌面端统一管理版本与更新策略。

## 安装方式

当前仓库仍处于规划期，首版安装器尚未发布。当前阶段建议按以下方式使用：

1. 克隆本仓库。
2. 阅读 [完整开发计划](docs/开发计划.md) 与 [已确认条目](docs/已确认事项.md)。
3. 基于文档推进桌面端、后台服务、更新中心与插件市场的统一实现。

首版发布目标：

- 提供 Windows `exe` 安装器。
- 默认安装到 `C:\Users\<主用户>\Cli Proxy API Desktop\`。
- 安装完成后自动注册后台服务、创建桌面入口，并接管受控 `codex` 调用入口。

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=Blackblock-inc/Cli-Proxy-API-Desktop&type=Date)](https://www.star-history.com/#Blackblock-inc/Cli-Proxy-API-Desktop&Date)
