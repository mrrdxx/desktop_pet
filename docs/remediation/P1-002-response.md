# P1-002 整改记录

日期：2026-10-23（Asia/Shanghai）。角色：开发方。对应检查：[P1-002 中间检查](../reviews/P1-002-review.md)。

## F-01：净位移平均速度会抵消往返甩动

### 问题

`MouseSampler.ComputeSpeed` 以 newest-oldest 的欧几里得净位移除以整个窗口时间，作为唯一速度输出。监理固定输入反例：

1. **快速往返抵消**：序列 `(0,0,0ms)→(200,0,100ms)→(0,0,200ms)`，两段各 2000 px/s，返回后输出 **0 且 IsValid=True**。相反方向运动抵消了净位移。
2. **静止后短促快划被稀释**：9 个静止样本 t=0…264ms，t=297ms 突然移动 100px（最新段约 3030 px/s），窗口输出仅约 **336.7**，低于产品建议阈值 1200。

### 修复

- **位置**：`src/DesktopPet.Core/Input/MouseSampler.cs`
- **新增近期分段速度信号**：`MouseSampleResult` 新增 `RecentSegmentSpeedPxPerSec` 和 `EquivalentRecentSegmentSpeedPxPerSec`，取最近 `RecentSegmentCount`（默认 3，≈100ms @30Hz）个连续分段中的最大单段速率。
  - 每段 = 两个连续样本间的位移/时间。
  - 取 max 而非 avg：短促快划只需一个快段即可被捕获，不被旧静止段稀释。
  - 不因方向反转抵消：每段独立计算欧几里得距离。
- **保留原净位移速度**：`SpeedPxPerSec`/`EquivalentSpeedPxPerSec` 仍表示窗口内的移动趋势（净位移/总时间），供后续行为系统判断"鼠标是否在持续靠近"。
- **甩动检测契约**：后续甩动检测器消费 `EquivalentRecentSegmentSpeedPxPerSec` 作为主信号（R-01 阈值 1200 等效像素/秒），同时结合边缘距离判断。
- **新增配置**：`MouseSamplerConfig.RecentSegmentCount`（默认 3），可运行时调整。

### 自测

- 新增 6 个测试 + 修改 1 个既有测试（总计采样器测试 23 个）：
  - `FastRoundTrip_NetSpeedCancels_RecentSegmentSpeedPreserved`：快速往返 (0,0)→(200,0)→(0,0)，验证净速度=0 但近期分段速度=2000。
  - `StationaryThenFastFlick_RecentSegmentNotDiluted`：9 个静止样本后 100px/33ms 快划，验证净速度<500 但近期分段速度>2500。
  - `SlowJitter_RecentSegmentBelowThreshold`：±5px 抖动，验证近期分段速度<200。
  - `SlowRoundTrip_RecentSegmentBelowThreshold`：20px/150ms 慢速往返，验证近期分段速度<200。
  - `RecentSegmentCount1_OnlyLastSegment`：RecentSegmentCount=1 只看最后一段。
  - `RecentSegmentSpeed_DPI_EquivalentAt150Percent`：150% DPI 相同视觉距离相同等效近期分段速度。
  - `FirstSample_ReturnsInvalid`（修改既有用例）：补充验证 `EquivalentRecentSegmentSpeedPxPerSec`=0。
- 保留全部已有测试：采样中断过滤（超限清窗、恰等于限保留）、瞬移过滤（超限清窗、恰等于限保留、150% 等效）、滑动窗口淘汰、DPI 等效速度、清空重置、参数可配置、边界情况。
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 88 + App 44 = **132/132**，0 失败，0 跳过。

### 限制

- 采样器已接入 App 层 `GetCursorPos`（30Hz），DPI 已接入窗口实际显示器；真实鼠标实机手感待验收。
- `RecentSegmentCount` 默认 3（≈100ms @30Hz）是初始建议值，实际手感可能需要调整；参数可配置。
- 慢速往返测试间隔 150ms（受 MaxSampleGapMs=200 限制，500ms 间隔会触发中断清窗）。
- **已整改，待 P1-002 交付时统一复核。**

## 测试数量更正

- 中间检查指出开发报告称 18 项采样器测试，实际为 17 项。本轮修正后新增 6 项 + 修改 1 既有用例，总计 **23 项**采样器测试（非此前错误记录的"新增 7/总 24"）。
- 全部测试：Core 88 + App 44 = **132/132**。

## App 接线与完整触发链

### 范围

将 Core `MouseSampler`、`HoverDetector`、`SwipeDetector` 接入 App 层 `MainWindow`，在现有 30FPS `DispatcherTimer` 的 `OnUpdateTick` 中完成完整触发链：DPI 接入 → 采样 → 悬停检测 → 甩动检测 → 动作触发。拖动/菜单/暂停时禁用所有触发。

