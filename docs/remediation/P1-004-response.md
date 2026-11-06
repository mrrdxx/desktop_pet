# P1-004 Core 首步整改记录

日期：2026-11-04（Asia/Shanghai）。开发方按 `docs/reviews/P1-004-review.md` 的 F-01～F-04 及 G-01 进行整改，补针对性测试并重跑 Core 集合。不修改监理报告，不自行关闭问题。

## F-01 高 跨日前进再回拨仍会对已用日期触发

**问题**：`Evaluate` 没有日期一致性检查，回拨到已消耗日期仍可触发。

**修复位置**：`src/DesktopPet.Core/Events/RareEventScheduler.cs` — `Evaluate` 方法新增 `currentLocalDate` 参数（line 136-142），在运行时间校验之后、状态检查之前进行日期一致性检查（line 155-170）：
- `currentLocalDate` 无法解析 → `InvalidDate`
- `currentDate < planDate` → `DateRollback`，不消耗计划
- `currentDate > planDate` → `DateAdvanced`，不消耗计划

`DenyReason` 枚举新增 `DateRollback`、`DateAdvanced`、`InvalidRuntime`、`InvalidPlan`、`InvalidDate`（line 28-35）。

**针对性测试**（`RareEventSchedulerTests.cs`）：
- `Evaluate_DateMismatch_Rollback_DeniesAndDoesNotConsume`：计划 Date2，评估 Date1 → DateRollback，不消耗。
- `Evaluate_DateMismatch_Advanced_DeniesAndDoesNotConsume`：计划 Date1，评估 Date2 → DateAdvanced，不消耗。
- `F01_ContinuousSequence_Day1Fire_Day2New_RollbackDay1_Deny_BackToDay2`：完整连续序列——日1触发消耗→日2新建→回拨日1 EnsurePlanForDate 返回 false + Evaluate 返回 DateRollback 不消耗日2计划→回到日2正常评估。
- `F01_ConsumedDay1_RollbackToDay1_DoesNotReFire`：已消耗日再次评估返回 PlanConsumed。
- `F01_RestoreConsumedPlan_Rollback_DoesNotReFire`：快照恢复后回拨评估返回 DateRollback。

**自测证据**：239/239 通过，0 失败/跳过。构建 0 警告/错误。

## F-02 中 运行时累计以 UTC 墙钟计时，未落实单调时间契约

**问题**：`EffectiveRuntimeTracker` 的 `currentUtc` 输入及 `UpdateTo` 差值累计使用 UTC 墙钟，系统时钟调整会导致错误累计。

**修复位置**：`src/DesktopPet.Core/Events/EffectiveRuntimeTracker.cs` — 全文重写为单调时间契约：
- `StartActivePeriod(double monotonicMinutes)` / `EndActivePeriod(double monotonicMinutes)` / `UpdateTo(double monotonicMinutes)` 输入改为 double 单调分钟值。
- `Restore(double accumulatedMinutes)` 只接受一个参数（累计总量），不恢复活跃引用。
- 公开属性 `LastActiveMonotonicMinutes`（替代旧 `LastActiveUtc`）。
- 类注释明确：单调时间源（如 `Stopwatch.GetTimestamp()`），UTC 仅用于日期判定，不用于活跃计时。

**针对性测试**（`EffectiveRuntimeTrackerTests.cs`）：
- `UpdateTo_MonotonicClockAlwaysAdvances_UtcJumpIrrelevant`：UTC 跳变 12 小时但单调时钟仅前进 5 分钟 → 只累计 5 分钟。
- `UpdateTo_MonotonicClockDoesNotGoBackward`：计数器回退 → 不负累计，引用不后退。
- 全部测试使用 double 单调分钟值，不再使用 DateTime。

**自测证据**：239/239 通过。

## F-03 中 无效快照和非法数值进入触发路径

**问题**：`RestorePlan` 接受无效日期/NaN/越界延迟的快照；`Evaluate` 对 NaN 运行时间仍可触发。

**修复位置**：
- `RareEventPlan.IsValid`（line 46-53）：校验日期可解析、延迟 [0, MaxTriggerDelayMinutes]、Roll 有限且 [0, 1)。
- `RareEventScheduler.RestorePlan`（line 223-236）：仅接受 `IsValid` 通过的快照，拒绝时返回 false 且保留现有有效计划。
- `RareEventScheduler.Evaluate`（line 148-149）：拒绝 NaN/Infinity/负运行时间 → `InvalidRuntime`，不消耗计划。
- `RareEventScheduler.Evaluate`（line 152-153）：拒绝无效计划 → `InvalidPlan`。
- `EffectiveRuntimeTracker.UpdateTo`（line 91-92）：拒绝 NaN/Infinity，不修改状态。
- `EffectiveRuntimeTracker.Restore`（line 121-124）：NaN/Infinity/负值钳位为 0。

**针对性测试**（`RareEventSchedulerTests.cs`）：
- `F03_RestorePlan_BadDate_Rejected` / `F03_RestorePlan_NanRoll_Rejected` / `F03_RestorePlan_OutOfRangeDelay_Rejected` / `F03_RestorePlan_NegativeDelay_Rejected`：各类无效快照被拒绝，CurrentPlan 为 null。
- `F03_RestorePlan_InvalidPlan_PreservesExistingValidPlan`：无效快照不覆盖已有有效计划。
- `F03_Evaluate_NaNRuntime_Rejected` / `F03_Evaluate_InfinityRuntime_Rejected` / `F03_Evaluate_NegativeRuntime_Rejected`：非法运行时间被拒绝，不消耗计划。
- `F03_Evaluate_InvalidDate_Rejected`：非法日期字符串返回 InvalidDate。
- `F03_RestorePlan_ValidPlan_AcceptedAndFunctional`：有效快照被接受且可正常触发。

**针对性测试**（`EffectiveRuntimeTrackerTests.cs`）：
- `UpdateTo_NaN_RejectedWithoutStateChange` / `UpdateTo_Infinity_RejectedWithoutStateChange` / `UpdateTo_NegativeInfinity_RejectedWithoutStateChange`。
- `Restore_NaN_ClampedToZero` / `Restore_Infinity_ClampedToZero` / `Restore_Negative_ClampedToZero` / `Restore_ValidValue_Accepted`。

**自测证据**：239/239 通过。

## F-04 中 完成报告中的文档更新未反映在当前磁盘

**问题**：STATUS 仍称"首步未开始实现"，TASKS P1-004 进度描述过时；ARCHITECTURE 交互区域段落仍说"清空/累计抚摸时间"与 D-013 不一致。

**修复位置**：
- `docs/STATUS.md`：更新为当前整改状态（F-01～F-04 整改完成、239/239 测试通过、整改记录已写入）。
- `docs/TASKS.md` P1-004 进度段落：更新为当前 API 签名和测试结果。
- `docs/ARCHITECTURE.md` Events 段落（§186-189）：更新 `Evaluate` 签名含 `currentLocalDate`；`EffectiveRuntimeTracker` 描述单调时间契约和 G-01 离线契约；`RareEventPlan` 描述 `IsValid` 校验。
- `docs/ARCHITECTURE.md` 交互区域段落（§191-197）：清理 D-013 冲突——"抚摸命中"→"手势命中区"、"累计抚摸时间"→"手势候选状态"、"清空抚摸累计值"→"清空手势候选状态"、添加 D-013 说明。

**验证**：保存后重新读取确认内容一致。

## G-01 接入前需要明确的时间契约

**问题**：恢复 45 分钟累计值后首次 UpdateTo 可能将离线时间计入有效运行时间；未明确累计是"当日"还是"整个会话"。

**修复位置**：
- `EffectiveRuntimeTracker` 类注释（line 1-33）：明确离线不计有效运行时间、Restore 后必须调 StartActivePeriod、累计为会话级跨日不重设。
- `Restore` 方法注释（line 106-118）：明确仅恢复累计总量，不恢复活跃引用。
- `StartActivePeriod` 方法注释（line 44-54）：明确 Restore 后必须调用。

