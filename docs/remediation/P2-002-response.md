# P2-002 首轮审查整改记录

日期：2026-11-06。对应审查报告：[P2-002-review](../reviews/P2-002-review.md)。
开发方独立整改，不修改监理报告，不自行关闭问题。

## 整改范围

仅本小步 F-01～F-03：Core 纯逻辑 `PhysicsSimulator` + 专项测试 + PRODUCT/ARCHITECTURE 规格同步。
不接 App、不进入 P2-003、不新增图片、不提交/推送。参数仍是初值，手感留 P2-004 实机校准。

## F-01 固定步长余量丢弃

### 问题

`Update` 每次调用独立换算步数，不足一步的余量被丢弃。高频小 dt（如 6×10ms）完全不动，而等价大 dt（1×60ms）走 3 步——跨调用不等价。

### 修复

- **`_timeCarryMs` 累积器**：`Update` 内部维护 carry，跨调用保留不足一步的余量。`availableMs = _timeCarryMs + elapsedMs`，`stepsToRun = (int)(availableMs / stepMs)`，`_timeCarryMs = availableMs - stepsToRun * stepMs`。
- **积压截断保留余量**：超出 `MaxStepsPerUpdate` 时截断步数并丢弃超出部分，但保留最后一个执行步的子步余量（`remainder = 0` 仅在截断时）。
- **清零时机**：`Launch`/`Reset`/`ForceStop` 清零 carry。新增 `ClearTimeAccumulation()` 供暂停恢复清时间不清运动状态。

位置：`src/DesktopPet.Core/Physics/PhysicsSimulator.cs` — `_timeCarryMs` 字段、`Update` 方法、`Launch`/`Reset`/`ForceStop`/`ClearTimeAccumulation`。

### 自测证据

- `SixTimes10ms_EquivalentToOne60ms`：6×10ms 与 1×60ms 产生相同 State（位置/速度完全一致）。
- `NonDivisibleMixedSequence_CarryPreserved`：10ms+10ms+10ms+10ms+10ms+10ms+10ms（70ms，FixedStepMs≈16.67）跨调用累积 carry，最终步数等价于 70ms 一次调用。
- `HighFrequencySmallDt_NoStuck`：100×1ms 不卡死，carry 累积后正确步进。
- `Launch_ClearsCarry`：先 Update 留 carry，再 Launch 后 carry 清零。
- `ClearTimeAccumulation_PreservesMotion`：清 carry 后位置/速度/弹跳计数不变。
- 专项测试 50/50 通过，全量 Core 347/347 通过，0 回归。

## F-02 自然停止支撑

### 问题

反弹后速度低于 `StopSpeedPxPerMs` 即设 `IsResting`，不区分碰撞面。侧墙低速碰撞或 `MaxBounces` 达上限后可永久半空静止。监理反例：`Launch(0.01,100,-0.03,-0.0333333)` 侧墙冻结 Y100；`MaxBounces=1, Launch(999,100,1,0)` 右墙冻结 Y100.5556。

### 修复

- **`ResolveCollisions` 返回 `(bounced, hitFloor)`**：只有底边碰撞设 `hitFloor=true`，侧墙/天花板设 `hitFloor=false`。
- **自然停止只在地板**：`if (hitFloor && _state.Speed < _config.StopSpeedPxPerMs)` 才设 `IsResting=true`。侧墙/天花板碰撞不触发自然停止——宠物继续受重力下落。
- **MaxBounces 达上限**：零水平速度但保留重力（`Vy = max(0, Vy)`），不设 `IsResting`——宠物必须落地板后自然停止。循环结束后额外检查：弹跳上限 + 在地板 + 低速 → 自然停止。
- **ForceStop 区别**：`ForceStop()` 在任意位置立即停止，不要求地板支撑。

位置：`src/DesktopPet.Core/Physics/PhysicsSimulator.cs` — `ResolveCollisions` 返回值、`Update` 停止判定逻辑、`ForceStop`。

### 自测证据

- `SideWallLowSpeed_FallsToFloor`：`Launch(0.01,100,-0.03,-0.0333333)` 侧墙低速不冻结，最终落地板停止。
- `CeilingBounce_DoesNotHover`：天花板碰撞后不悬浮，继续下落。
- `MaxBouncesReached_FallsToFloor`：`MaxBounces=1, Launch(999,100,1,0)` 右墙碰撞后不冻结，落地板停止。
- `MaxBouncesReached_HorizontalZeroed_GravityContinues`：弹跳上限后水平速度为零但 Y 持续下落。
- `ForceStop_AtArbitraryPosition`：半空 `ForceStop` 立即停止，不要求地板。
- `FloorBounce_LowSpeed_Stops`：地板低速反弹正常停止。
- 专项测试 50/50 通过，全量 Core 347/347 通过，0 回归。

### 补充：零/等于停止阈值收敛（监理 R1 补充要求）

原修复使用 `<`（严格小于），当 `StopSpeedPxPerMs=0` 时 `Speed` 恒 ≥ 0 不可能 `< 0`，零阈值合法配置永远无法静止；速度恰等于正阈值时也不停止。

- **修复**：将循环内地板自然停止判定（第 350 行）和循环后 MaxBounces 落地停止判定（第 376 行）从 `<` 改为 `<=`。零阈值时 `Speed=0 ≤ 0` 成立→静止；恰等于阈值时 `Speed=threshold ≤ threshold` 成立→静止。侧墙碰撞 `hitFloor=false` 不受影响，继续落地。

