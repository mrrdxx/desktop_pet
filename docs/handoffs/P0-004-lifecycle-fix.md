# P0-004 四项修复交接

日期：2026-10-22（Asia/Shanghai）。用户明确要求监理助手接手实现；开发 AI 已停手并交接。
本文件是**修复作者自测与交接**，不是独立监理通过。原 [审查报告](../reviews/P0-004-review.md) 未改写，F-01～F-04 不由本文件自动关闭。问题处置入口见 [整改记录](../remediation/P0-004-response.md)。

## 改动与当前边界

- ArchiveService：未知高版本先建立文件级只读保护，兼容字段可尽力读取，不兼容则使用默认运行数据并保留未来版本原件，不再归类为损坏。保存时同时检查保护标记、DTO 版本和磁盘版本；替换成 v1 DTO 或新建服务也不能自动降级。state/settings 各自保护；原有显式 `allowOverwriteUnknownVersion=true` API 保留，但应用自动保存不使用它。
- App：显示前应用暂停状态，SourceInitialized 时复用既有位置适配器验证旧位置；不存在的屏幕/越界位置回到可见工作区。退出先捕获最终状态并重新标脏后 Flush，增加关闭重入保护。Recover、布局恢复也会标脏。
- MonitorInterop：使用 MonitorFromWindow/GetMonitorInfo 记录实际窗口所在显示器名称；[API 依据](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow)。没有改写既有坐标框架。
- TrayService：高版本只读时配置托盘提示，说明对应更改不会保存。测试验证提示内容已配置，不保证系统通知一定展示。
- 新增 8 个高版本保护单元用例、永久的 [隔离集成入口](../../tests/Integration/README.md)。未更改 v1 字段结构、未添加依赖、未提交/推送或开启开机自启。

## 真实验证结果

- [生产构建](evidence/P0-004-helper-build.txt)：`dotnet build DesktopPet.slnx -c Debug --no-restore -m:1`，退出码 0，0 警告/错误。
- [正式 VSTest](evidence/P0-004-helper-vstest.txt)：`dotnet test DesktopPet.slnx -c Debug --no-build --no-restore -m:1`，最终经审批在沙箱外成功执行，退出码 0；Core 22/22、App 33/33，合计 **55/55 通过**（新增 8），0 失败/跳过。
- 诊断历史：[沙箱内直接执行输出](evidence/P0-004-helper-unit-direct.txt) 调用原测试方法、TestInitialize/TestCleanup 和 DataRow，也是 55 通过、0 失败；[替代执行器源码](evidence/P0-004-helper-UnitCheck.cs.txt) 保留。它不代替上述正式结果。
- 标准 `dotnet test` 此前在沙箱内两次 testhost 连接超时；即使临时目录移到 workspace 仍未成功，见 [沙箱输出](evidence/P0-004-helper-vstest-sandbox.txt)。沙箱外此前数次自动审批连接中断/超时，命令未执行，不是安全性否决；最终重试成功，已无待补的正式测试。
- [十场景隔离集成](evidence/P0-004-helper-lifecycle.txt)：`powershell -ExecutionPolicy Bypass -File tests/Integration/Run-Lifecycle.ps1`，退出码 0，ALL ISOLATED LIFECYCLE SCENARIOS PASS。目录 `artifacts/p0-004-fix/run-ceb36b1b/`。
- 暂停后重启：paused=true、timerEnabled=false，等待后帧不变化；手动 Pause/Resume 及暂停下 Recover 仍正常。
- 屏外旧位置：原 `100000,100000` + MISSING_DISPLAY 已恢复为实际矩形 `(2380,1348)-(2560,1528)`，完整位于当前工作区。
- 最终保存：等待 5.7 秒完成前一次防抖后，Recover 再退出，新的 `(793.333...,389.333...)` 正确保存；防抖前直接退出、真实文件重启恢复 `(350,260)` 也通过。
- 高版本 state/settings 字段变为对象后，启动、状态改变、改 DTO 版本和正常退出均保持原文，未生成 `.corrupted`；正常损坏文件与备份回退仍通过。
- 单实例：第二份实例提示确认后退出码 1，第一份继续运行，最后通过托盘事件退出码 0。所有测试实例正常退出，真实 LocalApplicationData/DesktopPet 文件摘要前后未变。

集成使用当前源码生成隔离副本，仅替换存档路径、锁名称并增加探针。菜单通过真实 ToolStripMenuItem.PerformClick 触发；不冒充 Explorer 通知区图标的实际点击或完整视觉验收。当前单屏 150%，本轮未重跑 100%、混合 DPI 多屏、真实锁屏/热插拔；不扩大既往验证范围。

替代单元执行的初次尝试在系统临时目录有 10 项 File.Move 权限失败，迁至 workspace 临时目录后 55/55 直接执行通过；这属于沙箱 IO 限制，不按产品回归判定。辅助执行器最初的项目路径/还原问题已修正，最终直接引用已构建测试 DLL，无新增包或修改全局 NuGet 配置。失败尝试保留为历史，正式通过仅依据最终成功执行。

## 版本

无 Git，当前 SHA256：

- App.xaml.cs：`0AE58BB7D682CB484BD08986792B942909786EB3FD26C3F7EE71FEBB373EA440`
- ArchiveService.cs：`2E430543745E0259B73BCE19E0A823EFF7FECF6AB2D41D4BB73456773DD2397B`
- MonitorInterop.cs：`76D78991C93B1A603A301B21810EBD70AE358B8CCB7DBA73591D957400C93EAF`
- TrayService.cs：`D09DDA60224C9FFBCBA559A93B9E868BC3E23B512D7B12D91C34C43E7264F35C`
- MainWindow.xaml.cs：`7EEC77CAC03D3621A98EB7C68D774153A71A60C1BFFF36823518512920D4E8BC`
- ArchiveVersionProtectionTests.cs：`5D6AA46145B024BA9AFBF5F9FC0444DF17FE36BA4492AFDD2D43D8CEE6A0B385`
- Run-Lifecycle.ps1：`0B5B2525EC6785689A2F92747EC64513D10A833DEC8315B0C74C8A140B006BE9`
- LifecycleProbe.cs：`C9684CB25819A6A716FADCCE15C49A66A3B5648F93A4100319F1F5F580511146`

接手前代码在 `artifacts/p0-004-fix/before/`，只用于对比，不覆盖当前实现；原 [暂停状态快照](../history/P0-004-before-helper-fix.md) 保留。

## 开发 AI 接回

1. 核对源码与本文，不回退修复，不修改原审查报告。
2. 核对正式单元测试和十场景隔离集成证据；代码未改时无需因接手重跑全部作者自测。后续修改按影响范围验证并追加真实结果；正式测试前应先构建当前代码。
3. 如需重跑启动链，使用 tests/Integration 的当前源码入口，不继续用旧 artifacts/p0-004-validation 过期副本。`-BuildOnly` 只构建、不启动桌面；详见该目录 README。
4. 请求针对 F-01～F-04 的复核；作者自测不能代签独立监理关闭，TASKS 保持 awaiting-verification，不进入 P0-005。Shell 图标实际点击和其余必要体验仍按既定范围补齐。
