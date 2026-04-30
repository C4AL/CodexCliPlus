# 测试与安全验收

## 常规验证顺序

```powershell
dotnet restore CodexCliPlus.sln
dotnet build CodexCliPlus.sln --configuration Release --no-restore
dotnet test tests\CodexCliPlus.Tests\CodexCliPlus.Tests.csproj --configuration Release --no-build --filter "Category!=LiveBackend&Category!=Smoke"
dotnet csharpier check src tests
```

PR CI 默认排除 `LiveBackend` 与 `Smoke` 分类。它们需要真实后端进程、端口和桌面/系统环境，由 `Backend Integration` 的 nightly/manual workflow 承担。

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
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- build-webui
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- publish
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- package-offline-installer
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- package-update
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- verify-package
dotnet run --project src\CodexCliPlus.BuildTool\CodexCliPlus.BuildTool.csproj -- write-checksums
```

Release 签名链路额外要求：

```powershell
$env:CODEXCLIPLUS_SIGNING_REQUIRED = "true"
$env:WINDOWS_CODESIGN_PFX_BASE64 = "<base64-pfx>"
$env:WINDOWS_CODESIGN_PFX_PASSWORD = "<pfx-password>"
$env:WINDOWS_CODESIGN_TIMESTAMP_URL = "http://timestamp.digicert.com"
```

包结构至少应包含：

- `CodexCliPlus.exe`
- `CodexCliPlus.exe.signature.json`（强制签名发布）
- `assets/backend/windows-x64/ccp-core.exe`
- `assets/webui/upstream/dist/index.html`
- `assets/webui/upstream/sync.json`
- `packaging/dependency-precheck.json`
- `packaging/update-policy.json`
- `packaging/uninstall-cleanup.json`
- `CodexCliPlus.Update.<version>.<rid>.zip`

## 安全规则

- 不要对真实用户配置目录运行启动 smoke；必须隔离 `CODEX_HOME`、`USERPROFILE`、`HOME`、`TEMP`、`TMP`。
- 不要使用 `taskkill /IM`、`Stop-Process -Name` 或按进程名批量结束后端。
- 不要把旧的 `artifacts/publish` 或 `artifacts/installer` 目录当作当前发布证据。
- 不要删除或重命名 `assets/webui/upstream/dist/index.html`、`assets/webui/upstream/sync.json`、`ccp-core.exe` 或上游许可证；`resources/webui/upstream/dist` 是本地生成目录，不应作为源码提交。
- 完整安装、卸载和更新验收应在 VM、隔离测试用户或一次性环境中执行。
