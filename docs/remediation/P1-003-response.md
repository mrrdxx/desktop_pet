# P1-003 整改记录

日期：2026-10-23（Asia/Shanghai）。开发 AI。对应监理报告：[P1-003-review](../reviews/P1-003-review.md)。

## F-01：日期回拨被当成次日

### 问题

`GrievanceExpressionState.ShouldExpress` 使用 `MarkedDate != currentLocalDate` 判断，只比较不相等。标记 2026-10-23 后传入 2026-10-22（回拨）也返回 true，与"次日"语义和回拨不重复的时间约定不符。

### 修复

- 位置：`src/DesktopPet.Core/Emotion/GrievanceExpressionState.cs` 的 `ShouldExpress`。
- 改为使用 `DateOnly.TryParse` 严格解析两个日期，只有 `current > marked` 才返回 true。
- 非法/缺失日期（null、空字符串、无法解析）返回 false，不触发表达。
- `Restore` 后的非法 `markedDate` 同样不会触发；调用方可 `Clear` 后重新 `TryMarkPending` 标记有效日期。

### 自测

新增 12 项测试（`GrievanceExpressionStateTests.cs`）：
- `ShouldExpress_ClockRollbackEarlierDate_DoesNotTrigger`：回拨到前一天不触发。
- `ShouldExpress_ClockRollbackSameDate_DoesNotTrigger`：同日不触发。
- `ShouldExpress_LaterDate_Triggers`：两天后触发。
- `ShouldExpress_MonthBoundary_Triggers`：月末跨月触发。
- `ShouldExpress_YearBoundary_Triggers`：年末跨年触发。
- `ShouldExpress_InvalidCurrentDate_DoesNotTrigger`：垃圾字符串不触发。
- `ShouldExpress_EmptyCurrentDate_DoesNotTrigger`：空字符串不触发。
- `ShouldExpress_NullCurrentDate_DoesNotTrigger`：null 不触发。
- `ShouldExpress_InvalidMarkedDate_DoesNotTrigger`：存档损坏的 markedDate 不触发。
- `ShouldExpress_NullMarkedDate_DoesNotTrigger`：pending=true 但 markedDate=null 不触发。
- `Restore_InvalidMarkedDate_CanBeRemarkWithValidDate`：损坏日期可清除后重新标记。
- `Restore_PendingThenRollback_DoesNotTrigger`：恢复后回拨不触发，次日仍可触发。

### 复核请求

F-01 已修正并测试覆盖同日/次日/回拨/跨月跨年/非法日期/存档恢复场景，请监理复核。

### R1 复核结果

监理 R1（2026-10-23）已**关闭** F-01：确认 ShouldExpress 使用 DateOnly 解析并比较 `current > marked`，回拨/同日/无效日期不触发，补充恢复路径测试。TryParse 为日期解析，不限定 yyyy-MM-dd 的 TryParseExact；本次要求的日期先后关系已满足。

## G-01：衰减接入须保留累计精度

### 问题

`EmotionDecay` 每次输出 int。如果 App 层每 tick 把短间隔衰减结果写回整数并推进基准时间，小于 0.5 的变化会被舍弃，在线衰减可能永远不发生。

### 修复

- 位置：`src/DesktopPet.Core/Emotion/EmotionDecay.cs`。
- 新增 `DecayAffinityFloat` / `DecayGrievanceFloat` 返回 double，保留浮点精度。
- 新增 `EmotionDecayAccumulator` 类：内部以 double 保存亲和/委屈值和衰减基准时间，`AdvanceTo(DateTime)` 按累计时间推进衰减，只在需要快照时取整。支持 `AddAffinity`/`AddGrievance`/`ResetGrievanceAfterExpression` 直接操作浮点状态。
- 时钟回拨（currentUtc < lastDecayUtc）视为零间隔，不增加情绪。

### 自测

新增 10 项测试（`EmotionDecayTests.cs`）：
- `DecayAffinityFloat_PreservesPrecision`：微小间隔浮点值有变化。
- `DecayGrievanceFloat_PreservesPrecision`：同上。
- `Accumulator_ManySmallSteps_MatchOneBigStep_Affinity`：1000 步 vs 1 步总时间相同，结果一致（容差 0.001）。
- `Accumulator_ManySmallSteps_MatchOneBigStep_Grievance`：同上。
- `Accumulator_IntegerRounding_DoesNotLoseDecayOverTime`：1000 个 1 秒 tick 后浮点值有衰减，int-only 不会。
- `Accumulator_ClockRollback_TreatedAsZeroElapsed`：回拨不增加情绪。
- `Accumulator_AddAffinity_PreservesFloatPrecision`：增量加到浮点值而非取整后的 int。
- `Accumulator_ResetGrievance_SetsToNeutral`：表达后重置为 50。
- `Accumulator_AdvanceThenSnapshot_MatchesDirectFloatDecay`：Accumulator 与直接浮点公式一致。

