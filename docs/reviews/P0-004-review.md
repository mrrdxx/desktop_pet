# P0-004 生命周期与存档验证

日期：2026-10-22（Asia/Shanghai）。角色：用户授权的监理/验证者。
结论：**验证未通过。F-01～F-04 阻断 P0-004 完成及 P0-005 开始**；已通过部分保持有效，不要求全部重做。
开发方整改入口：`docs/remediation/P0-004-response.md`（由开发方建立维护），流程按 REVIEW_PROCESS。

## 实际执行、隔离方式与边界

- 生产项目 `dotnet build DesktopPet.slnx -c Debug --no-restore -m:1`：退出码 0，0 警告/错误。
- 生产项目 `dotnet test DesktopPet.slnx -c Debug --no-build --no-restore -m:1`：退出码 0，Core 22 + App 25，共 47/47 通过；[原始测试输出](evidence/P0-004-tests.txt)。其中 ArchiveServiceTests 实际为 9 项，不是原 STATUS 的 10 项。
- 当前 App 硬编码 `new ArchiveService()`，没有测试目录参数，直接启动会接触真实 LocalApplicationData。因此建立 `artifacts/p0-004-validation/isolated-app/` 源码副本；生产源文件未修改。
- 副本差异仅为：项目引用路径适配；ArchiveService 构造参数改读测试目录环境变量；互斥锁名称改用专用 Local 名称；Show 后增加验证探针调用及探针代码。原生命周期、保存、恢复和托盘事件处理逻辑保持原样。隔离副本同样构建通过，不把它称为未改动的生产二进制。
- Windows 10.0.26200.0、单屏当前 150%。每个场景独立目录；默认 5 秒防抖不加速。测试前后实际 LocalApplicationData/DesktopPet 文件摘要未变化。
- 通过真实 ToolStripMenuItem.PerformClick 调用托盘菜单事件处理，观察定时器、帧状态、原生窗口矩形、存档、进程退出；**没有实际点击 Explorer 通知区域图标/展开隐藏图标**，不能声称完成 Shell 托盘视觉/点击验收。
- 单实例采用隔离命名互斥锁和两份真实进程，观察提示框并通过实际按钮 BM_CLICK 确认，第二份退出码 1，第一份继续运行、最后退出码 0；不宣称测试了跨 Windows 会话。
- [主要隔离集成结果](evidence/P0-004-isolated-integration.txt)、[单实例最终复测](evidence/P0-004-single-instance.txt)、[使用前次真实保存文件的重启验证](evidence/P0-004-restart.txt)。首次单实例测试曾对提示框关闭超时，后续发现按钮 ID 假设不适用，改用实际按钮后通过，不据此前期失败判定应用缺陷。
- 所有测试实例均正常退出；早期提示框通过 WM_CLOSE 清理，没有强杀冒充托盘退出。集成驱动的退出码 0 只表示执行结束，日志内明确包含 FAIL；不将其解释成所有场景通过。
- 探针与驱动版本留存：[ValidationProbe 文本](evidence/P0-004-ValidationProbe.cs.txt)、[驱动脚本副本](evidence/P0-004-Run-Validation.ps1)。复跑使用 artifacts 下完整测试副本；复制到 evidence 的脚本是留存，不是可脱离其相对目录直接执行的入口。原主要场景目录为 `artifacts/p0-004-validation/run-e6346a9a/`；单实例最终为 `run-14ac0f97/`；重启为 `run-01530d69/`。迁移仓库若需重跑，应带上这些被忽略材料或重新建立夹具。

## 审查版本

无 Git，以源文件 SHA256 标识：

- App.xaml.cs：`2AE2EBD3618BBF5D5192202E813114D149B5A80CFB5A76E7C1AAA28F46468516`
- ArchiveService.cs：`AC27B4239C61973D33CA6646EA0AEA030D7252A74CA860343F1121F4107BC2FE`
- SaveCoordinator.cs：`2A64BDC2324E8BE21426759139E0C989993A2899B7FB90589401DFB91FCF2E6E`
- SingleInstanceGuard.cs：`FDF34A9B95ED4AD80B43DE212C7EC7595A658FF716BA7E5DBC4B96063FB374E2`
- TrayService.cs：`C98288034FBAA86F4B28B8C7B5F5CF1B0811E0444125323F1E62F4FD058F13F8`
- MainWindow.xaml.cs：`45AB93EAB9D2EAC40B03B629600CE232D0E976B8B01B6D941C9BBEFFB87849F1`

