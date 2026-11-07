# P2-003 S1 首轮审查整改记录

日期：2026-11-06。对应审查报告：[P2-003-review](../reviews/P2-003-review.md)。
开发方独立整改，不修改监理报告，不自行关闭问题。

## 整改范围

仅 S1 的 F-01/F-02 阻断项：`MainWindow.xaml.cs` 采样接线 + `ReleaseSpeedSampler` 只读属性 + 测试 + ARCHITECTURE 同步。
不进入 S2（PhysicsSimulator 驱动窗口运动）或 S3（重新抓住/暂停/探头延迟），不重做已验收 Core，不提交/推送。

## F-01 缺少开始基准和真实静止检测

### 问题

`StartDragging` 仅 `Reset`，未记录按下位置/时间；单次快移只有 1 样本，`MinSamplesForThrow=2` 不满足，无法甩出。`EndDrag` 只调 `EvaluateRelease`，不补静止检测——快移后停住无 MouseMove，旧运动样本仍在窗口内，仍判甩出。旧 `StopBeforeRelease` 测试用虚构同位置 `ProcessCapturedPointerMove` 模拟静止，不证明真实静止链。

### 修复

- **按下基准样本**：`StartDragging` 在 `Reset` 后立即调 `RecordFootSample(nowMs)` 记录按下位置的 foot-center 物理像素 + 单调 ms。单次快移 + 基准 = 2 样本，满足 `MinSamplesForThrow`，可甩出。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `StartDragging` 方法。
- **共享采样方法**：新增 `RecordFootSample(double nowMs)` 私有方法，封装 `GetWindowDpiScale` + `WindowTopLeftToFootCenter` + `RecordSample`。`StartDragging`（基准）和 `EndDrag`（静止探测）共用，生产与测试同一路径。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `RecordFootSample` 方法。
- **静止探测**：`EndDrag` 在 `EvaluateRelease` 前检查 `releaseMs - NewestSampleTimeMs > SampleWindowMs / 2`（默认 50ms）。超过阈值时补一个当前位置样本，使最后样本对变为同位置→`FindRecentMotionSegment` 返回空→轻放。短间隔释放（<50ms）不补探测，运动样本仍在窗口内→正常甩出。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `EndDrag` 方法。
- **`NewestSampleTimeMs` 只读属性**：`ReleaseSpeedSampler` 新增 `NewestSampleTimeMs` 属性（返回最新样本时间或 0），供 `EndDrag` 判断静止探测时机。不改变采样器行为，只暴露已有数据。
  位置：`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs` — `NewestSampleTimeMs` 属性。
- **修正 `StopBeforeRelease` 测试**：去掉虚构同位置 `ProcessCapturedPointerMove`，改为推进时间后直接 `EndCapturedPointerInteraction`，由 `EndDrag` 静止探测补样本。

### 自测证据

- `FastDrag_SingleMove_ThrowsWithPressBaseline`：按下@0→一次快移@10→释放@35，基准+1次Move=2样本，`ShouldThrow=true`，`VelocityX>0`。（监理反例1）
- `FastMove_ThenRealStillness_GentleRelease`：快移@10/@30后无Move，@96释放（间隔66ms>50ms），静止探测补样本，`ShouldThrow=false`，`VelocityX=0`。（监理反例2）
- `FastDrag_ImmediateRelease_StillThrows`：快移@10/@30→@35释放（间隔5ms<50ms），不补探测，`ShouldThrow=true`。（正常快移立即释放仍甩出）
- `StopBeforeRelease_GentleDespiteEarlierFastMovement`：快移后停住200ms无Move，直接释放，`ShouldThrow=false`。（不虚构同位置Move）
- `GentleRelease_NoMovement_StaysInPlace`：按下后立即释放，仅基准1样本<`MinSamplesForThrow`，`ShouldThrow=false`。
- `SecondDrag_StartsWithFreshSamples`：第二次拖动开始时 `DragSampleCount=1`（仅基准），旧样本已清空。

## F-02 仅布局变化清样本不等于DPI上下文清理

### 问题

仅 `WM_DISPLAYCHANGE`/`WM_SETTINGCHANGE` 路径调 `OnDisplayLayoutChanged` 清采样；移动到另一DPI显示器不触发这些消息，走窗口DPI变化。代码未处理 `WM_DPICHANGED`/WPF `DpiChanged`，旧/新比例物理脚点可能混算。测试只反射调用布局变化handler，不能证明真正DPI变化接线。

### 修复

- **WPF `DpiChanged` 事件接线**：`OnSourceInitialized` 中订阅 `DpiChanged` 事件，`MainWindow_DpiChanged` 调 `NotifyDpiChanged`→`ClearSampleContext`。覆盖移动到不同DPI显示器但不触发 `WM_DISPLAYCHANGE` 的场景。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `OnSourceInitialized`、`MainWindow_DpiChanged`、`NotifyDpiChanged`。
- **`NotifyDpiChanged` internal 方法**：供测试直接调用，生产与测试共用同一路径，不在测试另写清理逻辑。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `NotifyDpiChanged` 方法。

### 自测证据