### 实现

- **位置**：`src/DesktopPet.App/MainWindow.xaml.cs`
- **DPI 接入**：`OnUpdateTick` 每个 tick 通过 `MonitorInterop.GetWindowDpiScale(_hwnd)` 读取窗口实际 DPI，设置 `_mouseSampler.Config.DpiScale` 和 `_swipeDetector.Config.DpiScale`。DPI 变化（差值 > 0.001）时 `_mouseSampler.Clear()` 清空旧采样，避免混合不同 DPI 上下文的样本。
- **采样**：`OnUpdateTick` 在非拖动/非菜单/非暂停状态下调用 `GetCursorPos` 获取物理像素坐标，用 `GetElapsedMs()` 时间戳构造 `MouseSample` 喂给 `_mouseSampler`。
- **悬停检测**：`HoverDetector.Update` 接收鼠标位置 + 宠物实体矩形（`GetPetRectPhysicalPx` 将 WPF DIP 转 physical px），1.2 秒驻留触发一次 `PlayAction("curious")`，离开 0.5 秒后重新允许。
- **甩动检测**：`SwipeDetector.Update` 接收 `MouseSampleResult` + 宠物实体矩形，`EquivalentRecentSegmentSpeedPxPerSec > 1200` AND 边缘距离 < 80 等效像素时触发 `PlayAction("dodge")`，冷却 3 秒。速度和距离信号来自同一样本的同一时刻，防止旧速度峰值与最新近距离错误组合。
- **禁用**：拖动/菜单/暂停时跳过采样和检测器更新，并调用 `HoverDetector.Reset()` 和 `SwipeDetector.Reset()` 清空状态。
- **Clear 时机**（按 ARCHITECTURE §5 和中间检查要求"拖动/菜单/暂停结束后清理历史"）：
  - 拖动开始（`PetCanvas_MouseLeftButtonDown`）
  - 拖动结束（`EndDrag`）
  - 菜单打开（`ShowNativeContextMenu`）
  - 菜单关闭（`ShowNativeContextMenu` finally 块）
  - 暂停（`PauseAnimation`）
  - 恢复（`ResumeAnimation`）
  - DPI 变化（`OnUpdateTick` 中检测到 DPI 改变）
- **公开属性**：`MouseSampler` 属性供测试和后续检测器访问。

### 新增 Core 模块

- **`src/DesktopPet.Core/Input/HoverDetector.cs`**：纯 Core 逻辑悬停检测器。`HoverDetectorConfig`（DwellMs=1200, LeaveResetMs=500, HoverMarginPx=0）。`HoverTrigger` 枚举（None/Triggered）。`PetRectPx` 结构体（物理像素矩形，Right/Bottom/CenterX/CenterY 属性）。
- **`src/DesktopPet.Core/Input/SwipeDetector.cs`**：纯 Core 逻辑甩动检测器。`SwipeDetectorConfig`（SpeedThreshold=1200, EdgeDistanceThreshold=80, CooldownMs=3000, DpiScale=1.0）。`SwipeTrigger` 枚举（None/Triggered）。速度和距离在同一次 `Update` 中评估，确保来自同一时刻。

### 新增测试

- **`tests/DesktopPet.Core.Tests/HoverDetectorTests.cs`**（7 项）：
  - `Hover_DwellReached_TriggersOnce`：1.2 秒驻留触发，继续驻留不重复触发。
  - `Hover_LeaveThenReturn_AfterReset_CanTriggerAgain`：离开 500ms 后返回可重新触发。
  - `Hover_QuickLeaveAndReturn_NoReset`：短暂离开（<500ms）不重置驻留计时。
  - `Hover_ContinuousDwell_TriggersOnlyOnce`：5 秒连续驻留只触发一次。
  - `Hover_Reset_ClearsState`：Reset 后可重新触发。
  - `Hover_CursorOutside_NoTrigger`：远离宠物不触发。
  - `Hover_WithMargin_TriggersWhenNearButOutside`：带 margin 时近处可触发。
- **`tests/DesktopPet.Core.Tests/SwipeDetectorTests.cs`**（8 项）：
  - `Swipe_FastAndClose_Triggers`：快速近距离甩动触发。
  - `Swipe_SlowNearPet_NoTrigger`：慢速近处不触发。
  - `Swipe_FastFarFromPet_NoTrigger`：快速远处不触发。
  - `Swipe_CooldownPreventsRetrigger`：冷却期内不重复触发，冷却后可重新触发。
  - `Swipe_FastFromFarThenSlowApproach_NoFalseTrigger`：**远处快划后慢慢靠近不误触**——速度信号和距离信号来自同一时刻，旧速度峰值已衰减。
  - `Swipe_FastFromFarThenOneFastSegmentNear_Triggers`：慢靠近后确实在近处快划可触发（非过度抑制）。
  - `Swipe_Reset_ClearsCooldown`：Reset 清除冷却。
  - `Swipe_InvalidSample_NoTrigger`：无效样本不触发。
  - `Swipe_At150PercentDPI_SameVisualSpeed_Triggers`：150% DPI 相同视觉甩动可触发。