## G-02：角落停留必须对应最后一次释放

### 问题

`CornerTracker.RecordCornerDrag` 不清除旧 `_lastDragStayStart`，`IsConditionMet` 不检查停留是否早于最后释放。前一次停留可替代最后 30 秒。

### 修复

- 位置：`src/DesktopPet.Core/Emotion/CornerTracker.cs`。
- `RecordCornerDrag` 现在清除 `_lastDragStayStart`（新拖动意味着旧停留结束）。
- `RecordStayStart` 在没有 `_lastDragEndTime` 时忽略（没有释放就没有停留）。
- `IsConditionMet` 检查 `_lastDragStayStart >= _lastDragEndTime`（停留必须在最后释放之后）。
- 新增 `CancelStay()` 方法供离开角落时调用。
- 新增 `IsStayActive` 属性。

### 自测

新增 7 项测试（`CornerTrackerTests.cs`）：
- `NewDragCancelsPreviousStay`：新拖动取消旧停留，第三次释放后停留满 30 秒才计分。
- `OldStayBeforeLastDrag_DoesNotCount`：第三次拖动后无新停留，不满足条件。
- `ThirdDragStayUnder30Seconds_NotMet`：第三次释放后停留不足 30 秒。
- `CancelStay_ThenNoCondition`：离开角落取消停留后不满足条件。
- `CancelStay_ThenRestartStay_CanMeetCondition`：取消后重新停留可满足。
- `FullSequence_ThreeDragsWithInterruption_OnlyLastStayCounts`：拖-停-拖-停-拖-停完整序列，只有最后停留计分。
- `NoDoubleAward_AfterConditionMetAndClear`：计分后 Clear，后续 tick 不重复加分。
- `RecordStayStart_BeforeAnyDrag_Ignored`：无拖动时停留被忽略。

## F-02：回拨后恢复时间会重复衰减

### 问题

`EmotionDecayAccumulator.AdvanceTo` 在负间隔被改为零后仍无条件执行 `_lastDecayUtc = currentUtc`，将基准倒退。后续时钟恢复到原时刻会把已经处理过的区间再次衰减。

监理 R1 复现：`new EmotionDecayAccumulator(50, 90, 2026-10-23T12:00:00Z)` → `AdvanceTo(00:00:00Z)` → `AdvanceTo(12:00:00Z)` 期望 90，实际降至 70。

### 修复

- 位置：`src/DesktopPet.Core/Emotion/EmotionDecay.cs` 的 `EmotionDecayAccumulator.AdvanceTo`。
- 回拨（`currentUtc < _lastDecayUtc`）时直接返回当前快照，**不更新 `_lastDecayUtc`**，保留原基准。
- 恢复到原时刻时 elapsed=0，不产生额外衰减；超过原时刻只计算新间隔。
- 存档恢复时沿用同样的时间约定：`EmotionDecayAccumulator` 构造函数接收 `lastDecayUtc` 作为基准，回拨逻辑一致。

### 自测

新增 9 项测试（`EmotionDecayTests.cs`，F-02 区段）：

- `F02_RollbackThenReturnToOriginal_NoExtraDecay_Grievance`：监理复现场景，回拨→恢复后仍为 90。
- `F02_RollbackThenReturnToOriginal_NoExtraDecay_Affinity`：亲近度回拨→恢复后不变。
- `F02_RollbackThenGoPastOriginal_OnlyNewIntervalDecayed_Grievance`：回拨→恢复→超过原时刻，与无回拨对照一致（浮点容差 0.001）。
- `F02_RollbackThenGoPastOriginal_OnlyNewIntervalDecayed_Affinity`：同上，亲近度。
- `F02_RollbackThenGoPastOriginal_BothEmotions`：亲近度+委屈同时覆盖。
- `F02_RepeatedRollback_NoExtraDecay`：3 次重复回拨→恢复，值不变。
- `F02_RepeatedRollbackThenAdvance_MatchesControl`：5 次重复回拨后真实推进，与对照一致。
- `F02_RollbackDoesNotMoveReference`：验证 `_lastDecayUtc` 不被回拨移动。
- `F02_RollbackAfterAdvance_ThenReturn_MatchesControl`：先推进 6h→回拨 2h→恢复→继续推进到 12h，与直接推进 12h 一致。

