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

## R3 阻断项整改：F-03～F-05 + G-03

对应监理 R3（`74ba984`），见 [P1-003-review](../reviews/P1-003-review.md) R3 章节。

### F-03：抚摸累计包含静止时间，且未在离开头部时结束

#### 问题

`PetCanvas_MouseMove` 中按下后只要单次 MouseMove 距离 <=8 就把整个事件间隔加入累计，按住静止两秒再微移 1 DIP 便累计两秒；moveDist=0 也满足条件。捕获后没有检查是否仍在有效头部区域。

#### 修复

- 位置：`src/DesktopPet.App/MainWindow.xaml.cs`。
- 新增 `PettingMinMovePx = 0.5`：低于此值视为零位移（静止），不累计。
- 新增 `PettingMaxInterEventMs = 500.0`：间隔超过 500ms 的有效微移不累计（长暂停不算连续抚摸）。
- `ProcessPettingMove` 在入口检查 `IsInHeadZone(localPos)`，离开头部时调用 `EndPetting()` 结束手势。
- `moveDist > PettingMaxMovePerFramePx` 时清除候选状态（转拖动由 `PetCanvas_MouseMove` 后续逻辑处理）。
- 头部区域仍为上 1/3，角色资源区域契约待后续阶段落实。

#### 自测

8 项手势链测试（`PettingGestureChainTests.cs`）：
- `F03_StationaryHoldThenSingleMicroMove_DoesNotAccumulate`：静止 2 秒后单次微移不累计。
- `F03_ContinuousMicroMoves_AccumulateToThreshold_TriggersPlus5`：持续有效微移达 1 秒 → +5 亲近度。
- `F03_MoveOutOfHeadZone_EndsPetting`：移出头部区域结束手势。
- `F03_LargeMovement_EndsPettingCandidate`：大幅移动清除候选状态。
- `F03_CooldownPreventsRepeatPlus5`：冷却内第二次 +5 不触发，冷却后第三次触发。
- `F03_ZeroDisplacement_DoesNotAccumulate`：零位移不累计。
- `F03_LongGapBetweenMoves_GapNotAccumulated`：长间隔不累计，短间隔累计。

### F-04：委屈表达缺少首次相遇条件和完整动作结束/打断

#### 问题

表达只检查日期和非拖动/菜单/抚摸，不检查鼠标是否进入交互范围；次日鼠标远处也启动并消耗待表达。达到十秒仅清标志/数值，不恢复 idle。拖动中断逻辑放在 EndDrag，按住拖动时表达标志可持续存在。

#### 修复

- 位置：`src/DesktopPet.App/MainWindow.xaml.cs`。
- 新增 `IsMouseInInteractionRange(screenX, screenY)`：检查鼠标是否在宠物实体 50 物理像素范围内（首次相遇条件）。
- `OnUpdateTick` 中 `ShouldExpress` 分支增加 `mouseInRange` 检查，远处不启动表达。
- 表达 10 秒完成后调用 `_animPlayer.PlayAction("idle")` 恢复 idle 动作。
- `InterruptGrievanceExpression()` 在 `PetCanvas_MouseLeftButtonDown` 中调用（拖动开始即打断，不等 EndDrag）。
- `InterruptGrievanceExpression()` 中调用 `_animPlayer.PlayAction("idle")` 恢复 idle。

#### 自测

已有表达测试已更新覆盖首次相遇、动作恢复、拖动按下即打断：
- `G01_GrievanceExpression_InterruptedByDrag`：验证拖动打断后 action 返回 idle。
- `G01_GrievanceExpression_Lasts10Seconds_ThenResets`：验证 10 秒后 action 返回 idle。
- `G01_GrievanceExpression_InterruptedByPetting`：使用 `TestGetCursorPos = (160, 160)` 确保鼠标在交互范围内。
- `G01_GrievanceExpression_InterruptedByMenu`：同上。

### F-05：所述抚摸增量与重启恢复尚无实际 App 链证据

#### 问题

测试以字面量构造累计器和表达对象，没有读写文件或执行 App 恢复映射。`Petting_AccumulatedTime_TriggersIncrement` 未断言实际亲近度 +5。

#### 修复

