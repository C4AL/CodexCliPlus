# Cli Proxy API Desktop

## 当前源码结构

- `src/`：Electron 主进程、预加载、桌面前端内核、共享类型
- `service/`：Windows 本地服务、运行时管理、状态持久化
- `sources/official-backend/`：官方完整后端基线源码
- `sources/official-management-center/`：官方管理中心基线源码
- `sources/cpa-uv-overlay/`：CPA-UV 覆盖层源码
- `scripts/`：同步、构建、打包、调试、烟测脚本

## 构建入口

- 安装依赖：`npm install`
- 同步受控源码：`npm run sources:sync`
- 构建桌面端：`npm run build`
- 构建服务：`npm run build:service`
- 构建 `codex.exe` shim：`npm run build:shim`
- 产出 Windows 分发物：`npm run dist:win`
- 执行打包态烟测：`npm run debug:smoke`

## 真实阻塞项

- 桌面首页仍是重构中的壳层，CPAD 自己的前端内核还没有收口。
- 官方管理中心能力还没有按桌面边界拆成可直接复用的模块。
- 官方完整后端虽然已同步进仓库，但运行链路还没有完全切到仓库内源码。
- 安装、升级、卸载、服务安装失败恢复还没有做到可验收。
- `codex` 目前只做本地检测与自测，还没有直接接入开发版 CPAD 后端。

## 现有发布产物

- `release/win-unpacked/`
- `release/Cli Proxy API Desktop-Setup-0.1.0-x64.exe`
- `release/Cli Proxy API Desktop-Portable-0.1.0-x64.exe`
- `release/latest.yml`

## 保留文档

- [已确认事项](docs/已确认事项.md)
- [CPA-UV 覆盖官方基线差异矩阵](docs/CPA-UV覆盖官方基线差异矩阵.md)
- [待整合资产清单](docs/待整合资产清单.md)
- [开源继承声明](docs/开源继承声明.md)
- [仓库协作约束](AGENTS.md)
- [许可证](LICENSE)
