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
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- build-webui
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- publish
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-online-installer
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-offline-installer
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-package
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- write-checksums
```

通用选项：

- `--configuration <Debug|Release>`，默认 `Release`
- `--runtime <rid>`，默认 `win-x64`
- `--version <version>`，默认 `1.0.0`
- `--repo-root <path>`，默认自动查找包含 `CodexCliPlus.sln` 的仓库根目录
- `--output <path>`，默认 `artifacts/buildtool`

## 发布流程

正式 GitHub Release 强制启用 Windows Authenticode 签名。Release workflow 需要以下配置：

- `WINDOWS_CODESIGN_PFX_BASE64`：代码签名 PFX 的 Base64 内容。
- `WINDOWS_CODESIGN_PFX_PASSWORD`：PFX 密码。
- `WINDOWS_CODESIGN_TIMESTAMP_URL`：可选 repository variable，默认 `http://timestamp.digicert.com`。

本地未配置证书时，BuildTool 会写入 `.unsigned.json` 侧车文件；Release workflow 设置 `CODEXCLIPLUS_SIGNING_REQUIRED=true`，缺少签名密钥会直接失败。

```powershell
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- fetch-assets --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-assets --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- build-webui --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- publish --configuration Release --runtime win-x64 --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-online-installer --configuration Release --runtime win-x64 --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- package-offline-installer --configuration Release --runtime win-x64 --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- verify-package --configuration Release --runtime win-x64 --version <version>
dotnet run --project src/CodexCliPlus.BuildTool/CodexCliPlus.BuildTool.csproj -- write-checksums --configuration Release --runtime win-x64 --version <version>
```

`build-webui` 会从 `resources/webui/upstream/source` 构建 WebUI，并把产物写入 `artifacts/buildtool/assets/webui/upstream`。`publish` 会校验后端资产、刷新 WebUI 生成产物，再执行桌面端 self-contained publish，并把后端、WebUI、许可证和发布清单复制到输出目录。

`publish` 会签名桌面主程序；`package-online-installer` 和 `package-offline-installer` 会签名安装器 exe。zip、SBOM、manifest 和 checksum 文件由 GitHub artifact attestation 覆盖，`release-manifest.json` 会记录 `signed`、`signatureKind`、`signatureMetadataPath` 和 `attestationExpected`。

## 输出结构

默认输出根目录为 `artifacts/buildtool`：

- `publish/<rid>/CodexCliPlus.exe`
- `publish/<rid>/assets/backend/windows-x64/ccp-core.exe`
- `publish/<rid>/assets/webui/upstream/dist/index.html`
- `publish/<rid>/assets/webui/upstream/sync.json`
- `packages/CodexCliPlus.Setup.Online.<version>.exe`
- `packages/CodexCliPlus.Setup.Online.<version>.exe.signature.json`
- `packages/CodexCliPlus.Setup.Online.<version>.<rid>.zip`
- `packages/CodexCliPlus.Setup.Online.<version>.<rid>.zip.signature.json`
- `packages/CodexCliPlus.Setup.Offline.<version>.exe`
- `packages/CodexCliPlus.Setup.Offline.<version>.exe.signature.json`
- `packages/CodexCliPlus.Setup.Offline.<version>.<rid>.zip`
- `packages/CodexCliPlus.Setup.Offline.<version>.<rid>.zip.signature.json`
- `SHA256SUMS.txt`
- `release-manifest.json`

## 发布校验

下载 Release 资产后，先用 `SHA256SUMS.txt` 校验字节，再核对 `release-manifest.json` 中的签名和 attestation 状态：

```powershell
Get-FileHash .\CodexCliPlus.Setup.Offline.<version>.exe -Algorithm SHA256
gh attestation verify .\CodexCliPlus.Setup.Offline.<version>.exe --repo C4AL/CodexCliPlus
```

安装器 exe 应包含 Authenticode 签名；非 PE 资产应有 GitHub artifact attestation。

## 发布约束

- `ccp-core.exe` 是 CodexCliPlus 的托管后端资产名，不能在发布目录中改回 `cli-proxy-api.exe`。
- `resources/webui/upstream/source` 和 `resources/webui/upstream/sync.json` 是 WebUI 来源契约；`resources/webui/upstream/dist` 是本地生成目录，不再作为源码跟踪内容。
- `resources/source-manifest.json` 记录字体、图标和 WebUI logo 的来源、许可证、大小与 SHA-256；变更这些资源必须同步更新 manifest。
- 在线安装器用于后续在线依赖/更新路径，离线安装器用于捆绑优先的完整校验路径。
- `verify-package` 是包结构和关键可执行文件校验，不等同于完整安装、卸载和更新验收。
- 稳定更新元数据指向 `https://github.com/C4AL/CodexCliPlus/releases/latest`；Beta 仍为保留渠道。