## 已通过范围

- 暂停菜单操作会停止动画定时器，等待一秒帧状态保持；暂停时 Recover 仍可使用；Resume 后帧状态继续变化，没有补演暂停时的积压。
- 已标脏的位置在五秒防抖到期前尚未落盘；托盘 Exit 立即 Flush，最终 `(350,260)` 保存成功、进程自然退出。
- 使用该次实际输出的 state.json 再启动，窗口恢复 `(350,260)`；设置文件保存的 animationSpeed=1.7 可被启动链读取。这里仅验证数据读取，不宣称动画速度设置已驱动实际动画。
- 常规 state/settings 服务往返、截断主文件保留原字节并回退有效备份、无备份时回退默认值、字段与 v1 兼容的高版本 JSON 不覆盖，均通过。
- 单实例在上述隔离范围通过。原生存档测试及正常退出路径并非全部失败，剩余整改仅针对下述四项。

## F-01：不兼容字段的高版本存档会被误判损坏并降级（P1，阻断）

- 位置：`src/DesktopPet.App/Persistence/ArchiveService.cs`，DeserializeFile 约 163–177 行、TryLoad 损坏分支、SaveState/SaveSettings。
- 复现输入：`{"schemaVersion":99,"windowLeft":{"newFormat":1},"futurePayload":42}`；设置文件对应 `{"schemaVersion":99,"animationSpeed":{"newFormat":2},"futurePayload":77}`。
- 实际：虽然先读出版本 99，代码仍按 v1 DTO 反序列化；JsonException 将其当损坏，移到 `.corrupted`，返回 schema=1 默认对象和 unknown=false。随后保存生成新的 v1 主文件，futurePayload 从当前主文件消失。原始未来版本尚留在 `.corrupted`，但已违反只读保护契约，不能当作兼容性恢复成功。
- 证据：主要集成日志 `higher version ... incompatibleFields=True` 及 unknown-settings 两项失败；主要场景目录内同时保留原 `.corrupted` 和新 v1 JSON。
- 整改目标：先确认模式版本，未知高版本进入独立只读保护，不依赖 v1 类型反序列化成功，也不能在降回默认对象后丢失文件级保护。state/settings 和后续保存/退出路径均适用。
- 验收：上述新格式反例在加载、尝试保存及正常退出后，原主文件字节不变、不被重分类为损坏；应用有明确的恢复/只读提示依据；正常 v1 损坏备份恢复保持有效。使用隔离目录，不动真实用户存档。

## F-02：防抖保存完成后再改变位置，退出不保证最后位置落盘（P1，阻断）

- 位置：`src/DesktopPet.App/App.xaml.cs` OnRecoverRequested、ShutdownMainWindow 约 110–122 行；`SaveCoordinator.Flush` 仅保存 dirty=true 状态。
- 复现：移动到 `(300,240)` 并 MarkStateDirty，等待 5.7 秒完成防抖；通过真实托盘 Recover 菜单项改变位置，再立即通过托盘 Exit。
- 实际：退出前窗口为 `(793.333...,389.333...)`，文件仍保存 `(300,240)`。ShutdownMainWindow 虽更新 `_state` 属性，却没有重新标脏；Dispose→Flush 看到 dirty=false 不写盘。已有 Flush 单测只覆盖预先标脏的情形，未覆盖此应用链路。
- 整改目标：正常退出必须将最终状态纳入保存协调器，Recover/布局恢复等位置变更的持久化责任也应明确，不能依赖此前恰好仍有 pending dirty。
- 验收：防抖已完成→位置再次改变→托盘退出→重启，恢复最后位置；仍有 pending 的立即退出保存、防抖和未知高版本保护均不能回归。

