# 构建与发布

## 开发构建

```powershell
pwsh ./build/scripts/fetch-assets.ps1
dotnet build CliProxyApiDesktop.sln
dotnet test src/DesktopHost.Tests/DesktopHost.Tests.csproj
```

## 本地运行与自动化验证

```powershell
dotnet run --project ./src/DesktopHost/DesktopHost.csproj
dotnet run --project ./src/DesktopHost/DesktopHost.csproj -- --smoke
dotnet run --project ./src/DesktopHost/DesktopHost.csproj -- --verify-onboarding
dotnet run --project ./src/DesktopHost/DesktopHost.csproj -- --verify-hosting
```

说明：

- `--smoke`：验证主窗口可启动并自动退出
- `--verify-onboarding`：验证首次运行向导可闭环完成并自动退出
- `--verify-hosting`：验证 WebView2 托管原版 `management.html` 的完整宿主链路

## 发布构建

```powershell
pwsh ./build/scripts/publish.ps1
pwsh ./build/scripts/build-installer.ps1
```

`publish.ps1` 会完成这些动作：

- 校验/下载 CLIProxyAPI Windows x64 二进制
- 校验/下载原版 `management.html`
- 下载 WebView2 Evergreen Bootstrapper
- 产出 `artifacts/publish/win-x64/`
- 将后端和管理页打包进 `assets/backend/windows-x64/` 与 `assets/webview2/`

`build-installer.ps1` 会：

- 在缺少 Inno Setup 时自动通过 `winget` 安装
- 编译 `build/installer/CPAD.iss`
- 输出安装包到 `artifacts/installer/`
- 生成 `release-info.txt`，包含版本号、commit 和 SHA256

## 安装器验证

```powershell
pwsh ./build/scripts/verify-installer.ps1
```

验证脚本会实际执行：

- 静默安装到临时目录
- 检查桌面和开始菜单快捷方式
- 运行已安装程序的 `--verify-onboarding`
- 运行已安装程序的 `--verify-hosting`
- 静默卸载并确认安装目录被清理

## 安装方法

1. 运行 `artifacts/installer/CPAD-Setup-<version>.exe`
2. 按安装向导完成用户级安装
3. 从桌面快捷方式或开始菜单启动 `CPAD`

## `official` / `cpa` 切换方法

桌面主窗口右侧 `Codex CLI` 区域提供：

- `设为 official`
- `设为 cpa`
- `恢复官方默认`
- `复制命令`
- `用当前源启动`
- `以 official 启动`
- `以 cpa 启动`

CPAD 会维护 `~/.codex/config.toml` 中的托管块，并在 `official` / `cpa` 间切换 `auth.json`。

## 发布原则

- 发布目标只包含 Windows x64
- 安装包必须包含桌面宿主、后端资源和管理页资源
- 安装器必须提供快捷方式、卸载入口和 WebView2 Runtime 检测
- 每个阶段都要先真实 build/test/运行验证，再进入下一阶段
- 发布说明必须输出版本号、commit、SHA256 和验证结果

## 资源来源

- CLIProxyAPI：官方 Windows x64 release
- Management WebUI：官方 `management.html` release
- WebView2：Microsoft Runtime
