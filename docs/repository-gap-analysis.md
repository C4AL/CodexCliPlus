# 仓库纠偏与差距分析

## 当前仓库与冻结范围的冲突

截至启动开发前，仓库主线只包含：

- `README.md`
- 图标资源
- `_codex_build_steps.md`

与冻结范围直接冲突或不完整的地方如下：

1. 仓库没有任何 `.sln`、WPF 宿主、测试项目或构建配置，无法满足“可编译、可运行、可打包”。
2. 仓库没有 `docs/` 实体内容，但 `README` 链接指向了不存在的文档。
3. 仓库没有后端资源接入策略，也没有原版 `management.html` 的集成方案说明。
4. 仓库没有 Codex CLI 配置托管、`official/cpa` 切换、备份和回滚设计。
5. 仓库没有安装器、发布脚本、验收与测试矩阵落地文件。

## 纠偏后的固定方向

为消除上面的偏差，仓库后续统一按以下方向推进：

- 只做 Windows x64 桌面宿主
- 只做 C# + WPF + WebView2 + .NET 10
- 原 Go 后端和原 WebUI 不重写，只接入官方发布资产
- Codex 只覆盖 CLI 使用场景
- 只支持 `official` / `cpa` 两种源切换

## 文档层面的纠偏动作

- 重写根 `README.md`
- 新增 `docs/architecture-freeze.md`
- 新增 `docs/build-and-release.md`
- 在所有文档中移除 Electron、插件中心、多平台等 V1 主线表述

## 实现层面的纠偏动作

- 建立 `src/DesktopHost`、`DesktopHost.Core`、`DesktopHost.Infrastructure`、`DesktopHost.Tests`
- 加入统一构建配置、测试、安装器与资源抓取脚本
- 用桌面端托管原版 CLIProxyAPI 和原版 `management.html`
- 用 OpenAI 官方 `config.toml` 的 `profile` 能力管理 `official/cpa`
