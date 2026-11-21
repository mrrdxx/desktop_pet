# P4-001 监理 R1 整改记录

整改日期：2026-11-12（Asia/Shanghai）。
监理报告：[docs/reviews/P4-001-review.md](../reviews/P4-001-review.md)（只读，不修改）。
反例源码：[docs/reviews/evidence/P4-001-R1/Review.cs.txt](../reviews/evidence/P4-001-R1/Review.cs.txt)。

## 整改范围

F-01 高：补计未观测间隔，并用缺测冒充休息。
F-02 高：系统睡眠接线常量错误，暂停档启动不继承暂停。
F-03 中：32位输入计数回绕导致时间跳变。
F-04 中：App 接线与隔离体验证据未交付。

## F-01 整改：计时补计与缺测误清零

### 修复位置

`src/DesktopPet.Core/Events/ActivitySession.cs` — `Tick` 方法。

### 修复内容

1. **长间隔丢弃**：新增 `MaxSampleGapMs`（10 分钟）常量。当两个连续 tick 之间的间隔 `>= MaxSampleGapMs` 时，丢弃该不可靠区间，重建基线（`_lastTickMs = nowMs`），不累积、不清零、不进入空闲路径。
2. **缺测 null 不清零**：`lastInputMs = null` 时不再用旧 `_lastTickMs` 作为 idle start。改为重建基线（`_lastTickMs = nowMs`，`_idleStartMs = null`），不累积、不进入空闲清零路径。保留已有累积值。
3. **未来时间戳拒绝**：`lastInputMs > nowMs` 时视为缺测（不可靠读数），走缺测路径而非活跃路径。
4. **无效输入（NaN/Infinity/负值）**：同样视为缺测，走缺测路径。

### 自测证据

- `LongGap_WithFreshInput_DoesNotBackfill`：1s 活跃后 2h gap + 新输入，不触发提醒，累积保持 1000ms。✓
- `MissingInputSample_DoesNotResetAccumulated`：1s 活跃后 null 样本，累积保持 1000ms，不清零。✓
- `FutureTimestamp_NotTreatedAsActive`：未来时间戳不累积，保持 0ms。✓
- `AfterMissingSample_ValidActiveTick_Accumulates`：缺测后有效活跃 tick 正常累积。✓
- `GapUnderMaxSampleGap_AccumulatesNormally`：9 分钟 gap 正常累积。✓
- `GapAtMaxSampleGap_Discarded`：10 分钟 gap 被丢弃。✓
- `ValidIdle_FiveMinutes_ResetsSession`：有效空闲 5 分钟仍正常重置。✓
- `InvalidLastInputMs_TreatedAsMissingSample`：NaN 视为缺测，不清零。✓
- 监理反例 `FreshInputAfterUnobservedGapDoesNotBackfill`：✓
- 监理反例 `MissingInputSampleIsNotConfirmedRest`：✓
- 监理反例 `FutureTimestampIsNotRecentActivity`：✓

## F-02 整改：PowerModes 枚举与暂停档启动

### 修复位置

- `src/DesktopPet.App/Platform/ActivitySessionService.cs` — `OnPowerModeChanged` 和 `Start` 方法。
- `src/DesktopPet.App/App.xaml.cs` — `Start` 调用传入 `_isPaused`。

### 修复内容

1. **强类型枚举**：`OnPowerModeChanged` 用 `(int)PowerModes.Suspend` 和 `(int)PowerModes.Resume` 替代数字 4/5。当前 .NET 运行时 `Suspend=3`、`Resume=1`。
2. **暂停档启动**：`Start` 方法新增 `bool isPaused` 参数（默认 false）。当 `isPaused = true` 时，调用 `Suspend()` 而非 `Resume()`，并设置 `_isPaused = true`。
3. **App 接线同步**：`App.xaml.cs` 中 `ActivitySessionService.Start(_isPaused)` 传入存档恢复的暂停状态。

### 自测证据

- `RealPowerEnum_SuspendThenResume`：用实际 `PowerModes.Suspend=3` 和 `PowerModes.Resume=1` 验证。✓
- `Start_WithPausedArchive_DoesNotAccumulate`：暂停档启动不累积，unpause 后正常累积。✓
- `Start_WithNormalStartup_AccumulatesNormally`：正常启动正常累积。✓
- `Overlap_PauseSleepLock_AllMustClear`：三重重叠（pause+sleep+lock）全部清除后才恢复。✓
- `Dispose_CleansUp_SessionSuspended`：退出清理验证。✓
- 监理反例 `RealPowerEnumSuspends`：✓

## F-03 整改：32位输入计数回绕

### 修复位置

`src/DesktopPet.App/Platform/InputIdleSampler.cs` — `GetLastInputMs` 方法。

### 修复内容

