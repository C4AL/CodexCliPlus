## 变更摘要

- 请简要说明本次变更。

## 验证

- [ ] `dotnet build CodexCliPlus.sln --configuration Release --no-restore`
- [ ] `dotnet test tests/CodexCliPlus.Tests/CodexCliPlus.Tests.csproj --configuration Release --no-build`
- [ ] WebUI 验证已按需运行
- [ ] BuildTool/发布验证已按需运行

## 检查项

- [ ] 未提交真实密钥、令牌、用户配置或本地缓存
- [ ] 桌面端新增用户可见文案保持简体中文
- [ ] 后端托管资产名仍为 `ccp-core.exe`
