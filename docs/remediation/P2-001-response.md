# P2-001 首轮审查整改记录

日期：2026-11-06。对应审查报告：[P2-001-review](../reviews/P2-001-review.md)。
开发方独立整改，不修改监理报告，不自行关闭问题。

## 整改范围

仅本小步：Core 纯逻辑 `ReleaseSpeedSampler` + 专项测试 + PRODUCT/ARCHITECTURE 规格纠正。
不接 App、不做重力/碰撞、不进入 P2-002、不新增图片、不提交/推送。

## F-01 近期释放方向与停止状态

### 问题

`EvaluateRelease` 用窗口 oldest→newest 净位移估算速度。方向反转时净位移方向与释放前实际运动方向相反；停止后松手仍保留旧速度。测试 `RoundTripDrag_WithResidualVelocity_ThrowsInResidualDirection` 把净位移方向当释放方向，是错误预期。

### 修复

- **`FindRecentMotionSegment`**：从末尾向前扫描，先确认末样本对在运动（`IsPairMoving`），再向前扩展只纳入方向一致的连续运动 pair（新增 `SameDirection` 方法检查 dx/dy 符号不翻转）。方向反转时 segment 只包含末段同方向运动，不取整个窗口净位移。
- **停止判定**：末样本对速度低于 `StopSpeedPxPerMs`（默认 0.02）时 `FindRecentMotionSegment` 返回 (-1,-1)，`EvaluateRelease` 返回轻放。不沿用旧峰值。
- **`ReleaseSpeedConfig`** 新增 `StopSpeedPxPerMs` 字段（默认 0.02）。

位置：`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs` — `FindRecentMotionSegment`、`SameDirection`、`ReleaseSpeedConfig.StopSpeedPxPerMs`。

### 测试纠正与新增

- 删除/纠正原 `RoundTripDrag_WithResidualVelocity_ThrowsInResidualDirection`（净位移方向≠释放方向）。
- 新增方向反转测试：
  - `DirectionReversal_FastRightThenFastLeft_ThrowsLeftward`：(100,200)@0→(400,200)@50→(300,200)@100，期望 VelocityX<0。
  - `DirectionReversal_FastLeftThenFastRight_ThrowsRightward`：反向对照。
  - `RoundTripDrag_ReturnToStart_ThrowsInLastDirection`：往返回到起点，期望按最后方向甩出。
- 新增连续序列停止测试：
  - `ContinuousSequence_Move33msThenStop33ms_Gentle`：33ms 连续移动后静止两帧，期望轻放。
  - `ContinuousSequence_RightThenLeftThenStop_Gentle`：方向反转后停止，期望轻放。
  - `ContinuousSequence_ContinuousFastDrag_Throws`：持续快拖仍可甩出。

## F-02 DPI 上下文重置

### 问题

采样器无 DPI/坐标上下文切换入口。DPI 变化导致坐标跳变被当成最大速度抛掷。测试 `DpiChange_PositionJump_ClampedToMaxSpeed` 直接断言 `ShouldThrow=true`，PRODUCT/ARCHITECTURE 也称"被钳位吸收"，是错误预期。

### 修复

- **`ClearSampleContext()`**：新增公开方法，清空所有样本。调用方（未来 App 集成）在 DPI/显示器上下文变化时调用，旧坐标不会与新坐标相减。
- 采样器不凭坐标自动识别 DPI 跳变；清空后需重新积累 `MinSamplesForThrow` 个有效样本才能估算速度。

位置：`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs` — `ClearSampleContext`。

### 测试纠正与新增

- 纠正原 `DpiChange_PositionJump_ClampedToMaxSpeed` → `ClearSampleContext_CoordinateJumpNotTreatedAsThrow`：仅上下文变化（坐标跳变）后立即松手，期望 `ShouldThrow=false`。
- 新增 `ClearSampleContext_ThenNewTrajectory_ThrowsCorrectly`：清空后新轨迹正常估算。
- 新增 `ClearSampleContext_ThenStationary_Gentle`：清空后静止，期望轻放。

## F-03 最新时间与有限值校验

### 问题

- `RecordSample` 用 `Queue.TryPeek` 取最旧样本当 last 比较，倒序时间被接收。
- 只校验采样时间，未校验 x/y 坐标和 `releaseTimeMs`。NaN 坐标输出 NaN 速度，NaN 释放时间仍抛掷。

### 修复

