# P6-003 交付记录（开发独立记录）

任务：P6-003 频次预测：样本不足不预测
状态：`awaiting-verification`（开发实现与自动验证已完成，待监理复核；不自行标 done，未提交/推送）
日期：2026-11-27
依赖：P6-002（done，监理 R1 接收）。本记录只读复用已验收的聚合桶，不重测、不改写 P6-001/P6-002 结论与报告。

## 1. 口径（已先写入权威文档）

- 已写入 [PRODUCT R-09](../PRODUCT.md#r-09-习惯学习阶段-6)「频次判断（P6-003 首版，保守起步值，尚未经真实预测效果验证）」与 [ARCHITECTURE §7「P6-003 频次判断约定（已实现）」](../ARCHITECTURE.md#p6-003-频次判断约定已实现)；[TASKS P6-003](../TASKS.md#p6-003-频次预测样本不足不预测) 展开范围/验收/验证方式。
- **窗口**：名义为当前本地日期之前 30 个完整日期，**与 P6-002「含今天保留 30 天」聚合保留上限取交集**（保留区间 `[today−29, today]`），并**排除尚未结束的当天**；默认有效区间 `[today−29, today−1]`，最多 **29 个已完成日期**。当天、未来与超出保留期的日期均不计入（既不作支持也不作反对），过期桶从首次查询起即排除、不依赖 drain。不延长数据保留、不为查询恢复已过期记录。
- **覆盖门槛**：窗口内至少 **7 个不同日期**存在有效统计数据（`ActiveMs>0` 的桶）。文档明确「有记录的日期」不等于「完整观察了一天」，缺失数据不得当作用户没使用。
- **候选门槛**：同一应用在**相同半小时时段**至少出现于 **5 个不同日期**，且该时段累计会话数至少 **5 次**。「出现」＝该 (`AppId`, 时段) 桶 `ActiveMs>0`；会话数取桶内 `SessionCount` 之和（一次使用＝一个会话，只计入起始桶）。
- **输出**：应用标识、时段（`HH:mm`）、支持日期数、会话数，另附 `ObservedDateCount`/`WindowDays`；`WindowDays` 为实际生效的完整日期数（默认配置下为 29，不冒称完整 30 日历史）。仅为统计依据，不包装为准确率、不伪称知道用户意图。
- 参数是可调初值（`UsageStatsFrequencyOptions`），首版默认值即上述数值，尚未经真实预测效果验证。

## 2. 实现范围

### Core 纯逻辑（新增 `src/DesktopPet.Core/Events/UsageStatsFrequencyJudge.cs`）

- `UsageStatsFrequencyJudge.Evaluate(buckets, localNow, enabled, options?)`：无状态、无持久化、无缓存；不可用时（`enabled=false`）直接返回空结果。
- 只读 `UsageStatsBucket`：以 `BucketStartLocal` 的本地墙钟为日期与时段来源（已按 30 分钟对齐，取 `HH:mm`）；无法解析的桶、空 `AppId`、`ActiveMs<=0` 桶跳过。
- 有效窗口 = 名义 `[today−WindowDays, today−1]` 与保留区间 `[today−(RetentionDays−1), today]` 的交集（`ExcludeToday=true`，默认即 `[today−29, today−1]`）；`today−30` 及更旧日期从首查即排除，不依赖 drain。先判覆盖日期数，再按 (`AppId`, 时段) 判支持日期数与会话数。
- 输出按支持日期数降序→会话数降序→`AppId` 序数→时段序数排序；`IsSuppressed` 表示无候选。
- 复用 P6-002 桶模型，不新增采集路径、不改桶语义、不写宠物存档。

### App 接线（`src/DesktopPet.App/Platform/UsageStatsService.cs`）

- 新增 `public UsageStatsFrequencyResult GetFrequencyPredictions()`：按需从当前已提交桶计算，**不保留预测缓存**，因此关闭（`Disable`）或 `ClearAll` 后下一次调用立即为空；重启由 `Restore` 恢复桶后重算，不恢复旧预测。
- 内部构造器新增可选 `Func<DateTimeOffset>? localNow`（默认 `DateTimeOffset.Now`），供测试注入固定本地日期；生产默认行为不变。
- 沿用单一全局统计开关，未新增独立预测开关。**仅内部结果**：不接气泡、不移动宠物、不做图标定位、不自动启动应用，不进入 P6-004。

## 3. 测试与证据（2026-11-27 本机实跑）

### Core 专项（新增 `tests/DesktopPet.Core.Tests/UsageStatsFrequencyJudgeTests.cs`，11 条）

- 关闭不输出（即使数据达标）；6 个覆盖日期拒绝；4 个支持日期拒绝；5 个支持日期但仅 4 会话拒绝；达标输出应用/时段/支持日期数/会话数；当天排除（反证 `ExcludeToday=false` 时达标）；窗口边界（today−29 计入、today−30 过期，`WindowDays=29`）；非法时间/空 AppId/零活跃桶跳过；`null` 输入空结果不抛；多候选排序；会话按桶求和（2×3+1×2=8）。

### App 隔离目录（`tests/DesktopPet.App.Tests/UsageStatsServiceTests.cs` 频次用例 6 条，`%TEMP%/DP6Stats_*`）

- 达标后输出候选，重启由恢复桶重算结果一致；默认关闭不输出、开启输出、再次关闭立即为空；`ClearAll` 后立即为空且落盘 `buckets` 为空、开关保留；当天/未来/`today−31`/`today−30` 均排除、仅 `today−29` 计入；反例 `today−30` 首查即排除（覆盖 6、候选 0）且 drain 前后+重启一致；正例 `today−29` 有效（覆盖 7、候选 1）且 drain 前后+重启一致。

### 回归与构建

- Core 全量 **613/613**、App 全量 **637/637**，0 失败 0 跳过（日志 `.temp\p6-003-*.log`、`.temp\p6-003-f01-*.log`，本机临时目录不入库）。
- App `DesktopPet.App` 与 `DesktopPet.DiagnosticProbe` 构建 **0 警告 0 错误**（日志 `.temp\p6-003-f01-app-build.log`、`.temp\p6-003-f01-probe-build.log`）。

## 4. 未验证 / 限制

- 未使用真实长期使用数据验证预测效果；门槛为保守起步值，本轮只证明“按既定口径可测试、可复现”，不宣称预测可靠或准确。
- 未做真实等待 30 天/跨月的实机观测；窗口、当天排除与过期边界由构造桶 + 注入本地日期覆盖。
- 时区切换/夏令时未专项验证（沿用 P6-002 已记录限制）。
- 未接任何展示（P6-004 范围）。

## 5. 复核请求

- 请核对 PRODUCT/ARCHITECTURE/TASKS 三处门槛数值与语义是否一致（30 天剔当天、7 个覆盖日期、5 个支持日期且 ≥5 会话、输出字段仅为统计依据）。
- 请核对关闭/清除/重启三条语义：关闭不输出、清除后立即失效、重启不恢复旧预测。
- 未修改 `docs/reviews/` 及证据；未提交、未推送；全部已有修改保留。

## F-01 整改（2026-11-27）

依据：[监理 R1 F-01](../reviews/P6-003-review.md)（中，阻断）。保持 P6-002 的 30 天聚合保留、7 天原始、默认关闭及 7/5/5 门槛不变，仅统一预测查询的有效日期范围，不重写计时/聚合、不迁移存档。

### 位置（修复）

- `src/DesktopPet.Core/Events/UsageStatsFrequencyJudge.cs`：
  - `UsageStatsFrequencyOptions` 新增只读 `RetentionDays`，默认 `UsageStatsCollector.AggregatedRetentionDays`（=30），与聚合保留一致；注释说明为纯过滤、不写盘、不清理。
  - `UsageStatsFrequencyJudge.Evaluate` 将名义窗口 `[today−WindowDays, today−1]` 与保留区间 `[today−(RetentionDays−1), today]` 取交集：`firstDay = max(today−WindowDays, today−(RetentionDays−1))`。默认即 `[today−29, today−1]`，`today−30` 从**第一次查询起**被排除，不依赖后续 `DrainPending`。
  - `UsageStatsFrequencyResult.WindowDays` 改为输出**实际生效的完整日期数**（`endExclusive − firstDay`），默认配置下为 29，不冒称完整 30 日历史。
- 未改动 `UsageStatsCollector.AggregatedRetentionDays` 常量、`PruneToRetention` 保留语义与 App `GetFrequencyPredictions()`「按需计算、无缓存」。

### 文档同步

- [PRODUCT R-09](../PRODUCT.md#r-09-习惯学习阶段-6)：明确名义 30 日与保留上限取交集、最多 29 个已完成日期、`WindowDays` 为实际生效完整日期数。
- [ARCHITECTURE §7](../ARCHITECTURE.md#p6-003-频次判断约定已实现)：窗口条目改为「名义窗口 ∩ 保留区间」，说明过期桶首查即排除、纯只读过滤；门槛与输出两条补充 `RetentionDays` 与 `WindowDays` 语义。
- [TASKS P6-003](../TASKS.md#p6-003-频次预测样本不足不预测)：范围/输出/实现/测试及 F-01 整改行同步。
- [STATUS](../STATUS.md)：口径与验证数字、F-01 整改状态同步。

### 自测证据（2026-11-27 本机实跑）

- 修正/新增 Core `UsageStatsFrequencyJudgeTests`（仍 11 条）：窗口边界改为 `today−29`（2026-10-27）计入、`today−30`（2026-10-26）排除；达标与边界用例断言 `WindowDays == 29`。**Core 频次专项 11/11**。
- 修正/新增 App `UsageStatsServiceTests` 频次用例（4→6 条）：
  - `Frequency_TodayAndExpiredBuckets_AreIgnored`：当天/未来/`today−31`/`today−30` 均排除，仅 `today−29` 计入（`ObservedDateCount=1`、`WindowDays=29`）。
  - 反例 `Frequency_TodayMinus30_ExcludedFromFirstQuery_StableAcrossDrainAndRestart`：notepad 含 `today−30` 共 5 支持日期；首次查询即 `ObservedDateCount=6`、候选 0；生产 `DrainPending()` 后仍 6/0；重启后仍 6/0。
  - 正例 `Frequency_TodayMinus29_ValidBeforeAndAfterDrainAndRestart`：`today−29` 有效，首查 7/候选 1；`DrainPending()` 后与重启后均保持 7/1。
  - Collector 与 Service 使用同一注入本地日期 `2026-11-25 12:00 +08:00`。
  - **App 统计专项 27/27**。
- 全量回归：Core **613/613**、App **637/637**，0 失败 0 跳过；App 与 DiagnosticProbe 构建 **0 警告 0 错误**（日志 `.temp\p6-003-f01-*.log`，不入库）。

### 限制

- 未真实等待跨月；日期交集与 drain/重启边界以固定注入时钟 + 构造桶覆盖，沿用既有未专项验证的时区/夏令时限制。
- 不延长数据保留、不为查询恢复已过期记录；若确需完整 30 个已完成日期属改变保存期限，须另请用户决定，本轮未采用。

### 复核请求

- 请核对：`today−30` 从首次查询起即被排除、`today−29` 有效；同一时刻 `DrainPending` 前后与重启后有效样本集及候选一致（含正反例）。
- 请核对 PRODUCT/ARCHITECTURE/TASKS/本记录口径一致，`WindowDays` 不再宣称完整 30 日历史。
- 未改 `docs/reviews/` 及证据；未改 P6-001/002 已接收内容；未进入 P6-004；未提交、未推送。

## R2 接收引用（2026-11-27）

依据：[监理 R2](../reviews/P6-003-review.md)（R2 段，2026-11-27）——**F-01 复核通过并关闭，P6-003 接收，可标记 done**。任务状态已由 `awaiting-verification` 更新为 `done`（见 [TASKS](../TASKS.md#p6-003-频次预测样本不足不预测)）。

- 接收依据：`Evaluate` 读取桶前计算名义窗口与保留区间交集，默认 `[today−29, today−1]`，过期桶从首查即排除，未依赖写盘/清理副作用、未延长保留；`WindowDays` 默认报告实际生效完整日期数 29，7/5/5 门槛不变；PRODUCT/ARCHITECTURE/TASKS 口径一致。
- 监理独立复跑：Core 频次专项 **11/11**、App 统计专项 **27/27**（含 `today−30` 首查拒绝与 `today−29` 保留的 drain 前后及真实文件重启正反例）；隔离观测最旧日期反例从首查即 6/0、drain 后仍 6/0（`stableAcrossNormalDrain=true`），保留期内对照 7/1 保持。
- 开发侧按报告要求仅追加本 R2 接收引用，不重复实现、不重跑测试，未改报告及原始证据；已有修改全部保留，未提交、未推送。P6-004 维持 `todo`，等待用户另行安排。
