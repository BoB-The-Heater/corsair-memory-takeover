# Corsair 内存接管助手

检测海盗船 RGB 内存与主板软件的接入状态，并维护已确认的官方服务。灯效在主板厂商软件中设置。

[下载 v0.3.3 预览版](https://github.com/BoB-The-Heater/corsair-memory-takeover/releases/tag/v0.3.3) · [网站](https://bob-the-heater.github.io/corsair-memory-takeover/) · [English](README.en.md) · [使用指南](GUIDE.md)

## 支持范围

| 主板软件 | 功能 |
| --- | --- |
| ASUS Aura Sync | 组件检测、已确认服务基线的维护（预览） |
| MSI Mystic Light | 组件检测、已确认服务基线的维护（预览） |
| GIGABYTE RGB Fusion | 检测、已适配组合的手动连接和登录自动连接（预览） |
| 其他品牌 | 检测与接入指引 |

适配仍为预览，不能保证所有硬件兼容。服务运行不代表实际同步，需在官方软件中确认内存可控。

## 使用

1. 下载 Windows x64 发行包，解压并运行 `CorsairTakeover.exe`。
2. 查看检测结果，按品牌指引完成官方组件安装和控制授权。
3. 华硕、微星用户确认实体灯效跟随后，可启用服务维护。取消维护使用「撤销」。
4. 技嘉「连接内存」先重新检测；已经连通时直接结束，需要恢复时按当前会话日志确认结果，不再固定等待 20 / 18 秒。仍限已适配的硬件与软件组合。
5. 「设置」可隐藏任务栏入口（保留托盘打开／退出）或关闭默认开启的「连接成功后静默退出」。静默退出仅作用于主动技嘉连接成功之后，普通检测与失败不会关闭窗口。
6. 技嘉开机连接需在「设置」点击「开启自动连接」并完成一次管理员授权。下次登录后延迟 25 秒检查，已连接则直接退出，否则只连接一次；完成后结束进程，上次结果在设置页查看。停止此功能使用「关闭自动连接」。

需要 .NET Framework 4.8，界面为简体中文。首次检测无需管理员权限；系统变更操作会请求管理员权限。程序未签名，发行页提供 SHA-256 校验。

## 开发

```powershell
.\build.ps1
.\run-tests.ps1
.\run-tests.ps1 -Suite ui
.\run-tests.ps1 -Suite preferences
.\run-tests.ps1 -Suite startup
```

使用 Windows 自带的 .NET Framework 编译器，无需 NuGet。测试使用模拟服务、模拟注册表和虚构日志。

本次重构的范围与前端选择状态见 [PRD](docs/prd/performance-refactor.md)，测量与验证限制见 [验证记录](docs/refactor-validation.md)。实机恢复和桌面交互仍未验收，按预览版提供。

方案 3 的原生界面、应用图标和设置页已实现。GUI 空闲不循环检测；不新增运行时依赖，退出释放本次窗口与托盘。UI 检查不显示窗口、不执行服务操作。[视觉核对](design-qa.md)说明离屏渲染与实际桌面验证的边界。源码在 `assets/app-icon.svg`，多尺寸 `assets/app.ico` 编译进 EXE；重建图标使用 `pwsh -File scripts/build-icon.ps1`。

网站文案位于 `scripts/build-site.py`，使用 Python 3 运行后生成 `docs/` 中的静态页面。

[贡献说明](CONTRIBUTING.md) · [更新记录](CHANGELOG.md) · [MIT License](LICENSE)

程序没有报告上传功能。独立项目，与各硬件厂商无隶属关系。
