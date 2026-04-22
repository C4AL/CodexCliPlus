# Edge Control

`edge-control` 是一个给 Codex 使用的本地 Edge 控制插件。它通过本地 bridge、Edge 扩展和 MCP 工具，把当前 Edge 会话暴露给 Codex，避免依赖截图、额外的 DevTools 窗口或单独的浏览器 profile。

本仓库已经整理为适合上传到 GitHub 的形态：

- README 为中文。
- 不包含广告、赞助引导或推广内容。
- 本地机密配置 `extension/config.local.js` 不进入仓库。
- 支持快速部署脚本。
- 支持 GitHub Actions 一键自动 patch release。

## 主要能力

- 枚举、聚焦、切换当前 Edge 标签页
- 导航、刷新、点击、输入、等待元素
- 读取 DOM、执行页面 JavaScript、发送 CDP 指令
- 对页面做快照、抽取链接、批量抓取、搜索页抓取
- 通过 crawler runtime 做多查询、多站点、多阶段页面采集
- 热重载本地 unpacked Edge 扩展

## 架构

完整链路如下：

`Codex MCP -> scripts/mcp-server.mjs -> localhost bridge -> Edge extension -> chrome.scripting / chrome.debugger`

对应目录如下：

- `extension/`：Edge 扩展
- `scripts/bridge-server.mjs`：本地 bridge
- `scripts/mcp-server.mjs`：Codex MCP server
- `scripts/lib/`：抓取与控制核心逻辑
- `skills/`：给 Codex 使用的辅助 skill

## 仓库公开上传前已经处理的内容

- `.mcp.json` 改为相对路径，不再写死本机绝对路径。
- `.gitignore` 已忽略 `extension/config.local.js`、`scripts/node_modules/`、`dist/` 等本地文件。
- 版本号已经统一到 `0.1.6`。
- 运行时本地配置改为通过 `scripts/install.ps1` 自动生成。
- 提供 `extension/config.local.example.js` 作为示例，但不会提交真实 token。

## 快速部署

### 方式一：最快部署

如果你使用 release 里的快速部署包，解压后直接运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\quick-deploy.ps1
```

这个脚本会完成下面几件事：

- 生成本地 bridge 和扩展配置
- 强制把 `skills/edge-browser-ops` 安装到全局 Codex skills 目录
- 如有需要则安装 Node 依赖
- 启动本地 bridge
- 如果本机安装了 `codex`，自动注册 MCP

首次使用仍然需要手动在 Edge 中加载一次 unpacked 扩展：

1. 打开 `edge://extensions`
2. 开启 `Developer mode`
3. 选择 `Load unpacked`
4. 选择当前项目下的 `extension` 目录

### 方式二：手动部署

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -StartBridge
powershell -ExecutionPolicy Bypass -File .\scripts\register-codex-mcp.ps1
```

如果你的发布包已经包含 `scripts/node_modules`，`install.ps1` 默认会直接复用，不会重复联网安装。

### 方式三：从 GitHub release 一键安装

仓库现在提供了一个 GitHub release 自举安装脚本：

- `scripts/install-from-github-release.ps1`

它会自动完成下面几步：

- 访问 GitHub release API
- 下载最新或指定 tag 的 `quick-deploy` 压缩包
- 解压到本机安装目录
- 调用 `quick-deploy.ps1`
- 强制安装全局 `edge-browser-ops` skill
- 注册 Codex MCP

如果你的公开仓库地址是 `your-name/codex-edge-control-bridge`，可以直接用这一条命令：

```powershell
powershell -ExecutionPolicy Bypass -Command "& { $tmp = Join-Path $env:TEMP 'install-edge-control-from-release.ps1'; Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/your-name/codex-edge-control-bridge/main/scripts/install-from-github-release.ps1' -OutFile $tmp; & $tmp -Repository 'your-name/codex-edge-control-bridge' }"
```

如果要安装指定 tag，可以追加 `-Tag 'v0.1.7'` 之类的参数。

## 目录中的关键脚本

- `scripts/install.ps1`：生成本地配置并按需安装依赖
- `scripts/install-global-skill.ps1`：强制把 `edge-browser-ops` 安装到全局 Codex skills
- `scripts/install-from-github-release.ps1`：从 GitHub release 下载并自举安装
- `scripts/start-host.ps1`：静默启动 bridge
- `scripts/register-codex-mcp.ps1`：把当前插件注册到 Codex MCP
- `scripts/quick-deploy.ps1`：一键部署入口
- `scripts/build-release.ps1`：构建发布资产
- `scripts/release-patch.ps1`：自动提升 patch 版本并打包
- `scripts/sync-version.mjs`：同步多个文件中的版本号

## GitHub 仓库上传

当前目录已经可以直接初始化并上传到 GitHub。一个常用流程如下：

```powershell
git init -b main
git add .
git commit -m "chore: initial public release"
git remote add origin <你的 GitHub 仓库地址>
git push -u origin main
```

上传前建议确认：

- 不要把 `extension/config.local.js` 加入版本控制
- 不要把本机 `dist/` 发布产物误提交到源码分支
- 如果你想保留快速部署体验，优先把 `release` 资产交给用户，而不是把 `scripts/node_modules/` 提交进源码仓库

## 自动 Patch Release

仓库已经内置 GitHub Actions 工作流：

- 工作流文件：`.github/workflows/release-patch.yml`
- 触发方式：GitHub Actions 页面手动运行 `Patch Release`

运行后会自动执行：

1. 安装 `scripts/` 下的依赖
2. 把版本号从当前版本提升一个 patch
3. 生成源码包和快速部署包
4. 创建 tag
5. 推送版本提交和 tag
6. 发布 GitHub Release

## 发布资产说明

`scripts/build-release.ps1` 会生成以下文件到 `dist/`：

- `edge-control-vX.Y.Z-source.zip`
  适合源码上传、二次开发、继续维护 GitHub 仓库
- `edge-control-vX.Y.Z-quick-deploy.zip`
  内含 `scripts/node_modules`，适合快速落地部署
- `checksums-vX.Y.Z.txt`
  提供 SHA-256 校验值
- `release-notes-vX.Y.Z.md`
  作为 GitHub Release 正文使用

公开仓库建议额外保留 `scripts/install-from-github-release.ps1`，这样新用户不需要先 clone 仓库，也可以直接从 release 安装。

## 手动构建发布包

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

如果要直接提升 patch 版本并同时打包：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\release-patch.ps1
```

## 当前边界

- 这是本地扩展 + bridge 的方案，不是对 Edge 内核的永久注入。
- 仍然需要一次性加载 unpacked 扩展，或者在 Edge 未运行时用带扩展参数的方式启动。
- MV3 service worker 仍然可能被浏览器挂起，但当前实现会自动重连。
- 已运行中的 Edge 会话不保证能通过命令行参数即时补挂扩展，这属于 Chromium 机制限制。

## 安全说明

- bridge 只监听本机 `127.0.0.1`
- 鉴权使用本地生成的 token
- `extension/config.local.js` 是运行时本地文件，不应提交到 GitHub
- 扩展拥有较高权限，请仅在你信任的本机环境中使用
