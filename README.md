<p align="center">
  <img src="https://raw.githubusercontent.com/Blackblock-inc/Cli-Proxy-API-Desktop/main/resources/icons/ico-transparent.png" alt="CPAD" width="160" />
</p>

<h1 align="center">CPAD</h1>

<p align="center">
  Cli Proxy API Desktop
  <br />
  基于 C# / .NET 10 的 Windows 原生桌面壳与本地服务
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" />
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases">
    <img alt="Release" src="https://img.shields.io/github/v/release/Blackblock-inc/Cli-Proxy-API-Desktop?style=flat-square" />
  </a>
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/blob/main/LICENSE">
    <img alt="License" src="https://img.shields.io/github/license/Blackblock-inc/Cli-Proxy-API-Desktop?style=flat-square" />
  </a>
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/stargazers">
    <img alt="Stars" src="https://img.shields.io/github/stars/Blackblock-inc/Cli-Proxy-API-Desktop?style=flat-square" />
  </a>
</p>

<p align="center">
  <a href="./docs/CPAD-.NET10-%E7%BB%9F%E4%B8%80%E9%87%8D%E6%9E%84%E6%96%B9%E6%A1%88.md">重构方案</a>
  ·
  <a href="./docs/%E5%BC%80%E6%BA%90%E7%BB%A7%E6%89%BF%E5%A3%B0%E6%98%8E.md">开源继承声明</a>
  ·
  <a href="https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/issues">Issue</a>
  ·
  <a href="https://qm.qq.com/q/Q48OP64HwQ">QQ群</a>
</p>

CPAD 是一个面向 Windows 的统一桌面项目，目标是用单一 `C# / .NET 10` 重构原有混合技术栈，形成可维护的桌面端与本地服务主线。

## 运行

```powershell
git clone https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop.git
cd Cli-Proxy-API-Desktop
dotnet build CPAD.sln
dotnet run --project apps/CPAD.Service
dotnet run --project apps/CPAD.Desktop
```


## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=Blackblock-inc/Cli-Proxy-API-Desktop&type=Date)](https://www.star-history.com/#Blackblock-inc/Cli-Proxy-API-Desktop&Date)
