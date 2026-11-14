# 性能重构验证记录

2026-11-12；需求与当前计划见 [PRD](prd/performance-refactor.md)。基线为 da3da31 / 公开 v0.3.1。开发分支 codex/mvp-performance-refactor；当前为本地候选，无提交、推送、部署或安装替换。

## 扫描阶段结果（前序候选，输入未变化，复用）

在本机 Windows x64 / .NET Framework 环境，以真实 GIGABYTE 只读 `--scan` 入口测量。每次启动新进程，隐藏运行，交替执行基线/候选各 5 次，时间包含进程启动至 JSON 输出完成；不是系统重启后的冷启动，也不是 GUI 首屏或实体灯效验证。

| 组 | 基线 ms | 候选 ms |
| --- | ---: | ---: |
| 1 | 865 | 318 |
| 2 | 757 | 308 |
| 3 | 745 | 305 |
| 4 | 753 | 311 |
| 5 | 747 | 309 |
| 中位数 | 753 | 309 |

中位数减少 58.96%。10 次读取均退出 0、Errors 为空；逐组对照除 CapturedUtc 外全部顶层字段的序列化值一致，包括服务、驱动、任务、会话证据与指纹。未用持久缓存或上一轮结果替代读取。

原始 JSON 仅位于忽略目录 dist/baseline/ 与 dist/candidate/ 的 host-scan-paired-*.json，勿纳入提交。只含耗时的本地汇总为 dist/scan-timings.json。

| 体积 | 基线 | 候选 |
| --- | ---: | ---: |
| EXE | 103936 bytes | 104448 bytes |
| 同算法、仅 EXE 的 ZIP | 47217 bytes | 47918 bytes |

候选 EXE 增加 512 bytes（约 0.49%），单 EXE ZIP 增加 701 bytes；不能宣称代码行数或二进制缩小。增加的是系统直读与资源释放代码，无第三方依赖。原公开发行包包含更多文档，52793 bytes，不能和单 EXE ZIP 直接比较优化比例。

候选 EXE SHA-256：`8887239D6F5306D0AEFCC4EF1D4E91C55E06A9F8D3AD875C904E0E6AE771985A`。

本地交付包为 `dist/CorsairMemoryTakeover-refactor-preview.zip`，50373 bytes，包含 EXE、使用指南、许可证和 EXE 校验值。已重新打开压缩包，核对内含 EXE SHA-256 与候选一致。该包减少了开发说明文档，不能将其比原公开包小归因于二进制变小。

## 实现与根因证据

- DCS 与 iCUE 驱动从两条 WMI 查询改为系统 ServiceController 枚举和只读注册表。基线阶段测量分别约 173 ms、141 ms。独立服务模块保留精确名称/路径匹配和原状态字符串，及时释放系统对象。
- GCC 任务只读查询从 dynamic 首次绑定改为小型 COM InvokeMember 调用。独立同进程探针原约 171 ms、新约 14 ms，返回一致；有副作用的注册、启动、撤销逻辑未更换实现。
- 只对 GetTask 的文件不存在错误认定任务缺失。账户信息、连接、访问拒绝等故障进入 Errors，不再静默解释成未启用。
- ASUS/MSI 跳过随后被 NativeProbe 覆盖的技嘉指纹计算；最终厂商指纹、文件哈希、签名验证和恢复上限仍保留。
- 缺 DCS 时 DcsStart 明确为 -1，避免原默认 0 冒充 Boot 启动类型。
- UI 检测开始不再同步读取必定不可用的撤销状态；品牌变化不再重复刷新帮助文本；扫描完成遇到已销毁窗体不再渲染。

## 验证与复用

选择本地验证，未触发云 CI。

