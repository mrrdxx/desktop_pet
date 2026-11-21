# P4-002 监理 R1 整改记录

整改时间：2026-11-13（Asia/Shanghai）。对应 [审查报告](../reviews/P4-002-review.md) R1。

## F-01：锁屏和睡眠通知取消表演

**问题**：系统锁屏和睡眠事件未接入休息提醒取消逻辑，表演可能在锁屏/睡眠后继续。

**修复位置**：
- `src/DesktopPet.App/App.xaml.cs` `OnSystemSessionSwitch`（原 private → 改 internal 供测试调用）：新增 `SessionSwitchReason.SessionLock=7` 分支，遍历 `_petWindows.Values` 调用 `CancelRestReminder()`。解锁（8）不续播。
- `src/DesktopPet.App/App.xaml.cs` `OnSystemPowerModeChanged`（原 private → 改 internal 供测试调用）：新增 `Microsoft.Win32.PowerModes.Suspend` 分支，遍历 `_petWindows.Values` 调用 `CancelRestReminder()`。唤醒不续播。

**设计理由**：锁屏和睡眠在 App 系统回调中直接遍历窗口取消，不依赖 ActivitySessionService 的暂停机制。`CancelRestReminder()` 已包含 `PlayAction("idle")` + `UpdateFrame()` 释放动画播放器，确保不留在 stretch/rest 帧。

**自测证据**：
- `LockScreen_CancelsPerformance`：模拟 `SessionSwitchReason.SessionLock`，断言 `IsRestReminderActive=false` 且 `CurrentAction` 非 stretch/rest。
- `Sleep_CancelsPerformance`：模拟 `PowerModes.Suspend`，断言同上。

## F-02：统一释放提醒动画

**问题**：找回后状态已结束，播放器仍停在 `stretch`。头部短按释放后无新手势接管，也留 stretch 帧。关闭/移除窗口时未释放。

**修复位置**：
- `src/DesktopPet.App/MainWindow.xaml.cs` `RecoverPet`：改用 `CancelRestReminder()` 替代裸 `Cancel()`+手动清归属，确保播放器回到 idle。
- `src/DesktopPet.App/MainWindow.xaml.cs` `SkipRestReminder`：新增 `PlayAction("idle")` + `UpdateFrame()`，头部短按释放后不留 stretch 帧。若调用方启动拖动/抚摸，会覆盖 idle。
- `src/DesktopPet.App/App.xaml.cs` `RemovePet`：在 `window.Close()` 前调用 `window.CancelRestReminder()`。
- `src/DesktopPet.App/App.xaml.cs` `OnPetWindowClosed`：第二只宠物关闭路径添加 `window.CancelRestReminder()`。

**自测证据**：
- `Recover_CancelsPerformance`：追加 `Assert.AreNotEqual("stretch", w.CurrentAction)` 和 `Assert.AreNotEqual("rest", w.CurrentAction)`。
- `SkipRestReminder_ReleasesAnimation`：`CancelRestReminder()` 后断言 `CurrentAction` 非 stretch/rest。
- `NaturalCompletion_NotStuckOnStretchOrRest`：自然结束后断言 `CurrentAction == "idle"` 且非 stretch/rest。

## F-03：选窗可见性 + 资源检查 + 预览显示

**问题**：`CanStartRestReminder()` 缺少 `IsVisible` 检查和动作资源存在性检查。DiagnosticProbe `RestReminderVerification` 调用 `CreatePetWindow` 但未 `Show()`，窗口不可见导致分发路径跳过。现有测试直接调用 `StartRestReminder` 而非通过 `OnRestReminderRequested` 真实分发。