- **`RecordSample`**：改为 `List<DragSample>`，与 `_samples[^1]`（最新样本）比较时间，拒绝 `<=` 最新时间的样本。新增 x/y NaN/Infinity 校验（拒绝且不污染历史）。时间 NaN/Infinity 也拒绝。
- **`EvaluateRelease`**：`releaseTimeMs` NaN/Infinity 返回轻放；`releaseTimeMs` 早于 `_samples[^1].TimeMs` 返回轻放（不用未来样本算速度）。
- **`PruneOldSamples`**：改为 `List<DragSample>.RemoveRange`。

位置：`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs` — `RecordSample`、`EvaluateRelease`、`PruneOldSamples`。

### 测试新增

- `RecordSample_NaNCoordinate_Rejected` / `RecordSample_InfinityCoordinate_Rejected`：非法坐标被拒绝，后续有效调用正常。
- `RecordSample_OutOfOrderTime_Rejected`：倒序时间被拒绝，有效历史不破坏。
- `RecordSample_SameTime_Rejected`：相同时间被拒绝。
- `EvaluateRelease_NaNReleaseTime_Gentle` / `EvaluateRelease_InfinityReleaseTime_Gentle`：非法释放时间返回轻放。
- `EvaluateRelease_ReleaseTimeBeforeNewestSample_Gentle`：早于最新样本的释放时间返回轻放。
- `EvaluateRelease_AllReturnedVelocitiesFinite`：所有返回速度有限。

## 规格纠正

### PRODUCT R-04

- 设计要点"速度估算用窗口内首尾样本的位移除以时间差"→"用近期运动段——从末尾向前连续同方向运动样本对的位移除以时间差"。
- "DPI 变化产生的坐标跳变被钳位到上限"→"DPI/坐标上下文变化须由调用方通过 `ClearSampleContext()` 显式通知采样器清空旧基准"。
- "松手前停住时，窗口内只有静止样本，自然得到零速度"→"末样本对速度低于 `StopSpeedPxPerMs` 时视为停止，返回轻放"。
- 参数表新增 `StopSpeedPxPerMs` 行（0.02 px/ms）。

### ARCHITECTURE §5

- "滑动窗口平均速度：用窗口内首尾样本位移除以时间差"→"近期运动段速度估算：从末尾向前找连续同方向运动样本对"。
- "DPI 变化产生的坐标跳变被钳位到上限"→"调用方须在上下文变化时调 `ClearSampleContext()` 清空旧基准"。
- "NaN/Infinity 时间戳被拒绝"→补充"x/y NaN/Infinity 坐标被拒绝；NaN/Infinity 释放时间或早于最新样本的释放时间返回轻放；所有返回速度有限"。
- 新增停止判定和 `StopSpeedPxPerMs` 描述。

## 自测证据

- 专项测试 44/44 通过（`dotnet test --filter "ReleaseSpeedSampler"`），0 失败/跳过。
- 全量 Core 297/297 通过，0 回归。
- 构建 0 警告/错误。
- 未运行 App 测试（本轮不接 App）。
- 未提交/推送。

## 限制与待复核

- `ClearSampleContext()` 的调用方（App 集成）尚未实现，P2-003 接入时需在真实 DPI 变化时调用。
- 参数初值仍为调试值，待 P2-004 实机手感校准。
- 不自行关闭 F-01～F-03，待监理复核。

## R1 整改复核关闭依据（2026-11-06）

监理 R1（见[报告 R1 段落](../reviews/P2-001-review.md#r1整改复核2026-11-06)）独立 Core 297/297 通过，8 项原反例/恢复检查通过，F-01～F-03 全部关闭，P2-001 纯 Core 范围接受，已标 `done`。

- **F-01 关闭**：按末尾连续同方向运动段计算，末对静止则轻放；原末段向左输出向左、连续 33ms 采样静止 66ms 返回轻放，持续快拖/对角/方向反转等专项通过。当前阈值及按分量符号的分段方式是首版可调策略，不宣称最终手感已校准。
- **F-02 关闭**：`ClearSampleContext` 明确由调用方在 DPI/坐标上下文变化时通知，清空后立即释放不抛、产生足够真实轨迹再正常抛；规格已移除"DPI 跳变限速后也抛"的错误定义。App 尚未接入，实际 DPI 变更时调用仍是 P2-003 的接线验收项。
- **F-03 关闭**：与真实最新时间比较、坐标/采样和释放时间有限值拒绝、早于最新的释放返回轻放。原倒序反例拒绝后保留正常方向，NaN/Infinity 坐标不进入历史，非法释放不污染随后有效评估。

不重复整改，不改监理报告。下一小步可安排 P2-002 有界物理 Core。
