# 构建与发布

## 常规构建

```powershell
dotnet tool restore
dotnet restore CodexCliPlus.sln
dotnet build CodexCliPlus.sln --configuration Release --no-restore
dotnet test tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj --configuration Release --no-build
dotnet run --project src/CodexCliPlus.App/CodexCliPlus.App.csproj
```

## WebUI 验证

```powershell
Push-Location resources/webui/upstream/source
npm ci
npm run lint
npm run type-check
npm run test
npm run build
Pop-Location
```

## BuildTool 命令面

当前 BuildTool 命令为：

```powershell
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- fetch-assets
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-assets
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- publish
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-online-installer
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-offline-installer
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-package
```

通用选项：

- `--configuration <Debug|Release>`，默认 `Release`
- `--runtime <rid>`，默认 `win-x64`
- `--version <version>`，默认 `1.0.0`
- `--repo-root <path>`，默认自动查找包含 `CodexCliPlus.sln` 的仓库根目录
- `--output <path>`，默认 `artifacts/buildtool`

## 发布流程

```powershell
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- fetch-assets --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-assets --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- publish --configuration Release --runtime win-x64 --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-online-installer --configuration Release --runtime win-x64 --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-offline-installer --configuration Release --runtime win-x64 --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-package --configuration Release --runtime win-x64 --version <version>
```

`publish` 会先构建 `resources/webui/upstream/source`，再执行桌面端 self-contained publish，并把后端、WebUI、许可证和发布清单复制到输出目录。

## 输出结构

默认输出根目录为 `artifacts/buildtool`：

- `publish/<rid>/CodexCliPlus.exe`
- `publish/<rid>/assets/backend/windows-x64/ccp-core.exe`
- `publish/<rid>/assets/webui/upstream/dist/index.html`
- `publish/<rid>/assets/webui/upstream/sync.json`
- `packages/CodexCliPlus.Setup.Online.<version>.exe`
- `packages/CodexCliPlus.Setup.Online.<version>.<rid>.zip`
- `packages/CodexCliPlus.Setup.Offline.<version>.exe`
- `packages/CodexCliPlus.Setup.Offline.<version>.<rid>.zip`

## 发布约束

- `ccp-core.exe` 是 CodexCliPlus 的托管后端资产名，不能在发布目录中改回 `cli-proxy-api.exe`。
- `resources/webui/upstream/dist/index.html` 和 `resources/webui/upstream/sync.json` 是 WebUI 打包契约的一部分。
- 在线安装器用于后续在线依赖/更新路径，离线安装器用于捆绑优先的完整校验路径。
- `verify-package` 是包结构和关键可执行文件校验，不等同于完整安装、卸载和更新验收。
- 稳定更新元数据指向 `https://github.com/C4AL/CodexCliPlus/releases/latest`；Beta 仍为保留渠道。
