# 贡献指南

感谢你愿意参与 CodexCliPlus。提交变更前请先确认改动范围清晰，并尽量让每个 PR 只处理一个主题。

## 开发准备

```powershell
dotnet tool restore
dotnet restore CodexCliPlus.sln
Push-Location resources/webui/upstream/source
npm ci
Pop-Location
```

## 本地验证

常规代码改动至少运行：

```powershell
dotnet build CodexCliPlus.sln --configuration Release --no-restore
dotnet test tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj --configuration Release --no-build
dotnet csharpier check src tests
```

WebUI 改动还需运行：

```powershell
Push-Location resources/webui/upstream/source
npm run lint
npm run type-check
npm run test
npm run build
Pop-Location
```

发布、安装器或资产链路改动还需运行 BuildTool 的资产、发布和包校验命令。

## 提交流程

- 不提交真实用户配置、密钥、令牌、日志或本地缓存。
- 后端托管资产名必须保持为 `ccp-core.exe`；上游压缩包内的 `cli-proxy-api.exe` 只作为输入来源。
- 桌面端用户可见文案保持简体中文。
- 大体积生成物应由 BuildTool、CI 或 release artifact 生成，不直接作为源码审查内容提交。