### 自测

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 104 + App 44 = **148/148**，0 失败，0 跳过。
  - 采样器测试 23 项（含 F-01 修正 6 新增 + 1 修改）。
  - 悬停检测器测试 7 项。
  - 甩动检测器测试 8 项（含"远处快划后慢靠近不误触"）。
  - 既有 Core 测试 66 项 + App 测试 44 项全部通过。
- 现有 App 测试（暂停一致性、拖动状态、资源加载、脚点稳定）全部通过，未受检测器接入影响。

### 限制

- **未验证**：真实鼠标互动行为需实机观察（悬停 1.2 秒触发好奇、快划触发躲避、拖动/菜单/暂停时不触发）。
- 混合 DPI 跨屏等效速度尚未实测（当前单屏环境）。
- `RecentSegmentCount` 默认 3（≈100ms @30Hz）和甩动/悬停阈值是初始建议值，实际手感可能需调整；参数可配置。
- **已实现完整触发链，待 P1-002 交付时统一复核。**

## 交互检查 03 整改：F-02 + F-03

日期：2026-10-23。对应：[P1-002 交互检查 03](../reviews/P1-002-review.md) §F-02、§F-03。

### F-02：连续采样下离开后无法重新触发悬停

#### 问题

`HoverDetector` 用 `_lastOutsideMs` 记录离开时间，但每次 outside Update 都覆盖为当前时间。连续 outside 采样（每 33ms）时，返回 inside 后 `(timeMs - _lastOutsideMs)` 只有约 33ms，远小于 `LeaveResetMs=500`，`_hasTriggered` 永不重置。

监理反例：t=0 进入、t=1200 首次 Triggered；t=1233…2223 每 33ms 连续 outside；t=2256 返回；t=3456 再驻留 1200ms 得到 None，预期 Triggered。

#### 修复

- **位置**：`src/DesktopPet.Core/Input/HoverDetector.cs`
- 将 `_lastOutsideMs`（每次 outside 覆盖）改为 `_leaveStartMs`（只在 inside→outside 转换的第一时刻设置）。
- 连续 outside 不覆盖 `_leaveStartMs`，离开时长从实际离开起点计算。
- inside 分支检查 `(timeMs - _leaveStartMs) >= LeaveResetMs` 判断是否重置 session。
- `Reset()` 同步更新字段名。

#### 新增测试

- `Hover_ContinuousOutsideSampling_ThenReturn_CanRetrigger`：复现监理反例——持续 outside 采样 30 次（每 33ms，持续 >1s）后返回，驻留 1200ms 可重新触发 Triggered。
- `Hover_ContinuousOutsideShort_DoesNotReset`：对照——持续 outside 仅 300ms（<500ms）后返回，不重置 session，继续驻留不重复触发。

#### 既有测试保留

全部 7 项既有悬停测试通过，包括 `Hover_LeaveThenReturn_AfterReset_CanTriggerAgain`（单次离开后返回可重新触发）和 `Hover_QuickLeaveAndReturn_NoReset`（短暂离开不重置）。

### F-03：旧速度峰值仍会与新的近距离组合

#### 问题

`SwipeDetector` 用 `EquivalentRecentSegmentSpeedPxPerSec`（最近 N 段最大值，可能来自旧快段）做速度检查，但用 `sample.Latest`（最新位置）做距离检查。旧快段在远处结束时，最新慢段已进入近处，旧峰值+新近距离=误触。

监理反例：宠物 rect=(500,500,120,120)，鼠标 `(300,560,0)→(410,560,33)→(430,560,66)`：快段约 3333px/s 结束于距宠物 90px（阈值外），最新段仅 606px/s 进入 70px 邻域，却因旧峰值输出 Triggered，预期 None。

#### 修复

- **位置**：`src/DesktopPet.Core/Input/MouseSampler.cs`、`src/DesktopPet.Core/Input/SwipeDetector.cs`
- `MouseSampleResult` 新增 `PeakSegmentEnd` 字段：达到 `RecentSegmentSpeedPxPerSec` 的那一段的终点位置（`MouseSample` 类型）。
- `MouseSampler.ComputeSpeed` 在遍历近期分段时，记录达到最大速度的段的终点样本。
- `SwipeDetector.Update` 距离检查从 `sample.Latest` 改为 `sample.PeakSegmentEnd`——速度信号和距离信号现在来自**同一段**，而非"同一结果中的不同时间点"。
- 不改变 `RecentSegmentCount` 或速度计算方式，不重引入 F-01 的往返抵消或短促快划稀释问题。