**针对性测试**（`EffectiveRuntimeTrackerTests.cs`）：
- `Restore_ThenStartActivePeriod_OfflineTimeNotCounted`：Restore(45) → StartActivePeriod(1000) → UpdateTo(1015) = 60，不是 1060。
- `Restore_WithoutStartActivePeriod_UpdateToReturnsAccumulatedOnly`：未调 StartActivePeriod 时 UpdateTo 只返回已累计值。
- `Restore_ThenStartAndPause_OnlyActiveTimeAdded`：完整生命周期 restore→start→active→pause→resume→active，暂停不计。
- `AccumulatedMinutes_AreSessionLevel_NotPerDayReset`：跨日累计不重设。

**自测证据**：239/239 通过。

## 验证总结

- Core 全量测试：239/239 通过（原 215 + 新增 24 项），0 失败/跳过。
- 构建：0 警告/错误。
- 未修改 `PetStateArchive`（正式存档格式不变）；未接 App 层。
- 未修改监理报告 `docs/reviews/P1-004-review.md`。
- 未提交、未推送。

## 复核请求

请监理复核 F-01～F-04 整改及 G-01 契约明确是否满足验收要求。开发方不自行关闭问题，等待监理结论。

## R1 整改：F-03/F-04/F-05（2026-11-04）

监理 R1 关闭 F-01/F-02，F-03/F-04 部分通过，新增 F-05。本轮只整改剩余项，不重复已关闭项。

### F-03 剩余：Start/End 有限值校验

**问题**：`StartActivePeriod(double.NaN)` 仍安装 NaN 引用，使后续 `UpdateTo(10)` 一直返回 0；`EndActivePeriod(double.PositiveInfinity)` 会把累计值写成 Infinity。

**修复位置**：`src/DesktopPet.Core/Events/EffectiveRuntimeTracker.cs`
- `StartActivePeriod`（line 51-57）：新增 NaN/Infinity/负值校验，拒绝时不修改 `_lastActiveMonotonicMinutes`，保留此前有效引用。
- `EndActivePeriod`（line 61-75）：新增 NaN/Infinity/负值校验，拒绝时不修改累计值和活跃引用，保留当前活跃状态。

**针对性测试**（`EffectiveRuntimeTrackerTests.cs`）：
- `F03_StartActivePeriod_NaN_RejectedWithoutStateChange`：活跃状态下 Start(NaN) 不替换引用，后续 UpdateTo 正常累计。
- `F03_StartActivePeriod_Infinity_RejectedWithoutStateChange`：同上，Infinity 被拒绝。
- `F03_StartActivePeriod_Negative_RejectedWithoutStateChange`：负值被拒绝。
- `F03_EndActivePeriod_NaN_RejectedWithoutStateChange`：活跃状态下 End(NaN) 不结束活跃区间，不修改累计。
- `F03_EndActivePeriod_Infinity_RejectedWithoutStateChange`：Infinity 不写入累计。
- `F03_EndActivePeriod_Negative_RejectedWithoutStateChange`：负值被拒绝。
- `F03_StartEnd_InvalidThenNormal_ContinuesCorrectly`：完整生命周期——valid start → accumulate → invalid start (rejected) → continue → invalid end (rejected) → valid end → resume → accumulate，验证不污染已有有效状态。

**自测证据**：253/253 通过，0 失败/跳过。构建 0 警告/错误。

### F-04 剩余：文档修正与磁盘验证

**问题**：STATUS 仍写"独立Core215""整改记录尚待建立"，TASKS 审查描述仍为首轮 215 及旧问题范围，未反映 R1 后实际状态。

**修复位置**：
- `docs/STATUS.md`：第7行更新为 R1 后实际状态（F-01/F-02 已关闭、F-03/F-04/F-05 整改）；第9-15行更新为 R1 整改内容；第19行更新下一步引用；第25行更新工作区描述。
- `docs/TASKS.md`：第144-148行 P1-004 进度段落更新为 R1 后实际 API 签名、测试结果和整改范围。
- `docs/ARCHITECTURE.md`：第187-189行 Events 段落更新 `RareEventPlan`（含 `AccumulatedBaselineMinutes`）、`RareEventScheduler`（含 `currentAccumulatedMinutes` 参数和 F-05 当日差值评估）、`EffectiveRuntimeTracker`（含 F-03 Start/End 校验和 F-05 差值说明）。

**磁盘验证**：保存后重新读取三份文档，确认内容正确写入磁盘。STATUS 第7行已为 R1 后状态，TASKS 第144-148行已更新，ARCHITECTURE 第187-189行已含 F-05 字段和 F-03 校验描述。

### F-05 新增：每日计划没有当日有效时间基准

**问题**：日1会话有效总量130分钟已触发，日2创建延迟20分钟的新计划，当日新增有效运行时间为0，将既有130直接传入 Evaluate 便立即触发。每日计划延迟被解释为相对整个会话起点，而非当日有效运行时间。

**修复位置**：
- `src/DesktopPet.Core/Events/RareEventPlan.cs`：
  - 新增 `AccumulatedBaselineMinutes` 字段（line 30-37）：计划创建时的会话累计值，当日有效时间 = 当前累计 - 基准。
  - `CreateNew` 新增 `accumulatedBaselineMinutes` 参数（line 66-88），NaN/Infinity/负值钳位为 0。
  - `AsConsumed` 保留 `AccumulatedBaselineMinutes`（line 103-109）。
  - `IsValid` 新增 Baseline 有限非负校验（line 46-55）。
- `src/DesktopPet.Core/Events/RareEventScheduler.cs`：
  - `EnsurePlanForDate` 新增 `currentAccumulatedMinutes` 参数（line 87-113），传入 `CreateNew` 作为基准。
  - `Evaluate` 延迟比较改为 `dailyEffectiveMinutes = effectiveRuntimeMinutes - _plan.AccumulatedBaselineMinutes`（line 187-193），与 `TriggerDelayMinutes` 比较决定延迟是否到达。
- `src/DesktopPet.Core/Events/EffectiveRuntimeTracker.cs`：
  - 类注释更新（line 25-33）：明确每日计划用差值（当前累计 - 基准）评估当日延迟，不是整个会话累计。

**针对性测试**（`RareEventSchedulerTests.cs`）：
- `F05_Day1Fire_Day2NewPlan_DelayRelativeToDay2Baseline`：日1延迟20基准0→130触发→日2延迟20基准130→累计130/149不触发→累计150触发。完整组合验证。
- `F05_Day2DelayNotReached_DoesNotConsumePlan`：延迟未达不消耗计划。
- `F05_SnapshotRestore_PreservesBaselineAndDelay`：快照保存→重启恢复→基准保留→延迟检查一致。
- `F05_FirstDay_BaselineZero_WorksAsBefore`：首日基准0，行为与 F-05 前一致。
- `F05_RollbackAndForward_BaselineNotAffected`：F-05 不破坏 F-01 回拨/前进行为。
- `F05_InvalidBaseline_RejectedByRestorePlan`：NaN 基准被 IsValid 拒绝。
- `F05_NegativeBaseline_RejectedByRestorePlan`：负基准被 IsValid 拒绝。

**自测证据**：253/253 通过（原 239 + 新增 14 项 F-03/F-05 针对性测试），0 失败/跳过。构建 0 警告/错误。

## R1 整改验证总结

- Core 全量测试：253/253 通过，0 失败/跳过。
- 构建：0 警告/错误。
- 未修改 `PetStateArchive`（正式存档格式不变）；未接 App 层。
- 未修改监理报告 `docs/reviews/P1-004-review.md`。
- 未提交、未推送。
- 文档已从磁盘重读验证。

## R1 复核请求

请监理复核 F-03（Start/End 有限值校验）、F-04（文档修正与磁盘验证）、F-05（每日有效时间基准）整改是否满足验收要求。开发方不自行关闭问题，等待监理结论。

## R2 关闭依据（2026-11-04）

监理 R2 独立运行 Core 253/253 通过，0 失败/跳过，构建无警告/错误；另有 12 项实际 Core 组合检查全部通过。未运行 App，未修改产品代码、正式存档或 Git 历史。