### 复核请求

F-02 已修正并测试覆盖"起点→回拨→恢复原时刻→超过原时刻"连续序列，与无回拨对照一致；覆盖亲近度、委屈值和重复回拨。请监理复核。

### R2 复核结果

监理 R2（2026-10-23，对象 `680befe`）已**关闭** F-02：确认负间隔直接返回快照、不移动有效时间基准；恢复到原时刻无额外衰减，超过原时刻只计算新时间。独立运行 Core 208/208 通过；用 .NET 10 PowerShell 加载实际 Core DLL 重跑原反例并加亲近度及无回拨对照，回拨后 80/90、基准仍为 `2026-10-23T12:00:00Z`，恢复原时刻仍为 80/90，再推进 12 小时委屈 70 与对照相同、亲近度差值 0。九项新增测试覆盖连续回拨/恢复/继续推进、两种情绪、重复回拨及基准不倒退，断言有实际区分度。

## 构建与测试

- `dotnet build DesktopPet.slnx -c Debug`：0 警告 0 错误。
- `dotnet test DesktopPet.slnx --no-build -c Debug`：Core 208 + App 82 = **290/290**，0 失败，0 跳过。
- 新增 38 项 Core 测试（F-01 12 + G-01 10 + G-02 7 + F-02 9）。
- 新增 15 项 App 运行时验证测试（G-01 衰减推进/暂停停止/多步一致性/表达标记/10秒持续/拖动打断/抚摸阻止/菜单阻止/重启恢复/v1回退）。

## App 接入运行时验证

### G-01 衰减推进

- `G01_DecayAdvancesAcrossMultipleTicks_GrievanceDecreases`：OnUpdateTick 中 AdvanceTo 实际推进衰减，多 tick 后浮点值持续下降。
- `G01_ManySmallTicks_MatchOneBigStep`：1000 步 vs 1 步总时间相同，结果一致（容差 0.001）。
- `G01_PauseStopsDecay_GrievanceUnchanged`：暂停时 OnUpdateTick 跳过 AdvanceTo，grievance 不变；恢复后继续衰减。

### G-02 角落防重复计分

- `G02_CornerStayConditionMet_AwardsGrievance_NoDoubleAward`：3 次角落拖动 + 30 秒停留 → +20 委屈 → Clear → 后续不重复加分。
- `G02_NewDragCancelsPreviousStay_OnlyLastStayCounts`：新拖动取消旧停留，只有最后释放后的停留计入 30 秒条件。
- `G02_LeaveCornerCancelsStay_ReenterStartsFresh`：离开角落取消停留，重新进入从头开始计 30 秒。
- `G02_DragInterruptsCornerTracking_InOnUpdateTick`：拖动时 OnUpdateTick 跳过角落跟踪，不触发计分。

### 委屈表达 10 秒持续与打断

- `G01_GrievanceExpression_Lasts10Seconds_ThenResets`：表达持续 10 秒，期间 pending 和 grievance 不变；10 秒后清空 pending + 重置 grievance 为 50。
- `G01_GrievanceExpression_InterruptedByDrag`：拖动打断表达 → 清空 pending + 重置 grievance 为 50（R-02）。
- `G01_GrievanceExpression_InterruptedByPetting`：抚摸候选状态阻止表达启动；结束后表达可启动。
- `G01_GrievanceExpression_InterruptedByMenu`：菜单打开阻止表达启动；关闭后表达可启动。

### 重启存档恢复

- `G01_RestartRestore_EmotionStateFromArchive`：从存档值恢复 accumulator 和 grievance expression 状态。
- `G01_V1ArchiveFallback_NeutralEmotionAndCurrentTime`：v1 存档回退默认值 50 + 当前时间作为衰减基准。

## 限制

- 整改不修改监理报告。
- IsInCornerZone 依赖真实显示器几何，测试中通过直接操作 CornerTracker 验证角落逻辑；OnUpdateTick 中的角落判定路径在无显示器环境下不走 IsInCornerZone=true 分支。
- 委屈表达使用 curious 动作作为占位，最终"背对用户"美术待后续阶段。