#### 新增测试

- `Swipe_FastSegmentFarEnd_SlowSegmentNear_NoFalseTrigger`：复现监理 F-03 反例——`(300,560,0)→(410,560,33)→(430,560,66)`，快段 3333px/s 结束于距宠物 90px（阈值外），最新慢段进入 70px 邻域，验证 `PeakSegmentEnd=(410,560)` 且输出 None。
- `Swipe_FastSegmentNearEnd_Triggers`：正对照——`(300,560,0)→(490,560,33)→(500,560,66)`，快段 5758px/s 结束于距宠物 10px（阈值内），验证 `PeakSegmentEnd=(490,560)` 且输出 Triggered。

#### 既有测试调整

4 个既有甩动测试的输入序列被调整，使快段的终点确实在宠物附近（而非快段在远处结束、慢段在近处结束——那恰好是 F-03 反例模式）：

- `Swipe_FastAndClose_Triggers`：改为 `(100,560,0)→(490,560,33)→(495,560,66)`，快段终点 490 距边缘 10px。

---

## F-04：App 触发、DPI 更新及有状态恢复缺少有效验证（补证）

### 问题

监理 R2 F-04 指出原 11 项 App 集成测试存在以下缺口：

1. `Integration_NormalTick_SamplerReceivesMousePosition` 和 Resume 用例以 `WindowCount >= 0` 断言，无法发现采样调用被删除；反射调用使用 `method?.Invoke`，成员不存在可能静默跳过。
2. `Integration_DpiScale_AppliedToSamplerAndDetector` 未调用 DPI 更新分支，只检查初始 1.0。
3. 拖动/菜单/暂停 Reset 和清空用例从空状态出发，即使移除部分 Reset/Clear 也可能通过。
4. 11 项中没有通过实际更新入口稳定触发 curious 或 dodge 的正向路径。

### 修复

#### 注入委托（`src/DesktopPet.App/MainWindow.xaml.cs`）

新增 3 个 `internal` 委托字段，null 时走原 Win32 路径，生产无行为变化：

- `TestGetCursorPos`：注入可控鼠标位置 `(int x, int y)`，替代 `GetCursorPos`。
- `TestGetElapsedMs`：注入可控单调时钟 `double`，替代 `GetElapsedMs`。
- `TestGetDpiScale`：注入可控 DPI `double`，替代 `MonitorInterop.GetWindowDpiScale`。

`OnUpdateTick` 已修改：DPI 分支先检查 `TestGetDpiScale`；采样分支先检查 `TestGetCursorPos`；时钟使用 `TestGetElapsedMs?.Invoke() ?? GetElapsedMs()`。

#### InternalsVisibleTo（`src/DesktopPet.App/DesktopPet.App.csproj`）

新增 `<InternalsVisibleTo Include="DesktopPet.App.Tests" />`，使测试项目可访问 `internal` 委托字段。

#### 重写集成测试（`tests/DesktopPet.App.Tests/MouseInteractionIntegrationTests.cs`）

原 11 项测试全部替换为 8 项 F-04 驱动测试，通过注入委托驱动实际 `OnUpdateTick` 逻辑：

1. **`F04_Hover_DwellTriggersCurious_OnceOnly_LeaveReenterRetriggers`**：鼠标驻留宠物中心 1200ms+ 触发 `curious`，`HasTriggered` 为 true；持续驻留不重复触发；离开 660ms 后重新进入并驻留 1200ms+ 再次触发。
2. **`F04_Swipe_NearFastSegment_TriggersDodge`**：从 (400,160) 快划到 (170,160)，速度≈6970 eq px/s，PeakSegmentEnd 距边缘 70px < 80，触发 `dodge` 并进入冷却。
3. **`F04_Swipe_FarFastSegment_DoesNotTrigger`**：从 (600,160) 快划到 (400,160)，速度≈6061 px/s 但 PeakSegmentEnd 距边缘 300px >> 80，不触发。
4. **`F04_Swipe_CooldownPreventsRetrigger`**：首次触发后 66ms 内再次快划，冷却阻止新触发；时钟推进 3100ms 过冷却后，新快划再次触发。
5. **`F04_DpiChange_UpdatesSamplerAndDetector_ClearsOldSamples`**：DPI 1.0 下建立 3 个样本，切换到 1.5 后下一 tick 更新 `LastDpiScale`、`MouseSampler.Config.DpiScale`、`SwipeDetector.Config.DpiScale` 均为 1.5，旧样本清空（WindowCount ≤ 1）。
6. **`F04_Interrupt_AfterStateBuilt_DragClearsAndRecovers`**：先建立非空采样（10 ticks）+ 悬停中（IsHovering）+ 冷却中（IsInCooldown），再进入拖动（含 `MouseSampler.Clear()` 模拟真实路径），验证采样器空、检测器重置；解除后重新驻留触发 curious。
7. **`F04_Interrupt_AfterStateBuilt_MenuClearsAndRecovers`**：同上，先建立状态，菜单中断（含 `MouseSampler.Clear()`），验证清理；菜单关闭后重新触发。
8. **`F04_Interrupt_AfterStateBuilt_PauseClearsAndRecovers`**：同上，先建立状态（含冷却），暂停中断，验证清理；恢复后重新触发。