位置：`src/DesktopPet.Core/Physics/PhysicsSimulator.cs` — 第 350 行、第 376 行比较运算符。

### 补充自测证据

- `F02_ZeroStopThreshold_FloorBounceRests`：`StopSpeedPxPerMs=0, BounceRestitution=0`，直落→地板反弹后 `Speed=0`→静止。
- `F02_ZeroStopThreshold_NoUpdateAfterRest`：零阈值静止后后续 `Update` 步数为 0，状态不变。
- `F02_ExactThreshold_RestsWhenSpeedEqualsThreshold`：探测首次地板反弹后速度，设为 `StopSpeedPxPerMs` 精确值→恰等于阈值→静止。
- `F02_ZeroThreshold_SideWallContinuesToFloor`：`StopSpeedPxPerMs=0, BounceRestitution=0`，侧墙碰撞后 `Speed=0` 但 `hitFloor=false`→不静止，继续落地→最终地板静止。
- 专项测试 54/54 通过（50 + 4 新增），全量 Core 351/351 通过，0 回归。

## F-03 输入/配置边界

### 问题

`Launch` 不拒绝 NaN/Infinity 坐标/速度，NaN 初始坐标继续输出 NaN。`Update` 不拒绝颠倒工作区（`WorkArea(200,200,0,0)`）。非法 Config 未校验。

### 修复

- **`Launch` 返回 `bool`**：拒绝 NaN/Infinity 坐标/速度（`PhysicsState.IsFinite` 检查），返回 `false` 且不改已有状态/不设 `_launched`。
- **`Update` 返回 `Rejected` 字段**：拒绝非法 dt（NaN/Infinity/负值）、非法 `WorkArea`（`!area.IsValid`）、非法 `PhysicsConfig`（`!_config.IsValid`），返回 `Rejected=true` 且不推进模拟、不污染已有状态。
- **`PhysicsConfig.IsValid`**：`FixedStepMs > 0 && MaxStepsPerUpdate >= 1 && GravityPxPerMs2 >= 0 && BounceRestitution ∈ [0,1] && StopSpeedPxPerMs >= 0 && MaxBounces >= 1`，所有值有限。
- **`WorkArea.IsValid`**：四边有限且 `Left <= Right && Top <= Bottom`。
- **`PhysicsState.IsFinite`**：X/Y/Vx/Vy 均有限。
- **`PhysicsUpdateResult.Rejected`**：新增字段，`true` 表示输入被拒绝、状态未变。

位置：`src/DesktopPet.Core/Physics/PhysicsSimulator.cs` — `Launch` 返回值、`Update` 校验逻辑、`PhysicsConfig.IsValid`、`WorkArea.IsValid`、`PhysicsState.IsFinite`、`PhysicsUpdateResult.Rejected`。

### 自测证据

- `Launch_NaNCoordinates_Rejected` / `Launch_InfinityVelocity_Rejected`：NaN/Infinity 被拒绝，返回 false。
- `Launch_Rejected_DoesNotCorruptExistingState`：拒绝后已有状态不变。
- `Update_NaNDt_Rejected` / `Update_NegativeDt_Rejected` / `Update_InfinityDt_Rejected`：非法 dt 被拒绝。
- `Update_InvertedWorkArea_Rejected`：`WorkArea(200,200,0,0)` 颠倒被拒绝。
- `Update_NaNWorkArea_Rejected`：NaN 边界被拒绝。
- `Update_InvalidConfig_Rejected`：非法 Config 被拒绝。
- `Update_ValidThenInvalid_NoCorruption`：有效序列混入非法调用不腐蚀已有状态。
- `DegenerateWorkArea_ZeroSize_Rejected`：零尺寸工作区被拒绝。
- `ConfigIsValid_EachField`：各字段非法时 `IsValid=false`。
- 专项测试 50/50 通过，全量 Core 347/347 通过，0 回归。

## 规格同步

- **PRODUCT R-04 P2-002 参数表**：补充 carry 累积器、`ClearTimeAccumulation`、自然停止只在地板、ForceStop 区别、输入校验语义。`MaxBounces` 含义改为"达上限后零水平速度但重力继续，落地板后自然停止"。
- **ARCHITECTURE §5 P2-002 段落**：补充 carry/ClearTimeAccumulation/ResolveCollisions 返回值/自然停止只在地板/MaxBounces 重力继续/ForceStop 区别/输入校验/IsValid 系列校验属性/IsLaunched 标志。

## 限制与待复核

- 参数仍是初值，手感留 P2-004 实机校准。
- `ClearTimeAccumulation()` 调用方（App 集成）待 P2-003 实现。
- 未接 App、未新增图片、未进入 P2-003。
- 不自行关闭 F-01～F-03，提交监理复核。

## 监理 R2 关闭依据

[监理 R2](../reviews/P2-002-review.md) 已关闭 F-01～F-03 全部阻断项，纯 Core 验收完成。独立 Core 351/351 通过，原反例复验通过。F-02 零/等于停止阈值收敛（`<=` 比较）经 R2 确认：`StopSpeed=0` 默认重力回弹反例现仅一次碰撞即可地板静止。F-01/F-03 维持 R1 关闭。TASKS 已标 `done`。App/实际手感未实现或验证，P2-003 须用户明确开工。开发方不修改监理报告，仅追加本关闭依据。