- 位置：`src/DesktopPet.App/App.xaml.cs`、`tests/DesktopPet.App.Tests/`。
- 抽出 `App.MapArchiveToEmotionObjects` 和 `App.MapEmotionObjectsToArchive` 静态方法，提供生产共用映射入口。
- `EndPetting` 已支持 `TestEmotionAccumulator` 注入，测试中可直接断言 +5。
- `Petting_AccumulatedTime_TriggersIncrement` 已修复：注入 `TestEmotionAccumulator`，断言 `AffinityInt == 55` 和冷却设置。

#### 自测

5 项隔离存档链测试（`IsolatedArchiveChainTests.cs`）：
- `F05_V2Archive_LoadPetSaveReload_AffinityPersisted`：v2 存档 → 加载 → +5 → 保存 → 重载 → 验证 55。
- `F05_V1Archive_DefaultEmotionValues_UpgradeToV2`：v1 → 默认值 → 保存为 v2 → 重载。
- `F05_PendingGrievanceExpression_PersistsAcrossSaveReload`：待表达状态跨保存/重载持久化。
- `F05_LastDecayUtc_PersistsAcrossSaveReload`：衰减基准时间跨保存/重载持久化。
- `F05_V2Archive_CornerAward_PersistsAcrossSaveReload`：角落 +20 委屈跨保存/重载持久化。

### G-03：暂停时间语义需准确记录

#### 问题

Pause 停止 tick，但 Resume 后 `AdvanceTo(DateTime.UtcNow)` 会补算暂停时段；表达和角落停留又使用继续流逝的 nowMs。语义未明确。

#### 修复

- 语义决策（已写入 PRODUCT.md R-02）："暂停时段不计入衰减和动作持续时间。"
- 位置：`src/DesktopPet.Core/Emotion/EmotionDecay.cs`、`src/DesktopPet.Core/Emotion/CornerTracker.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`。
- `EmotionDecayAccumulator.SkipInterval(TimeSpan)`：将 `_lastDecayUtc` 前移指定间隔，不执行衰减。
- `CornerTracker.SkipInterval(double ms)`：将所有时间戳前移指定毫秒，不修剪或判定条件。
- `ResumeAnimation()` 中计算暂停时长，对 accumulator 调用 `SkipInterval`，对 corner tracker 调用 `SkipInterval`，对 `_grievanceExpressionStartMs` 和 `_pettingCooldownUntilMs` 前移补偿。

#### 自测

5 项暂停语义测试（`PauseSemanticsTests.cs`）：
- `G03_PauseLongIntervalResume_DecayNotAdvancedDuringPause`：暂停期间衰减不推进，恢复后 `LastDecayUtc` 已前移。
- `G03_PauseDuringExpression_ExpressionNotCompletedByPause`：暂停期间表达不完成，恢复后继续计时。
- `G03_PauseDuringCornerStay_WindowNotConsumedByPause`：暂停期间角落窗口和停留不被消耗。
- `G03_AccumulatorSkipInterval_ShiftsReferenceWithoutDecay`：SkipInterval 前移基准不执行衰减。
- `G03_CornerTrackerSkipInterval_ShiftsTimestampsWithoutPruning`：SkipInterval 前移时间戳不修剪。

### 构建与测试（R3 整改后）

- `dotnet build DesktopPet.slnx -c Debug`：0 警告 0 错误。
- `dotnet test DesktopPet.slnx --no-build -c Debug`：Core 208 + App 104 = **312/312**，0 失败，0 跳过。
- App 测试新增 22 项（F-03 手势链 8 + F-05 隔离存档链 5 + G-03 暂停语义 5 + F-04 表达更新 4 项已有测试增强）。

### 复核请求

F-03/F-04/F-05/G-03 已按监理 R3 报告路径修复并补测试覆盖，请监理复核。

## R4 剩余范围整改：F-03～F-05 + G-03

对应监理 R4（`4ea4462`），见 [P1-003-review](../reviews/P1-003-review.md) R4 章节。
已认可的修复和测试保留，F-01/F-02 不重做。仅针对 R4 指出的剩余范围整改。

### F-03 R4 剩余：头部区域边界与资源约定

#### 剩余问题