#### F-04 要求落实

- **恒真断言已全部替换**：无 `WindowCount >= 0`，所有断言检查具体值（`CurrentAction == "curious"/"dodge"`、`IsInCooldown`、`IsHovering`、`HasTriggered`、`DpiScale == 1.5`、`WindowCount == 0` 等）。
- **反射成员不存在明确失败**：`CreateMainWindowOrFail` 构造失败 `Assert.Fail`；`InvokeTick` 方法不存在 `Assert.Fail`；`SetField` 字段不存在 `Assert.Fail`。不再使用 `method?.Invoke` 静默跳过。
- **DPI 真正执行更新逻辑**：通过 `TestGetDpiScale` 从 1.0 切换到 1.5，驱动 `OnUpdateTick` 中的 DPI 变更分支，断言三处配置更新且旧样本清空。
- **中断从有状态开始**：三个中断测试均先建立非空采样（WindowCount ≥ 2）、进行中悬停（IsHovering = true）和已触发冷却（IsInCooldown = true），再验证中断清理和恢复后重新触发。
- **不重开已关闭问题**：F-01～F-03 不重新测试或申请关闭。
- **不增加只检查默认值的测试**：原 3 项默认配置检查测试已删除。

### 自测证据

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 测试：Core 108 + App 52 = **160/160**，0 失败，0 跳过。
  - App 52 = 既有 44（PauseActionSwitch 4 + 其他 40）+ F-04 新增 8。
  - 原 11 项集成测试已全部替换。
- **未验证**：真实鼠标互动手感仍需实机观察，不冒充为自动测试通过。
- `Swipe_CooldownPreventsRetrigger`：同步调整三次甩动的输入序列。
- `Swipe_Reset_ClearsCooldown`：同步调整两次甩动的输入序列。
- `Swipe_At150PercentDPI_SameVisualSpeed_Triggers`：改为 `(150,840,0)→(640,840,33)→(645,840,66)`，快段终点 640 距边缘 110 physical px（等效 73px < 80）。

其余既有测试（`Swipe_SlowNearPet_NoTrigger`、`Swipe_FastFarFromPet_NoTrigger`、`Swipe_FastFromFarThenSlowApproach_NoFalseTrigger`、`Swipe_FastFromFarThenOneFastSegmentNear_Triggers`、`Swipe_InvalidSample_NoTrigger`）无需调整，全部通过。

### 自测

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 108 + App 44 = **152/152**，0 失败，0 跳过。
  - 采样器测试 23 项（不变）。
  - 悬停检测器测试 9 项（原 7 + 新增 2）。
  - 甩动检测器测试 10 项（原 8 + 新增 2，其中 4 个既有用例输入序列调整）。
  - 既有 Core 测试 66 项 + App 测试 44 项全部通过。

### 限制

- **未验证**：真实鼠标互动手感仍需实机观察。
- **已整改 F-02/F-03，待监理复核。不自行标记问题关闭或任务 done。**

## 复核 R1 关闭依据

日期：2026-10-23。对应：[P1-002 复核 R1](../reviews/P1-002-review.md) §复核 R1。

- 监理独立构建零警告/错误，正式测试 Core 108 + App 44 = 152/152，无失败/跳过。
- 监理使用交互检查 03 的未修改 Core 探针重新执行：连续 outside 后返回并驻留的第二次悬停已从 None 变为 Triggered；快段在 90px 外结束、慢段到 70px 内的原甩动误触已变为 None。
- **F-02 通过，关闭**：离开时间仅在离开段首个样本记录，后续 outside 不覆盖；持续离开重新触发、短暂离开不重置的对照测试通过。
- **F-03 通过，关闭**：近期速度峰值携带 PeakSegmentEnd，距离匹配该快段终点，不再匹配无关的最新慢段位置。原反例及有效近处快段正对照通过。四个旧甩动测试输入已核对，新输入使被选中的峰值段实际位于近处。
- 原 F-01 保持关闭。当前报告 F-01～F-03 均已关闭，P1-002 仍保持 awaiting-verification：真实鼠标互动及完整 App 触发/打断链还没有独立实机验证。
- 开发方不再返工已关闭问题，不重复申请关闭，不自行进入 P1-003。