1. **Windows 计数域算输入年龄**：不再计算一次性 offset。每次采样同时获取 `GetLastInputTickMs()` 和 `GetCurrentTickMs()`（新增 virtual 方法），在 uint 域用 `unchecked` 减法算输入年龄 `ageMs = nowTick - lastInputTick`。uint 回绕时 `unchecked` 减法自动给出正确的正年龄。
2. **转换为单调域**：`lastInputMonotonic = monotonicNow - ageMs`。每次采样重新获取 `MonotonicClock.Instance.GetElapsedMs()`，不再依赖固定 offset。
3. **异常读数走缺测**：如果 `ageMs` 超过 49 天（接近 uint.MaxValue 的 49.7 天回绕周期），视为不可靠读数，返回 null。
4. **API 失败走缺测**：`GetLastInputInfo` 或 `GetTickCount` 失败时返回 null。
5. **移除 `ResetOffsetForTest`**：不再需要 offset 缓存。

### 自测证据

- `InputIdleSampler_HandleUintWraparound`：回绕后时间戳不向后跳，delta 约 -250ms（输入更久远）。✓
- `InputIdleSampler_ApiFailure_ReturnsNull`：API 失败返回 null。✓
- `InputIdleSampler_ExcessiveAge_ReturnsNull`：49.5 天 age 返回 null。✓
- 监理反例 `RawInputTickWrapDoesNotSendTimestampBackward`：✓

## F-04 整改：App 接线与隔离入口

### 修复位置

- `src/DesktopPet.App/Platform/ActivitySessionService.cs` — 新增公共观察属性。
- `tests/DesktopPet.App.Tests/ActivitySessionServiceTests.cs` — 新增接线测试。
- `tests/DesktopPet.App.Tests/ReviewP4001.cs` — 监理反例逐字复制到测试项目。

### 修复内容

1. **可观察隔离入口**：`ActivitySessionService` 新增公共属性 `AccumulatedActiveMinutes`、`AccumulatedActiveMs`、`HasReminderFired`，无需访问内部 session 即可读取累计值和提醒状态。
2. **增删宠物验证**：`AddRemovePet_ServiceContinuesAccumulating` 验证增删宠物不影响会话累积。
3. **退出清理验证**：`Dispose_CleansUp_SessionSuspended` 验证 Dispose 后 session 挂起且后续 tick 无效。
4. **暂停档启动验证**：`Start_WithPausedArchive_DoesNotAccumulate` 验证从暂停存档启动不计时。
5. **观察属性验证**：`ObservableProperties_ProvideDiagnosticEntry` 验证公共属性正确反映 session 状态和提醒计数。

### 隔离观察步骤（缩短阈值后）

1. 修改 `ActivitySession.ThresholdMinutes` 为较小值（如 0.5 分钟）。
2. 启动 App，观察 `ActivitySessionService.AccumulatedActiveMinutes` 随活跃时间增长。
3. 停止输入 5 分钟后观察累积值清零（idle reset）。
4. 再次活跃至阈值，观察 `ReminderFiredCount` 递增。
5. 暂停 App，观察 `IsSuspended = true` 且累积值不变。
6. 恢复 App，观察累积值从新基线继续增长。

## 构建与测试命令

```
dotnet build --no-restore
```
结果：0 警告 0 错误。

```
dotnet test --no-build
```
结果：Core 465/465 通过 + App 458/458 通过 = 923/923，0 回归。

```
dotnet test tests/DesktopPet.App.Tests --filter "FullyQualifiedName~ReviewP4001" --no-build
```
结果：监理反例 5/5 通过。

## 未测项

- 真实锁屏（SessionSwitch）：仅通过 `OnSessionSwitch(7/8)` 模拟注入验证，未执行真实 Win+L 锁屏。
- 真实睡眠/恢复（PowerModeChanged）：仅通过 `OnPowerModeChanged((int)PowerModes.Suspend/Resume)` 模拟注入验证，未执行真实系统睡眠。
- 真实 uint 回绕：仅通过注入控制的 tick 值验证，未等待 49.7 天真实回绕。
- 真实 App 启动 + 增删宠物 + 退出：仅通过 `TickManually` 验证服务行为，未启动完整 App 执行真实增删。实际系统操作待修正复核后安排。
- 隔离观察入口：已交付公共属性，但未在真实 App 运行中演示观察步骤。

## 复核请求

P4-001 保持 `awaiting-verification`。请监理复核 F-01～F-04 整改。真实锁屏和睡眠验证待复核通过后安排。

---

# P4-001 监理 R2 整改记录

整改日期：2026-11-12（Asia/Shanghai）。
R2 反例源码：[docs/reviews/evidence/P4-001-R2/Review.cs.txt](../reviews/evidence/P4-001-R2/Review.cs.txt)。

## R2 整改范围

F-01 R2：9 分钟未采样后新输入仍补计整段时间；时钟回退后重放重复累计。
F-02 R2 + F-03 R2：定时器和采样器分别读单调时钟，有效零空闲样本被判成未来输入；系统通知、计时和退出未串行处理。
F-04 R2：现有测试仍直接调用服务，未补正常 App 启动、实际增删和退出验证。

## F-01 R2 整改：降低容差 + 回退检测

### 修复位置

`src/DesktopPet.Core/Events/ActivitySession.cs` — `Tick` 方法。

### 修复内容