**修复位置**：
- `src/DesktopPet.App/MainWindow.xaml.cs` `CanStartRestReminder()`：新增 `if (!IsVisible) return false;`（首行检查）和 `if (!_character.Actions.ContainsKey("stretch") && !_character.Actions.ContainsKey("rest")) return false;`（资源检查）。
- `tests/DesktopPet.DiagnosticProbe/Program.cs` `RestReminderVerification`：`CreatePetWindow` 后添加 `mainWindow.Show()`，使 `IsVisible=true`，分发路径可选中窗口。
- `tests/DesktopPet.App.Tests/RestReminderIntegrationTests.cs`：所有现有测试中 `CreatePetWindow` 后添加 `w.Show()`（共 10 处），适配新的 `IsVisible` 守卫。

**新增真实分发路径测试**（通过 `app.OnRestReminderRequested` 端到端）：
- `Dispatch_DualPet_OnlyOneStarts`：双宠均可见空闲，分发后仅一只开始表演。
- `Dispatch_FirstBusy_SecondSelected`：首只暂停（忙），分发选第二只。
- `Dispatch_NoAvailableWindow_Skipped`：唯一窗口暂停，分发静默略过。
- `Dispatch_InvisibleWindow_Skipped`：窗口未 Show，分发跳过。
- `CanStartRestReminder_FalseWhenNotVisible`：未 Show 窗口 `CanStartRestReminder` 返回 false，Show 后返回 true。

**自测证据**：构建 0 警告 0 错误，全量 974/974 通过（Core 486 + App 488），0 回归。新增 9 项测试（5 项真实分发路径 + 2 项 F-02 动作断言 + 2 项 F-01 锁屏/睡眠）。

## 隔离预览命令

```powershell
# 构建隔离预览
dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj

# 运行 rest-reminder 场景（短阈值 1 分钟自动触发，窗口先 Show 再分发）
# 输出：rest-reminder-observations.jsonl + rest-reminder-sidecar.json
dotnet test tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --filter "RestReminderVerification"
# 或直接运行探针可执行文件（按探针 Main 入口参数）
```

预览流程：探针创建窗口 → `Show()` → 短阈值 `ActivitySessionService` 通过 `TickManually` 累积活跃时间 → 1 分钟后 `RestReminderRequested` 事件 → `App.OnRestReminderRequested` 真实分发 → `MainWindow.StartRestReminder` → stretch → rest → ~30s 自然结束。操作者可点击身体跳过、拖动取消、暂停取消。

## R2 整改（2026-11-13）

对应 [审查报告](../reviews/P4-002-review.md) R2 的 F-01、F-03、M-12 验收入口三项收尾。代码、测试与预览实跑均已完成。

### F-01：系统通知线程（锁屏/睡眠回调统一 Dispatcher 串行处理）

**修复位置**：`src/DesktopPet.App/App.xaml.cs` `OnSystemSessionSwitch` / `OnSystemPowerModeChanged` 的表演取消按各宠物窗口的 `w.Dispatcher` 编组（`CheckAccess` 直接执行，否则 `BeginInvoke`）。生产环境中所有窗口与 App 同在 UI 线程，编组与 App Dispatcher 串行等价；后台线程触发回调时不再跨线程触碰窗口。未选用 `App.Dispatcher` 是因为测试可能复用来自其他线程的 App 实例。`ActivitySessionService` 的计时通知保持其内部自编组，不在本轮范围。

**新增反例测试**（`tests/DesktopPet.App.Tests/RestReminderIntegrationTests.cs`，含 `PumpDispatcher` 泵当前线程 Dispatcher）：
- `WorkerLockNotification_CancelsPerformanceViaDispatcher`：后台线程模拟 `SessionSwitchReason.SessionLock`，表演被取消；解锁（8）后不续播。
- `WorkerSleepNotification_CancelsPerformanceViaDispatcher`：后台线程模拟 `PowerModes.Suspend`，表演被取消；唤醒（Resume）后不续播。

### F-03：选窗条件（共享社交协调器权威状态 + stretch/rest 同时存在）

