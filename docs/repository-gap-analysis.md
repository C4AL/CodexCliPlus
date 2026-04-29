# 项目状态与待办

## 快照

- 日期：2026-04-29
- 桌面前端基线：仓库内置 WebUI，由 `CodexCliPlus.App` 通过 WebView2 本地加载。
- 构建入口：`CodexCliPlus.sln`、`CodexCliPlus.BuildTool`、`justfile`。
- 发布状态：在线/离线安装器链路已进入 BuildTool；完整安装、卸载、更新验收仍需隔离环境证明。

## 已完成

- CodexCliPlus 命名的 solution、项目文件、测试项目和发布产物名已建立。
- WPF shell、托盘行为、WebView2 宿主、后端托管、诊断和安全凭据存储已建立。
- WebUI 源码位于 `resources/webui/upstream/source`，构建产物和同步元数据位于 `resources/webui/upstream/dist` 与 `resources/webui/upstream/sync.json`。
- 桌面 bootstrap 已覆盖 `desktopMode`、`apiBase`、`managementKey`，并避免将管理密钥写入浏览器持久化存储。
- `CodexCliPlus.BuildTool` 负责：
  - `fetch-assets`
  - `verify-assets`
  - `publish`
  - `package-online-installer`
  - `package-offline-installer`
  - `verify-package`
- 自动化验证覆盖 .NET 构建/测试、WebUI lint/type/test/build、CSharpier、knip 和 SafeSmoke 静态/启动检查。

## 待完成

- 对 9 个主要管理页面和关键二级流程执行固定视口截图验收，并把重叠、空白闪烁和滚动性能问题纳入回归检查。
- 在隔离 VM 或一次性测试用户中跑通真实安装、卸载、更新和 `KeepMyData` 组合验收。
- 发布并验证 `C4AL/CodexCliPlus` 的稳定 GitHub Releases 线。
- 继续保持 WebUI 上游同步卫生，避免把可追踪的上游差异变成不可审计的零散改写。
- 逐步收敛保留的兼容代码和源码注释债务，但不改变后端管理 API、WebView2 bridge payload 或打包目录契约。

## 当前非阻塞债务

- 部分 WPF 原生管理页仍保留用于编译、测试和过渡期兼容，不是运行时主前端方向。
- BuildTool 的 `verify-package` 只能证明包结构和关键文件存在，不能替代完整安装/卸载验收。
- 上游后端文档位于 `resources/backend/**`，描述的是 CLIProxyAPI 行为，不代表 CodexCliPlus 桌面 UI 或安装器承诺。