- **F-03 通过并关闭**：Start/End 拒绝 NaN、正负 Infinity 和负值，不修改有效累计/活跃引用。独立验证有效活跃状态中混入非法调用后仍能正确累计，新增 7 项针对性测试亦通过。
- **F-05 通过并关闭**：计划快照增加 `AccumulatedBaselineMinutes`，`EnsurePlanForDate` 传入当前会话累计，`Evaluate` 使用差值；`AsConsumed` 保留基准，恢复校验基准有限非负。独立从真实 `Ensure`/`CreateNew` 调用建立日2基准130，累计130/149均不触发；经过 JSON 序列化/反序列化、恢复累计与新单调基准后，总量150时才评估触发。回拨拒绝保持，原反例修复。
- **F-04 按最终结果关闭，明确归属**：ARCHITECTURE 新字段/签名/手势说明已同步。监理直接补齐 STATUS/TASKS 摘要并重新读取核对，保留原问题和受审快照。
- F-01/F-02 维持 R1 关闭；G-01 离线及每日差值契约按现有实现接受。

本轮结论：**Core 首步接受，F-01～F-05 全部关闭**。解除原 Core 问题对后续存档/App 步骤的阻断。开发方仅追加本次关闭依据，不重复申请已关闭问题。

## R3 整改：F-06/F-07（2026-11-05）

监理 R3 仅提出 F-06/F-07，F-01～F-05 保持关闭。本轮只整改 F-06/F-07，不重复已关闭项。暂不接 OnUpdateTick 或探头表现，不提交、不推送。

### F-06 生产持久化适配入口

**问题**：存档恢复只有测试内映射，未建立可复用生产闭环。测试手工构造 `RareEventPlanSnapshot` 并在测试内手工转 `RareEventPlan`，不读取 `loaded.AccumulatedRuntimeMinutes` 恢复 `EffectiveRuntimeTracker`。

**修复位置**：`src/DesktopPet.App/App.xaml.cs`
- 新增 `MapArchiveToRareEventObjects(PetStateArchive state)` 静态方法（约 line 85-110）：从存档恢复 `EffectiveRuntimeTracker`（`Restore(state.AccumulatedRuntimeMinutes)`）和 `RareEventScheduler`（`RestorePlan` 从 `RareEventPlanSnapshot` 转 `RareEventPlan`）。这是生产共用恢复入口，测试通过此方法验证，不另写平行转换。
- 新增 `MapRareEventObjectsToArchive(PetStateArchive state, EffectiveRuntimeTracker tracker, RareEventScheduler scheduler)` 静态方法（约 line 112-130）：将 tracker 累计和 scheduler 计划快照写入存档。这是生产共用保存入口。
- 非法计划处理：`RestorePlan` 仅接受 `IsValid` 通过的快照，非法计划被拒绝且 `scheduler.CurrentPlan` 为 null，生产映射不崩溃。
- 基准大于累计等不一致数据：`tracker.Restore` 恢复累计值，`Evaluate` 用差值（累计 - 基准）评估，负差值自然得到 `DelayNotReached`，不崩溃。

**针对性测试**（`tests/DesktopPet.App.Tests/RareEventPersistenceTests.cs`，6 项）：
- `F06_ProductionChain_SaveReload_RemainingWaitPreserved`：真实 `EnsurePlanForDate` + `EffectiveRuntimeTracker` 运行 → 生产入口保存到隔离目录 → 新 `ArchiveService` 加载 → 生产入口恢复 → 验证同日不重抽（`EnsurePlanForDate` 返回 false）、还差 5 分钟时新增 4 不触发（`DelayNotReached`）、新增 5 才评估（fire 或 roll-failed）。
- `F06_ProductionChain_OfflineTimeNotCounted`：保存 50 分钟累计 → 重载恢复 → `StartActivePeriod(0)` + `UpdateTo(10)` → 总计 60（不是 60 + 离线间隔）→ 计划基准保留 50 → dailyEffective=10 < delay → 不触发。
- `F06_ProductionChain_ConsumedPlanDoesNotRefire`：运行到延迟达标记 Evaluate 消耗计划 → 保存 → 重载恢复 → `Consumed` 标志保留 → 再次 Evaluate 返回 `PlanConsumed`。
- `F06_ProductionChain_NullPlan_SurvivesReload_FreshPlanNextDay`：null 计划保存 → 重载恢复为 null → 次日 `EnsurePlanForDate` 创建新计划。
- `F06_ProductionChain_InvalidPlanSnapshot_RejectedGracefully`：非法日期快照 → 生产映射恢复 → `RestorePlan` 拒绝 → `scheduler.CurrentPlan` 为 null。
- `F06_ProductionChain_BaselineExceedsAccumulated_NoCrash`：基准 100 > 累计 30 → 生产映射恢复 → Evaluate 差值 -70 < delay → `DelayNotReached`，不崩溃。

**自测证据**：App 174/174 通过（原 168 + 新增 6 项 F-06 测试），0 失败/跳过。构建 0 警告/错误。

### F-07 文档同步 v3 并清除 affinity

**问题**：ARCHITECTURE §7 仍写 state.json v2、默认 2，并列出已取消的 affinity 字段；PRODUCT R-02 仍写"从 v1 演进到 v2"并列出 affinity；PetStateArchive 类头注释仍称 v2。

**修复位置**：
- `docs/ARCHITECTURE.md` §7（约 line 125-155）：
  - "state.json v2" → "state.json v3"，`schemaVersion` 默认 3。
  - 删除 `affinity` 字段行。
  - 新增 `accumulatedRuntimeMinutes` 和 `rareEventPlan` 字段说明。
  - 恢复规则新增 v1→v2 升级、v1/v2→v3 升级、F-06 稀有事件恢复约定段落。
- `docs/PRODUCT.md` R-02（约 line 80-86）：
  - "从 v1 演进到 v2" → "从 v1 演进到 v3（完整格式见 ARCHITECTURE §7）"。
  - 删除 `affinity` 字段行，新增"亲近度（affinity）已按 D-013 取消，不再持久化或恢复"。
  - 新增 v3 稀有事件字段说明。
- `src/DesktopPet.Core/Persistence/PetStateArchive.cs` 类头注释：v2 → v3，补充 v2 文件读取时稀有事件字段回退默认值。

**磁盘验证**：保存后重新读取三份文件，确认内容正确写入磁盘。ARCHITECTURE 中 `affinity` 已完全清除（grep 无结果）；PRODUCT 中仅剩第 84 行说明性文字"亲近度（affinity）已按 D-013 取消"，非字段定义。

### R3 整改验证总结

- 全量测试：Core 253 + App 174 = 427/427 通过，0 失败/跳过。构建 0 警告/错误。
- 未修改监理报告 `docs/reviews/P1-004-review.md`。
- 未接 OnUpdateTick 或探头表现。未提交、未推送。

### R3 复核请求

请监理复核 F-06（生产持久化适配入口）和 F-07（文档同步 v3 并清除 affinity）整改是否满足验收要求。开发方不自行关闭问题，等待监理结论。

## R4 整改：F-07 剩余文档一致性（2026-11-05）

监理 R4 关闭 F-06，F-07 部分通过：存档段已改 v3 并删除 affinity 字段，但 R-02 前半段仍有亲近度数值、抚摸 +5/2 秒冷却、>= 70 与 D-008 阶段 2 靠近、7 天半衰期等 D-013 已取消内容，与后面的"affinity 已按 D-013 取消"自相矛盾。本轮仅清理这些冲突，F-01～F-06 不重做，不改产品代码、不重跑全套测试、不接探头、不提交或推送。

### 保存冲突处理

监理 R4 报告了 IDE 未保存版本与磁盘版本的保存冲突。按监理指示：先确认磁盘版本为最新（监理已按用户授权校准 STATUS/TASKS），不点击"覆盖"，不恢复旧全文。本轮以磁盘文件和监理 R4 为准进行编辑。

### F-07 剩余清理

**问题**：PRODUCT R-02 前半段（细化参数段落）仍包含 D-013 已取消的亲近度计分、衰减和主动靠近规格。

**修复位置**：`docs/PRODUCT.md` R-02 细化参数段落：
- 第 58 行："单次抚摸增量 +5 亲近度，冷却 2 秒" → "抚摸计分（亲近度）已按 D-013 取消，不再累计或持久化"。
- 第 59 行："亲近度阈值 >= 70 为高亲近，主动靠近行为安排在阶段 2" → "亲近度已按 D-013 取消。高亲近主动靠近行为按 D-008 延后到阶段 2，尚未实现"。
- 第 74 行："亲近度向中性 50 回落：半衰期 7 天" → "亲近度已按 D-013 取消，不再有亲近度衰减"。

