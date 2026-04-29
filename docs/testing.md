# 测试与安全验收

## 常规验证顺序

```powershell
dotnet restore CodexCliPlus.sln
dotnet build CodexCliPlus.sln --configuration Release --no-restore
dotnet test tests\CodexCliPlus.Tests\CodexCliPlus.Tests.csproj --configuration Release --no-build
dotnet csharpier check src tests
```

UI 自动化测试需要本机可用的 WebView2/桌面交互环境：

```powershell
dotnet test tests\CodexCliPlus.UiTests\CodexCliPlus.UiTests.csproj --configuration Release
```

## WebUI 验证

```powershell
Push-Location resources\webui\upstream\source
npm run lint
npm run type-check
npm run test
npm run build
npm run knip:strict
Pop-Location
```

依赖变更后先运行 `npm ci` 或 `npm install`，并提交同步后的 `package-lock.json`。

## SafeSmoke

`tests/CodexCliPlus.Tests/Smoke/SafeSmoke.ps1` 是桌面 smoke 的安全入口。默认模式只做静态检查，不启动桌面进程：

```powershell
powershell -ExecutionPolicy Bypass -File tests\CodexCliPlus.Tests\Smoke\SafeSmoke.ps1
```

需要启动 smoke 时，必须指向当前发布输出中的 `CodexCliPlus.exe`，并使用隔离根目录：

```powershell
powershell -ExecutionPolicy Bypass -File tests\CodexCliPlus.Tests\Smoke\SafeSmoke.ps1 `
  -Launch `
  -AppPath "$PWD\artifacts\buildtool\publish\win-x64\CodexCliPlus.exe" `
  -RemoveSmokeRoot
```

脚本只允许启动 `CodexCliPlus.exe`，并只清理本次启动进程及其隔离 smoke 根目录下的 `ccp-core.exe` 子进程。

## 发布与包验证

发布或打包相关改动追加：

```powershell
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- fetch-assets
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- verify-assets
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- publish
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- package-online-installer
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- package-offline-installer
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- verify-package
```

包结构至少应包含：

- `CodexCliPlus.exe`
- `assets/backend/windows-x64/ccp-core.exe`
- `assets/webui/upstream/dist/index.html`
- `assets/webui/upstream/sync.json`
- `packaging/dependency-precheck.json`
- `packaging/update-policy.json`
- `packaging/uninstall-cleanup.json`

## 安全规则

- 不要对真实用户配置目录运行启动 smoke；必须隔离 `CODEX_HOME`、`USERPROFILE`、`HOME`、`TEMP`、`TMP`。
- 不要使用 `taskkill /IM`、`Stop-Process -Name` 或按进程名批量结束后端。
- 不要把旧的 `artifacts/publish` 或 `artifacts/installer` 目录当作当前发布证据。
- 不要删除或重命名 `resources/webui/upstream/dist/index.html`、`assets/webui/upstream/sync.json`、`ccp-core.exe` 或上游许可证。
- 完整安装、卸载和更新验收应在 VM、隔离测试用户或一次性环境中执行。