**修复位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `CanStartRestReminder()` 改读共享 `SocialCoordinator.CurrentSession` 权威状态（`InitiatorId` 或 `TargetId` 命中本窗口即拒绝），不再依赖窗口本地标志；并要求 `stretch` 与 `rest` 动作同时存在，缺任一 `return false`。配套测试缝：`src/DesktopPet.Core/Social/SocialCoordinator.cs` 新增 internal `TestSeedSession`（Core csproj 加 `InternalsVisibleTo DesktopPet.App.Tests`），`MainWindow` 暴露 internal `TestCharacter`。

**新增测试**：
- `CanStartRestReminder_FalseWhenSharedSocialSessionActive`：权威社交会话激活时拒绝。
- `CanStartRestReminder_FalseWhenStretchMissing` / `_FalseWhenRestMissing`：缺任一动作跳过。
- `Dispatch_FirstPetMissingRest_SecondSelected`：首只缺 `rest` 时分发选第二只。

### M-12 验收入口（可操作暂停）

- `src/DesktopPet.App/MainWindow.xaml.cs`：窗口右键原生菜单新增 `MenuIdPauseToggle=5`「Pause/Resume」项（标签随 `App.IsGloballyPaused` 切换），走与托盘相同的 `App.OnPauseToggled` 路径，暂停即取消表演且恢复不续播。
- `src/DesktopPet.App/App.xaml.cs`：新增 internal `IsGloballyPaused` 访问器。
- `tests/DesktopPet.DiagnosticProbe/Program.cs`：`rest-reminder` 场景操作说明更新为右键 Pause/Resume。

### 自测证据（均为本轮实际执行）

- 专项过滤：`dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter "FullyQualifiedName~RestReminder"` → 28/28 通过，EXIT=0。
- 全量：`dotnet test DesktopPet.slnx` → Core 486 + App 494 = 980/980 通过（较 R2 的 974 新增 6 项），EXIT=0。
- 探针构建：`dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj` → 0 警告 0 错误。
- 隔离预览实跑（本轮实际跑通）：
  - 构建：`dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj`
  - 启动：`tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe <baseDir> rest-reminder <resultFile>`（本次 `baseDir=artifacts\p4-002-r2-preview`，`resultFile=artifacts\p4-002-r2-preview\result.json`）
  - 日志：`artifacts\p4-002-r2-preview\rest-reminder\rest-reminder-observations.jsonl`、`rest-reminder-sidecar.json`、`rest-reminder-cleanup.json`、`state.json`
  - 实测结果：t=0 `stretch` → `rest`，t≈30s 自然回 `idle`，PROBE_EXIT=0。
  - 人工操作步骤：约 30 秒内可点击身体跳过、拖动取消；右键 →「Pause/Resume」验证暂停取消表演且恢复后不续播。

### R2 F-01 收尾（监理复核意见后第二轮，2026-11-13）

**问题**：此前的实现在系统线程上做 `_petWindows.Values.ToList()` 快照，再按窗口 BeginInvoke——集合读取仍在后台线程，与 UI 线程的窗口创建/移除存在竞态；窗口可能在排队期间被移除或关闭。

**修复位置**：`src/DesktopPet.App/App.xaml.cs`
- 新增 `_petWindowsDispatcher` 字段（`CreatePetWindow`/`TestSetMainWindow` 时从窗口捕获）。生产中所有窗口与 `_petWindows` 的全部变更都在单一 App UI 线程，该字段即生产 UI Dispatcher；不直接用 `App.Dispatcher`（测试可能复用来自其他线程的 App）。
- 新增 `CancelRestRemindersOnUiDispatcher()` + `CancelRestRemindersCore()`：锁屏（reason 7）与睡眠（Suspend）回调改为调用它——「集合读取 → 快照 → 取消」作为一个整体编组到该 UI Dispatcher，快照在执行时读取。排队期间移除/关闭的窗口不在快照中（不会对已关闭窗口调用取消）；期间新建的窗口会被包含。
- 背景线程调用不再触碰窗口集合；生产等价性不变（通知与取消仍与 UI 线程串行）。