保留的委屈相关规格（阈值 >= 80、角落计分 +20、委屈表达、委屈衰减半衰期 12 小时、暂停语义、存档演进 v3）不变。

**磁盘验证**：保存后重新读取 PRODUCT R-02 全文（第 56-90 行），确认三处修改正确写入磁盘。grep 检查无亲近度数值/半衰期/计分矛盾残留。

### R4 整改验证总结

- 未修改产品代码、未重跑测试、未接探头。未提交、未推送。
- 未修改监理报告 `docs/reviews/P1-004-review.md`。
- F-01～F-06 保持关闭，不重复整改。

### R4 复核请求

请监理复核 F-07 剩余文档一致性清理是否满足验收要求。开发方不自行关闭问题，等待监理结论。

## R4 补充整改：F-07 R-02 主体段落 D-013 矛盾清理（2026-11-05）

监理 R4 指出 F-07 仅剩文档一致性：R3 整改已清理"细化参数"段落中的亲近度计分/衰减/阈值，但 R-02 主体段落（第 49、53 行）仍有亲近度保存、"抚摸增加亲近度"和"高亲近主动靠近延后到阶段 2（D-008）"等 D-013 已取消内容，与后文"affinity 已按 D-013 取消"自相矛盾。第 59 行"高亲近主动靠近行为按 D-008 延后到阶段 2"也与 D-013 完全取消矛盾。

### 保存冲突处理

编辑器未保存缓冲区已不可用（窗口重启），无独有内容需备份。以磁盘文件（监理已按用户授权校准 STATUS/TASKS）和监理 R4 为准进行编辑，未点击"覆盖"，未恢复旧全文。

### F-07 主体段落清理

**修复位置**：`docs/PRODUCT.md` R-02 段落：

- 第 49 行："每只宠物独立保存亲近度和委屈值" → "每只宠物独立保存委屈值……亲近度（affinity）已按 D-013 完全移除，不再保存、计分或持久化"。
- 第 52-53 行："头部按住未移动或短按松开不增加亲近度" 补充"亲近度计分已按 D-013 完全移除，头部小幅手势不再计分"；"抚摸增加亲近度。高亲近主动靠近鼠标由用户于 2026-10-24 明确延后到阶段 2……决策见 D-008" → "抚摸不再增加亲近度（D-013）。高亲近主动靠近行为已按 D-013 取消，不保留、不暂缓、不安排后续恢复"。
- 第 59 行："高亲近主动靠近行为按 D-008 延后到阶段 2，尚未实现" → "高亲近主动靠近行为已按 D-013 取消，不保留、不暂缓、不安排后续恢复"。

保留的委屈相关规格（阈值 >= 80、角落计分 +20、委屈表达、委屈衰减半衰期 12 小时、暂停语义、存档演进 v3）不变。R3 已清理的细化参数段落（第 58、74、84 行）不变。

## R9 整改：F-09 委屈与探头高优先级协调（2026-11-05）

监理 R9 关闭 F-10，F-09 低优先级分支排他部分通过，但 F-09 整体未关闭：高优先级委屈表达与探头的双向协调尚未实现。R9 剩余反例：合法昨日 pending 委屈（值 85）、鼠标在宠物附近，同一 tick 委屈分支先进入表达后调度器 `ShouldFire`，`StartProbe` 未检查 `_isExpressingGrievance` 强行播放 curious；同时 `EndProbe` 无条件回 idle 覆盖未完成的 10 秒委屈表达。

### F-09 剩余 委屈已开始时不启动探头

**问题**：`StartProbe` 调用条件仅检查 `!_isProbing && !_isDragging`，未检查 `_isExpressingGrievance`，委屈表达中探头仍会启动并覆盖委屈表情。

**修复位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `OnUpdateTick` 稀有事件调度段（`StartProbe` 调用条件）：
- `!_isProbing && !_isDragging` → `!_isProbing && !_isDragging && !_isExpressingGrievance`
- 调度器 `ShouldFire` 返回 true 时仍执行 `MarkConsumed` 消耗计划，但探头视觉不启动。委屈属非空闲状态，符合 R-03"不合适时延迟至空闲行为期间"和"当日一直没有机会则跳过"——探头视觉跳过，额度已用。

### F-09 剩余 探头中委屈启动时安全结束探头不覆盖委屈

**问题**：探头进行中委屈需要启动时，`EndProbe` 无条件回 idle，会覆盖仍在进行的委屈表达；且委屈在屏幕边缘探头位置表达不符合"委屈应在宠物正常位置表达"。

**修复位置**：`src/DesktopPet.App/MainWindow.xaml.cs`：
1. `OnUpdateTick` 委屈启动分支：先设 `_isExpressingGrievance = true`，再检查 `_isProbing` 调 `EndProbe(restoreOriginal: true, nowMs)` 安全结束探头（恢复原始位置），然后播放 grievance。先设标志确保 `EndProbe` 内的 `!_isExpressingGrievance` 守卫生效。
2. `EndProbe` 回 idle 条件：`!_isDragging` → `!_isDragging && !_isExpressingGrievance`，委屈进行中不回 idle。

### 针对性测试

`tests/DesktopPet.App.Tests/ProbeVisualTests.cs` 新增两项测试（驱动实际 `OnUpdateTick`，断言真实动作/位置/计划消费）：

- `F09_GrievanceActive_SchedulerFires_ProbeDoesNotStart_GrievanceContinues`：委屈表达已开始，同 tick 调度器 `ShouldFire`，断言探头不启动（`_isProbing=False`）、委屈继续（`_isExpressingGrievance=True`、动作=grievance）、计划已消耗（`Consumed`）。
- `F09_ProbeActive_GrievanceStarts_ProbeSafelyEnds_GrievanceContinues`：探头进行中委屈启动，断言探头安全结束（`_isProbing=False`）、原始位置已恢复、委屈继续（`_isExpressingGrievance=True`、动作=grievance）、不回 idle。

### 规格同步

- `docs/PRODUCT.md` R-03 动作所有权段落：补充委屈与探头双向协调规则（委屈已开始不启动探头但计划消耗；探头中委屈启动先安全结束探头恢复原位置再播放委屈；EndProbe 不回 idle 覆盖委屈）。
- `docs/ARCHITECTURE.md` F-09 探头动作所有权段落：同步双向协调实现细节（`StartProbe` 条件含 `!_isExpressingGrievance`；委屈分支先设标志再调 `EndProbe(restoreOriginal: true)`；`EndProbe` 回 idle 含 `!_isExpressingGrievance` 守卫）。

### 自测证据

- 构建 0 警告 0 错误。
- 全量测试通过：Core 253 + App 208 = 461/461，0 失败/跳过。
- F-09 相关测试 6 项全部通过（含两项新协调测试 + 既有排他/拖动打断测试）。

### R9 复核请求

请监理复核 F-09 剩余高优先级协调整改是否满足验收要求。开发方不自行关闭问题，等待监理结论。

## R10 整改：F-09 委屈期间延迟 Evaluate 保留未消费计划（2026-11-05）

监理 R10（`docs/reviews/P1-004-review.md` R10 段落）关闭 F-10，F-09 视觉优先级修复接受，但指出唯一剩余是延迟/消费语义：`Evaluate` 在检查委屈状态之前已经消费计划（`Evaluate` 内部在延迟达到时执行 `_plan = _plan.AsConsumed()`，无论 `ShouldFire` 还是 `RollFailed`），后续只阻止 `StartProbe` 是提前跳过，不是 R-03"延迟至空闲，当日一直无机会才跳过"。R10 要求：委屈进行时在有消费副作用的 `Evaluate` 之前延迟评估，保留原计划未消费状态；委屈结束后再评估同一计划。已经实际开始过的探头被委屈打断，维持已消费且不重播。同时纠正 PRODUCT/ARCHITECTURE/整改说明中"委屈中 ShouldFire 仍消费，当日额度已用"的错误解释。

### F-09 剩余 委屈期间延迟 Evaluate

**问题**：`Evaluate` 内部在延迟达到时直接执行 `_plan = consumed`（line 199-200），无论 `ShouldFire` 还是 `RollFailed` 都会消费计划。即使 `StartProbe` 有 `!_isExpressingGrievance` 守卫不启动探头，计划已被 `Evaluate` 消费——候选从未展示且当天再无机会，这是提前跳过。