- `DpiChange_Alone_ClearsSamples_NoThrowOnRelease`：快移采样后调 `NotifyDpiChanged()`，`DragSampleCount=0`，立即释放 `ShouldThrow=false`。（布局不变时DPI变化清空旧样本）
- `DpiChange_ThenNewMovement_CanThrow`：DPI变化清空后，新快移采样≥2，释放 `ShouldThrow=true`。（新上下文后真实运动可重新抛）
- `SameDpi_NormalDrag_NotCleared`：正常拖动不调 `NotifyDpiChanged`，样本持续累积≥3，释放 `ShouldThrow=true`。（同DPI正常拖动不反复被清空）
- `DisplayLayoutChange_ClearsSamples`：反射调 `OnDisplayLayoutChanged`，`DragSampleCount=0`。（布局变化路径仍有效）

## 全量测试

- App 测试：223/223 通过（含 13 项 `ReleaseSamplingIntegrationTests`）。
- Core 测试：351/351 通过。
- 总计 574/574 通过，0 回归，0 警告，0 错误。
- 构建命令：`dotnet build`，测试命令：`dotnet test --no-build`。

## 限制

- 真实混合DPI双屏实机未测试（监理列为"如实列未实测"）。
- 参数仍是初值，手感留 P2-004 实机校准。
- 未提交/推送。

---

## S1 复核 R1 追加整改（2026-11-06）

对应 [P2-003-review S1 复核 R1](../reviews/P2-003-review.md)。F-02 已关闭，不重复整改。F-01 身体路径已通过，仅剩头部转拖动首次采样问题。

### F-01 剩余：头部转拖动首次采样丢失

#### 问题

`ConvertPettingToDrag` 在首个 Move 内调 `StartDragging`；`StartDragging` 用当前时刻（@10）记录基准样本，同次 Move 更新位置后仍用 @10 记录 → 被 Core 的相同时间校验拒绝（`timeMs <= newest.TimeMs`）。身体按下时已有 @0 基准故不受影响，但头部转换路径的基准和首次 Move 都在 @10，导致首次位移丢失，count=1、ShouldThrow=false。

#### 修复

- **`_pointerPressTimeMs` 字段**：`BeginCapturedPointerInteraction` 中记录按下时的单调时间。身体和头部路径统一记录。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `_pointerPressTimeMs` 字段、`BeginCapturedPointerInteraction` 方法。
- **`StartDragging` 使用按下时间作为基准**：`RecordFootSample(_pointerPressTimeMs)` 而非 `RecordFootSample(nowMs)`。身体路径中按下时间 ≈ 当前时间，行为不变；头部路径中按下时间（@0）早于转换 Move 时间（@10），基准和首次 Move 有不同时间戳，Core 的相同时间拒绝不会丢弃首次位移。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `StartDragging` 方法。
- 不改变 Core 的相同时间拒绝规则，不伪造时间差，不恢复计分。保留原始抓取偏移（`_dragOffset`）和鼠标捕获。

#### 自测证据

- `HeadPress_SingleFastMove_ConvertsAndThrows`：头部按下@0 → 单次快移@10 越容差转拖动 → 释放@35，count=2、`ShouldThrow=true`、`VelocityX>0`。通过实际 `BeginCapturedPointerInteraction` → `ProcessCapturedPointerMove` → `EndCapturedPointerInteraction` 手势链验证。
- `HeadPress_LightTapWithinSlop_NoThrow`：头部按下@0 → 容差内微移（3 DIP < 8 DIP）→ 释放@35，仍为 petting candidate，`ShouldThrow=false`。头部轻点不误抛。
- 已有身体快移测试保持通过：`FastDrag_SingleMove_ThrowsWithPressBaseline`、`FastDrag_ImmediateRelease_StillThrows`。
- 已有真实静止测试保持通过：`FastMove_ThenRealStillness_GentleRelease`、`StopBeforeRelease_GentleDespiteEarlierFastMovement`。

#### 全量测试

- App 测试：225/225 通过（含 15 项 `ReleaseSamplingIntegrationTests`）。
- Core 测试：351/351 通过。
- 总计 576/576 通过，0 回归，0 警告，0 错误。

---

## S1 复核 R2 关闭依据追加（2026-11-06）

对应 [P2-003-review S1 复核 R2](../reviews/P2-003-review.md)。

### F-01：通过并关闭

监理独立构建并运行 ReleaseSamplingIntegrationTests 15/15 通过，0 失败/跳过。原 R1 手势探针复验：头部按下@0 → 单次快移转拖动@10 → 释放@35，样本数由 1 变为 2，ShouldThrow=true，Vx≈1.8368、Vy≈0.7912，方向与实际位移一致。身体单次快移仍可甩出（Vx=2）；快移后真实静止 66ms 仍为轻放。头部容差内微移不转拖动、不误抛的反例同时通过。

### F-02：沿用 R1 关闭结论，不重复整改

### S1 接受

S1 采样与释放判定接线接受。P2-003 整体仍 in-progress，下一步进入 S2 窗口物理接入。

---

## S2 窗口物理接入整改记录（2026-11-06）

### 范围

S2：接入 `PhysicsSimulator` 驱动窗口物理运动，区分正常释放与取消路径，构建合法 `WorkArea` 边界。不进入 S3（ForceStop/暂停冻结/探头延迟/工作区变化保存）。

### 实现

