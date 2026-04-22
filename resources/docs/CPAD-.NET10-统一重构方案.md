# CPAD .NET 10 统一重构方案

## 目标

CPAD 的目标不是继续维护旧的 `Electron + React/TypeScript + Go service + PowerShell` 混合结构，而是统一到单一 `C# / .NET 10` 主线，形成更稳定、更容易演进的 Windows 桌面项目。

## 架构方向

### 桌面端

- `WPF + MVVM`
- 负责原生窗口、状态总览、桌面交互
- `WebView2` 只作为嵌入式预览位和后续扩展位

### 本地服务

- `ASP.NET Core`
- 负责本地状态接口、运行时控制、插件与更新中心入口
- 后续可部署为 `Windows Service`

### 分层类库

- `CPAD.Domain`：领域模型与常量
- `CPAD.Application`：应用抽象与接口
- `CPAD.Contracts`：前后端共享 DTO
- `CPAD.Infrastructure`：文件、进程、状态、路径与系统集成

## 当前仓库结构

```text
/
  apps/
    CPAD.Desktop/
    CPAD.Service/
  src/
    CPAD.Domain/
    CPAD.Application/
    CPAD.Contracts/
    CPAD.Infrastructure/
  resources/
    icons/
  docs/
  reference/
```

## 已完成内容

- `.NET 10 SDK` 固定到 `global.json`
- 根目录主线切换到 `.NET 结构`
- `CPAD.Service` 提供 `/api/system/status`
- `CPAD.Desktop` 切换为原生 WPF 状态总览界面
- `WebView2` 接入为嵌入预览位
- 旧主代码已从主线移除

## 保留的参考内容

当前只保留仍对迁移有价值的参考源码：

- `reference/upstream/official-backend`
- `reference/upstream/cpa-uv-overlay`
- `reference/plugins/edge-control`

这些目录不参与主线编译，只用于行为对照和能力迁移。

## 下一步

1. 把模式切换写入与读取逻辑收口到 `CPAD.Service`
2. 把 CPA runtime 的 build / start / stop 整体迁入 `.NET`
3. 把插件市场刷新、安装、启停、诊断迁入 `.NET`
4. 把更新中心的检查、同步和执行链路迁入 `.NET`
5. 视后续需求决定 WebView2 继续只做预览位，还是承接更完整的管理界面