**修复位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `OnUpdateTick` 稀有事件调度段：
- `Evaluate` 调用前增加 `_isExpressingGrievance` 守卫：委屈进行时 `result = null`（不调用 `Evaluate`），计划保留原日期、roll、delay、baseline 和未消费状态。
- `EnsurePlanForDate` 和 `runtimeTracker.UpdateTo` 仍正常执行（跨日计划创建和运行时间累计不受委屈影响）。
- `result is not null && result.Value.ShouldFire` 替代原 `result.ShouldFire`（`TriggerResult` 是值类型 struct，可空包装后用 `.Value` 访问）。
- 委屈结束后下一 tick `_isExpressingGrievance` 为 false，`Evaluate` 正常调用，同一计划被评估。
- `StartProbe` 条件保留 `!_isExpressingGrievance` 作为二级守卫（`Evaluate` 已被延迟，`ShouldFire` 不会在委屈期间为 true，但守卫提供额外安全）。

### 已展示后被委屈打断的探头不重播

**逻辑**：探头在委屈开始前已通过 `Evaluate` 消费计划（`ShouldFire=true` → `MarkConsumed`）。委屈打断时 `EndProbe` 安全结束探头恢复原位置。委屈期间 `Evaluate` 被延迟，但计划已消费。委屈结束后 `Evaluate` 运行，返回 `PlanConsumed`（`ShouldFire=false`），不重播探头。两种情形（从未评估 vs 已消费后被打断）不混同。

### 纠正错误规格

R9 整改中 PRODUCT R-03 和 ARCHITECTURE F-09 段落被写成"委屈中 ShouldFire 仍 MarkConsumed 消耗，当日额度已用"，与 R-03 原延迟规则冲突。R10 已纠正：

- `docs/PRODUCT.md` R-03 动作所有权段落：改为"委屈表达已开始时，延迟评估（不调用有消费副作用的 `Evaluate`），保留原计划未消费状态，委屈结束后再评估同一计划"。
- `docs/ARCHITECTURE.md` F-09 探头动作所有权段落：改为"`Evaluate` 调用被延迟（`result = null`，不评估不消费），保留原计划未消费状态"，并补充"已经实际开始过的探头被委屈打断，维持已消费且不重播"。

### 针对性测试

`tests/DesktopPet.App.Tests/ProbeVisualTests.cs` 修改两项 + 新增两项测试（均驱动实际 `OnUpdateTick`，断言真实动作/位置/计划消费）：

**修改**：
- `F09_GrievanceActive_SchedulerFires_ProbeDoesNotStart_GrievanceContinues`：纠正断言——委屈期间 `Consumed=false`（原错误断言 `Consumed=true`）；委屈结束后（10.5 秒）同计划评估，`ShouldFire=true`（概率 1.0），探头启动，`Consumed=true`。
- `F09_ProbeActive_GrievanceStarts_ProbeSafelyEnds_GrievanceContinues`：补充断言——探头已消费后委屈打断，委屈期间和结束后计划维持 `Consumed=true`，不重播探头。

**新增**：
- `F09_GrievanceActive_RollFailedAfterGrievance_PlanPreservedThenConsumed`：`triggerProbability=0.0`，委屈期间 `Consumed=false`，委屈结束后评估 → `RollFailed` → `Consumed=true`，探头不启动。证明未中 roll 在委屈结束后才消费。
- `F09_GrievanceActive_SnapshotPreservesUnconsumedPlan_RestoreAndEvaluate`：委屈期间快照保留未消费计划，`RestorePlan` 后计划仍未消费，`Evaluate` 正常触发 `ShouldFire=true`。证明隔离存档往返保留等待计划。

### 自测证据

- 构建 0 警告 0 错误。
- 全量测试通过：Core 253 + App 210 = 463/463，0 失败/跳过。
- F-09 相关测试 8 项全部通过（含修改 2 项 + 新增 2 项 + 既有排他/拖动打断 4 项）。

### R10 复核请求

请监理复核 F-09 剩余延迟/消费语义整改是否满足验收要求。开发方不自行关闭问题，等待监理结论。

**磁盘验证**：保存后重新读取 PRODUCT R-02 全文（第 47-90 行），确认三处修改正确写入磁盘。grep 检查 `亲近度|affinity|半衰期 7|>= 70|D-008` 仅匹配第 49 行的取消声明文字，无计分/半衰期/阈值/D-008 延后等矛盾残留。

### R4 补充整改验证总结

- 未修改产品代码、未重跑测试、未接探头。未提交、未推送。
- 未修改监理报告 `docs/reviews/P1-004-review.md`。
- F-01～F-06 保持关闭，不重复整改。

### R4 补充复核请求

请监理复核 F-07 R-02 主体段落 D-013 矛盾清理是否满足验收要求。开发方不自行关闭问题，等待监理结论。

## R5 关闭依据（2026-11-05）

监理 R5（`docs/reviews/P1-004-review.md` R5 段落）仅核对磁盘文档，未修改产品代码、未重跑构建/测试。

- **F-07 通过并关闭**：PRODUCT 当前将好感度、计分/冷却/衰减和高亲近主动靠近明确取消，委屈与非计分手势保留，存档说明为 v3；ARCHITECTURE 已与 DTO 一致。仍含"亲近度"等词的行是取消声明，不是待实现规格。整改记录所称 grep "仅第 49 行"不准确，实际有多行取消声明；不影响语义一致，不据此追加整改。抚摸 1 秒句仅作为非计分手势措辞，后续不得据此恢复累计计分。
- IDE 窗口已重启、旧未保存缓冲区不可用，监理不再要求找回，以磁盘当前结果接受冲突收尾。
- **F-01～F-07 全部关闭，Core 与可靠存档小步接受**。P1-004 整体仍 in-progress，运行时调度/生命周期接线及探头表现尚未实现/验收。

开发方不重复已关闭项，不修改监理报告。按 D-015 继续下一小步。

## R6 整改：F-08 计划创建与抽签未中未通知保存队列（2026-11-05）

监理 R6（`docs/reviews/P1-004-review.md` R6 段落）提出 F-08：`OnUpdateTick` 中 `EnsurePlanForDate` 的"已创建"返回值被丢弃；`Evaluate` 在 `RollFailed` 时已将计划设 `Consumed`，但 `App.MarkStateDirty` 仅在 `result.ShouldFire` 为 true 时调用。导致新建计划和"已尝试但未中"无法进入 5 秒防抖保存。F-01～F-07 保持关闭，不重做。

### F-08 计划创建/日期更新/消费状态变化通知保存队列

**问题**：
1. `EnsurePlanForDate` 返回 true（新建计划）时返回值被丢弃，不调用 `MarkStateDirty`。
2. `Evaluate` 返回 `RollFailed` 时计划已被消耗（`AsConsumed`），但 `MarkStateDirty` 仅在 `ShouldFire=true` 时调用，导致 roll-failed 的消耗不进入保存队列。
3. 若每 tick 无条件标脏，5 秒防抖定时器会被反复重置，永远无法完成落盘。

**修复位置**：`src/DesktopPet.App/MainWindow.xaml.cs`

- 新增 `_lastRareEventPlanDate` (string?) 和 `_lastRareEventPlanConsumed` (bool?) 字段（约 line 97-103），跟踪上次已知计划状态。
- `OnUpdateTick` 稀有事件块（约 line 720-774）改为：
  - 捕获 `EnsurePlanForDate` 返回值 `planCreated`。
  - `Evaluate` 后捕获 `currentPlan?.LocalDate` 和 `currentPlan?.Consumed`。
  - `stateChanged = planCreated || currentDate != _lastRareEventPlanDate || currentConsumed != _lastRareEventPlanConsumed`。
  - `ShouldFire` 时调 `MarkConsumed` 并设 `stateChanged = true`。
  - 仅在 `stateChanged` 时更新跟踪字段并调用 `App.MarkStateDirty()`。
  - 无状态变化时不标脏，防抖正常完成。

**辅助修改**：`src/DesktopPet.App/App.xaml.cs`
- 新增 `TestSetupIsolatedSave` internal 方法：配置隔离目录的 `ArchiveService`/`SaveCoordinator` 和运行时对象，绕过完整 `OnStartup`，供测试验证真实 `MarkStateDirty → SaveCoordinator → file` 路径。
- 新增 `TestCleanup` internal 方法：清理 SaveCoordinator 和单例状态。
- `FlushFinalState` 从 `private` 改为 `internal`，供测试调用真实退出保存路径。