1. 系统编译器正式构建成功。首次集中 run-tests.ps1 的 108 项通过（原 86 + 当时 22 项新定点）。随后发现任务错误常量含义需修正，仅重跑最终 23 项 probe 定点，全部通过；原 86 项输入未变，复用其成功结果。因此最终覆盖为 86 + 23 项，未虚报为一次完整 109 项运行。
2. 新定点涵盖服务状态映射、非法状态、缺服务、驱动精确匹配、任务禁用/动作类型/路径、缺任务与访问/配置故障。数据为 FIXTURE，不代表 ASUS/MSI 实机验证。
3. 原 UI 离屏诊断：首次构造约 63 ms；MOCK 渲染稳态约 2–8 ms；不支持据此声称实际窗口绘制或高 DPI 缩放通过。
4. 修改后的离屏 smoke 通过：忙态禁用撤销、品牌帮助文本/选择同步、销毁后 SetBusy 不访问控件；Visible=false、IsHandleCreated=false、OpenForms=0。初始探针错误地期望无句柄 RichTextBox 发 TextChanged，未获得该事件；修正探针为核对实际文本与品牌选中状态后通过。没有通过修改生产行为迁就探针。
5. git diff --check 通过。最终真实扫描对照见上表。

测试记录与临时探针留在忽略目录 dist/，没有把机器报告、安装状态、用户路径加入公开材料。

## 方案 3 与连接链（用户补充后的当前候选）

用户已选第三张预览，并明确慢发生在连接内存/执行连接操作。前端现已按方案 3 实现：浅色顶栏、60/40 左右分栏、设备行、真实状态驱动的接入步骤，保留详情、帮助、导出及适用的维护/撤销入口。操作等待期间显示已用时间，不伪造百分比进度。未新增第三方依赖或运行时模型请求。

当前产物：`dist/option3-candidate/CorsairTakeover.exe`，104960 bytes（102.5 KiB），SHA-256 `7C21FDBB4EB6BF1B28DD1BEC7FAACCCA331F23046E0A43A5F3F7C6DB1E66F927`。本地压缩包 `dist/CorsairMemoryTakeover-option3-preview.zip` 为 50937 bytes，EXE/指南/许可证/校验值已核对；没有更新 GitHub 发布或替换运行中的旧版。与最初 103936-byte EXE 相比增加 1024 bytes，不能宣称二进制已缩小。

### 连接慢的证据及修复范围
- 历史 v0.3 技嘉连接链在重开 GCC 后无条件 `Sleep(20000)`，条件重试另 `Sleep(18000)`；正常操作有 3 次 Scan，重试有 4 次，UI 完成后另刷新。这是代码可证的固定等待，不是实机连接计时。停止/启动服务各 12 秒、进程退出 8 秒是超时上限，不能累加成每次必经耗时。
- 当前公开 v0.3.1 已移除技嘉自动重连，仅保留检测与指引。因此扫描优化和当前候选均不能宣称解决了旧版技嘉 20 秒等待。已向用户询问是否保留并重构旧连接功能，尚待答复；未擅自重新开放其服务/驱动操作。
- 当前 ASUS/MSI 恢复前校验由全服务 WMI 遍历改成已确认服务名的 ManagementObject 实例读取。一次只读对照：原遍历 306 个服务约 235.435 ms，新读取约 6.389 ms，Name/Command/StartMode/Account 一致，随机缺失服务返回 null。该结果只代表查询部分，不代表真实服务启动或灯效连接耗时。
- 保留 fresh Scan、服务身份/命令/账户/自动启动复核、路径保护、签名与哈希、执行前 SCM 状态、最多 15 秒的状态等待。状态达到 Running 即结束，未用固定 sleep 替代。