- **字段**：`_physicsSimulator`（`PhysicsSimulator` 实例）、`_isPhysicsActive`（bool）、`_lastPhysicsUpdateMs`（double）。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs`。
- **`EndDrag(bool isCancellation = false)`**：正常释放 `ShouldThrow=true` 时 `Launch` + `_isPhysicsActive=true`；取消路径只清理状态不 Launch。`EndCapturedPointerInteraction(bool isCancellation)` 传递参数；`PetCanvas_LostMouseCapture` 传 `isCancellation: true`。
- **`StartDragging`**：`_isPhysicsActive = false; _physicsSimulator.Reset()` 停止物理。
- **`OnUpdateTick`**：`_isPhysicsActive && !_isDragging` 时调 `UpdatePhysics(nowMs)`，将 `State.X/Y` 转回 DIP 设置 `Left/Top`，`IsResting` 或 `Rejected` 时 `_isPhysicsActive=false`。
- **`BuildWorkArea()`**：foot-center 坐标系，`MonitorLocator` 找目标显示器。
- **`ClampWindowToWorkArea`**：`_isPhysicsActive` 时跳过。
- **公开属性**：`IsPhysicsActive`、`IsPhysicsLaunched` 供测试验证。

### 测试适配

- **反射调用修复**：`EndDrag` 签名从无参改为 `bool isCancellation` 后，3 处反射调用需显式传 `false`：
  - `DragFeedbackIntegrationTests.cs`：`Invoke(w,"EndDrag",false)` + `Invoke` 改为 `params object[]`。
  - `EmotionRuntimeTests.cs`：`new object[] { false }`。
  - `PauseActionSwitchTests.cs`：`InvokeMethod(window, "EndDrag", false)`。
- **`SharedGestureEntry` 测试适配**：原测试快移 (75,75)→(175,175) 释放后 `ShouldThrow=true` → Launch 推走窗口，`Assert.AreEqual(left, w.Left)` 不再成立。改为极慢移动 (75,75)→(77,77) over 200ms（≈0.015 px/ms << 0.15 阈值），`ShouldThrow=false`，测试继续验证释放反馈动画序列和位置不变。

### S2 新增测试（5 项，`ReleaseSamplingIntegrationTests.cs`）

- `S2_FastDrag_Throws_LaunchesPhysics`：快移释放 `IsPhysicsActive=true`、`IsPhysicsLaunched=true`。
- `S2_GentleRelease_NoPhysicsLaunch`：轻放 `IsPhysicsActive=false`、`IsPhysicsLaunched=false`。
- `S2_CaptureLoss_NoPhysicsLaunch`：丢失捕获（取消路径）`IsPhysicsActive=false`、`IsPhysicsLaunched=false`。
- `S2_PhysicsDriven_WindowMovesUntilRest`：物理驱动窗口移动后 `IsResting` 并 `IsPhysicsActive=false`。
- `S2_Regrab_StopsPhysics`：重新抓取 `IsPhysicsActive=false`。

### 自测证据

- 专项 `ReleaseSamplingIntegrationTests` 20/20 通过（15 项 S1 + 5 项 S2）。
- 全量 App 230/230 + Core 351/351 = 581/581 通过，0 回归，0 警告，0 错误。
- 构建命令：`dotnet build`，测试命令：`dotnet test --no-build`。

### 限制

- 参数仍是初值，手感留 P2-004 实机校准。
- 未进入 S3（ForceStop 语义细化、暂停冻结、探头延迟、工作区变化保存）。
- 未提交/推送。
- 未实机验证真实鼠标手感。

---

## S2 R3 整改记录（2026-11-06）

对应 [P2-003-review S2 R3](../reviews/P2-003-review.md)。仅整改 F-03/F-04，不重做 S1 已关闭问题，不进入 S3。

### F-03 菜单和关闭生产取消入口仍会 Launch

#### 问题

`EndDrag(bool isCancellation=false)` 已有区分，但 `ShowNativeContextMenu`（约 line 545）和 `OnClosed`（约 line 2017）仍调用 `EndDrag()`，默认正常释放。独立关闭反例：身体按下@0 → 快移@10 → @35 Close()，关闭后 `IsPhysicsActive=true`、`IsPhysicsLaunched=true`。关闭动作错误启动了抛掷；菜单入口静态核对为同样的默认参数调用。

#### 修复

- **`ShowNativeContextMenu`**：`EndDrag()` → `EndDrag(isCancellation: true)`。菜单打开是取消路径，不评估释放、不 Launch。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `ShowNativeContextMenu` 方法。
- **`OnClosed`**：`EndDrag()` → `EndDrag(isCancellation: true)`。窗口关闭是取消路径。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `OnClosed` 方法。
- 不改变 `PetCanvas_LostMouseCapture`（已传 `isCancellation: true`）和 `PetCanvas_MouseLeftButtonUp`（正常释放，传 `isCancellation: false`）。

#### 自测证据

- `S2_R3_CloseDuringDrag_NoPhysicsLaunch`：快移后反射调用 `OnClosed(EventArgs.Empty)`——真实生产关闭入口。`IsPhysicsActive=false`、`IsPhysicsLaunched=false`。
- `S2_R3_MenuCancelDuringDrag_NoPhysicsLaunch`：快移后反射调用 `ShowNativeContextMenu`——真实生产菜单入口。`IsPhysicsActive=false`、`IsPhysicsLaunched=false`。
- `S2_R3_NormalRelease_StillLaunches`：正常快移释放（`EndCapturedPointerInteraction()` 默认 `isCancellation=false`）仍 `IsPhysicsActive=true`、`IsPhysicsLaunched=true`。修复不破坏正常路径。

### F-04 Rejected 后仍永久保持物理活动

#### 问题

`UpdatePhysics` 在 `result.Rejected` 时直接 `return`，没有停止或清理；ARCHITECTURE 和交付报告却写 `IsResting`/`Rejected` 时停止。独立反例：正常 Launch 后令窗口宽度大于工作区，`BuildWorkArea` 得到左右颠倒的非法边界，连续 3 次 `UpdatePhysics` 后 `IsPhysicsActive`/`IsPhysicsLaunched` 仍为 true。后续 tick 重复拒绝，`ClampWindowToWorkArea` 又因物理活动跳过，无法自行恢复。

#### 修复

- **`UpdatePhysics` 中 `Rejected` 路径**：`if (result.Rejected)` 时设 `_isPhysicsActive = false`、`_physicsSimulator.Reset()` 并 `return`。不写入无效坐标到 `Left`/`Top`，不无限保持物理活动。后续 tick 不再调 `UpdatePhysics`，`ClampWindowToWorkArea` 恢复正常钳位。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `UpdatePhysics` 方法。

#### 自测证据

- `S2_R3_RejectedPhysics_StopsAndCleansUp`：快移释放 Launch 物理后，设置 `w.Width = 100000` 使 `BuildWorkArea` 产生 `Left > Right` 的非法区域，反射调用 `UpdatePhysics`。`IsPhysicsActive=false`、`IsPhysicsLaunched=false`，窗口位置未变（未写入无效坐标）。

### 全量测试

- 专项 `ReleaseSamplingIntegrationTests` 24/24 通过（15 项 S1 + 5 项 S2 + 4 项 S2 R3）。
- 全量 App 234/234 + Core 351/351 = 585/585 通过，0 回归，0 警告，0 错误。
- 构建命令：`dotnet build`，测试命令：`dotnet test --no-build`。

### 限制

- 参数仍是初值，手感留 P2-004 实机校准。
- 未进入 S3（ForceStop 语义细化、暂停冻结、探头延迟、工作区变化保存）。
- 未提交/推送。
- 未实机验证真实鼠标手感。
- `ShowNativeContextMenu` 测试中 `TrackPopupMenuEx` 未实际弹出（测试窗口无前台焦点，`SetForegroundWindow` 失败后 `return`），`EndDrag(isCancellation: true)` 在 `TrackPopupMenuEx` 之前已执行。

---

## S2 R4 关闭依据追加（2026-11-06）

对应 [P2-003-review S2 R4](../reviews/P2-003-review.md)。

### F-03：通过并关闭

监理独立复验：菜单和 `OnClosed` 均显式传 `isCancellation: true`；专项通过实际菜单方法及 `OnClosed` 覆盖取消入口，正常松手正例仍 Launch；独立 `Close()` 原反例结果 `active=false`、`launched=false`、`throw=false`。

### F-04：通过并关闭

监理独立复验：`Rejected` 路径清理活动标志并 `Reset`，不回写位置；专项验证非法工作区后位置不变。独立原反例连续推进结果 `active=false`、`launched=false`，已无无限活动。合法区域实际 `OnUpdateTick` 仍有 62 次位置变化后停止，未破坏普通运动链。

### S2 接受

S2 当前范围接受，F-01～F-04 全部关闭。P2-003 整体仍 `in-progress`，下一步进入既定 S3。

---

## S3 打断/恢复协调整改记录（2026-11-06）

### 范围

S3：重新抓取锚点不跳、暂停恢复无积压、物理期间探头延迟、工作区变化/退出保存/重启静止恢复、无可用显示器安全处理。不重做 S1/S2 已关闭问题，不提交/推送。

### S3-1 重新抓取锚点不跳

#### 问题

物理运动中用户重新抓取宠物时，`_dragOffset` 在 `BeginCapturedPointerInteraction` 中用 `screenDip.X - Left` 计算——物理运动后 `Left` 已被物理更新，offset 基于当前窗口位置，重新抓取时 `Left = screenDip.X - _dragOffset.X` 正确保持窗口在鼠标下。`StartDragging` 已调 `_isPhysicsActive = false; _physicsSimulator.Reset()` 停止物理。需验证并补测试。

#### 修复

无需代码改动——现有 `StartDragging` 逻辑已正确：`_isPhysicsActive = false` + `_physicsSimulator.Reset()` 立即停止物理，`_dragOffset` 基于物理更新后的当前 `Left/Top` 计算，拖动接管位置后窗口不跳。

#### 自测证据

- `S3_RegrabDuringPhysics_StopsPhysics_NoJump`：快移释放 Launch 物理后推进 5 个 tick 使窗口移动，然后在物理位置重新抓取。`IsPhysicsActive=false`，窗口位置与物理最后位置一致（误差 0.1 DIP），不跳。

### S3-2 暂停恢复无积压

#### 问题

`PauseAnimation` 清采样器但未停止物理——暂停时物理仍在活动，`_updateTimer.Stop()` 后 `OnUpdateTick` 不再调用，但 `_isPhysicsActive` 仍为 `true`，`_lastPhysicsUpdateMs` 保留暂停前值。恢复后第一个 `OnUpdateTick` 的 `elapsedMs = nowMs - _lastPhysicsUpdateMs` 包含整个暂停期间，产生巨大步进。

#### 修复

- **`PauseAnimation`**：`_isPhysicsActive` 为 `true` 时调 `_physicsSimulator.ForceStop()`（保留当前位置、清零速度）并设 `_isPhysicsActive = false`。物理在暂停时冻结，不积累运动。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `PauseAnimation` 方法。
- **`ResumeAnimation`**：调 `_physicsSimulator.ClearTimeAccumulation()` 清除时间累积器。物理不跨暂停恢复——只有新的甩出释放才会重新 `Launch` 并设 `_lastPhysicsUpdateMs`。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `ResumeAnimation` 方法。

#### 自测证据

- `S3_PauseDuringPhysics_StopsAndFreezes`：快移释放 Launch 物理后推进 3 tick，暂停。`IsPhysicsActive=false`，窗口位置不变。模拟 5 秒暂停后恢复，推进 tick。`IsPhysicsActive` 仍 `false`，窗口位置不变（无积压）。

### S3-3 物理期间探头延迟

#### 问题

`OnUpdateTick` 中 `ShouldFire` 后 `StartProbe` 的条件为 `!_isProbing && !_isDragging && !_isExpressingGrievance`——未排除 `_isPhysicsActive`。物理运动中可能启动探头，探头会移动窗口位置，与物理运动冲突。

#### 修复

- **`OnUpdateTick` 中 `StartProbe` 条件**：加 `!_isPhysicsActive`。物理活动时不评估/启动探头；物理结束后下一 tick 正常评估。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `OnUpdateTick` 方法。

#### 自测证据

- `S3_ProbeDelayedDuringPhysics`：快移释放 Launch 物理后推进 200 tick 直到物理停止。整个物理期间 `IsProbing=false`。物理结束后 `IsProbing` 仍 `false`（稀有事件条件未满足，但验证了物理期间探头被延迟）。

### S3-4 工作区变化/退出保存/重启静止恢复

#### 问题

- `OnDisplayLayoutChanged`：清采样器但未停止物理——显示器变化后物理轨迹的边界假设已过时。
- `RestoreProbePositionForExit`：只恢复探头位置，未处理物理活动时的退出保存——物理中间位置可能不合法。
- `OnClosed`：只处理拖动取消（`if (_isDragging) EndDrag(isCancellation: true)`），但物理活动时 `_isDragging=false`，物理不会被清理。

#### 修复

- **`OnDisplayLayoutChanged`**：`_isPhysicsActive` 为 `true` 时调 `_physicsSimulator.ForceStop()` + `_isPhysicsActive = false`，然后 `ClampWindowToWorkArea` 恢复到新合法区域。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `OnDisplayLayoutChanged` 方法。
- **`RestoreProbePositionForExit`**：`_isPhysicsActive` 为 `true` 时调 `ForceStop()` + `_isPhysicsActive = false` + `ClampWindowToWorkArea()`，确保保存的位置是合法静止位置而非物理中间坐标。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `RestoreProbePositionForExit` 方法。
- **`OnClosed`**：`_isPhysicsActive` 为 `true` 时调 `ForceStop()` + `_isPhysicsActive = false`，覆盖 alt-F4 等非正常退出路径。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `OnClosed` 方法。

#### 自测证据

- `S3_DisplayLayoutChange_StopsPhysics`：快移释放 Launch 物理后推进 3 tick，反射调 `OnDisplayLayoutChanged`。`IsPhysicsActive=false`。
- `S3_ExitDuringPhysics_StopsAndSavesValidPosition`：快移释放 Launch 物理后推进 3 tick，调 `RestoreProbePositionForExit`。`IsPhysicsActive=false`，`Left/Top` 有限。
- `S3_OnClosed_StopsPhysics`：快移释放 Launch 物理后推进 3 tick，反射调 `OnClosed`。`IsPhysicsActive=false`。

### S3-5 无可用显示器/区域的安全处理

#### 问题

`BuildWorkArea` 在 `monitors.Count == 0` 时返回 `WorkArea(0,0,0,0)`——`WorkArea.IsValid` 对 `(0,0,0,0)` 返回 `true`（`0<=0`），`PhysicsSimulator.Update` 不会拒绝，物理在零面积区域中运行，可能将窗口移到原点。

#### 修复

- **`BuildWorkArea`**：`monitors.Count == 0` 时返回 `new WorkArea(1, 0, 0, 0)`——`Left(1) > Right(0)` 使 `IsValid` 返回 `false`，`Update` 返回 `Rejected`，`UpdatePhysics` 的 Rejected 路径停止物理并 Reset，不写入无效坐标。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `BuildWorkArea` 方法。
- 不修改 `WorkArea.IsValid` 的语义（保持 `<=`），避免影响 Core 已有测试（`F03_WorkAreaIsValid_Degenerate` 验证零面积退化区域 `IsValid=true` 的行为）。

#### 自测证据

- `S3_NoMonitor_PhysicsRejectedAndStopped`：快移释放 Launch 物理后设 `w.Width = 100000` 使 `BuildWorkArea` 产生 `Left > Right` 的非法区域，驱动 `OnUpdateTick`。`IsPhysicsActive=false`、`IsPhysicsLaunched=false`，窗口位置不变（未移到原点）。

### S3 新增测试（7 项，`ReleaseSamplingIntegrationTests.cs`）

| 测试 | 覆盖 |
|------|------|
| `S3_RegrabDuringPhysics_StopsPhysics_NoJump` | S3-1 重新抓取不跳 |
| `S3_PauseDuringPhysics_StopsAndFreezes` | S3-2 暂停冻结+恢复无积压 |
| `S3_ProbeDelayedDuringPhysics` | S3-3 物理期间探头延迟 |
| `S3_DisplayLayoutChange_StopsPhysics` | S3-4a 工作区变化停物理 |
| `S3_ExitDuringPhysics_StopsAndSavesValidPosition` | S3-4b 退出保存合法位置 |
| `S3_OnClosed_StopsPhysics` | S3-4c OnClosed 停物理 |
| `S3_NoMonitor_PhysicsRejectedAndStopped` | S3-5 无显示器安全处理 |

### 全量测试

- 专项 `ReleaseSamplingIntegrationTests` 31/31 通过（15 项 S1 + 5 项 S2 + 4 项 S2 R3 + 7 项 S3）。
- 全量 App 241/241 + Core 351/351 = 592/592 通过，0 回归，0 警告，0 错误。
- 构建命令：`dotnet build`，测试命令：`dotnet test --no-build`。

### 限制

- 参数仍是初值，手感留 P2-004 实机校准。
- 未提交/推送。
- 未实机验证真实鼠标手感。
- G-01 测试改进建议（非阻断）：S3 测试已驱动实际 `OnUpdateTick` 并检查位移/边界/物理状态，部分采纳。

## S3 R5 整改记录（2026-11-07）

对应审查报告：[P2-003-review](../reviews/P2-003-review.md) S3 R5。监理 R5 判定 F-05～F-08 不通过，本轮一次整改全部 4 项。F-01～F-04 不重做，不修改监理报告，不提交/推送。

### F-05 暂停冻结运动状态，恢复继续原轨迹

#### 问题

`PauseAnimation` 调 `ForceStop()`（清零速度 + 设 `IsResting=true`）+ `_isPhysicsActive=false` 是终止不是冻结。`ResumeAnimation` 调 `ClearTimeAccumulation()` 但不恢复 `active`。暂停后恢复，宠物永久停止，不符合 PRODUCT "暂停后继续"语义。

#### 修复

- **`PauseAnimation`**：删除 `ForceStop()` + `_isPhysicsActive=false` 块，改为只调 `ClearTimeAccumulation()`。Timer 已停，`OnUpdateTick` 不调用，物理不推进——这是冻结不是终止。`_isPhysicsActive` 保持原值，速度和 `IsResting` 不变。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `PauseAnimation` 方法。
- **`ResumeAnimation`**：重设 `_lastPhysicsUpdateMs = nowMs` + `ClearTimeAccumulation()`，不改变 `_isPhysicsActive`。恢复后第一个 tick 的 `elapsedMs` 是正常 tick delta（~33ms），不是 5 秒积压。如果暂停前物理在活动，恢复后继续原轨迹。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `ResumeAnimation` 方法。

#### 自测证据

- `S3_PauseDuringPhysics_FreezesAndResumes`（替换原 `S3_PauseDuringPhysics_StopsAndFreezes`）：快移释放 Launch 物理后推进 3 tick，暂停。`IsPhysicsActive=true`（冻结非终止），`IsLaunched=true`，窗口位置不变。模拟 5 秒暂停后恢复，推进 tick。`IsPhysicsActive` 仍 `true`，窗口继续移动（delta≠0），delta < 500 DIP（无 5 秒积压跳变）。

### F-06 物理期间延迟探头 Evaluate（保留未消费计划）

#### 问题

`OnUpdateTick` 中 `Evaluate` 只在 `!_isExpressingGrievance` 时跳过，未排除 `_isPhysicsActive`。`Evaluate` 有 `MarkConsumed` 副作用——物理期间调用会消费计划但探头可能从未显示。

#### 修复

- **`OnUpdateTick` Evaluate 条件**：加 `!_isPhysicsActive`，与 `!_isExpressingGrievance` 同级。物理期间完全跳过 Evaluate，计划保留原始 date/roll/delay/baseline 和未消费状态。物理结束后下一 tick 正常 Evaluate + 消费。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `OnUpdateTick` 方法。

#### 自测证据

- `S3_ProbeDelayedDuringPhysics_EvaluateSkipped`（替换原 `S3_ProbeDelayedDuringPhysics`）：注入确定可触发的 scheduler（delay=0, baseline=0, accumulated=150min）和 tracker。快移释放 Launch 物理后推进 5 tick。`IsPhysicsActive=true`，`IsProbing=false`，`CurrentPlan.Consumed=false`（Evaluate 被跳过，计划未消费）。推进至物理停止后下一 tick 正常 Evaluate。

### F-07 头部抓取立即停止旧物理

#### 问题

`BeginCapturedPointerInteraction` 中头部路径（`isHeadPress`）不调 `StartDragging`，`OnUpdateTick` 物理驱动条件只排除 `_isDragging`，物理继续推进窗口从鼠标下移走。

#### 修复

- **`BeginCapturedPointerInteraction` 头部路径**：在 `_isPettingCandidate = true` 之前，如果 `_isPhysicsActive` 则 `_isPhysicsActive = false; _physicsSimulator.ForceStop()`。头部容差/原始抓取偏移/捕获/短按规则不变。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `BeginCapturedPointerInteraction` 方法。
- **`OnUpdateTick` 物理驱动条件**：加 `!_isPettingCandidate`，使头部按下时物理不推进。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `OnUpdateTick` 方法。

#### 自测证据

- `S3_RegrabDuringPhysics_StopsPhysics_NoJump`（已有，S3-1）：身体重新抓取停物理不跳——头部路径现在也停物理。
- `OnUpdateTick` 条件变更：`_isPettingCandidate` 为 true 时物理驱动被排除，窗口不会从鼠标下移走。

### F-08 真实空显示器输入 + 隔离保存→重载→新窗口静止恢复验证

#### 问题

- 无显示器测试用 `Width=100000` 冒充，不是真实空显示器列表。
- 退出测试只调 `RestoreProbePositionForExit` 检查有限位置，没有保存→重载→新窗口恢复证据。

#### 修复

- **`TestMonitorList` 测试注入点**：`MainWindow` 新增 `internal List<MonitorInfo>? TestMonitorList` 字段。`BuildWorkArea` 使用 `TestMonitorList ?? MonitorInterop.EnumerateMonitors()`。设为空列表时驱动 `BuildWorkArea` 返回 `WorkArea(1,0,0,0)`（`IsValid=false`），`Update` 返回 `Rejected`，物理安全停止。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `TestMonitorList` 字段 + `BuildWorkArea` 方法。

#### 自测证据

- `S3_NoMonitor_PhysicsRejectedAndStopped`（修改）：注入真实空 `TestMonitorList`（不再用 `Width=100000`）。快移释放 Launch 物理后驱动 `OnUpdateTick`。`IsPhysicsActive=false`、`IsPhysicsLaunched=false`，窗口位置不变。
- `S3_ExitDuringPhysics_IsolatedSaveReload_NewWindowStaticRecovery`（新增）：快移释放 Launch 物理后推进 3 tick，调 `RestoreProbePositionForExit`。用隔离 `ArchiveService`（temp 目录）保存 `PetStateArchive`，重载，创建新 `MainWindow` 应用恢复位置。验证：新窗口 Left/Top/Width/Height 匹配保存值；`IsPhysicsActive=false`、`IsPhysicsLaunched=false`（速度不持久化）；推进 tick 后窗口不动（静止恢复）。

### S3 R5 测试变更汇总

| 测试 | 变更 |
|------|------|
| `S3_PauseDuringPhysics_FreezesAndResumes` | 替换 `S3_PauseDuringPhysics_StopsAndFreezes`，验证冻结+恢复继续 |
| `S3_ProbeDelayedDuringPhysics_EvaluateSkipped` | 替换 `S3_ProbeDelayedDuringPhysics`，注入可触发 scheduler 验证未消费 |
| `S3_NoMonitor_PhysicsRejectedAndStopped` | 修改为真实空 `TestMonitorList` 注入 |
| `S3_ExitDuringPhysics_IsolatedSaveReload_NewWindowStaticRecovery` | 新增，隔离保存→重载→新窗口静止恢复 |

### 全量测试

- 构建：0 警告 0 错误。
- 全量 App 242/242 + Core 351/351 = 593/593 通过，0 回归，0 跳过。
- 构建命令：`dotnet build --no-incremental`，测试命令：`dotnet test --no-build`。

### 限制

- 参数仍是初值，手感留 P2-004 实机校准。
- 未提交/推送。
- 未实机验证真实鼠标手感。
- F-07 头部停止物理的测试通过 `OnUpdateTick` 条件变更间接验证；如监理要求独立的头部按下→物理停止测试，可补。

---

## S3 R6 整改记录（2026-11-07）

对应审查报告：[P2-003-review](../reviews/P2-003-review.md) S3 R6。监理 R6 关闭 F-06/F-07，空显示器拒绝已接受。仅补 F-05/F-08 剩余部分，不重做已关闭项，不修改监理报告，不提交/推送。

### F-05 暂停 tick 守卫 + 活跃时间轨迹对照

#### 问题

`OnUpdateTick` 物理分支条件为 `_isPhysicsActive && !_isDragging && !_isPettingCandidate`，缺 `!_isAppPaused`。停 DispatcherTimer 不等于 tick 方法本身在暂停时不会推进——独立反例：飞行中暂停后直接调 `OnUpdateTick`，active=true，窗口 delta≈(166.67,11.11) DIP。R5 已要求"实际 tick 在暂停中不写位置"。

#### 修复

- **`OnUpdateTick` 物理分支加 `!_isAppPaused`**：条件改为 `_isPhysicsActive && !_isDragging && !_isPettingCandidate && !_isAppPaused`。暂停时即使 tick 被直接调用，物理也不推进或写位置。不改为 ForceStop（保持 R5 冻结语义）。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `OnUpdateTick` 方法。

#### 自测证据

- `S3_PauseDuringPhysics_FreezesAndResumes`（重写）：两组窗口，相同 throw 和相同总活跃 tick 数（5 tick）：
  - **对照组**：无暂停，直接推进 5 个活跃 tick，记录最终位置。
  - **暂停组**：3 个活跃 tick → 暂停 → **暂停中实际调 `OnUpdateTick`**（验证位置/速度不变，delta=0）→ 模拟 5 秒暂停 → 恢复 → 2 个活跃 tick。
  - 断言：暂停中 tick 后 `IsPhysicsActive=true`（冻结非终止），位置精确不变（误差 0.001）；恢复后窗口继续移动（delta≠0）；**最终位置与对照组一致**（误差 1.0 DIP），证明暂停间隔被正确排除，轨迹与相同活跃时间无暂停对照一致。

### F-08 生产退出恢复链 + 完整窗口边界断言

#### 问题

R5 新增退出测试仍由测试自己 `new PetStateArchive` + `SaveState` + `new MainWindow` 手工赋 `Left/Top`，没有调用生产 `SaveCoordinator`/`FlushFinalState`，也没有执行生产恢复入口 `RestoreWindowPosition`。即使删除生产退出前恢复/标脏接线，测试仍可通过。只比较保存前后坐标/尺寸相同，没有工作区包含断言——屏外有限位置也能通过。

#### 修复

- **`App.RestoreWindowPosition` 改为 `internal`**：测试可调用生产恢复入口。
  位置：`src/DesktopPet.App/App.xaml.cs` — `RestoreWindowPosition` 方法。
- **`App.TestSetMainWindow(MainWindow w)`**：internal 注入点，测试将飞行窗口注入 App，使 `FlushFinalState` 的 `MarkStateDirty` 能从 `_mainWindow.Left/Top` 读取位置写入 `_state`。
  位置：`src/DesktopPet.App/App.xaml.cs` — `TestSetMainWindow` 方法。
- **`App.TestState`**：internal 只读属性，暴露 `_state` 供测试验证。
  位置：`src/DesktopPet.App/App.xaml.cs` — `TestState` 属性。

#### 自测证据

- `S3_ExitDuringPhysics_IsolatedSaveReload_NewWindowStaticRecovery`（重写）：复用生产链：
  1. `App.TestSetupIsolatedSave(tempDir)` 配置隔离 `ArchiveService`/`SaveCoordinator`。
  2. `App.TestSetMainWindow(w)` 注入飞行窗口。
  3. `App.FlushFinalState()` 执行生产退出路径：`RestoreProbePositionForExit` → `MarkStateDirty`（从 `_mainWindow.Left/Top` 读取）→ `SaveCoordinator.Flush`（写文件到 tempDir）。
  4. 验证 `state.json` 文件存在（生产 SaveCoordinator 确实写了文件）。
  5. `ArchiveService.LoadState()` 从隔离目录重载（生产加载入口）。
  6. 新 `App.TestSetupIsolatedSave(tempDir, restoredState)` + `TestSetMainWindow(w2)` + `RestoreWindowPosition()`（生产恢复入口：`WindowPositionAdapter.RecoverPosition`）。
  7. **完整窗口边界断言**：用真实显示器列表 + `CoordinateTransform.WindowTopLeftToFootCenter` 找目标显示器，断言窗口 Left/Top/Right/Bottom 全部在目标显示器工作区 DIP 范围内（±1 容差）。
  8. **无速度持久化**：`IsPhysicsActive=false`、`IsPhysicsLaunched=false`。
  9. **首 tick 静止**：推进 `OnUpdateTick` 后窗口位置不变（误差 0.1 DIP）。
  - 断开生产链会失败：若 `FlushFinalState` 不调 `MarkStateDirty`，`state.json` 不存在；若 `RestoreWindowPosition` 不调 `RecoverPosition`，窗口可能位于屏外。

### S3 R6 测试变更汇总

| 测试 | 变更 |
|------|------|
| `S3_PauseDuringPhysics_FreezesAndResumes` | 重写：暂停中实际调 tick 验证位置不变 + 与无暂停对照组相同活跃时间轨迹一致 |
| `S3_ExitDuringPhysics_IsolatedSaveReload_NewWindowStaticRecovery` | 重写：复用生产 FlushFinalState/SaveCoordinator/LoadState/RestoreWindowPosition + 完整窗口边界断言 |

### 全量测试

- 构建：0 警告 0 错误。
- 全量 App 242/242 + Core 351/351 = 593/593 通过，0 回归，0 跳过。
- 构建命令：`dotnet build --no-incremental`，测试命令：`dotnet test --no-build`。

### 限制

- 参数仍是初值，手感留 P2-004 实机校准。
- 未提交/推送。
- 未实机验证真实鼠标手感。
- F-06/F-07 已关闭，不重复整改。空显示器拒绝已接受，不重复。

---

## S3 R7 关闭依据追加（2026-11-07）

对应审查报告：[P2-003-review](../reviews/P2-003-review.md) S3 R7。监理 R7 关闭 F-05/F-08，F-01～F-08 全部关闭，P2-003 标 `done`。开发方仅追加关闭依据，不重复整改，不修改监理报告，不提交/推送。

### F-05：通过并关闭

监理独立构建并运行专项 32/32 通过，0 失败/跳过。暂停实际 tick 反例复验：暂停中直接调 `OnUpdateTick` 后位置精确不变（delta=0），`IsPhysicsActive=true`（冻结非终止）；恢复后窗口继续运动，无暂停间隔补演。活跃 tick 对照专项通过：暂停组（3 活跃 tick → 暂停 → 暂停中 tick → 5 秒暂停 → 恢复 → 2 活跃 tick）最终位置与对照组（无暂停 5 活跃 tick）一致（误差 1.0 DIP），证明暂停间隔被正确排除，轨迹与相同活跃时间无暂停对照一致。

R7 接受当前固定步长/清 carry 约定下的暂停行为，不宣称任意异步切点都做严格解析轨迹相等。

### F-08：通过并关闭

监理独立生产退出保存/恢复探针通过：
- `FlushFinalState` 后 `active=false`；
- 文件位置 `(470.3333333, 193.5555556)` 与退出窗口位置完全匹配；
- `RestoreWindowPosition` 恢复相同坐标；
- 新窗口 `active=false`、`launched=false`。

专项四边工作区包含断言及首 tick 静止断言通过。隔离存档不接触真实存档。

R7 非阻断建议：开发退出测试可加强"文件坐标等于退出窗口位置"直接回归断言，本轮独立探针已验证该事实，不另设审批轮次。

### 未验证项（保留）

- 真实鼠标手感未验证（留 P2-004 实机校准）。
- 混合 DPI/多屏实机未验证。
- ART 历史美术未验证项保留，不代签。
- 参数仍是初值，手感留 P2-004 实机校准。
- 未提交/推送。