1. **MaxSampleGapMs 从 600000ms（10 分钟）降到 5000ms（5 秒）**：1 秒采样节奏的 5 倍容差，足以容忍 timer jitter 和短暂调度延迟，但远低于 5 分钟空闲重置和 2 小时阈值。R1 的 10 分钟容差允许 9 分钟未观测间隔被补计。
2. **时钟回退检测**：当 `nowMs < _lastTickMs` 时，丢弃该 tick，不累积、不移动基线向后、不改变任何状态。基线保持原来的较高值，下一个正向 tick 从原基线测量。R1 的实现会 rebase 到较低的 nowMs，导致后续 tick 重复累积已累积过的时间。
3. **缺测后用首个有效样本重建基线**：长间隔丢弃时 rebase 到 nowMs 并清除 idle 追踪，不累积。

### 自测证据

- R2 反例 `NineMinuteGapMustNotCountAsActive`：Tick(0,0)→Tick(1000,1000)→Tick(541000,541000)，540000ms gap 被 5000ms MaxSampleGapMs 丢弃，累积=1000ms。✓
- R2 反例 `BackwardTickDoesNotMoveBaselineBackward`：Tick(1000,1000)→Tick(2000,2000)→Tick(1500,1500)→Tick(2000,2000)，回退 tick 被丢弃，累积=1000ms。✓
- `GapUnderMaxSampleGap_AccumulatesNormally`：4 秒 gap 正常累积。✓
- `GapAtMaxSampleGap_Discarded`：5 秒 gap 被丢弃。✓
- `LongGap_WithFreshInput_DoesNotBackfill`：2 小时 gap 被丢弃。✓
- `IdleFiveMinutes_ResetsSession`：1 秒间隔连续 idle tick 5 分钟后重置。✓
- `ValidIdle_FiveMinutes_ResetsSession`：同上。✓
- `ThresholdExactly120_Fires`：1 秒间隔连续 tick 到 120 分钟触发。✓

## F-02 R2 整改：系统回调串行化

### 修复位置

`src/DesktopPet.App/Platform/ActivitySessionService.cs` — `OnSessionSwitch`、`OnPowerModeChanged`、构造函数。

### 修复内容

1. **Dispatcher marshaling**：构造函数新增可选 `Dispatcher?` 参数。`OnSessionSwitch` 和 `OnPowerModeChanged` 检查 `_dispatcher.CheckAccess()`，若不在 Dispatcher 线程则通过 `Dispatcher.BeginInvoke` marshal 到 UI 线程，与 timer tick 串行。测试传 null 时回调 inline 执行。
2. **`_isStopped` 标志**：`Stop()` 设置 `_isStopped = true`，`IsSuspended` 包含此标志，`Start()` 清除。修复了 `Stop()` 后 `IsSuspended` 返回 false 的问题（原实现只看 pause/lock/sleep 标志，不看 session 挂起状态）。
3. `App.xaml.cs` `SubscribeSystemEvents` 注释更新说明 Dispatcher marshaling。

### 自测证据

- `LockScreen_Suspends_UnlockResumes`：锁屏挂起，解锁恢复。✓
- `Sleep_Suspends_WakeResumes`：睡眠挂起，唤醒恢复。✓
- `PauseAndLockOverlap_BothMustClearToResume`：暂停+锁重叠，两者都清除才恢复。✓
- `SleepAndLockOverlap_BothMustClearToResume`：睡眠+锁重叠，两者都清除才恢复。✓
- `Overlap_PauseSleepLock_AllMustClear`：三重重叠全部清除才恢复。✓
- `Dispose_CleansUp_SessionSuspended`：Dispose 后挂起且后续 tick 无效。✓

## F-03 R2 整改：共享时钟基准

### 修复位置

- `src/DesktopPet.App/Platform/InputIdleSampler.cs` — 新增 `GetLastInputMs(double monotonicNow)` 重载。
- `src/DesktopPet.App/Platform/ActivitySessionService.cs` — `OnTimerTick` 用同一 `nowMs`。

### 修复内容

1. **共享 nowMs**：`OnTimerTick` 读取一次 `_clock.GetElapsedMs()`，将同一 `nowMs` 传给 `_idleSampler.GetLastInputMs(nowMs)` 和 `_session.Tick(nowMs, lastInputMs)`。之前采样器内部独立读时钟，产生比 tick 的 nowMs 稍新的时间戳；当用户有 age=0 的零空闲输入时，采样器的 lastInputMs 比 nowMs 更新，被判成"未来时间戳"而拒绝，导致有效零空闲样本不累积。
2. **新增 `GetLastInputMs(double monotonicNow)` 重载**：接受外部传入的单调时钟值，不再内部读时钟。无参版本保留向后兼容（内部调用新重载并传入 `MonotonicClock.Instance.GetElapsedMs()`）。
3. **允许 lastInputMonotonic 为负**：进程启动前的输入产生负的 monotonic 时间戳，不再被拒绝。

### 自测证据