### 本批验证
- 系统编译器正式构建当前候选成功。
- 新增 10 项恢复查询/配置校验定点通过；前序未受影响的核心规则与采集检查复用，不重复全量回归或扫描 benchmark。
- `run-tests.ps1 -Suite ui` 的 15 项实际控件检查通过，包括导航、品牌帮助、缺项/失败不报成功、旧步骤清除、维护暂停与忙态重复操作保护。检测数据为合成；未执行系统变更。
- 1160 × 800 / 960 × 680 两种客户区的实际控件离屏渲染已检查。独立视觉复核无 P0/P1/P2，按钮字体已按 P3 建议提高。见 [视觉核对](../design-qa.md)。真实桌面点击、系统标题栏、高 DPI/多屏拖拽、服务启动和实体灯效/重启/唤醒仍未验证。
- RichTextBox 离屏渲染正文缺失属于微软记录的 DrawToBitmap 限制；帮助实际文本与品牌切换经控件断言验证，未用空白位图声称视觉通过。测试根控件放入不可见 parking window，不显示 Form、不操作真实界面。
- `git diff --check` 通过。所有机器读取报告、临时探针和渲染位图保留在忽略目录。

用户随后明确保留连接功能，并补充图标、隐藏任务栏与成功后静默退出；以上待选择状态已被替代，当前交付见下一节。

## 当前交付：手动连接、图标与退出设置

2026-11-12，`codex/mvp-performance-refactor` 本地候选。当前 EXE 为 `dist/connection-candidate/CorsairTakeover.exe`，137216 bytes（134 KiB），SHA-256 `57BE0AD85369572FF3D59B24444B50FB1817912E6D29BB805C6B449143800E58`。相比公开基线 103936 bytes 增加 33280 bytes；增加了手动恢复流程、图标和设置，不能宣称体积已缩小。未新增 NuGet、网页运行时、常驻进程、云服务或运行时模型请求。

交付压缩包 `dist/CorsairMemoryTakeover-connection-preview.zip` 为 72554 bytes（约 70.9 KiB），仅包含 EXE、指南、许可证和校验值。已打开归档核对条目及内含 EXE 的 SHA-256，没有装入机器检测报告。已从最终 EXE 提取图标并实际查看，与设计资源一致。

### 已实现及保护
- 保留原窄范围技嘉适配门限。操作前校验签名、受保护路径、组件指纹、精确服务/驱动配置与进程身份；健康连接直接返回，零重启。未连接时仅执行一次恢复，不写启动类型，不恢复技嘉常驻维护。
- 新 GCC 会话每 500 ms 检查一次，日志确认上限 38 秒，确认后立即返回；系统服务与进程操作耗时另计，不承诺整次操作 38 秒硬上限。旧版固定 20 秒及重试 18 秒 sleep 已移除。
- 连接成功要求同一新进程、全部内存模块、已注册连接、服务运行、无读数/通信/写入错误，末尾完整重扫并复核指纹。仅 GIGABYTE 合并最多 5 份日志尾部以处理轮转，其他品牌保留原诊断行为。
- 审查修正：旧会话错误不再阻止发起恢复；停止请求发出后即记录可能已改变的状态，等待超时也进入原运行状态恢复，回滚失败与原失败均保留。该证据为合成测试与代码审查，非真实故障注入。
- 多尺寸 ICO 编译进 EXE，用于主窗口、任务栏和托盘；头部使用同一图标。隐藏任务栏只在实际窗口显示时创建托盘，双击/菜单可打开，关闭时释放。设置原子保存到当前用户目录，损坏或保存失败明确反馈。
- 自动退出默认开启，只在手动技嘉连接操作成功且最新扫描再次通过连接证据校验后关闭本次 GUI，不弹成功消息。普通扫描、取消、失败和信息不完整均不触发退出。已有单独启用的维护进程不受 GUI 关闭影响。

