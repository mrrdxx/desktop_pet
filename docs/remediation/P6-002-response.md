# P6-002 整改记录（开发独立记录）

任务：P6-002 聚合：7 天原始 → 30 天聚合
状态：`awaiting-verification`（开发实现与自动验证已完成，待监理复核；不自行标 done，未提交/推送）
日期：2026-11-26
依赖：P6-001（done，监理 R11 最终接收，F-01～F-06 关闭）。本记录不重测、不改写 P6-001 结论与报告。

## 1. 频次定义（先明确，用户 2026-11-26 要求）

- **一次使用＝一个会话**，不是一次 5 秒采样。
- 会话＝同一 `AppId` 下无中断的连续已验证前台区间链；相邻已验证区间之间只要没有中断，就继续同一会话。
- 结束会话的条件（任一）：缺样/长缺口、空闲超过输入年龄阈值、锁屏、睡眠、暂停、关闭采集、其他生命周期事件（统一走 `ResetBaseline`），以及切换到另一应用。
- 会话计数 `SessionCount` 只记在**会话起始时间所在桶**；跨 30 分钟桶、跨午夜只分裂 `ActiveMs`，不重复计次。
- 已写入权威文档：[PRODUCT R-09](../PRODUCT.md#r-09-习惯学习阶段-6) 新增「频次定义（一次使用＝一个会话）」；[ARCHITECTURE §7](../ARCHITECTURE.md#p6-002-聚合与保留约定已实现) 同步。

## 2. 实现范围与关键口径

### 数据模型（`src/DesktopPet.Core/Persistence/UsageStatsArchive.cs`）

- `CurrentSchemaVersion` 1→2，新增 `buckets` 列表与 `UsageStatsBucket`（`AppId`/`Date`/`BucketStartLocal`/`ActiveMs`/`SessionCount`）。
- v1 文件缺 `buckets` 反序列化为空列表，属附加式演进，不写迁移代码、不伪造历史桶。

### 桶与及时聚合（`src/DesktopPet.Core/Events/UsageStatsCollector.cs`）

- 新增常量 `BucketMinutes=30`、`AggregatedRetentionDays=30`。
- 固定桶：`FloorToBucket` 以本地零点对齐 [:00,:30)/[:30,:60)，桶键 `AppId + "\u0001" + BucketStartLocal`。
- 提交路径：`CommitVerifiedInterval`（先判会话→再按午夜拆段）→ `AddSegment` → `AddBuckets`（同一已验证片段立即切成 30 分钟桶并累加 `ActiveMs`）。聚合在采样提交时即时发生，不等原始记录过期。
- `_buckets` 为已提交字典，`Buckets` 暴露只读视图（按桶起点+`AppId` 排序）。

### 会话计数（同一文件）

- `_sessionOpen`/`_lastCommittedAppId` 跟踪；`BeginOrContinueSession(appId)` 仅在无开启会话或应用变化时返回 true（新会话）。
- `ResetBaseline()` 关闭会话（缺口/空闲/挂起/关闭/生命周期事件），既有的单调回退 floor 语义保持不变。
- 午夜拆段时 `startsNewSession && firstPiece` 只让首个片段计数，跨午夜/跨桶不重复；`AddBuckets` 内 `firstBucket && countSessionStart` 落 `SessionCount++`。

### 保留与清理（同一文件）

- `PruneToRetention()` 同一次调用执行三件事：原始段 7 天、事件 7 天、聚合桶 30 天。
- 两者都按**被统计日期** `Date` 判定（`today.AddDays(-(days-1))` 为最老保留日），互不影响：原始过期不连带删除仍在 30 天内的桶；生成新桶不会延长旧桶寿命。
- 清理发生在每次落盘路径上，采集关闭（`Disable`）时 `DrainPending` 仍照常执行，因此关闭状态下过期桶继续清理。

### 幂等/不重不漏（同一文件 + App 服务）

- `Restore` 只恢复已提交段/事件/桶，不恢复基线、不恢复会话状态、不恢复 generation；重启后已保存桶不重新计次，新活动是新会话。
- 落盘为整份快照覆盖写：`UsageStatsService.PersistSnapshot` 写全部 `Segments/Events/Buckets`，重复 drain、写失败重试不会叠加。
- 待写单元仅携带本轮新增段/事件，桶由快照覆盖写保证一致；写入仍走 P6-001 的单一串行写路径与 generation guard（清除后旧代次不写回）。
- `ClearAll` 同时清 `_buckets`、重置会话状态并 bump generation 后立即落盘，重启不恢复。

### App 接线（`src/DesktopPet.App/Platform/UsageStatsService.cs`）

- 新增 `Buckets` 只读转发；`PersistSnapshot` 写入 `Buckets`。
- 无其他生产逻辑改动：不新增聚合开关，沿用 P6-001 单一全局开关、默认关闭、清除保留开关、独立 `stats.json`，不触碰 `state.json`/宠物存档。

## 3. 测试（针对性 + 隔离存储）

### Core（`tests/DesktopPet.Core.Tests/UsageStatsCollectorTests.cs`，新增 7 条）

1. `Aggregation_ManySamplesInOneBucket_OneSessionNotPerSample`：同桶 11 个已验证区间 → 1 个桶、`ActiveMs=55000`、`SessionCount=1`（不按 5 秒采样计次）。
2. `Aggregation_CrossBucket_OneSessionSplitDuration_SessionCountAtStart`：10:28→10:32 跨桶，时长各 120000ms，仅起始桶计 1。
3. `Aggregation_CrossMidnight_SessionCountOnStartDayOnly`：23:58→00:02，两日桶各 120000ms，仅起始日计 1。
4. `Aggregation_AppSwitch_StartsNewSession`：chrome→notepad 各 1 个会话。
5. `Aggregation_IdleInterruption_StartsNewSession`：空闲中断后同应用第二轮活跃 → 2 个会话。
6. `Aggregation_ThirtyDayRetention_ByCountedDate_IndependentOfRaw7Days`：16 天前原始段被删、16 天前桶保留（30 天内）、37 天前桶被删。
7. `ClearAll_ClearsBuckets_NoPendingResurrection`：清除后桶为空、generation 递增、`DrainPending` 返回 null（旧代次无复生）。

### App（`tests/DesktopPet.App.Tests/UsageStatsServiceTests.cs`，新增 3 条，均在 `%TEMP%/DP6Stats_*` 独立目录）

1. `Buckets_PersistedAndRestored_NoDoubleCountOnRestart`：3 次 tick → 1 桶/10000ms/1 会话；`stats.json` 落桶；重启恢复后 `SessionCount` 与 `ActiveMs` 不变（不重计）。
2. `Buckets_ExpiredByCountedDate_PrunedWhileDisabled`：预置 2026-09-01 桶 → 默认关闭 `Start` 恢复 → `Disable` 同一条 drain 清理并落盘为空（关闭时仍按被统计日期清理，不等原始过期）。
3. `Buckets_ClearedWithStats_ToggleKept`：清除后桶为空并落盘，`IsEnabled` 保持 true（清除保留开关）。

隔离性：测试只读写各自临时目录的 `stats.json`；未写宠物 `state.json`，未改动 `settings.json` 之外的宠物存档字段。

## 4. 验证命令与结果（2026-11-26 本机实跑）

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj` | 通过 **602/602**，0 失败 0 跳过 |
| `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj` | 通过 **631/631**，0 失败 0 跳过 |
| `dotnet build src/DesktopPet.App/DesktopPet.App.csproj -v m` | 成功，0 警告 0 错误 |
| `dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj -v m` | 成功，0 警告 0 错误 |

工作目录 `C:\Users\mrrdxx\Desktop\pet`；测试日志临时文件在 `.temp\p6-002-*-test.log`（不入库）。全部已有修改保留，本任务未提交/推送。

## 5. 证据路径

- 实现：`src/DesktopPet.Core/Persistence/UsageStatsArchive.cs`、`src/DesktopPet.Core/Events/UsageStatsCollector.cs`、
  `src/DesktopPet.App/Platform/UsageStatsService.cs`。
- 测试：`tests/DesktopPet.Core.Tests/UsageStatsCollectorTests.cs`（P6-002 段，L917 起）、
  `tests/DesktopPet.App.Tests/UsageStatsServiceTests.cs`（11 段，L958 起）。
- 规格：`docs/PRODUCT.md` R-09 频次定义；`docs/ARCHITECTURE.md` §7「P6-002 聚合与保留约定（已实现）」；
  `docs/TASKS.md` P6-002 范围与证据。

## 6. 未验证项 / 限制

- 未做真实长时间（跨月）实机聚合观测；跨桶、跨午夜、30 天过期均由注入时钟/注入日期覆盖，未以真实墙钟等待 30 天。
- 未新增实机驱动：本轮为纯逻辑与隔离目录测试，不占用鼠标/屏幕，不产生新的实机 run 证据。
- 未验证项不等同已证明；30 天保留的真实长期行为与真实应用会话边界（实际前台切换/锁屏）待监理安排实机复核。
- 不进入 P6-003 预测、P6-004 展示；未宣称已实现习惯预测。

## 7. 复核请求

请监理复核：① 频次口径是否为“一次使用＝一个会话”，且不以 5 秒采样计次；② 30 分钟固定桶边界与跨桶/跨午夜时长分裂、会话仅在起始桶计数；③ 原始 7 天与聚合 30 天按被统计日期独立过期、关闭时仍清理、及时聚合不等原始过期；④ 重复处理/重启/失败重试不重计不漏计（整份快照覆盖写 + Restore 不重建会话）；⑤ 清除同清桶并防回写、不触碰宠物存档、未新增聚合开关。
复核通过后由监理关闭；开发方不自行标 done。

---

# R1 接收引用（2026-11-27）

[监理 R1](../reviews/P6-002-review.md) 已接收 P6-002（结论：接收、可标 done，本轮无阻断项、不新增 F 编号；TASKS 状态已为 `done`）。监理重新编译并实跑统计专项 Core **39/39**、App **21/21**，另做隔离补充验证 **6/6**（精确半小时边界与连续跨午夜计一次、6000 次受控采样逐桶核对时长/会话、重复 drain→重启新会话、写失败重试不叠加、清除后再重启、v1 加载与 v2 保存、原始 7/8 天与聚合 30/31 天保留边界），证据见 `docs/reviews/evidence/P6-002-R1/`。

开发侧本次仅追加本接收引用：未重复实现、未重测、未改监理报告与 `docs/reviews/evidence`，未提交/推送，全部已有修改保留；P6-003/004 不自动启动。