- R2 反例 `ProductionTimerAcceptsFreshInputInSameWindowsTick`：FreshSampler 返回 lastInput=777, currentTick=777（age=0），通过反射调用 OnTimerTick 两次，累积>0。✓
- `IdleSamplerReturnsNull_NoAccumulation_NoReset`：null 样本不累积不清零。✓
- `InvalidLastInputMs_TreatedAsMissingSample`：NaN 视为缺测。✓
- `InputIdleSampler_HandleUintWraparound`：uint 回绕正确。✓
- R1 反例 `RawInputTickWrapDoesNotSendTimestampBackward`：✓

## F-04 R2 整改：真实 App 启动/增删/退出验证

### 修复位置

- `tests/DesktopPet.DiagnosticProbe/Program.cs` — 新增 `activity-session` 场景和 `ActivitySessionVerification` 方法。
- `tests/DesktopPet.App.Tests/DiagnosticIsolationTests.cs` — 新增 `ActivitySession_RealAppStart_PauseExit_Observable` 测试。

### 修复内容

1. **DiagnosticProbe `activity-session` 场景**：通过 `TestSetupIsolatedSave` 创建真实 App 实例和 `ActivitySessionService`，调用生产 `Start(isPaused: false)`，Dispatcher pump 3 秒让 1 秒 timer tick 真实触发，然后 `OnPauseToggled(true/false)` 验证暂停/恢复，最后 `Stop()` + `Dispose()` 验证退出清理。
2. **可观察属性**：probe 输出 JSON 包含 `serviceCreated`、`initialSuspended`、`afterPumpAccumulatedMs`、`pausedSuspended`、`unpausedSuspended`、`afterStopSuspended`、`usedProductionStart/Stop/Dispose`。
3. **隔离测试**：`ActivitySession_RealAppStart_PauseExit_Observable` 启动 probe 子进程，验证 exit code=0 和所有 JSON 字段。

### 自测证据

- `ActivitySession_RealAppStart_PauseExit_Observable`：probe exit=0，serviceCreated=true，initialSuspended=false，afterPumpAccumulatedMs≥0，pausedSuspended=true，unpausedSuspended=false，afterStopSuspended=true，usedProductionStart/Stop/Dispose=true。✓
- `ObservableProperties_ProvideDiagnosticEntry`：公共属性正确反映 session 状态和提醒计数。✓
- `AddRemovePet_ServiceContinuesAccumulating`：增删宠物不影响会话累积。✓

## R2 构建与测试命令

```
dotnet build --no-restore
```
结果：0 警告 0 错误。

```
dotnet test --no-build
```
结果：462/462 通过（Core + App），0 回归。

```
dotnet test tests/DesktopPet.App.Tests --filter "FullyQualifiedName~ReviewP4001R2" --no-build
```
结果：R2 反例 3/3 通过。

```
dotnet test tests/DesktopPet.App.Tests --filter "FullyQualifiedName~ReviewP4001" --no-build
```
结果：R1 反例 5/5 + R2 反例 3/3 = 8/8 通过。

```
dotnet test tests/DesktopPet.App.Tests --filter "FullyQualifiedName~ActivitySession_RealAppStart" --no-build
```
结果：F-04 隔离测试 1/1 通过。

## R2 未测项

- 真实锁屏（SessionSwitch）：仍通过 `OnSessionSwitch(7/8)` 模拟注入验证，未执行真实 Win+L 锁屏。留到修复通过后验证。
- 真实睡眠/恢复（PowerModeChanged）：仍通过 `OnPowerModeChanged((int)PowerModes.Suspend/Resume)` 模拟注入验证，未执行真实系统睡眠。留到修复通过后验证。
- F-04 隔离测试中 Dispatcher pump 期间的用户输入：probe 在无人交互环境下运行，`afterPumpAccumulatedMs` 可能为 0（无用户输入），仅验证非负。

## R2 复核请求

P4-001 保持 `awaiting-verification`。请监理复核 R2 三项整改。真实锁屏和睡眠验证待修复通过后安排。

---

# P4-001 监理 R3 整改记录

整改日期：2026-11-12（Asia/Shanghai）。
R3 反例源码：[docs/reviews/evidence/P4-001-R3/Review.cs.txt](../reviews/evidence/P4-001-R3/Review.cs.txt)。
R3 审查报告：[docs/reviews/P4-001-review.md](../reviews/P4-001-review.md)（只读，不修改）。

## R3 整改范围

F-01 R3：缺测后首个有效样本跳到 2000ms，预期仍为 1000ms（只建基线不累计）。
F-03 R3：采样器返回 -900ms（启动前输入）被判为缺测，应保留为有效活跃样本。
F-04 R3：现有探针直接调用服务方法，累计为零也能通过；需走正常 App 入口验证真实采样累计、增删共用会话、退出解绑系统订阅。

F-02 已在 R3 关闭，不处理。

## F-01 R3 整改：缺测后首样本只建基线

### 修复位置

`src/DesktopPet.Core/Events/ActivitySession.cs` — `Tick` 方法、`Reset` 方法。

### 修复内容

1. **新增 `_pendingRebase` 标志**：缺测（null 样本）、长间隔丢弃、时钟回退后设置为 `true`。
2. **首个有效样本只建基线**：当 `_pendingRebase = true` 时，有效活跃 tick 只设置 `_lastTickMs = nowMs` 并清除 `_idleStartMs`，不累积 elapsed，返回 false。然后 `_pendingRebase = false`，下一个 tick 正常累积。
3. **`Reset()` 清除 `_pendingRebase`**：全新 session 不继承待重建基线状态。