R4 指出 IsInHeadZone 仍只检查 `Y <= height/3`，没有 X/Y 下界或 X 上界；头部区域仍硬编码，资源区域契约未落实。

#### 修复

- 位置：`src/DesktopPet.App/MainWindow.xaml.cs` 的 `IsInHeadZone`。
- **版本更正（R5）**：之前记录称"代码已有完整边界检查，R4 检查的旧版本问题已在 R3 整改中修复"是不准确的。经 R5 核对 `git show 4ea4462:src/DesktopPet.App/MainWindow.xaml.cs`，`4ea4462` 的 IsInHeadZone 仍仅检查 `Y <= height/3`，没有 X/Y 下界或 X 上界。完整 X/Y 边界检查（X ∈ [0, width]，Y ∈ [0, height/3]）是在 `6fee5a0` 中新增的。R4 关于当时版本的发现保留，当前修复认可。
- 资源区域契约已在 ARCHITECTURE §8 "P1-003 交互区域约定"中记录，代码注释引用该约定。
- 新增 3 项测试覆盖透明区域不继续累计、负 X 零 Y 组合、超大 X 值。

#### 自测

新增 3 项测试（`PettingGestureChainTests.cs`）：
- `F03_R4_TransparentArea_DoesNotContinueAccumulating`：从头部移到身体/透明区域，累计时间不增加。
- `F03_R4_NegativeXZeroY_NotHead`：负 X 零 Y 组合不算头部。
- `F03_R4_VeryLargeX_SmallY_NotHead`：超大 X（10000）小 Y 不算头部（R4 反例）。

### F-04 R4 剩余：表达优先级与快划覆盖

#### 剩余问题

R4 指出 tick 的悬停/甩动分支没有 `_isExpressingGrievance` 条件，表达开始后近处快划仍可 `PlayAction("dodge")`，表达状态继续为 true，十秒生命周期与实际动作脱节。

#### 修复

- 位置：`src/DesktopPet.App/MainWindow.xaml.cs` 的 `OnUpdateTick`。
- 在 hover/swipe 分支条件中添加 `!_isExpressingGrievance`，表达期间抑制悬停和甩动检测。
- 表达结束后（`_isExpressingGrievance = false`），hover/swipe 自动恢复正常工作。

#### 自测

新增 3 项测试（`GrievanceExpressionLifecycleTests.cs`）：
- `F04_R4_SwipeDuringExpression_DoesNotOverrideAction`：表达期间快划不覆盖表达动作，表达标志和计时器保持一致。
- `F04_R4_AfterExpressionCompletes_InputResumes`：表达完成后 hover/swipe 恢复正常工作。
- `F04_R4_HoverDuringExpression_DoesNotResetTimer`：表达期间 hover 不重置表达开始时间。

修正 1 项已有测试：
- `F04_AfterCompletion_NoRepeatExpression`：表达完成后 hover 可触发 curious（正确行为），断言改为"不重启表达"而非"保持 idle"。

### F-05 R4 剩余：生产复用已测映射

#### 剩余问题

R4 指出 `MapArchiveToEmotionObjects`/`MapEmotionObjectsToArchive` 只有定义，`OnStartup`/`MarkStateDirty` 仍保留各自独立的内联映射，测试调用的是生产未使用的另一份映射。

#### 修复

- 位置：`src/DesktopPet.App/App.xaml.cs`。
- `OnStartup` 改为调用 `MapArchiveToEmotionObjects(_state)` 初始化情绪对象，不再内联映射。
- `MarkStateDirty` 改为调用 `MapEmotionObjectsToArchive(_state, EmotionAccumulator, GrievanceExpression)`，不再内联映射。
- 生产启动和保存路径现在与测试使用同一份映射方法，消除两套维护。

#### 自测

新增 1 项完整链测试（`IsolatedArchiveChainTests.cs`）：
- `F05_R4_RealPettingChain_PersistedViaProductionMapping`：真实 ProcessPettingMove/EndPetting 手势计分 +5 → 生产映射保存 → 隔离存档 → 重载 → 生产映射恢复 → 验证 55。闭合报告所声称的互动链。

### G-03 R4 剩余：统一时间源暂停验证

#### 剩余问题