## App 层自动集成验证

日期：2026-10-23。对应 R1 下一步建议："可在有运行条件时补 App 集成验证（实际更新入口、DPI、命中、拖动/菜单/暂停恢复）"。

### 范围

验证 `MainWindow.OnUpdateTick` 实际更新入口的集成接线：DPI 接入、采样器/检测器配置、拖动/菜单/暂停三种中断状态下检测器 Reset 和采样跳过、恢复后采样器清空可重新开始。**不验证真实鼠标手感**——测试通过反射调用 `OnUpdateTick` 和设置中断标志，不依赖真实 `GetCursorPos` 返回值。

### 实现

- **新增公开访问器**（`src/DesktopPet.App/MainWindow.xaml.cs`）：`HoverDetector`、`SwipeDetector`、`IsDragging`、`IsContextMenuOpen`、`LastDpiScale`，供测试验证集成状态。
- **新增测试文件**：`tests/DesktopPet.App.Tests/MouseInteractionIntegrationTests.cs`（11 项）。

### 新增测试

1. `Integration_DpiScale_AppliedToSamplerAndDetector`：构造后采样器和甩动检测器 DPI=1.0（hwnd 尚未创建）。
2. `Integration_NormalTick_SamplerReceivesMousePosition`：两次 tick 后采样器不崩溃，WindowCount ≥ 0。
3. `Integration_DragState_DetectorsResetAndSamplingSkipped`：拖动时采样器空、悬停/甩动检测器 Reset。
4. `Integration_MenuState_DetectorsResetAndSamplingSkipped`：菜单打开时采样器空、检测器 Reset。
5. `Integration_PauseState_DetectorsResetAndSamplingSkipped`：暂停时采样器空、检测器 Reset。
6. `Integration_ResumeAfterPause_SamplerClearedForFreshStart`：暂停→恢复后采样器清空，tick 可重新采样。
7. `Integration_DragEnd_SamplerClearedForFreshStart`：拖动结束后采样器清空，动作回 idle。
8. `Integration_HoverDetector_DefaultConfig`：悬停检测器默认配置 DwellMs=1200、LeaveResetMs=500。
9. `Integration_SwipeDetector_DefaultConfig`：甩动检测器默认配置 SpeedThreshold=1200、EdgeDistanceThreshold=80、CooldownMs=3000。
10. `Integration_MouseSampler_DefaultConfig`：采样器默认配置 WindowSize=10、RecentSegmentCount=3。
11. `Integration_AllInterrupts_NoDetectionActions`：拖动/菜单/暂停三种中断下 tick 不改变当前动作。

### 自测

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 108 + App 55 = **163/163**，0 失败，0 跳过。
  - 新增 App 集成测试 11 项，既有 App 测试 44 项全部通过。

### 限制

- **未验证**：真实鼠标互动手感（悬停触发好奇、快划触发躲避）仍需实机观察。App 集成测试验证的是接线逻辑，不是真实鼠标行为。
- 测试环境中 `GetCursorPos` 可能返回固定值或失败，不影响接线验证——测试断言的是中断状态和检测器状态，不依赖具体鼠标位置。
- **不冒充实机通过。**

## 复核 R3 补证：DPI 共用逻辑 + 重复触发有效观测

日期：2026-10-23。对应：[P1-002 复核 R3](../reviews/P1-002-review.md) §复核 R3。

### R3 剩余点 1：DPI 注入与 Win32 共用更新逻辑

#### 问题

监理 R3 指出：`OnUpdateTick` 的 `if(TestGetDpiScale is not null)` 与 `else if(_hwnd != Zero)` 各自重复 LastDpiScale/两处 Config/Clear。当前测试只执行前一套；删除真实分支的配置更新，测试仍可能通过。

#### 修复

- **位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `OnUpdateTick`（第 521-535 行）
- 合并为单一路径：只在"取得 DPI 值"处选择来源（`TestGetDpiScale?.Invoke()` 或 `MonitorInterop.GetWindowDpiScale(_hwnd).ScaleX` 或 fallback `_lastDpiScale`），随后共用同一段变化检测（`Math.Abs(currentDpi - _lastDpiScale) > 0.001`）、配置更新（`_mouseSampler.Config.DpiScale` + `_swipeDetector.Config.DpiScale`）和清空（`_mouseSampler.Clear()`）。
- 测试和生产走同一分支，不再有两套重复代码。删除真实分支的配置更新会导致 DPI 测试失败。

#### 修复附带

- 修正 `MonitorInterop.GetWindowDpiScale` 返回值字段名：`.dpiX` → `.ScaleX`（返回类型为 `(double ScaleX, double ScaleY)`）。

### R3 剩余点 2：重复触发要观察新事件

#### 问题