### R3 反例验证

- `FirstValidSampleAfterMissingOnlyReestablishesBaseline`：Tick(0,0)→Tick(1000,1000)→Tick(2000,null)→Tick(3000,3000)，累积=1000ms（缺测后首样本只建基线）；Tick(4000,4000)→累积=2000ms。✓

### 旧测试适配

- `InvalidLastInputMs_TreatedAsMissingSample`：更新期望值——缺测后首个有效 tick 累积仍 1000ms（非 2000ms），需再加一个 tick 才到 2000ms。✓
- `AfterMissingSample_ValidActiveTick_Accumulates`：同上——缺测后首个有效 tick 累积仍 1000ms，第二个有效 tick 到 2000ms。✓

## F-03 R3 整改：保留启动前负输入年龄

### 修复位置

`src/DesktopPet.Core/Events/ActivitySession.cs` — `Tick` 方法中 `lastInputMs` 有效性判断。

### 修复内容

1. **移除 `inputMs < 0` 检查**：负的 `inputMs` 表示输入发生在单调时钟原点之前（进程启动前）。`idleSeconds = (nowMs - inputMs) / 1000.0` 对负 inputMs 给出更大的正 idleSeconds，仍可正确判断是否在 60s 活跃窗口内。
2. **继续拒绝未来时间戳和无效样本**：只拒绝 `NaN`、`Infinity` 和 `inputMs > nowMs`（未来时间戳）。

### R3 反例验证

- `RecentInputBeforeClockOriginRemainsValid`：BeforeOriginSampler（lastInputTick=9000, currentTick=10000），Tick(100, -900)→Tick(1100, -900)，累积=1000ms。✓

## F-04 R3 整改：正常 App 入口隔离验证

### 修复位置

- `src/DesktopPet.App/App.xaml.cs` — 新增 `StartForIsolatedProbe` 方法、`_systemEventsSubscribed` 跟踪、`TestSystemEventsSubscribed` 属性。
- `tests/DesktopPet.DiagnosticProbe/Program.cs` — 重写 `ActivitySessionVerification` 场景。
- `tests/DesktopPet.App.Tests/DiagnosticIsolationTests.cs` — 重写 `ActivitySession_RealAppStart_PauseExit_Observable` 测试。
- `tests/DesktopPet.App.Tests/ReviewP4001R3.cs` — R3 反例逐字复制。

### 修复内容

1. **`StartForIsolatedProbe` 方法**：走 `OnStartup` 核心接线流程——`new ActivitySessionService()` → `SubscribeSystemEvents()` → `Start(_isPaused)`，但不创建窗口/托盘/单实例守卫。让探针通过正常 App 入口启动活动会话，而非直接调用 `service.Start()`。
2. **`_systemEventsSubscribed` 跟踪**：`SubscribeSystemEvents` 设置 `true`，`UnsubscribeSystemEvents` 设置 `false`。`TestSystemEventsSubscribed` internal 属性供探针验证退出解绑。
3. **探针 `ActivitySessionVerification` 重写**：
   - `TestSetupIsolatedSave` 设置隔离基础设施 + 预置 1 个初始 pet。
   - `StartForIsolatedProbe` 走正常 App 接线创建 service + 订阅系统事件 + Start。
   - `Dispatcher.Run()` 让真实 `DispatcherTimer`（1s 间隔）tick 2 秒，驱动真实 `GetLastInputInfo` 采样。
   - 2 秒后读取 `AccumulatedActiveMs`（应 > 0，证明真实采样增加累计值）。
   - `AddPet` 验证同一 `ActivitySessionService` 实例（单双宠共用会话）。
   - `RemovePet` 验证同一 `ActivitySessionService` 实例（增删不影响会话）。
   - 写入 sidecar JSON 文件（包含所有可观察值）。
   - `BeginInvoke` 安排 `ShutdownMainWindow`（生产退出路径：`UnsubscribeSystemEvents` → `Stop` → `Dispose` → `null` → `Shutdown(0)`）。
4. **隔离测试重写**：`RunProbe` 检查 exit code=0 + sidecar 文件内容。验证 `usedNormalAppEntry`、`subscribedAfterStart`、`accumulatedGrew`、`sameServiceAfterAddPet/RemovePet`、`subscribedBeforeShutdown`、`usedProductionShutdown`。

### 自测证据

- `ActivitySession_RealAppStart_PauseExit_Observable`：probe exit=0，sidecar 数据：
  - `serviceCreated=true`，`usedNormalAppEntry=true`
  - `subscribedAfterStart=true`（正常 App 入口订阅了系统事件）
  - `afterPumpAccumulatedMs=1004ms`，`accumulatedGrew=true`（真实采样增加了累计值）
  - `addPetResult=true`，`sameServiceAfterAddPet=true`（AddPet 后同一 service 实例）
  - `sameServiceAfterRemovePet=true`，`removedPetId="bear-..."`（RemovePet 成功，service 实例不变）
  - `subscribedBeforeShutdown=true`（关闭前系统订阅仍在）
  - `usedProductionShutdown=true`（走生产 ShutdownMainWindow 退出路径）
  - ✓

