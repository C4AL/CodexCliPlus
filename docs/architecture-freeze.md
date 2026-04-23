# CPAD 架构冻结

## 目标

将 CPAD 冻结为一个面向 Windows 的桌面宿主，而不是重新实现 CLIProxyAPI 生态。

## 冻结范围

- 平台：Windows 10/11 x64
- 宿主技术栈：C# + WPF + WebView2 + .NET 10
- 后端：保留 CLIProxyAPI Go 后端
- 管理界面：保留原版 `management.html`
- Codex 范围：只面向 Codex CLI
- 源切换：只做 `official` / `cpa`

## 非谈判原则

- 不重写原 WebUI
- 不重写原 Go 后端
- 不把桌面端特性硬塞进原 WebUI 源码
- 只在桌面宿主层新增能力
- 所有新增能力必须可编译、可运行、可打包、可回归

## 宿主负责的能力

- 后端资源准备、版本固定与校验
- 运行目录、日志目录、配置目录初始化
- 启动/停止/重启后端
- 端口探测、健康检查、故障提示
- 托盘、最小化、退出策略、开机自启
- WebView2 承载原管理页
- Web 与 Native 的最小桥接
- Codex CLI 检测、配置备份、`official/cpa` 切换、启动入口
- Windows 安装器和发布产物整理

## 明确不做

- Electron
- 插件中心
- 跨平台桌面端
- 面向 Codex IDE / Codex App 的专门适配
- 对原 WebUI 主业务流程的 fork 式改造

## 目录策略

- 安装目录：桌面宿主程序与打包好的运行资源
- 应用数据目录：`%LocalAppData%\CliProxyApiDesktop\`
- 日志目录：`%LocalAppData%\CliProxyApiDesktop\logs\`
- 桌面配置：`%LocalAppData%\CliProxyApiDesktop\config\desktop.json`
- 后端运行目录：`%LocalAppData%\CliProxyApiDesktop\backend\`
- Codex 配置：继续使用 `~/.codex/config.toml`

## 上游资源策略

- CLIProxyAPI：固定官方 release 版本并在构建阶段抓取
- `management.html`：固定官方 release 版本并在构建/打包阶段准备
- Codex CLI：遵循 OpenAI 官方 `config.toml` 和 `profile` 机制

## 安全与默认值

- 后端默认只监听 `127.0.0.1`
- 默认关闭远程管理暴露
- 默认生成本地管理密钥
- 桌面端访问默认经由本地回环地址

## V1 成功标准

- `dotnet build` 和 `dotnet test` 可通过
- 桌面端可拉起后端并嵌入管理页
- WebUI 核心功能保持可用
- Codex `official/cpa` 可切换并支持真实启动验证
- 可生成 Windows x64 安装器