**针对性测试**（`tests/DesktopPet.App.Tests/RareEventPersistenceTests.cs`，5 项，全部走真实 `App.Current` 路径，不设 `TestRuntimeTracker`/`TestRareEventScheduler`）：
- `F08_TickCreatesPlan_NotifiesRealSaveQueue_FileWritten`：tick 创建新计划 → `App.MarkStateDirty` → `FlushFinalState` → 隔离文件包含计划字段、未消耗。
- `F08_TickRollFailed_NotifiesSaveQueue_ConsumedPersisted`：roll=0.0 始终 roll-failed → tick 到延迟点消耗计划 → `FlushFinalState` → 隔离文件中 `Consumed=true` → 重载恢复后 Evaluate 返回 `PlanConsumed` 不重触发。
- `F08_TickShouldFire_NotifiesSaveQueue_ConsumedPersisted`：roll=1.0 始终 fire → tick 消耗计划 → `FlushFinalState` → 隔离文件中 `Consumed=true`。
- `F08_NoStateChangeTick_DoesNotMarkDirty`：连续 tick 无状态变化（同计划、延迟未达、未消耗）→ dirty-tracking 字段不变，验证不会每 tick 标脏。
- `F08_FullChain_Create_RollFail_Save_Restore_NoRefire`：tick 创建 → tick roll-failed 消耗 → 真实 flush → 隔离文件重载 → 生产映射恢复 → `Consumed` 保留、Evaluate 不重触发。端到端闭合生产保存链。

**自测证据**：
- F-08 专项：5/5 通过，退出码 0。
- 全量：Core 253 + App 189 = 442/442 通过，0 失败/跳过。构建 0 警告/错误。

**文档同步**：`docs/ARCHITECTURE.md` 新增"2026-11-05 P1-004 App 运行时接线"段落，描述 OnUpdateTick 接线和 F-08 保存通知规则。

### R6 整改验证总结

- 未修改监理报告 `docs/reviews/P1-004-review.md`。
- 未接探头视觉表现。未提交、未推送。
- F-01～F-07 保持关闭，不重复整改。

### R6 复核请求

请监理复核 F-08（计划创建/日期更新/消费状态变化通知保存队列）整改是否满足验收要求。开发方不自行关闭问题，等待监理结论。

## R7 关闭依据（2026-11-05）

监理 R7（`docs/reviews/P1-004-review.md` R7 段落）独立完整构建/测试 Core 253 + App 189 = 442/442 通过，0 失败/跳过，未见编译警告/错误。另用隔离探针走真实 `App.Current`/`SaveCoordinator`，8 项检查全部通过：创建通知/落盘未消耗计划、无变化不再标脏、roll-failed 通知/落盘 Consumed 与累计、已消耗不再通知、ShouldFire 通知/落盘快照。探针仅调用 `SaveCoordinator.Flush`，不调用 `App.FlushFinalState` 或手工 Map/Save，直接覆盖原缺失通知反例。

- **F-08 通过并关闭**：tick 保留 `EnsurePlanForDate` 创建结果，比较计划日期/Consumed 与上次状态，状态改变才调用 `App.MarkStateDirty`。独立探针验证了创建、roll-failed、ShouldFire 三类保存通知和落盘内容一致。无状态变化时不标脏，防抖正常完成。
- 开发新增测试的质量限制：`FlushFinalState` 自行补标脏，部分无变化测试仅比跟踪字段；监理由独立队列检查补齐证据，不要求反复往返。整改记录"概率0/1"称为"roll0/1"是措辞误差，源码实际使用可控触发概率；不作为新阻断。
- ARCHITECTURE 运行接线段已同步。

结论：**F-01～F-08 全部关闭，运行时调度及保存通知小步接受**。探头表现尚未实现，P1-004 整体继续 in-progress。按 D-015 可进入最小探头表现。开发方追加本关闭依据，不重复整改，不修改监理报告。不提交/推送。

## R8 整改：F-09/F-10（2026-11-05）

监理 R8（`docs/reviews/P1-004-review.md` R8 段落）提出 F-09/F-10，F-01～F-08 保持关闭。本轮仅整改 F-09/F-10 及相关测试/规格，不重做已关闭项，不扩展新表现，不提交/推送。

### F-09 普通好奇逻辑提前覆盖探头表情

**问题**：`OnUpdateTick` 普通 hover/CuriosityReaction 分支未排除 `_isProbing`；`StartProbe` 直接播放 curious，被该分支自动接管，约 0.3 秒后变回 idle，未随 3 秒探头生命周期保持。

**修复位置**：`src/DesktopPet.App/MainWindow.xaml.cs` OnUpdateTick

- hover/curiosity 分支条件（约 line 584）已含 `!_isProbing`：探头活跃期间整个 hover/curiosity 分支被跳过，不会触发 `PlayAction("curious")` 或 `CuriosityReaction.Start/ShouldEnd`。
- else 分支（约 line 614）：`_curiosity.Cancel()` 有 `!_isProbing` 守卫，探头期间不取消 curiosity 计时器。
- 高优先级委屈表达（`_isExpressingGrievance`）在探头期间按既有规则运行，不受 F-09 排他影响。

**针对性测试**（`ProbeVisualTests.cs`，共 4 项 F-09 测试）：
- `F09_ProbeActive_MouseFarAway_CuriousPersists_FullLifetime`：实际 tick 驱动探头 0/400/2500ms，鼠标远离，curious 全程保持不变回 idle；3100ms 探头正常结束恢复原位置。
- `F09_ProbeActive_MouseHovering_CuriousPersists`：探头期间鼠标悬停在宠物上，curious 不被覆盖。
- `F09_AfterProbeEnds_NormalCuriosityAutoEndStillWorks`：探头结束后既有独立好奇自动结束行为不退化。
- `F09_PauseDuringProbe_ResumeContinuesRemainingTime`：探头期间暂停 5 秒，恢复后仅按有效时长继续（1000ms + 2000ms = 3000ms），暂停时长不计入探头剩余时间。

### F-10 头部短按被当作拖动打断，导致不归位

**问题**：`BeginCapturedPointerInteraction` 入口在命中和头部手势分流之前无条件 `CancelProbeForDrag`；有效头部按下仅设置 `_isPettingCandidate`，未真正 `StartDragging`，导致轻点取消探头归位责任。

**修复位置**：`src/DesktopPet.App/MainWindow.xaml.cs`

- `BeginCapturedPointerInteraction`（约 line 935-977）：移除入口处无条件 `CancelProbeForDrag`。头部按下仅设 `_isPettingCandidate`，不取消探头。身体按下调 `StartDragging`（内部含 `CancelProbeForDrag`）。
- `StartDragging`（约 line 988-998）：`CancelProbeForDrag` 仅在此调用——身体立即拖动和头部 `ConvertPettingToDrag` 都经此路径。
- `ConvertPettingToDrag`（约 line 1308-1312）：转拖动时调 `StartDragging`，间接执行 `CancelProbeForDrag`。
- 头部短按/微移未转拖动时经 `EndPetting` 释放，不取消探头；探头继续计时归位。透明/无效按下在 `BeginCapturedPointerInteraction` 返回 false，不触及探头状态。

**针对性测试**（`ProbeVisualTests.cs`，共 5 项 F-10 测试，全部调用实际手势链）：
- `F10_HeadShortPress_Release_NoMove_ProbeContinuesAndRestores`：头部可见点按下→无移动释放→推进 3 秒归位，保留原抓取点。
- `F10_HeadPressConvertsToDrag_CancelsProbe_NoRestore`：头部累计超过 8 DIP 转拖动后不归位，原抓取点不跳。
- `F10_BodyPressDrag_CancelsProbe_NoRestore`：身体拖动沿用当前优先级取消探头不归位。
- `F10_InvalidPress_Rejected_DoesNotCancelProbe`：透明点拒绝不取消探头，推进 3 秒归位。
- `F10_HeadMicroMove_WithinSlop_DoesNotCancelProbe`：头部微移（3 DIP < 8 DIP 阈值）不转拖动不取消探头，释放后归位。