## F-03：重启仅恢复暂停标记，没有恢复暂停行为（P2，阻断）

- 位置：`src/DesktopPet.App/App.xaml.cs` OnStartup 约 57–72 行；MainWindow 构造时直接启动 `_frameTimer`。
- 复现：seed state.json 的 isPaused=true 后启动隔离 App。
- 实际：`_isPaused=True`、托盘菜单为 Resume，但 `timerEnabled=True`；等待一秒后帧状态已变化。通过菜单手动 Pause 的路径则正常。
- 整改目标：启动时在合适时点应用存档的暂停行为，使状态、菜单、定时器一致；保持暂停下菜单和找回可用。
- 验收：暂停→退出→重启，动画/主动更新保持停止；Resume 后正常继续，位置不跃迁且无积压补演。增加实际启动链回归，不只断言 JSON 中布尔值。

## F-04：启动直接赋值旧坐标，没有执行可见位置恢复（P1，阻断）

- 位置：`src/DesktopPet.App/App.xaml.cs` OnStartup 约 60–63 行及保存位置逻辑。
- 复现：state.json 保存 WindowLeft/Top=100000，SavedDeviceName=MISSING_DISPLAY，再启动。
- 实际：WPF/Win32 最终窗口矩形为 `(32767,32767)-(32947,32947)`，完全在当前工作区之外；App 未调用已有显示器恢复适配器，手动 Recover 虽可用但不代替自动恢复。
- 整改目标：加载存档后的坐标进入既有工作区/显示器恢复流程，再显示或确认可见；保存显示器信息和实际恢复契约一致。不必引入新坐标框架。
- 验收：有效位置仍原位恢复；旧屏不存在、旧位置越界等注入布局反例均回到可见工作区。真实混合 DPI 热插拔仍可按原计划记未验证，不要求额外设备。

## 接续与限制

开发方维护独立整改记录，补充四项对应的应用/服务回归和实际命令、版本、自测结果；完成后提交复核，不修改本报告。
TASKS 退回 in-progress，P0-005 暂不开始。本轮没有接管生产实现，也没有重开 P0-003 已关闭的问题。Shell 托盘真实点击、真实休眠/锁屏等未执行部分如实保留；不把内存菜单调用写成全套人工桌面验证通过。

---

## 复核 R1（2026-10-22，独立审查者）

角色：独立审查者（用户指定担任本轮复核）。对照真实代码、版本和证据逐项复核 F-01～F-04。
修复由用户授权的监理助手实现，修复作者自测见 [修复交接](../handoffs/P0-004-lifecycle-fix.md) 和 [整改记录](../remediation/P0-004-response.md)。本复核不代替用户实机体验。

### 复核方法与版本

- 无 Git。当前源文件 SHA256 与修复交接记录完全一致（6 个文件逐个比对）：
  App.xaml.cs `0AE58BB7…`、ArchiveService.cs `2E430543…`、MonitorInterop.cs `76D78991…`、
  TrayService.cs `D09DDA60…`、MainWindow.xaml.cs `7EEC77CA…`、ArchiveVersionProtectionTests.cs `5D6AA461…`。
- 独立构建：`dotnet build DesktopPet.slnx -c Debug --no-restore -m:1`，退出码 0，0 警告/错误。
- 独立测试：`dotnet test DesktopPet.slnx -c Debug --no-build --no-restore -m:1`，退出码 0，Core 22/22 + App 33/33 = 55/55 通过。
- 逐项阅读修改代码，不凭关键词存在或测试总数判定通过。
- 隔离集成证据（`handoffs/evidence/P0-004-helper-lifecycle.txt`）为修复作者自测结果；本审查者核对其内容与代码逻辑一致，未重跑隔离集成（当前单屏 150%，无第二屏）。
- ARCHITECTURE §7 第 132 行已补充高版本只读保护描述，与代码一致。

### F-01：不兼容字段的高版本存档会被误判损坏并降级 — **通过**