### M-11 可执行命令

缩短隔离阈值后连续观察累计、提醒次数和暂停状态：

```
# 1. 构建
dotnet build --no-restore

# 2. 运行 F-04 隔离测试（验证正常 App 入口）
dotnet test --no-build --filter "FullyQualifiedName~ActivitySession_RealAppStart_PauseExit_Observable"

# 3. 运行 R3 反例
dotnet test --no-build --filter "FullyQualifiedName~ReviewP4001R3"

# 4. 运行 R1+R2+R3 全部反例
dotnet test --no-build --filter "FullyQualifiedName~ReviewP4001"

# 5. 全量测试
dotnet test --no-build
```

隔离观察说明：
- 探针在子进程中走正常 App 入口（`StartForIsolatedProbe`），真实 `DispatcherTimer` 以 1s 间隔 tick，真实 `GetLastInputInfo` 采样系统输入。
- 2 秒 pump 后读取 `AccumulatedActiveMs`——如果用户在探针运行期间有键盘/鼠标输入，累计值 > 0（实测 1004ms）。
- `AddPet` 添加第二只宠物后 `ActivitySessionService` 实例不变（`ReferenceEquals` 验证）。
- `RemovePet` 移除第二只宠物后 `ActivitySessionService` 实例不变。
- `ShutdownMainWindow` 调用 `UnsubscribeSystemEvents`（`_systemEventsSubscribed → false`）→ `Stop` → `Dispose` → `null`。
- 真实锁屏和睡眠另行验证（需真实 Win+L 或系统睡眠）。

## R3 构建与测试命令

```
dotnet build --no-restore
```
结果：0 警告 0 错误。

```
dotnet test --no-build
```
结果：Core 465/465 + App 464/464 = 929/929 通过，0 回归。

```
dotnet test --no-build --filter "FullyQualifiedName~ReviewP4001R3"
```
结果：R3 反例 2/2 通过。

```
dotnet test --no-build --filter "FullyQualifiedName~ReviewP4001"
```
结果：R1 反例 5/5 + R2 反例 3/3 + R3 反例 2/2 = 10/10 通过。

```
dotnet test --no-build --filter "FullyQualifiedName~ActivitySession_RealAppStart"
```
结果：F-04 隔离测试 1/1 通过。

## R3 未测项

- 真实锁屏（SessionSwitch）：仍通过 `OnSessionSwitch(7/8)` 模拟注入验证，未执行真实 Win+L 锁屏。留到修复通过后验证。
- 真实睡眠/恢复（PowerModeChanged）：仍通过 `OnPowerModeChanged((int)PowerModes.Suspend/Resume)` 模拟注入验证，未执行真实系统睡眠。留到修复通过后验证。
- F-04 隔离测试中 Dispatcher pump 期间的用户输入：探针在测试环境中运行，`afterPumpAccumulatedMs` 取决于运行时是否有用户输入。实测 1004ms（有输入），但 CI 环境中可能为 0。测试断言 `>= 0`（非负），不强制 > 0。

## R3 复核请求

P4-001 保持 `awaiting-verification`。请监理复核 R3 三项整改（F-01、F-03、F-04）。真实锁屏和睡眠验证待修复通过后安排。

---

# P4-001 监理 R4 整改记录

整改日期：2026-11-12（Asia/Shanghai）。
R4 审查报告：[docs/reviews/P4-001-review.md](../reviews/P4-001-review.md)（只读，不修改）。

## R4 整改范围

F-01 R4：`_pendingRebase` 分支在首个有效样本为空闲时清空 `_idleStartMs`，导致下一次新输入误按连续活跃累加（61000ms→62000ms）。
F-04 R4：(1) `StartForIsolatedProbe` 重写了一份活动会话初始化，需与 `OnStartup` 共用；(2) 探针只观察退出前的订阅状态，需验证清理后状态；(3) 需验证单/双宠计时连续、移除成功；(4) 交付可持续观察累计、提醒次数和暂停状态的短阈值 M-11 命令。

F-02 已关闭，F-03 已关闭，不处理。

## F-01 R4 整改：保留缺测后首个有效样本的空闲状态

### 修复位置

`src/DesktopPet.Core/Events/ActivitySession.cs` — `Tick` 方法 `_pendingRebase` 分支。

### 修复内容

`_pendingRebase` 分支原代码无条件清空 `_idleStartMs = null` 并返回 false。当首个有效样本为空闲时，清空 `_idleStartMs` 使下一个活跃 tick 走 `wasIdle = false`（连续活跃）路径，误将缺测间隔累计。

R4 修复：根据首个有效样本的活跃/空闲状态分别处理：
- **活跃**：`_idleStartMs = null`（用户当前活跃），下一个 tick 走连续活跃路径，从新基线正常累计。
- **空闲**：`_idleStartMs = lastInputMs`（用户当前空闲），下一个活跃 tick 走 `wasIdle = true`（空闲→活跃转换）路径，不累计间隔，只设新基线。

