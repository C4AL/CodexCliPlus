# Cli Proxy API Desktop

## 简介

Cli Proxy API Desktop 是一个面向 Windows 的中文桌面一站式 Hub，目标是把当前分散的 CPA、Codex、本地模式切换、插件市场、更新中心和持久化统计统一成一个可安装、可更新、可卸载、可持续演进的桌面产品。

本项目当前处于建库与架构冻结阶段。首版目标不是继续维护多个分散仓库，而是以 `Cli Proxy API Desktop` 为唯一主仓库，逐步吸收原 `CPA-UV` 及其相关桥接项目、管理中心项目和自有插件集合。

点击链接加入群聊【CPAD(CLI Proxy API Desktop)交流群】：https://qm.qq.com/q/OgLgNmwk2O

相关文档：

- [完整开发计划](docs/开发计划.md)
- [已确认条目](docs/已确认事项.md)
- [待整合资产清单](docs/待整合资产清单.md)
- [开源继承声明](docs/开源继承声明.md)
- [Codex 仓库协作约束](AGENTS.md)

## 当前开发状态

仓库现已推进到 `M4 插件市场`，当前已落地：

- Electron 桌面主进程、预加载桥和前端首页骨架
- Windows Service Go 宿主骨架
- 安装目录布局、`service-state.json` 与 `app.db` 约定
- SQLite 状态持久化骨架与服务事件记录
- `codex-mode.json` 与 `codex.exe` shim 骨架
- Windows Service 管理命令骨架：`install / remove / start / stop / status`
- CPA Runtime 受控命令骨架：`cpa-runtime status / build / start / stop`
- 受控 CPA Runtime 构建输出、状态文件、日志文件与托管 `config.yaml`
- 受控 CPA Runtime 默认端口收敛为 `127.0.0.1:2723` 对应的托管配置基线，旧默认端口会在托管配置中自动迁移
- Codex 模式切换已宿主化，并接入桌面前端动作入口
- 更新中心状态检查与同步骨架，已能跟踪官方主程序基线、官方管理中心基线、CPA-UV 源仓、插件源仓与受控运行时
- 插件市场清单格式、刷新、安装、更新、启用、禁用与诊断命令已落地
- CPAD 主仓已保持为独立的 Electron + Go 构建项目，旧项目只作为来源与受控外部资产，不再反向污染主仓结构
- 本地构建链路：`npm run build`、`npm run build:service`、`npm run build:shim`、`npm run service:status`、`npm run cpa:runtime:*`、`npm run plugin:market:*` 与 `npm run update:center:*`

当前目标是继续完成 `4.3 第三阶段：后端接管` 的收尾动作，在保持 CPAD 主仓结构干净的前提下，对接官方主程序仓 `router-for-me/CLIProxyAPI` 与官方管理中心仓 `router-for-me/Cli-Proxy-API-Management-Center` 的最新基线，并确保 CPA-UV 覆盖兼容不丢能力。

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
- 变更同步采用点对点方式执行，每次同步只允许一个明确源与一个明确目标，避免重新引入多份真实状态源。

## 开源继承与许可证声明

本项目的方向不是从零开始另起炉灶，而是要逐步吸收并重构现有 `CPA-UV` 及其相关桥接能力。`CPA-UV` 当前基于上游 `router-for-me/CLIProxyAPI` 演进，而 `CLIProxyAPI` 当前采用 `MIT License`；本项目在后续整合其实质代码、资源或其重要衍生部分时，将继续遵守并保留该开源许可链路中的版权与许可声明。

当前仓库已直接采用 MIT 许可证，并明确保留上游许可继承关系。后续只要有来自 `CLIProxyAPI` / `CPA-UV` 的代码或实质性衍生内容进入本仓库，就必须继续保留相应版权与许可文本。

详细说明见：

- [LICENSE](LICENSE)
- [开源继承声明](docs/开源继承声明.md)

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

当前仓库尚未发布正式安装器，但已提供本地开发骨架。当前阶段建议按以下方式使用：

1. 克隆本仓库。
2. 阅读 [完整开发计划](docs/开发计划.md)、[已确认条目](docs/已确认事项.md) 与 [开源继承声明](docs/开源继承声明.md)。
3. 安装 Node.js 依赖：`npm install`
4. 构建桌面壳：`npm run build`
5. 构建服务宿主：`npm run build:service`
6. 构建 `codex.exe` shim：`npm run build:shim`
7. 查看当前安装目录布局：`npm run service:layout`
8. 查看当前宿主快照：`npm run service:status`
9. 查看当前 Codex 模式：`npm run codex:mode:status`
10. 查看 CPA Runtime 状态：`npm run cpa:runtime:status`
11. 受控构建 CPA Runtime：`npm run cpa:runtime:build`
12. 受控启动 CPA Runtime：`npm run cpa:runtime:start`
13. 受控停止 CPA Runtime：`npm run cpa:runtime:stop`
14. 刷新插件市场清单：`npm run plugin:market:refresh`
15. 查看插件市场状态：`npm run plugin:market:status`
16. 刷新更新中心状态：`npm run update:center:check`
17. 查看更新中心状态：`npm run update:center:status`
18. 同步官方双基线工作树：`npm run update:center:sync`
19. 启动桌面开发环境：`npm run dev`

首版发布目标：

- 提供 Windows `exe` 安装器。
- 默认安装到 `C:\Users\<主用户>\Cli Proxy API Desktop\`。
- 安装完成后自动注册后台服务、创建桌面入口，并接管受控 `codex` 调用入口。

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=Blackblock-inc/Cli-Proxy-API-Desktop&type=Date)](https://www.star-history.com/#Blackblock-inc/Cli-Proxy-API-Desktop&Date)