### 本批实际验证与复用
1. `pwsh -NoProfile -File build.ps1 -OutputDirectory dist/connection-candidate` 成功，使用系统 .NET Framework 编译器和嵌入图标。
2. 后端 `--gigabyte-tests` 29 项合成检查通过，覆盖健康零副作用、新会话提前成功、超时、进程替换、旧日志、读数失败、组件变化和停止后等待失败。证据复用，主代理没有重复运行。
3. `run-tests.ps1 -Suite preferences` 25 项通过。操作唯一临时目录，覆盖默认、保存/恢复、损坏、严格类型、文件占用导致保存失败后旧内容保持及临时文件清理；未写实际用户配置。
4. `run-tests.ps1 -Suite ui` 27 项通过。实际控件树在不可见 parking window 核对导航、设置属性、错误反馈、忙态、严格自动退出条件、无实际托盘与无窗体显示。
5. `run-tests.ps1 -Suite rules` 86 项既有规则/日志/原生服务维护回归通过。选择器跳过已验证且未受影响的 probe/recovery 与本轮 29 项，避免重复。前序 23 项 probe、10 项服务查询证据继续复用。
6. 最新 EXE 隐藏执行一次真实只读 `--scan`：退出 0，墙钟 412 ms，Errors=0，当前会话 2 条内存，通信/已安装内存写入错误均为 0，Health=接管链路正常，当前组合匹配手动连接资格。报告仅保存在忽略目录，未装入交付包。此单次测量不作为新的性能分布，也不是连接恢复计时。
7. 两种实际控件尺寸 1160×800 / 960×680 的首页与设置页已离屏渲染并查看；没有关键操作消失。小尺寸首页允许滚动，设置页文字完整。截图为合成状态，详见 [视觉核对](../design-qa.md)。
8. `git diff --check` 通过；未运行云 CI，未提交、推送、发布或替换现有程序。

### 未验证与边界
未执行真实 `--repair/--enable/--restore/--watch`，未触发 UAC、启停真实服务/驱动或 GCC。实体连接恢复耗时、灯效、真实 Windows 任务栏/托盘点击、GUI 静默退出的实际桌面体验与高 DPI 仍未验收。代码与合成检查通过不能代替这些实机结果。已完成当前本地可运行候选交付；如继续实机验证，应针对该候选取得相应操作授权，不重复已经通过且未变的离线检查。

## v0.3.2 发布交付