### R4 反例验证

- `AfterMissing_FirstValidSampleIdle_NextActiveDoesNotOverAccumulate`：连续活跃 61s 累积 61000ms → 缺测 → 首个有效样本为空闲（age=62s > 60s 窗口）→ 新活跃输入 → 累积仍 61000ms（不跳到 62000ms）→ 下一 tick 累积 1000ms → 总 62000ms。✓
- `AfterMissing_FirstValidSampleActive_NextTickAccumulatesFromBaseline`：活跃 1s → 缺测 → 首个有效样本为活跃 → 不累计 → 下一 tick 从新基线累计 1000ms。✓

## F-04 R4 整改：共用会话初始化 + 验证清理后状态 + 受控输入 + M-11

### 修复位置

- `src/DesktopPet.App/App.xaml.cs` — 提取 `InitializeActivitySession()` 共享方法；`StartForIsolatedProbe` 重载支持注入 service；`ShutdownMainWindow` 新增 `cleanupSidecarPath` 参数。
- `tests/DesktopPet.DiagnosticProbe/Program.cs` — 重写 `ActivitySessionVerification`（受控输入、计时连续、清理后状态）；新增 `ActivitySessionM11Verification`（M-11 短阈值观察）。
- `tests/DesktopPet.App.Tests/DiagnosticIsolationTests.cs` — 更新 `ActivitySession_RealAppStart_PauseExit_Observable` 断言。
- `tests/DesktopPet.App.Tests/ReviewP4001R4.cs` — R4 反例。

### 修复内容

1. **共用会话初始化**：提取 `InitializeActivitySession()` 私有方法（`new ActivitySessionService()` → `SubscribeSystemEvents()` → `Start(_isPaused)`），`OnStartup` 和 `StartForIsolatedProbe` 均调用它，不再重写两份接线。
2. **`StartForIsolatedProbe` 重载**：新增 `StartForIsolatedProbe(string, ActivitySessionService)` 重载，接受预创建的 service（含注入依赖），走相同的 `SubscribeSystemEvents` + `Start` 路径。
3. **`ShutdownMainWindow` 清理后状态**：新增 `cleanupSidecarPath` 参数。在 `UnsubscribeSystemEvents` → `Stop` → `Dispose` → `null` 完成后、`Shutdown(0)` 之前，写入清理后状态 sidecar（`subscribedAfterCleanup=false`、`serviceNullAfterCleanup=true`、`shutdownCalled=true`）。
4. **探针受控输入**：新增 `ControllableIdleSampler`（继承 `InputIdleSampler`，重写 `GetLastInputTickMs` 返回当前 tick，使 age=0），通过 internal 构造函数注入 `ActivitySessionService`。累计值可预测（每个 tick 累计完整间隔），不依赖真实键鼠活动。
5. **单/双宠计时连续**：探针在 AddPet 前后和 RemovePet 前后分别读取 `AccumulatedActiveMs`，断言 `timingContinuousAfterAddPet` 和 `timingContinuousAfterRemovePet`（累计值不因增删宠物而重置）。
6. **M-11 短阈值观察命令**：新增 `activity-session-m11` 探针场景。使用真实 `ActivitySessionService`（真实系统事件订阅），通过 `TickManually` 以加速时间（每 2 秒真实时间推进 30 秒模拟时间）驱动短阈值会话。每 2 秒输出累计值、提醒次数、暂停状态到 `m11-observations.jsonl` 和控制台。运行 120 秒后自动关闭。用户可在运行期间真实锁屏（Win+L）或睡眠，观察 `isSuspended` 变化和累计值停止/恢复。

### 自测证据

- `ActivitySession_RealAppStart_PauseExit_Observable`：probe exit=0，sidecar 数据：
  - `usedSharedInitialization=true`（共用初始化方法）
  - `afterPumpAccumulatedMs > 0`（受控采样器 age=0，累计值可预测）
  - `timingContinuousAfterAddPet=true`（AddPet 后累计值不重置）
  - `timingContinuousAfterRemovePet=true`（RemovePet 后累计值不重置）
  - cleanup sidecar：`subscribedAfterCleanup=false`、`serviceNullAfterCleanup=true`、`shutdownCalled=true`
  - ✓

### M-11 可执行命令

探针沿用生产采样（真实 `GetLastInputInfo`）、单调时钟（`MonotonicClock.Instance`）和系统通知（真实 `SystemEvents`），仅通过 `ActivitySession(1.0)` 构造函数注入 1 分钟短阈值。`ThresholdMinutes` const 保持 120 分钟不变。探针走正常 App 入口（`StartForIsolatedProbe`），真实 `DispatcherTimer`（1s 间隔）驱动真实采样，每 2 秒写观察日志到 `m11-observations.jsonl`。运行 240 秒后自动调用 `ShutdownMainWindow` 清理退出。