- `ArchiveService.DeserializeFile`（第 163–206 行）：先读 `schemaVersion`（第 171 行），未知高版本进入独立分支（第 175–191 行），尝试 v1 反序列化失败时不归类为损坏（`corrupted=false`，第 190 行），返回带 `SchemaVersion=文件版本` 的默认对象和 `unknownVersion=true`。
- `TryLoad`（第 113–157 行）：`corrupted=false` 时不移到 `.corrupted`，不触发备份回退。
- `LoadState`/`LoadSettings`（第 52–57、83–88 行）：`_stateReadOnly`/`_settingsReadOnly` 设为 true。
- `SaveState`/`SaveSettings`（第 64–79、90–103 行）：三层保护——(1) `_stateReadOnly` 标记、(2) DTO `SchemaVersion > Current`、(3) `HasHigherVersion` 重读磁盘文件。任一为 true 即跳过保存。
- `TrayService.ShowReadOnlyArchiveNotice`（第 59–65 行）：配置中文提示文本，App.xaml.cs 第 77–78 行在 `stateUnknown || settingsUnknown` 时调用。
- 新增 8 个单元测试（`ArchiveVersionProtectionTests.cs`）覆盖：不兼容字段类型只读保护、兼容字段仍受保护、新服务不能覆盖、备份中高版本保护、state/settings 独立保护。测试使用报告中的反例输入 `{"schemaVersion":99,"windowLeft":{"newFormat":1},...}`。
- 隔离集成 `readonly-state`/`readonly-settings` 场景：启动识别高版本、不生成 `.corrupted`、正常退出后原文件字节不变。
- 正常 v1 损坏备份恢复：`archive-cases` 场景中 `corrupt primary preserved byte-for-byte and valid backup selected` 通过，未回归。
- 验收对照：报告要求的"加载、尝试保存及正常退出后原主文件字节不变、不被重分类为损坏""应用有明确的恢复/只读提示依据""正常 v1 损坏备份恢复保持有效"均已满足。

### F-02：防抖保存完成后再改变位置，退出不保证最后位置落盘 — **通过**

- `App.FlushFinalState`（第 171–181 行）：调用 `MarkStateDirty()` 捕获最终位置并标脏，然后 `Dispose()` → `Flush()` 立即写盘。
- `App.MarkStateDirty`（第 155–169 行）：读取 `_mainWindow.Left/Top` 写入 `_state`，调用 `_saveCoordinator.MarkStateDirty(_state)`。
- `App.ShutdownMainWindow`（第 135–152 行）：`_isShuttingDown` 防重入（第 137–138 行），先 `FlushFinalState()`（第 140 行），再清理托盘和互斥锁，最后 `Close()` + `Shutdown(0)`。
- `App.OnExit`（第 183–190 行）：也调用 `FlushFinalState()`，第二次调用时 `_saveCoordinator is null` 直接 return（第 173–174 行），无重复写入。
- `MainWindow.RecoverPet`（第 502–515 行）和 `OnDisplayLayoutChanged`（第 480–495 行）：位置变更后均调用 `app.MarkStateDirty()`。
- `SaveCoordinator.Flush`（第 59–77 行）：检查 `_stateDirty && _pendingState is not null` 后保存。`Dispose`（第 91–99 行）调用 `Flush`。
- 隔离集成 `recover-after-save` 场景：等 5.7 秒完成防抖 → Recover → 退出，最终保存 `(793.333...,389.333...)` 而非旧的 `(300,240)`。`fast-final` 场景：防抖前退出保存 `(350,260)` 通过。
- 验收对照：报告要求的"防抖已完成→位置再次改变→托盘退出→重启恢复最后位置""仍有 pending 的立即退出保存""防抖和未知高版本保护均不能回归"均已满足。`readonly-state`/`readonly-settings` 场景同时验证了高版本保护不回归。

### F-03：重启仅恢复暂停标记，没有恢复暂停行为 — **通过**