**新增测试**：`QueuedLockCancel_WindowClosedAndAddedDuringQueue_SnapshotAtExecution`——后台线程触发锁屏排队取消；排队期间关闭第二只（真实 `OnPetWindowClosed` 路径：取消其表演并从集合移除）、再新建第三只并开始表演；泵 UI Dispatcher 后断言：无异常、剩余窗口被取消回 idle、排队期间新建的窗口被包含并取消（证明集合在执行时读取）、已关闭窗口不在集合中。

**自测证据**：专项 RestReminder 29/29 通过；全量 `dotnet test DesktopPet.slnx` Core 486 + App 495 = 981/981，0 失败。构建 0 警告 0 错误。

### 未验证项

- 真实锁屏/睡眠系统事件（测试为后台线程模拟事件）。
- 真实鼠标点击/拖动/右键暂停（探针本轮未做真实输入，待 M-12 实机验收）。
- 用户观感（伸懒腰/休息姿态辨识度、脚点稳定）。

## R5 F-04 整改（2026-11-13）

对应 [审查报告](../reviews/P4-002-review.md) R5 的 F-04：`stretch_0.png` 首帧脸下边缘黑线，且三帧几乎与 idle 相同、开场动作在默认 120×120 大小下辨识度不足。

### 修复位置与做法

- 仅重绘运行素材 `assets/pets/bear/stretch_0.png`、`stretch_1.png`、`stretch_2.png`，以 `idle_0.png` 为底图用 PIL 程序化重绘（不触碰 `rest_0.png`、`idle_0.png`、`character.json` 与任何代码）。
- 开场改为三帧打哈欠，帧时序保持 character.json 原定义（400/800/400ms，共 1600ms），整体仍为 stretch → rest 循环 → 约 30 秒自然结束：
  - `stretch_0`（400ms，哈欠渐开）：双眼改闭眼弧线（∩ 形，深色 (61,22,7)），嘴部为中等椭圆张口（约 8×6px）配深红口腔内色 (128,72,68)。
  - `stretch_1`（800ms，哈欠最大）：闭眼弧更宽，嘴部为大椭圆张口（约 14×12px），在 120×120 默认大小下一眼可辨。
  - `stretch_2`（400ms，收尾回落）：上眼睑半遮（半闭眼），嘴部收小为约 6×5px，向 rest 的安静姿态过渡。
- 黑线根因：原 `stretch_0` 在嘴部下方 y≈42-50 有一横跨 x≈23-97 的深色块（旧帧程序化生成残留）。新三帧均从干净的 `idle_0` 底图重绘，该深色块不再存在。
- 脚点不变：三帧 y=103-110 行像素与 `idle_0` 逐像素一致（不透明底行 y=110），轮廓与身体不动。

### 自测证据（均为本轮实际执行）

- 像素检查（PIL）：三帧 120×120；y40-60 逐行最大深色连续段分别为 5px（stretch_0，y=45）、11px（stretch_1，y=50，即大张嘴本体）、7px（stretch_2，y=48），远小于原黑线的约 74px；y=103-110 与 `idle_0` 逐像素一致。ASCII 逐行渲染目检确认闭眼弧、张口渐变可辨。
- 全量测试：`dotnet test DesktopPet.slnx` → Core 486 + App 495 = 981/981 通过，0 回归。
- 探针构建：`dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj` → 0 警告 0 错误。
- 隔离预览实跑：`DesktopPet.DiagnosticProbe.exe artifacts\p4-002-r5-f04-preview rest-reminder <resultFile>` → t=0 `stretch` → t=2-30s `rest` → t=32s 回 `idle`，约 30 秒自然结束，t=70s 自动清理退出，exit=0。日志：`artifacts\p4-002-r5-f04-preview\rest-reminder\rest-reminder-observations.jsonl` 等。
- 4 倍放大对比图（idle_0 / stretch_0/1/2 / rest_0 并排）：`artifacts\p4-002-f04-preview\stretch_frames_4x.png`（本机 artifacts，不入库）。