R4 指出现有测试用 Thread.Sleep 推进真实 UTC、另用 clockMs 推进不同长度单调时间，不能证明精确排除暂停段。需统一可控 UTC/单调时间来源，并覆盖暂停中退出/启动仍暂停后恢复边界。

#### 修复

- 位置：`src/DesktopPet.App/MainWindow.xaml.cs`。
- 新增 `TestGetUtcNow` 注入点：当非 null 时，`OnUpdateTick` 中 `emotionAcc.AdvanceTo` 使用注入的 UTC 而非 `DateTime.UtcNow`。
- 生产代码不受影响（`TestGetUtcNow` 默认 null，使用 `DateTime.UtcNow`）。
- 测试中 `TestGetElapsedMs`（单调时钟）和 `TestGetUtcNow`（UTC）使用同一时间源推进，消除分歧。

#### 自测

新增 3 项统一时间源测试（`PauseSemanticsTests.cs`）：
- `G03_R4_UnifiedTimeSource_PauseResume_DecayMatchesControl`：统一时间源下暂停→恢复，衰减量与无暂停对照一致（容差 0.01，因 33ms 粒度取整）。
- `G03_R4_PauseExitRestartPaused_Resume_NoDoubleCountDecay`：暂停中退出→重启仍暂停→恢复，暂停间隔不计入衰减，总衰减匹配 4s 活跃时间。
- `G03_R4_UnifiedTimeSource_PauseDuringExpression_CompletesAtActive10s`：统一时间源下表达期间暂停，恢复后表达在 10s 活跃时间（非墙钟时间）完成。

### 构建与测试（R4 整改后）

- `dotnet build DesktopPet.slnx -c Debug`：0 警告 0 错误。
- `dotnet test DesktopPet.slnx --no-build -c Debug`：Core 208 + App 120 = **328/328**，0 失败，0 跳过。
- App 测试新增 10 项（F-03 R4 3 + F-04 R4 3 + F-05 R4 1 + G-03 R4 3），修正 1 项已有测试。

### 复核请求

F-03/F-04/F-05/G-03 的 R4 剩余范围已整改并补测试覆盖，请监理复核。

## R5 剩余范围整改：F-03 + G-03 + 版本归因更正

对应监理 R5（`6fee5a0`），见 [P1-003-review](../reviews/P1-003-review.md) R5 章节。
F-04/F-05 已关闭，不重复整改。仅针对 R5 指出的剩余范围整改。

### F-04 关闭依据（R5）

监理 R5 已**关闭** F-04：hover/swipe 分支已有 `!_isExpressingGrievance`，表达期间动作/计时不被覆盖；结束后输入恢复的断言与正常 hover 重新触发相容。不重做此前通过部分。

### F-05 关闭依据（R5）

监理 R5 已**关闭** F-05：OnStartup/MarkStateDirty 已分别实际调用共用映射，原重复内联消除；F05_R4_RealPettingChain_PersistedViaProductionMapping 驱动生产 ProcessPettingMove/EndPetting 增加至 55，再经共用映射、隔离文件保存/重载、共用恢复验证。不重做此前通过部分。

### F-03 R5 剩余：头部矩形内透明像素检测

#### 剩余问题

R5 指出 IsInHeadZone 只检查矩形边界，未结合当前帧 Alpha。测试 F03_R4_TransparentArea_DoesNotContinueAccumulating 移到 (70,80) 是下方身体区，不能证明"上 1/3 内透明空白"的行为。

#### 修复

- 位置：`src/DesktopPet.App/MainWindow.xaml.cs` 的 `IsInHeadZone`。
- IsInHeadZone 在矩形边界检查通过后，额外调用 `PetImageGeometry.Contains(PetImage, localPoint)` 检查当前帧该坐标的 Alpha 值。透明像素（Alpha=0）不算头部，防止在头部矩形内的空白处继续累计抚摸时间。
- ARCHITECTURE §8 已同步补充 Alpha 检查说明。
- PRODUCT R-02 已同步：当前头部区域为图像矩形内上 1/3 且需命中当前帧不透明像素（固定方案），角色 JSON 中的 per-character 交互区域字段待后续阶段加入。

#### 自测