- `App.OnStartup`（第 58–63 行）：`_isPaused = _state.IsPaused` → `new MainWindow()`（构造函数启动定时器）→ `if (_isPaused) _mainWindow.PauseAnimation()` → 然后 `Show()`。
- `MainWindow.PauseAnimation`（第 521–523 行）：`_frameTimer.Stop()`。构造函数第 106–111 行启动定时器，`PauseAnimation` 在 `Show()` 之前停止它。
- `TrayService.SetPaused`（第 52–57 行）：App.xaml.cs 第 71 行 `_tray.SetPaused(_isPaused)` 同步菜单状态。
- 时序正确：`new MainWindow()` 启动定时器 → 立即 `PauseAnimation()` 停止 → `Show()`。DispatcherTimer 的 Start/Stop 幂等，Stop 后无挂起 tick。
- 隔离集成 `paused-start` 场景：`paused=True; timerEnabled=False`，等待后帧不变化。`normal` 场景中手动 Pause/Resume 及暂停下 Recover 正常。
- 验收对照：报告要求的"暂停→退出→重启，动画/主动更新保持停止""Resume 后正常继续，位置不跃迁且无积压补演""增加实际启动链回归"均已满足。

### F-04：启动直接赋值旧坐标，没有执行可见位置恢复 — **通过**

- `App.OnStartup`（第 66 行）：`_mainWindow.SourceInitialized += (_, _) => RestoreWindowPosition()`。不再直接赋值存档坐标。
- `MainWindow` 构造函数（第 102–103 行）：`Left=100; Top=100` — 安全默认值。
- `App.RestoreWindowPosition`（第 81–98 行）：枚举显示器 → 查找保存的显示器（不存在则主屏）→ 非有限坐标回退 100 → 调用 `WindowPositionAdapter.RecoverPosition`（既有适配器）→ 写回 Left/Top。
- `SourceInitialized` 在 HWND 创建后、首次可见渲染前触发。`MainWindow_SourceInitialized`（设置 exStyle）和 App 的 `RestoreWindowPosition` 都挂在此事件，执行顺序无冲突（位置恢复不依赖 exStyle）。
- `App.MarkStateDirty`（第 164–165 行）：`MonitorInterop.GetWindowMonitorDeviceName(hwnd)` 使用 `MonitorFromWindow` + `GetMonitorInfoW` 记录实际显示器名称。
- `MonitorInterop.GetWindowMonitorDeviceName`（第 136–143 行）：P/Invoke 实现正确，`MONITOR_DEFAULTTONEAREST` 标志合理。
- 隔离集成 `offscreen-start` 场景：`100000,100000` + `MISSING_DISPLAY` → 恢复为 `(2380,1348)-(2560,1528)`，完整位于当前 150% 工作区。`restart-position` 场景：有效位置 `(350,260)` 原位恢复。
- 验收对照：报告要求的"有效位置仍原位恢复""旧屏不存在、旧位置越界等注入布局反例均回到可见工作区""保存显示器信息和实际恢复契约一致"均已满足。真实混合 DPI 热插拔仍记未验证，不要求额外设备。

### 复核结论

**F-01～F-04 全部通过。** 代码修改与整改记录描述一致，验收条件均满足，已通过部分未回归。

### 复核限制

- 隔离集成证据为修复作者自测结果，本审查者核对其内容与代码逻辑一致但未重跑（当前单屏 150%，无第二屏）。
- Shell 托盘真实点击（Explorer 通知区域图标）未执行；集成使用 `ToolStripMenuItem.PerformClick` 验证实际事件处理。
- 100% DPI 启动链、真实混合 DPI 双屏/热插拔、真实锁屏/休眠未实测，不宣称已通过。
- 系统通知气泡是否实际展示不作保证（仅验证配置内容）。
- 上述限制不阻断 F-01～F-04 的关闭：报告验收条件未要求这些项。

### 后续

- P0-004 阻断项已全部关闭。TASKS 可由开发方据本复核更新为 `done`（需同时满足正常任务验收证据和文档同步）。
- P0-005 可在 P0-004 标 done 后开始。
- Shell 托盘真实点击、100% 启动链、混合 DPI 双屏等未验证项按既定范围在后续阶段补齐，不阻塞当前任务。