监理 R3 指出：悬停测试的 triggeredCount 在每个仍显示 curious 的 tick 都加一，并非触发次数；冷却测试第一次 dodge 后仅检查 CurrentAction 仍为 dodge，无法区分"未重触"与"重播"。

#### 修复

- **位置**：`tests/DesktopPet.App.Tests/MouseInteractionIntegrationTests.cs`
- **新增 `GetAnimPlayer` 辅助方法**：通过反射获取 `_animPlayer` 私有字段（`AnimationPlayer` 类型），失败 `Assert.Fail`。用于在测试中显式切换到 idle，使新触发可观测为 idle→curious/dodge 转换。
- **悬停测试 `F04_Hover_DwellTriggersCurious_OnceOnly_LeaveReenterRetriggers`**（已在上轮重写，本轮保留）：
  - 首次触发 curious 后，`PlayAction("idle")` 回 idle，持续驻留 10 tick 断言保持 idle（证明不重播）。
  - 离开 660ms 后重新进入，`PlayAction("idle")` 回 idle，清空 sampler（避免重新进入跳跃被误判为 swipe），断言阈值前 30 tick（990ms）保持 idle，阈值后才切 curious（证明新触发）。
- **冷却测试 `F04_Swipe_CooldownPreventsRetrigger`**（本轮重写）：
  - 首次 dodge 触发后，`PlayAction("idle")` 回 idle。
  - 冷却内（66ms 后）再次快划，断言 `CurrentAction == "idle"`（idle 保持证明无新触发，而非旧 dodge 保留）+ `IsInCooldown == true`。
  - 时钟推进 3100ms 过冷却后，`PlayAction("idle")` 回 idle，新快划断言 `CurrentAction == "dodge"`（idle→dodge 转换证明新触发事件）。

### 覆盖描述收窄

监理 R3 指出：拖动/菜单测试主动 `MouseSampler.Clear()` 后才模拟中断，只证明该预清理条件下的状态禁用/恢复，不能证明生产入口执行了 Clear。

#### 更正

三个中断测试（Drag/Menu/Pause）的覆盖描述已更正为注释说明：
- 测试主动 `MouseSampler.Clear()` 只证明该预清理条件下的状态禁用/恢复。
- 生产入口的 Clear（`PetCanvas_MouseLeftButtonDown`、`ShowNativeContextMenu` 开/关、`PauseAnimation`）由静态核对补充，不声称自动覆盖入口清理。
- 此说明不增加第三个独立整改门槛。

### 保留的已成立证据

- 首次动作触发正反断言（`F04_Swipe_NearFastSegment_TriggersDodge`、`F04_Swipe_FarFastSegment_DoesNotTrigger`）未重写，保留 R3 已确认成立的证据。
- 构造/反射失败不静默跳过、可控输入驱动 OnUpdateTick→Sampler→Detector→PlayAction 的接线验证保留。

### 自测

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 测试：Core 108 + App 52 = **160/160**，0 失败，0 跳过。
- **未验证**：真实鼠标互动手感仍需实机观察，不冒充为自动测试通过。

### 限制

- **已整改 R3 两点，待监理复核。不自行标记 F-04 关闭或任务 done。**
- 真实鼠标手感（悬停触发好奇、快划触发躲避、拖动/菜单/暂停不触发）仍单列未验证。

## 复核 R4 关闭依据

日期：2026-10-23。对应：[P1-002 复核 R4](../reviews/P1-002-review.md) §复核 R4。

- 监理独立构建零警告/错误，正式测试 Core 108 + App 52 = 160/160，无失败/跳过。未修改生产代码或开发测试。
- **R3 点 1（DPI 共用）通过**：DPI 仅在取值处区分注入与 Win32，之后统一 currentDpi 变化检测、LastDpiScale/两处配置更新及采样 Clear。原"两套重复更新分支"缺口解除。
- **R3 点 2（重复触发有效观测）通过**：悬停首次触发后显式回 idle，持续驻留断言保持 idle；离开再入从 idle 开始，阈值前保持、到期切 curious。冷却测试首次 dodge 后回 idle，冷却内保持 idle、到期后新快段切 dodge，能区分新触发和旧动作残留。
- **覆盖描述收窄确认**：测试主动清理采样及反射设置拖动/菜单状态的限制已明确收窄，生产入口 Clear 调用由静态核对补充；不声称已自动操作原生菜单或真实鼠标捕获。
- **F-04 通过，关闭。F-01～F-04 全部关闭。** 开发方不再返工或重复请求这些问题关闭。
- **P1-002 继续 awaiting-verification**：真实鼠标手感尚未执行，用户未明确豁免。有运行条件时补实机证据，或由用户明确决定是否接受此项暂未验证。不进入 P1-003，不要求用户现在重新操作，不追加无关测试。