### 未验证项

- 用户实机观感（120×120 默认大小下打哈欠辨识度）与真实输入，待监理复核素材后进行 M-12。

## R9 F-05 整改（2026-11-13）

对应 [审查报告](../reviews/P4-002-review.md) R9 的 F-05：打开右键菜单时尚未选择命令，休息表演的动画即被覆盖为 idle（`CurrentAction=idle` 且 `IsRestReminderActive=true` 不一致）。

### 根因

`ShowNativeContextMenu` 打开菜单时无条件调用 `EndDrag(isCancellation: true)`（用于右键打断进行中的左键拖动），而 `EndDrag` 末尾无条件 `_animPlayer.PlayAction("idle")`——即使 `_isDragging=false`（无拖动进行）也会把休息动画切回 idle。真实拖动取消表演的正确入口是按下实体时的 `SkipRestReminder()`（`BeginCapturedPointerInteraction`），菜单打开路径并不需要重置动画。

### 修复位置

- `src/DesktopPet.App/MainWindow.xaml.cs` `EndDrag`：动画重置（`PlayAction("idle")` 与 `_dragRelease` 启动）改为仅在 `wasDragging`（真实拖动结束）时执行；`UpdateFrame` 保持调用。拖动中的右键（`wasDragging=true` + `isCancellation`）行为不变。
- `src/DesktopPet.App/MainWindow.xaml.cs`：菜单生命周期提取为 `EnterContextMenuMode()`（`_isContextMenuOpen=true` + `EndDrag(cancellation)` + hover 重置）与 `ExitContextMenuMode()`；命令分发提取为 `HandleContextMenuCommand(uint)`，`ShowNativeContextMenu` 与新测试缝 `TestOpenContextMenuLifecycle(int command, Action? onMenuShown)` 共用同一进入/退出/分发序列（测试缝跳过阻塞的 `TrackPopupMenuEx` 与窗口样式/前台切换）。
- 找回（MenuIdRecover）、移除（MenuIdRemovePet）、退出（MenuIdExit）命令分发路径原样搬入 `HandleContextMenuCommand`，行为未变。
- `tests/DesktopPet.App.Tests/PauseActionSwitchTests.cs` `PauseThenDragStart_PlayerStaysPaused`：原测试用反射切到 `drag` 后直接调用 `EndDrag`，依赖旧的无条件 idle 重置；现按真实拖动语义在 `EndDrag` 前设置 `_isDragging=true`（`StartDragging` 在生产中即如此）。

### 新增对照测试（`tests/DesktopPet.App.Tests/RestReminderIntegrationTests.cs`）

- `ContextMenuOpenClose_NoCommand_KeepsRestPerformance`：stretch 阶段开/关菜单（无命令）→ 保持 active+stretch；进入 rest 后再次开/关 → 保持 active+rest；约 30 秒仍自然结束回 idle。
- `ContextMenuPauseCommand_CancelsPerformance`：选 Pause（MenuIdPauseToggle=5）→ 表演取消、动作回 idle；Resume 不重播。
- `ContextMenuRecoverCommand_CancelsPerformance`：选 Recover（MenuIdRecover=2）→ 表演取消，动作非 stretch/rest。

### 自测证据（均为本轮实际执行）

- 专项：`dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter "FullyQualifiedName~RestReminder"` → 32/32 通过（原 29 + 新增 3）。
- 全量：`dotnet test DesktopPet.slnx` → Core 486 + App 498 = 984/984 通过，0 失败。

### 未验证项

- 真实右键菜单开/关的实机观感（测试用 `TestOpenContextMenuLifecycle` 缝模拟生命周期），待监理复核与 M-12 收尾时实机确认。

## 限制

- 真实锁屏/睡眠/唤醒需人工验证（测试用模拟事件）。
- 预览窗口在隔离环境中显示，实际观感需操作者确认。
- 未提交/推送，保留 P4-001 R4 整改的未提交修改。