用户于 2026-11-12 明确授权上传 GitHub。发行源码使用 `codex/mvp-performance-refactor` 分支及 `v0.3.2` 标签；保留既有 `v0.3.1` 发布，默认分支与网站不在本轮合并/部署范围。远端公开状态及附件见 [v0.3.2 预发行](https://github.com/BoB-The-Heater/corsair-memory-takeover/releases/tag/v0.3.2)。

发布前将程序集及安装文件名称的版本标识更新为 0.3.2，产品版本为 `0.3.2-preview`。仅版本元数据/文档改变，行为代码沿用上述候选，复用已通过的本地行为验证。重新构建和归档检查通过：

- EXE：137216 bytes；SHA-256 `59E2B5C083C8E5CF75BF26F0C56E78D54E3437D92F4D8D87CF8CED4A1356BEE4`。
- ZIP：`CorsairMemoryTakeover-v0.3.2-win-x64.zip`，72580 bytes；SHA-256 `2BD59A4474B5009D0CEA4C3C30A3E125A2B979B2F7BB45E9B8425715D3018548`。
- ZIP 中仅 EXE、GUIDE、LICENSE、SHA256SUMS 四项；压缩包内 EXE 与当前构建逐字节哈希一致。另提供外置校验文件，含 ZIP 和 EXE 两个哈希。
- 发布提交采用 GitHub 官方支持的 `[skip ci]` 标记以复用本地证据，未修改 workflow、仓库安全设置或所需检查。没有把跳过云端检查记为通过。
- 发布审查未发现凭据、用户标识或真实机器报告进入源码清单；PRD 本机绝对路径已移除。临时发布脚本、原始检测与验证产物保持忽略。

## v0.3.3 登录自动连接修复

2026-11-14，用户报告不会开机自启动，并选择登录后自动连接、确认成功后静默退出。根因是 v0.3.2 仅恢复手动连接，没有技嘉登录入口。新增独立的当前用户登录任务，延迟 25 秒、每次登录执行一次，完成后退出，不增加 watcher、唤醒行为、任务重试或运行时模型请求。

设置页提供开启、关闭、实际登记状态和上次结果。登记将当前构建复制到受保护目录，以程序哈希、组件指纹和当前 SID 绑定任务；同名外来任务拒绝覆盖。组件变化、读取错误或日志失败不报告成功。已确认归属的残留任务、安装文件缺失或结果损坏仍保留关闭入口，干净关闭后可重新开启。跨账户提权在写入前拒绝。

本批选择本地验证，未运行云 CI：

- `run-tests.ps1 -Suite startup` 最终 69 项通过。涵盖健康零恢复、至多一次连接、基线漂移、任务/程序身份、配置读回、启用回滚、关闭失败、重新开启、损坏状态清理及用户 SID。测试为合成数据，由后端代理定点执行，主代理复用结果。
- `run-tests.ps1 -Suite ui` 的 35 项通过，包括新增开启/关闭/未知/残留任务/忙态分支及既有设置。首次 UI fixture 使用了不匹配的驱动名称，按真实识别契约修正 fixture 后通过；未放宽生产门限。
- 使用真实 Windows Task Scheduler COM 创建内存中的未注册任务定义，调用生产配置/描述/校验方法成功；没有 Register、Run、Delete。真实 `ReadStatus` 返回未开启。首次只读检查复现缺任务返回 `FileNotFoundException`，修复仅 `GetTask` 的 `0x80070002` 映射后通过；其他读取错误不吞掉。
- 最终 EXE 的开启/关闭入口在当前非管理员令牌下分别退出 2；不同用户 SID 退出 4，未显示窗口或执行系统写入。此前成功的连接、偏好、规则、采集测试输入未变，直接复用，不重复整套扫描或性能对照。
- 两种实际控件客户区 1160×800、960×680 的设置页离屏预览已检查，小窗口底部通过控件滚动验证可达；未调用 Form.Show、Application.Run、真实鼠标键盘或创建桌面托盘。预览只证明实际控件布局，不代表桌面交互与 DPI 验收。
- 系统 .NET Framework 正式构建成功，`git diff --check` 通过。EXE 172032 bytes（168 KiB），产品版本 0.3.3-preview，SHA-256 `78DAE8F434D913E8E1F7E82FFA186A788EC893B10C760D3B82400F4858F146EC`。比 v0.3.2 增加 34816 bytes；未实现二进制缩小，无新增第三方运行时。
- 交付 ZIP 为 `CorsairMemoryTakeover-v0.3.3-win-x64.zip`，85080 bytes，SHA-256 `179DDE2087793B682F4455361C422370BFA2F18B0D88CFB5D6BB56C0AAE1CD87`。归档只含 EXE、GUIDE、LICENSE 与 EXE 校验文件；归档内程序与最终构建哈希一致。外置校验文件同时包含 ZIP 和 EXE 两行。临时诊断、原生读数、SID 和渲染预览均不纳入。

本机登记状态与实际重启登录需另列证据；上述只读、合成与离屏检查不证明已启用或硬件恢复成功。当前会话非管理员，未自行触发 UAC 或真实服务/驱动启停。真实跨账户 UAC、登录触发、硬件恢复耗时及实体灯效未验证。

接口依据：[TaskFolder.RegisterTaskDefinition](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskfolder-registertaskdefinition)、[Task Scheduler 安全上下文](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks)、[UAC 身份与凭据提示](https://learn.microsoft.com/windows/security/identity-protection/user-account-control/how-user-account-control-works)。

## 一手接口依据（链接）

- [ServiceController.GetDevices](https://learn.microsoft.com/en-us/dotnet/api/system.serviceprocess.servicecontroller.getdevices?view=netframework-4.8)
- [TaskService.Connect](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskservice-connect)
- [ActionCollection.Item](https://learn.microsoft.com/en-us/windows/win32/taskschd/actioncollection-item)
- [Task Scheduler 错误码](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-error-and-success-constants)
- [已核实的 v0.3.1 预发行](https://github.com/BoB-The-Heater/corsair-memory-takeover/releases/tag/v0.3.1)