## 帮手实机修复核对与用户体验反馈

日期：2026-10-23。对应：[修复交接](../handoffs/P1-002-live-input-fix.md)、[实机验证](../history/P1-002-live-validation.md)。

### 帮手修复背景

用户授权助手"请你来验证，遇到问题你直接编码解决"。助手在真实桌面执行 18 项自动输入验证，发现透明窗口角落停留也触发 curious（基线 17/18 通过），随后修复并再次验证 18/18 通过。助手同时是实现者和自测者，不是独立审查签字。

### 代码改动核对

接手者核对了实际 diff（git diff），确认以下改动：

1. **`src/DesktopPet.App/Platform/PetImageGeometry.cs`（新增）**：缓存 BitmapSource 的 BGRA Alpha 到 `ConditionalWeakTable`，提供 `Contains`（按像素 Alpha 命中）和 `OpaqueBounds`（不透明区域外接矩形）。实现合理：按 Stretch=None 自然 DIP 尺寸映射，居中偏移正确。
2. **`src/DesktopPet.Core/Input/HoverDetector.cs`**：`Update` 新增可选 `isOverEntity` 参数，null 时保留原矩形逻辑。改动最小化，不破坏纯 Core 调用方。
3. **`src/DesktopPet.App/MainWindow.xaml.cs`**：
   - 悬停调用传入 `IsOverVisiblePet`（Alpha 命中），通过 `PetImage.PointFromScreen` 转换屏幕坐标到图像本地坐标。
   - `GetPetRectPhysicalPx` 改为使用 `PetImageGeometry.OpaqueBounds` + `PetImage.PointToScreen`，不再用 Left/Top × DPI 推断。
   - 未显示窗口时有 `TransformToAncestor` 回退路径。
4. **`tests/DesktopPet.App.Tests/VisiblePetInteractionTests.cs`（新增 3 项）**：透明像素不命中/不透明命中、透明角落不触发但身体触发、甩动边界使用可见区域。断言具体，无恒真。
5. **`tests/DesktopPet.InteractionProbe/`（新增）**：真实鼠标轨迹入口，不作为产品功能。
6. **文档同步**：ARCHITECTURE.md 悬停检测器描述已更新；STATUS/TASKS 已同步。

### 构建与测试验证

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 测试：`dotnet test DesktopPet.slnx --no-build -c Debug`：Core 108 + App 55 = **163/163**，0 失败，0 跳过。
- 帮手实机自动输入：18/18 通过（两轮，含 150% DPI 真实 HWND），十场景生命周期全部通过。详见 [实机验证](../history/P1-002-live-validation.md)。

### 用户体验反馈

用户原话（2026-10-23）："我已人工体验当前版本，暂未发现阻碍继续开发的问题。当前素材是占位图，本次接受的是 P1-002 的交互功能，不代表最终表情、美术和动作观感验收完成。"

- 用户明确接受 P1-002 交互功能（悬停触发好奇、快划触发躲避、拖动/菜单/暂停禁用）。
- 用户明确区分：接受的是交互功能，不是最终美术/表情/动作观感。
- 用户未发现阻碍继续开发的问题。

### 完成门槛评估

按 REVIEW_PROCESS §6 关闭条件：

1. **正常任务验收证据**：163/163 自动测试通过；18/18 实机自动输入两轮通过；十场景生命周期通过；用户人工体验通过且明确接受交互功能。
2. **同步的文档**：ARCHITECTURE.md、STATUS.md、TASKS.md、整改记录均已同步。
3. **阻断项的监理通过/用户明确豁免**：
   - 原 F-01～F-04 已由监理 R4 全部关闭。
   - 帮手修复（透明角落误触）是 R4 之后的新代码改动，帮手同时为实现者，按 REVIEW_PROCESS 不代签监理结论。
   - 用户明确接受 P1-002 交互功能并要求收尾标 done，构成对剩余"真实鼠标手感"验收项的用户明确豁免（REVIEW_PROCESS §5："用户可以明确豁免某个问题对指定任务/阶段的阻断"）。
   - 帮手修复的独立核对由接手者通过 diff 审查和构建/测试重验完成，核对结果记录于此。接手者非帮手本人，核对范围：代码 diff、Alpha 命中逻辑、坐标转换、测试断言有效性、构建和测试重跑。

### 限制

- 帮手修复的独立核对由本轮接手者完成（非帮手本人），但接手者仍属开发方，不等于监理复核签字。用户已明确豁免真实鼠标手感验收项并要求标 done。
- 占位美术不是最终表情/动作观感验收；100% DPI 新版本、混合 DPI 双屏、真实锁屏/睡眠恢复仍未重测。
- InteractionProbe 真实鼠标轨迹入口占用鼠标，不作为常规 CI 使用。
