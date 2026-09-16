# Corsair 内存接管助手

检测海盗船 RGB 内存与主板软件的接入状态，并维护已确认的官方服务。灯效在主板厂商软件中设置。

[下载](https://github.com/BoB-The-Heater/corsair-memory-takeover/releases/tag/v0.3.1) · [网站](https://bob-the-heater.github.io/corsair-memory-takeover/) · [English](README.en.md) · [使用指南](GUIDE.md)

## 支持范围

| 主板软件 | 功能 |
| --- | --- |
| ASUS Aura Sync | 组件检测、已确认服务基线的维护（预览） |
| MSI Mystic Light | 组件检测、已确认服务基线的维护（预览） |
| GIGABYTE RGB Fusion | 检测与官方接入指引 |
| 其他品牌 | 检测与接入指引 |

适配仍为预览，不能保证所有硬件兼容。服务运行不代表实际同步，需在官方软件中确认内存可控。

## 使用

1. 下载 Windows x64 发行包，解压并运行 `CorsairTakeover.exe`。
2. 查看检测结果，按品牌指引完成官方组件安装和控制授权。
3. 华硕、微星用户确认实体灯效跟随后，可启用服务维护。取消维护使用「撤销」。

需要 .NET Framework 4.8，界面为简体中文。首次检测无需管理员权限；系统变更操作会请求管理员权限。程序未签名，发行页提供 SHA-256 校验。

## 开发

```powershell
.\build.ps1
.\run-tests.ps1
```

使用 Windows 自带的 .NET Framework 编译器，无需 NuGet。测试使用模拟服务、模拟注册表和虚构日志。

网站文案位于 `scripts/build-site.py`，使用 Python 3 运行后生成 `docs/` 中的静态页面。

[贡献说明](CONTRIBUTING.md) · [更新记录](CHANGELOG.md) · [MIT License](LICENSE)

程序没有报告上传功能。独立项目，与各硬件厂商无隶属关系。