### 规格同步

- `docs/ARCHITECTURE.md` 探头视觉表现段落（约 line 203/206）：
  - 第 203 行更新为 F-10 规则：仅 `StartDragging` 时调 `CancelProbeForDrag`，头部短按/微移/无效按下不取消探头。
  - 第 206 行更新为 F-09 规则：探头活跃期间 hover/curiosity 分支被 `_isProbing` 条件排他，不接管或覆盖探头表情。

### R8 整改验证总结

- 全量测试：Core 253 + App 206 = 459/459 通过（原 450 + 新增 9 项 F-09/F-10 测试），0 失败/跳过。构建 0 警告/错误。
- 未修改监理报告 `docs/reviews/P1-004-review.md`。
- 未提交、未推送。
- F-01～F-08 保持关闭，不重复整改。

### R8 复核请求

请监理复核 F-09（探头动作所有权排他）和 F-10（仅真正拖动取消探头）整改是否满足验收要求。开发方不自行关闭问题，等待监理结论。

## R11 关闭依据（2026-11-05）

监理 R11（`docs/reviews/P1-004-review.md` R11 段落）独立完整构建/测试 Core 253 + App 210 = 463/463 通过，0 失败/跳过，未见编译警告/错误。另重跑原同 tick/连续延迟反例，摘要、TRX 及源/结果见 `evidence/P1-004-probe-R11/`。

- **F-09 通过并关闭**：有消费副作用的 `Evaluate` 现在在委屈活跃时不调用；`UpdateTo` 与 `Ensure` 按既有规则执行。独立连续 tick 输出委屈中 `expressing=True; probing=False; action=grievance; duringGrievanceConsumed=False`，表达结束后 `consumed=True; probing=True; action=curious`，同一原计划得以延迟而非跳过。开发新增/修正实际 tick 测试覆盖必中、未中、快照保留，以及已展示后被委屈打断不重播，相关全量通过。PRODUCT/ARCHITECTURE 已纠正为延迟评估、不提前消费，与 R-03 及 R9/R10 要求一致。低优先级排他、头部短按归位等此前接受部分不重审。
- **F-01～F-10 全部关闭**，P1-004 代码与自动验证部分接受，进入实机验收。任务改 `awaiting-verification`，不标 done。

### 实机验收入口

实机夹具已准备：`tests/Integration/Run-ProbeLive.ps1` + `ProbeLiveProbe.cs`。

- 隔离存档：预置 `state.json`（`accumulatedRuntimeMinutes=150`，`triggerDelayMinutes=20`，`triggerRoll=0.0`，`consumed=false`），通过生产 `MapArchiveToRareEventObjects` 恢复，不设 Test 注入字段。
- 可控触发：运行时间 150 分钟超过延迟 20 分钟，roll=0.0 必中 0.5 概率，首个 `OnUpdateTick` 即 `ShouldFire`。不要求等 20 分钟，不改正式概率。
- 不污染真实存档：使用 `DESKTOPPET_VALIDATION_DIR` 环境变量指定隔离目录，运行前后校验真实 `%LOCALAPPDATA%/DesktopPet` 快照不变。
- 探针观察：探头启动（窗口移到边缘、action=curious）、探头结束（~3 秒后窗口恢复原位置、action=idle）、计划已消费（存档 `consumed=true`）。不发送鼠标输入，拖动/暂停/退出交互留用户手动测试。
- 夹具编译验证通过（`-BuildOnly`，0 警告 0 错误）。

### 剩余实机验收范围

真实窗口显示最近边缘/半身可见、3 秒归位、身体/头部真正拖动打断且抓取点稳定、头部轻点仍归位、暂停剩余时间/退出保存恢复。ART-001/003 既有未验证项保留，不自动代签。开发方不重复已关闭整改，不修改监理报告。不提交/推送。

## F-11 中 夹具在探头开始后改写位置，原位置和边缘断言失真（R12）

**问题**：`ProbeLiveProbe.cs` 先 `await Task.Delay(500)` 让首个 tick 触发探头，随后强行 `Left=500/Top=400` 并捕获为 `original`——此时窗口已被探针移离边缘，`windowMoved` 与 `original` 比较得到 false；3 秒后产品恢复真实原位置也被错比为未恢复。驱动脚本 90 秒超时分支直接 `throw` 未执行 `finally` 存档比较。未记录实际 HWND DPI、工作区/窗口矩形、起止时间。未区分自动退出/超时/人工交互模式。

**修复位置**：
- `tests/Integration/ProbeLiveProbe.cs`（全文重写，153→329 行）：
  - 删除 `await Task.Delay(500)` + `Left=500/Top=400` 覆盖。基线在首 tick 之前捕获（`BASELINE` 状态），之后只观察、不改 `Left/Top`。
  - 新增 `ProbeLogState(tag)`：记录 hwnd、DPI（`GetDpiForWindow`）、DPI scale、窗口 DIP/物理坐标/尺寸、action、isProbing。
  - 新增 `ProbeLogMonitor()`：通过 `MonitorInterop.GetWindowDpiScale` + `EnumerateMonitors` 记录所有显示器 full/work 矩形和 DPI scale。
  - 新增 `ProbeArchiveSnapshot()`：SHA256 哈希比较真实存档，在 `finally` 始终执行。
  - 新增 `DESKTOPPET_PROBE_MODE` 环境变量区分 `auto`（tray PerformClick 退出）/ `interactive`（窗口保持开放）。
  - 记录 `PROBE_START_DETECTED`/`PROBE_END_DETECTED` 时间戳和 `PROBE_DURATION`。
  - 边缘距离记为 `EDGE_DISTANCE` + `NEAREST_EDGE` INFO 日志，不自动标 PASS（半身可见为主观实机项）。
  - `finally` 始终执行存档快照比较和退出尝试，即使 catch re-throw。
- `tests/Integration/Run-ProbeLive.ps1`（修改，107→168 行）：
  - 新增 `-Interactive` 参数：设置 `DESKTOPPET_PROBE_MODE=interactive`，超时 10 分钟，窗口保持开放。
  - 超时分支改为记录日志 + `process.Kill()`，不直接 `throw`（确保 `finally` 执行存档比较）。
  - `finally` 始终执行真实存档快照比较（`before` vs `after`）。
  - 新增 `ProbeLive-Log` 驱动侧日志函数，写入 `live.log`。
  - auto 模式运行后校验必需日志标记（`BASELINE`/`PROBE_ACTIVE`/`PROBE_ENDED`/`MONITOR`/`HWND_DPI`/`ARCHIVE_SNAPSHOT_AFTER`）。

**自测证据**（`artifacts/probe-live/run-187ea070/`）：
- `-BuildOnly` 编译通过，0 警告 0 错误。
- auto 模式运行：`PROCESS_EXIT_CODE=0`，`EXIT_MODE=auto (process exited on its own)`，`ALL PROBE LIVE CHECKS PASS`。
- 日志完整记录：
  - `HWND_DPI scale=(1.0000,1.0000)` — 实际 HWND DPI=96（100%）。
  - `MONITOR device=\\.\DISPLAY1 dpiScale=1.0000 full=(0,0,1920,1080,1920x1080) work=(0,0,1920,1032,1920x1032)` — 工作区矩形。
  - `STATE[BASELINE] windowDIP=(100.00,100.00)` — 基线位置（首 tick 之前）。
  - `STATE[PROBE_ACTIVE] windowDIP=(100.00,-60.00) action=curious` — 探头移动到 top 边缘。
  - `EDGE_DISTANCE top=-60.0px (half-window=60.0px)` — 半身可见距离（INFO，需人工确认）。
  - `PROBE_DURATION=2.92s` — 探头持续时间。
  - `STATE[PROBE_ENDED] windowDIP=(100.00,100.00) action=idle` — 恢复基线位置。
  - `ARCHIVE_SNAPSHOT_AFTER` 与 `REAL_ARCHIVE_BEFORE` 哈希一致 — 真实存档不变。
  - `PASS archive rareEventPlan.consumed=True` — 计划已消费。
- 真实存档快照不变：`PASS original user archive snapshot unchanged`。
- 交互模式可用（`-Interactive` 参数，窗口保持开放 10 分钟）。