新增 2 项 R5 测试（`PettingGestureChainTests.cs`）：
- `F03_R5_TransparentInHeadRect_NotHead_AdjacentOpaque_IsHead`：(60,5) 透明 → false，(60,20) 不透明 → true，对照验证。
- `F03_R5_TransparentInHeadRect_EndsPetting_NoAccumulation`：从不透明头部像素移动到头部矩形内透明区域，抚摸结束且累计时间不增加。

修正 4 项已有测试（坐标适配 Alpha 检查）：
- `Petting_HeadZone_UpperThird_IsHead`：(60,10) 透明 → 改为 (60,20) 不透明。
- `F03_ContinuousMicroMoves_AccumulateToThreshold_TriggersPlus5`：pingpong 范围从 60..75 改为 60..67（最大步距 7 < 8 DIP）。
- `F03_CooldownPreventsRepeatPlus5`：同上 pingpong 范围修正。
- `F05_R4_RealPettingChain_PersistedViaProductionMapping`：移动序列从 `60+i*2` 改为 `60+(i%8)` pingpong。

### G-03 R5 剩余：跨对象隔离恢复验证

#### 剩余问题

R5 指出 G03_R4_PauseExitRestartPaused_Resume_NoDoubleCountDecay 全程仍使用同一个 window/testAcc，只以注释模拟退出重启。需补真正的隔离状态保存→关闭旧对象→共用映射恢复新对象/新窗口→仍暂停→恢复断言。

#### 修复

- 语义明确：暂停状态退出期间属于冻结时段，与活跃暂停同等对待（PRODUCT "暂停时段不计入衰减"）。存档保存的 LastDecayUtc 不因退出而推进，恢复后新 accumulator 从保存的基准继续，退出间隔不计入衰减。
- 新增 `G03_R5_IsolatedSaveCloseRestore_ExitPaused_NoDoubleCountDecay` 测试（`PauseSemanticsTests.cs`）：
  1. 创建 window1 + accumulator1，运行 2s 活跃 tick。
  2. 暂停 window1。
  3. 通过 `MapEmotionObjectsToArchive` + `ArchiveService` 保存到隔离临时目录。
  4. 关闭 window1（旧对象释放）。
  5. 推进统一时间 10s（模拟退出期间）。
  6. 从隔离存档加载，通过 `MapArchiveToEmotionObjects` 恢复新 accumulator2（新对象、新单调时钟）。
  7. 创建 window2，注入 accumulator2，设为暂停（模拟 OnStartup 检测 IsPaused=true）。
  8. 恢复 window2，运行 2s 活跃 tick。
  9. 断言恢复后衰减只匹配 2s 活跃时间，10s 退出间隔不计入。

#### 自测

- 1 项新增测试通过：`G03_R5_IsolatedSaveCloseRestore_ExitPaused_NoDoubleCountDecay`。
- 真正执行了保存→关闭→重载→新对象恢复链路，不再复用同一对象和注释模拟。

### 版本归因更正

R5 版本事实核对发现，之前 F-03 R4 整改记录称"代码已有完整边界检查，R4 检查的旧版本问题已在 R3 整改中修复"是不准确的。实际 `git show 4ea4462:src/DesktopPet.App/MainWindow.xaml.cs` 的 IsInHeadZone 仍仅 `Y <= height/3`；`git diff 4ea4462..6fee5a0` 明确显示完整 X/Y 边界在 `6fee5a0` 新增。已在 F-03 R4 整改记录中追加版本更正说明，保留旧记录不删除，不修改监理报告。

### 构建与测试（R5 整改后）

- `dotnet build DesktopPet.slnx -c Debug`：0 警告 0 错误。
- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj -c Debug --no-build --no-restore`：**App 124/124**，0 失败，0 跳过。
- App 测试新增 1 项（G-03 R5 1），修正 4 项已有测试（F-03 坐标适配 + F-05 坐标适配）。
- Core 208 项未重跑，不把开发方全量写成独立全量通过。

### 复核请求

F-03 R5 剩余范围（头部矩形内透明像素检测 + PRODUCT 同步）和 G-03 R5 剩余范围（跨对象隔离恢复验证）已整改并补测试覆盖。版本归因更正已追加。F-04/F-05 已由 R5 关闭，不重复整改。请监理复核。
