# 使用指南

## 开始

需要 Windows x64 和 .NET Framework 4.8。解压发行包后运行 `CorsairTakeover.exe`，查看「接管概览」「检测详情」「接入与帮助」。

`F5` 重新检测，`Alt + 1 / 2 / 3` 切换页面。

## 官方接入

- **华硕**：安装 Armoury Crate 和 Corsair RGB Memory Plugin for ASUS Aura Sync。若使用 iCUE，通过官方界面开放第三方控制；重新扫描后确认内存可见、灯效跟随。[官方步骤](https://help.corsair.com/hc/en-us/articles/31086864483729-Memory-Enable-RGB-control-through-third-party-software-Armoury-Crate)
- **微星**：安装 iCUE、MSI Center 和 Mystic Light，按提示完成更新与重启，再确认内存可控。Dragon Center、MSI Center S 不在当前适配范围。[官方步骤](https://help.corsair.com/hc/en-us/articles/14649496854029-RAM-How-To-Enable-RGB-Control-with-MSI-Mystic-Light-for-DRAM)
- **技嘉及其他品牌**：本工具提供检测和接入指引，请在厂商软件中完成识别与同步设置。

## 服务维护（华硕 / 微星预览）

官方软件已经能控制实体灯效、检测通过且目标服务全部运行后，才可启用维护。程序保存用户确认的服务基线，在登录及唤醒时检查。

仅启动基线中已停止的自动服务；运行中的服务和 iCUE 设置保持不变。组件或服务配置变化后暂停维护。每次事件最多恢复一次，间隔至少 10 分钟，每个 UTC 日期最多 3 次。

插件缺失、未授权或服务运行但内存不可见时，需按官方步骤排查。服务维护不能代替实体灯效及重启测试。

## 撤销与升级

「撤销」移除工具自己的登录任务，并恢复工具之前修改且未被外部再次改动的设置。只关闭窗口不会取消维护。

旧版仍在维护时，先撤销，再使用新版启用。版本化程序和记录保存在 `%ProgramData%\CorsairTakeoverAssistant`。

## 反馈

检测报告保存在本机，没有自动上传功能。提交问题时仅提供必要的脱敏片段，删除个人路径、序列号、SID 和设备标识。