**限制**：
- 半身可见（`NEAREST_EDGE=top distance=-60.0px`）为日志 INFO，不自动标已验证，需人工实机确认。
- 拖动打断、头部轻点、暂停/退出恢复等交互项仍为人工实机验收范围，不自动验证。
- 当前环境单显示器 100% DPI；多屏/高 DPI 场景需在对应环境另行验证。

**复核请求**：请监理确认 F-11 夹具修正是否满足 R12 通过条件。不重做 F-01～F-10，不改产品代码，不提交/推送。

## F-11 补充：最终判定逻辑修正与负对照（R13）

**问题**（R13）：位置/时序修复已通过，但最终判定有漏洞：
- 驱动超时分支设 `_TimeoutOccurred` 但结尾未据此判失败。
- `Process.ExitCode` 只打印不参与 PASS 判定。
- `ProbeTrayExit` catch 写 `auto_failed`，不匹配 `FAIL|ERROR` 正则，被吞掉。
- 负对照证明：有 6 标记 + 超时/退出失败仍得 ALL PASS。

**修复位置**：
- `tests/Integration/ProbeLiveProbe.cs`（`ProbeTrayExit` 方法）：
  - catch 中 `_probeLiveFailures++` 并写 `FAIL auto tray exit failed: {ex.Message}`（匹配驱动 FAIL 正则）。
- `tests/Integration/Run-ProbeLive.ps1`（最终判定段重写）：
  - 运行前初始化 `$script:_TimeoutOccurred = $false`、`$script:_ProcessExitCode = $null`。
  - auto 最终 PASS 必须同时满足 5 个条件：未超时 + 进程 exit 0 + 日志零 FAIL/ERROR + 6 个必需标记齐全 + `RESULT failures=0` 存在。
  - 任一条件不满足收集为 `$judgmentErrors`，有错误则 throw。
- `tests/Integration/Run-NegativeControls.ps1`（新增，负对照测试脚本）：
  - 不启动/kill 任何进程，用合成日志测试判定逻辑。
  - NC1 超时 + 非零退出 → REJECTED。
  - NC2 非零退出 → REJECTED。
  - NC3 缺 `RESULT failures=0` → REJECTED。
  - NC4 tray exit 失败（FAIL in log）→ REJECTED。
  - NC5 缺 `PROBE_ENDED` 标记 → REJECTED。
  - PC1 全部条件满足 → PASSED。

**自测证据**：
- `-BuildOnly` 编译通过，0 警告 0 错误。
- 负对照全部正确：`ALL NEGATIVE CONTROLS CORRECT`（5 负例 REJECTED + 1 正例 PASSED）。
  证据：`artifacts/probe-live/negative-controls/`。
- 正常 auto 复验通过（`artifacts/probe-live/run-42d42643/`）：
  - `PROCESS_EXIT_CODE=0`，`EXIT_MODE=auto (process exited on its own)`。
  - `HWND_DPI scale=(1.5000,1.5000)` — 150% DPI 环境。
  - `MONITOR work=(0,0,2560,1528,2560x1528)` — 工作区矩形。
  - `STATE[BASELINE] windowDIP=(100.00,100.00)` → `STATE[PROBE_ACTIVE] windowDIP=(100.00,-60.00)` → `STATE[PROBE_ENDED] windowDIP=(100.00,100.00)`。
  - `PROBE_DURATION=2.87s`，`RESULT failures=0`，`PASS archive rareEventPlan.consumed=True`。
  - 真实存档快照不变：`PASS original user archive snapshot unchanged`。
  - `ALL PROBE LIVE CHECKS PASS`。

**限制**：
- 半身可见（`NEAREST_EDGE=top distance=-90.0px`，150% DPI 环境）为日志 INFO，不自动标已验证，需人工实机确认。
- 拖动打断、头部轻点、暂停/退出恢复等交互项仍为人工实机验收范围。
- 当前环境单显示器 150% DPI；多屏/其他 DPI 场景需在对应环境另行验证。

**复核请求**：请监理确认 F-11 最终判定修正是否满足 R13 通过条件。不重做 F-01～F-10，不改产品代码，不提交/推送。

### R14 关闭依据（2026-11-06）

监理 R14 独立执行修正后的 `Run-ProbeLive.ps1` 默认 auto，确认：

- 构建无警告/错误，正常结束 exit 0、`RESULT failures=0`、真实存档哈希不变。
- 150% DPI 144 环境，基线/边缘/归位状态有效。
- 驱动状态初始化正确，最终判定同时检查未超时、进程 exit 0、日志无 FAIL/ERROR、必要标记齐全、明确零失败 RESULT。
- `ProbeTrayExit` 异常递增失败并输出 FAIL。
- 监理另提取 `Run-ProbeLive.ps1` 实际 Final PASS 代码对 5 个合成负例 + 1 个正例独立执行：超时/非零退出/缺 RESULT/托盘失败/缺 PROBE_ENDED 全部拒绝，唯一完整正例通过。无实际超时 kill 或失败进程启动；正常 auto 独立运行的真实 exit 0 证据另存，不与合成数据混淆。

**F-11 通过并关闭。F-01～F-11 全部关闭，代码/自动验证及隔离自动场景接受。**

P1-004 继续 `awaiting-verification`，只剩真实观感/输入交互：半身可见、身体/头部真正拖动打断和抓取点、头部轻点归位、暂停剩余时间与退出恢复。auto 轮询/菜单 PerformClick 不等于系统鼠标与 Explorer 图标点击；未实际目测半身，不代填通过。ART-001/003 既有缺口保留。

不新增产品代码、不重做旧整改或重跑无关全套；不提交/推送。使用既有隔离 Interactive 入口逐项实际验证，保持真实存档不受污染、正式概率不变。

### R15/R16 实机验收关闭依据（2026-11-06）

监理 R15/R16 在用户授权下执行实机验收，原始记录见 [实机记录](../history/P1-004-live-validation.md)，监理结论见 [审查报告 R15/R16](../reviews/P1-004-review.md)（第 271-291 行）。开发方仅接收记录并追加整改收尾，不重复验收/整改，不改监理报告与实机记录。

**R15（用户授权监理执行实机）**：

- 150% 单屏右边缘：60×120 逻辑像素裁切区域，已目测约半身可见。
- 暂停/恢复：隔离控制调用生产暂停事件，冻结超过 60 秒后恢复，约 3 秒归位。
- 退出/重启：探头中退出保存原位置，同目录/同 EXE 新进程重启确认恢复位置/paused=true/probing=false，exit 0。
- 真实存档 SHA256 不变，自有实例正常结束。
- 鼠标拖动/轻点因 Computer Use 工具坐标校验失败未完成，不代填通过，不归为产品缺陷。

**R16（继续实机验收）**：

- 身体拖动打断：上边缘探头中拖动身体成功（Left 400→426，与鼠标水平 26 DIP 一致），松手后探头取消且未归原位。
- 头部拖动：真实鼠标 Down/Move/Up 进入生产手势链，物理位移 (48,19) 与窗口 DIP 位移 (32,12.6667)×1.5 一致，抓取点误差近 0；边界钳位单列，不当抓取漂移。
- 头部轻点归位：真实按下/释放未拖动，IsProbing 仍 true，按有效时间归位；普通计时版本同样执行，原位置恢复。
- 真实菜单 Pause/Resume/Exit：实际鼠标点击 TrayService 菜单，状态切换正确；Exit 保存原位置，同目录重启恢复且未重播。
- 真实存档 SHA256 不变，全部自有实例清理。

**R16 结论（审查报告第 290 行）**：F-01～F-11 均关闭，P1-004 在本次环境验收通过，可标 done。开发方只需接收记录、原样保留监理材料并追加整改收尾，不重复验收/整改，不自动提交推送。

**明确保留的未验证项**：

- 100%/混合 DPI/多屏未覆盖，不宣称全面平台兼容。
- Explorer 通知区域图标入口未验证（菜单由诊断入口打开）。
- 短程输入验证不等于长期使用稳定性保证。
- ART-001/003 原独立未验证状态不变，不扩大为美术验收。
- 夹具限定（ShowInTaskbar=True、可控触发/冻结、鼠标事件观测）仅在 artifacts 副本，产品源码未改。

**P1-004 标 done。** 不新增产品代码、不重做旧整改或重跑无关全套测试、不提交/推送。阶段 2 须用户另行安排。