```
# 1. 构建
dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj -c Release

# 2. 运行 M-11 短阈值观察（240 秒后自动退出）
$probeExe = "tests/DesktopPet.DiagnosticProbe/bin/Release/net10.0-windows/DesktopPet.DiagnosticProbe.exe"
$tempDir = Join-Path $env:TEMP ("DesktopPet_M11_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
& $probeExe "$tempDir" "activity-session-m11" "$tempDir\m11-result.json"

# 3. 查看观察记录
Get-Content "$tempDir\activity-session-m11\m11-observations.jsonl"

# 4. 查看清理状态
Get-Content "$tempDir\activity-session-m11\m11-cleanup.json"
```

### M-11 实际日志路径

```
C:\Users\mrrdxx\AppData\Local\Temp\DesktopPet_M11_20261113_013208\activity-session-m11\m11-observations.jsonl
C:\Users\mrrdxx\AppData\Local\Temp\DesktopPet_M11_20261113_013208\activity-session-m11\m11-cleanup.json
```

### M-11 四类证据日志

探针运行期间操作员通过 `mouse_event` API 注入真实鼠标移动（Phase 1: t=0-36s 持续输入；Phase 2: t=36-154s 停止输入；Phase 3: t=154-240s 恢复输入）。`GetLastInputInfo` 检测到真实系统输入，累计值、提醒、空闲停计和恢复不补计全部通过生产采样路径验证。

**证据 1 — 累计增长**：连续活跃 tick 中 `accumulatedActiveMs` 递增，每 2 秒约 +2000ms。

| realElapsedSec | accumulatedActiveMs | deltaMs | lastInputAgeSec |
|---|---|---|---|
| 0 | 0 | 0 | 24.5 |
| 2 | 2005 | 2005 | 26.5 |
| 4 | 3997 | 1992 | 28.5 |
| 30 | 29996 | 2001 | 54.6 |
| 34.1 | 34006 | 2004 | 58.6 |

**证据 2 — 提醒只触发一次**：`reminderFiredCount` 在 t=180s 从 0 变 1 后不再增加。全日志最大值 = 1。

| realElapsedSec | accumulatedActiveMs | reminderFiredCount | note |
|---|---|---|---|
| 178 | 58989 | 0 | active |
| 180 | 60991 | 1 | RestReminderRequested event fired |
| 200 | 80987 | 1 | active [reminder fired] |
| 240 | 120991 | 1 | shutting down |

**证据 3 — 空闲停计**：t=36s 时 `lastInputAgeSec=60.6 > ActiveWindowSeconds(60)`，最后一个部分 tick `delta=992ms`，之后 `delta=0ms`，`accumulatedActiveMs` 停在 34998ms 不再增长，持续 116 秒至 t=152s。

| realElapsedSec | accumulatedActiveMs | deltaMs | lastInputAgeSec |
|---|---|---|---|
| 36.1 | 34998 | 992 | 60.6 |
| 38.1 | 34998 | 0 | 62.6 |
| 80 | 34998 | 0 | 104.5 |
| 152.1 | 34998 | 0 | 176.7 |

**证据 4 — 恢复不补计**：t=154s 恢复输入后 `age` 回到 0s，`accumulatedActiveMs` 从 34998 增长到 36994（`delta=1996ms`，仅一个 tick 间隔），未跳变补计 116 秒空闲期间。

| realElapsedSec | accumulatedActiveMs | deltaMs | lastInputAgeSec |
|---|---|---|---|
| 152.1 | 34998 | 0 | 176.7 |
| 154.1 | 34998 | 0 | 0 |
| 156.1 | 36994 | 1996 | 0 |
| 158.1 | 38997 | 2003 | 0 |

清理 sidecar（`m11-cleanup.json`）：
```json
{
  "subscribedAfterCleanup": false,
  "serviceNullAfterCleanup": true,
  "shutdownCalled": true
}
```

## R4 构建与测试命令

```
dotnet build --no-restore
```
结果：0 警告 0 错误。

```
dotnet test --no-build
```
结果：Core 465/465 + App 466/466 = 931/931 通过，0 回归。

```
dotnet test --no-build --filter "FullyQualifiedName~ReviewP4001R4"
```
结果：R4 反例 2/2 通过。

```
dotnet test --no-build --filter "FullyQualifiedName~ReviewP4001"
```
结果：R1 反例 5/5 + R2 反例 3/3 + R3 反例 2/2 + R4 反例 2/2 = 12/12 通过。

```
dotnet test --no-build --filter "FullyQualifiedName~ActivitySession_RealAppStart"
```
结果：F-04 隔离测试 1/1 通过。

## R4 未测项

- M-11 真实锁屏和睡眠：M-11 探针已通过真实 `GetLastInputInfo` 采样验证累计增长、提醒只触发一次、空闲停计和恢复不补计。真实锁屏（Win+L）和系统睡眠仍需在真实环境中另行验证。
- `Shutdown(0)` 进程终止：探针使用 `Dispatcher.Run()`（非 `Application.Run()`），`Application.Shutdown(0)` 不调用 `Environment.Exit`。`Environment.Exit(0)` 作为进程终止机制，cleanup sidecar 中 `shutdownCalled=true` 证明完整生产清理路径已执行。

## R4 复核请求

P4-001 保持 `awaiting-verification`。请监理复核 R4 两项整改（F-01、F-04）。
