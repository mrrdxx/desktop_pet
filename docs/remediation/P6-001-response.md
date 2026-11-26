# P6-001 整改记录（开发独立记录）

任务：P6-001 本地统计：事件采集、关闭与清除
状态：`awaiting-verification`（开发实现与自动验证已完成，待监理复核，不自行标 done，未提交/推送）
日期：2026-11-26

## 1. 修改范围

### Core

- 新增 `src/DesktopPet.Core/Events/UsageStatsCollector.cs`：纯逻辑采集器。常量
  `DefaultForegroundSampleMs=5000` / `DefaultInputAgeThresholdMs=60000` / `DefaultMaxSampleGapMs=15000` / `RawRetentionDays=7`
  （技术初值按监理 R2，30 分钟桶留给 P6-002）。四类事件互不混用：`app-start`（桌宠自身启动，仅每次服务启用后一次性写入）、
  `unlock` / `resume` 分开、`boot` 无独立可靠来源故不记录。输入年龄超过阈值（60s）或缺口（锁屏/睡眠/暂停/缺样）只关段不补计，
  恢复重建基线；日历时间负责日期归属（跨午夜拆段），单调差值负责时长（跨重启不接续）。
- 新增 `src/DesktopPet.Core/Persistence/UsageStatsArchive.cs`：独立 `stats.json` 存储
  （`schemaVersion=1`，camelCase），同目录 temp 文件→`File.Move(overwrite: true)` 同步安全写；损坏文件改名 `.corrupted` 重建；
  原始记录按被统计日期判定 7 天过期，关闭采集时清理仍执行；`Restore` 不恢复 generation（重启不恢复已清除数据）。
- 新增 `tests/DesktopPet.Core.Tests/UsageStatsCollectorTests.cs`：采样/阈值/生命周期/缺样不补计/跨日/识别失败计数等专项测试。
- 修改 `src/DesktopPet.Core/Persistence/PetStateArchive.cs`（L161）：新增 `UsageStatsEnabled=false` 默认字段
  （默认关闭，用户 2026-11-25 决定）；随 settings 存档持久化，清除统计不清除该开关。

### App

- 新增 `src/DesktopPet.App/Platform/UsageStatsService.cs`：独立服务，三构造器（生产默认/隔离目录/测试注入），
  5s `DispatcherTimer` 采样，同一单调快照喂 idle/前台采样；生命周期经 Dispatcher 串行化（`dispatcher:null` 时同步执行）；
  drain 时先按保留期剪枝再 temp→`Move(overwrite:true)` 同步提交，generation guard 丢弃过期代次写单元
  （清除后不存在任何旧代次写回）；公开 `IsEnabled/Start/Stop/Enable/Disable/ClearAll/OnAppStart/OnSessionSwitch/OnPowerModeChanged/OnPauseToggled` 与
  internal `SampleTickManually/DrainPending/StatsFilePath` 测试缝。
- 新增 `src/DesktopPet.App/Platform/ForegroundAppSampler.cs`：前台应用标识采集；
  `TryGetForegroundAppId` public virtual（测试可覆写），`NormalizeAppId` 只保留小写规范化可执行文件基名
  （含去扩展名，不存绝对路径/PID/窗口标题/URL）。识别失败单一口径：丢弃该应用归属区间 + `CoverageMismatchCount` 计数。
- 修改 `src/DesktopPet.App/App.xaml.cs`：
  - `TestSetupIsolatedSave`（L194 区域）：隔离目录下创建 `UsageStatsService`（`autoStartTimer:false`）+ `SetUsageStatsServiceForTest` 测试缝；
  - `OnStartup` 内 `InitializeUsageStats()`（调用点 L611）：仅当 settings `UsageStatsEnabled=true` 才 `Enable` + `OnAppStart`；
  - 系统事件接线（`SessionSwitch`/`PowerModeChanged` 转发到服务，unlock/resume 事件区分）、`OnPauseToggled` 通知暂停/恢复；
  - `SetUsageStatsEnabled(bool)`（L1264）：写 settings + `MarkSettingsDirty`，开启即 `Enable`+`OnAppStart`，关闭即 `Disable`（保留旧数据）；
  - `ClearUsageStats()`（L1287）：调用 `ClearAll`，开关与配置保留；
  - `TestCleanup`/退出路径释放订阅与 `Dispose`。
- 修改 `src/DesktopPet.App/MainWindow.xaml.cs`：右键菜单新增 `MenuIdStatsToggle`/`MenuIdStatsClear`
  （HandleContextMenuCommand 分支 L1002-1017），标签随开关状态动态更新（"Usage Stats: On/Off" 与 "Clear Usage Stats"）。
- `InputIdleSampler` 复用 P4-001 既有实现（`internal virtual GetLastInputTickMs/GetCurrentTickMs` 测试缝已在）；
  R1 整改时按既有可测性设计补 `virtual`（`GetLastInputMs(double)`，无行为变化），见下节 R1 段。

### 测试（本会话新增）

- 新增 `tests/DesktopPet.App.Tests/UsageStatsServiceTests.cs`，13 项专项：
  1. `DisabledByDefault_NeverSamples_NoStatsFile`（默认关闭不采样不产生文件）
  2. `Enable_SampleSwitchApp_PersistsSegmentWithSchemaVersion1`（启用→切换前台→段落盘，schema=1、归一化）
  3. `InputAgeBeyondThreshold_ClosesSegment_IdleNotAttributed`（输入年龄超阈值关段，空闲区间不归属）
  4. `ForegroundResolutionFailed_DropsIntervalAndCountsMismatch`（识别失败丢弃 + 覆盖计数 +2 且落盘）
  5. `Lifecycle_LockSuspendPauseCloseSegment_UnlockResumeRecordDistinctEvents`（锁屏/暂停/睡眠关段，unlock/resume/app-start 事件四类区分）
  6. `Disable_KeepsOldData_StopsNewCollection`（关闭保留旧数据、停止新采集）
  7. `ClearAll_EmptiesArchive_NewDataDoesNotResurrectOld`（清除后新数据不复生旧历史）
  8. `Restart_RestoresPreviouslySavedStats`（重启恢复已保存统计）
  9. `ClearThenRestart_DoesNotRestoreClearedHistory`（清除后重启不恢复）
  10. `NormalizeAppId_KeepsOnlyLowercasedBaseName`（归一化口径）
  11. `AppWiring_SystemAndPauseLifecycle_ForwardedToStatsService`（App 转发真实 `SessionSwitchEventArgs`/`PowerModeChangedEventArgs`/暂停）
  12. `AppWiring_SetUsageStatsEnabled_PersistsToggleAndDisableKeepsData`（开关持久化到 settings.json）
  13. `AppWiring_ClearUsageStats_KeepsToggle_DoesNotTouchPetArchive`（清除保留开关、settings/state 逐字节不变）

## 2. 关键口径落实

- 默认关闭：`PetStateArchive.UsageStatsEnabled=false`；`InitializeUsageStats` 仅 `true` 才启用；测试 1/11/12 覆盖。
- 四类事件、外部应用仅按前台观测不称启动：collector 事件枚举 + 测试 5；`boot` 无独立来源不记录（不安排开机启动/常驻补测）。
- 识别失败单一口径：丢弃 + 计数（测试 4），无 unknown 归属分支。
- 关闭保留旧数据且保留期清理照常（禁用后 drain 仍剪枝）；清除删统计与待写缓存、保留开关配置、generation guard 防旧写回、
  重启不恢复（测试 6/7/9，Restore 不恢复 generation）。
- 不采集标题/URL/正文/按键、不存完整路径/PID 作长期身份：字段白名单（日期/单调时间段/应用标识/事件类型）设计 + 归一化测试 10。
- 统计独立存储、独立服务，不碰宠物存档（`UsageStatsArchive` 独立读 `stats.json`，测试 13 验证 settings/state 内容不变）；
  不触碰 P4 休息提醒链、不给 MainWindow 更新循环堆逻辑（服务自带 5s 定时器）。

## 3. 验证命令与结果（2026-11-26）

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests\DesktopPet.App.Tests --filter FullyQualifiedName~UsageStatsServiceTests` | 通过 13/13，0 失败 0 跳过 |
| `dotnet test tests\DesktopPet.App.Tests` | 通过 623/623，0 失败 0 跳过 |
| `dotnet test tests\DesktopPet.Core.Tests` | 通过 580/580（含新增 UsageStatsCollectorTests），0 失败 0 跳过 |
| `dotnet build DesktopPet.slnx -c Debug` | 已成功生成，0 警告 0 错误 |

工作目录 `C:\Users\mrrdxx\Desktop\pet`；git HEAD `0aa994d`；既有未提交修改（P5B 等）全部保留，本任务未提交/推送。

## 4. 证据路径

- 专项测试：`tests/DesktopPet.App.Tests/UsageStatsServiceTests.cs`（13 项，含 App 隔离接线与隔离存储验证）。
- Core 测试：`tests/DesktopPet.Core.Tests/UsageStatsCollectorTests.cs`。
- 实现：`src/DesktopPet.Core/Events/UsageStatsCollector.cs`、`src/DesktopPet.Core/Persistence/UsageStatsArchive.cs`、
  `src/DesktopPet.App/Platform/UsageStatsService.cs`、`src/DesktopPet.App/Platform/ForegroundAppSampler.cs`、
  `src/DesktopPet.App/App.xaml.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`、`src/DesktopPet.Core/Persistence/PetStateArchive.cs`（L161）。
- 规格：`docs/PRODUCT.md` R-09 细化段、`docs/ARCHITECTURE.md` §7 P6-001 约定（已同步）。

## 5. 未测项 / 限制

- 未做真实系统锁屏/睡眠/暂停操作（另行申请时段许可后实测）；App 接线测试使用构造的真实事件对象
  （`SessionSwitchEventArgs`/`PowerModeChangedEventArgs` 走生产转发路径）。
- 未用真实前台窗口/真实用户数据做夹具；`ForegroundAppSampler` 由测试覆写驱动，真实前台观测与识别失败率待实机确认。
- 实机右键菜单 9/10 真实点击未测（命令分支与动态标签由单测覆盖）。
- 7 天保留期清理由注入日期驱动测试，未经真实天数等待。
- 聚合（30 分钟桶）、预测、展示属 P6-002~004，本任务未实现，样本不足等无样本场景不宣称已实现习惯预测。

## 6. 复核请求

请监理复核：① Core 事件/统计口径与四类事件区分；② 默认关闭与开关持久化、关闭保留/清除防回写/重启不恢复语义；
③ App 生命周期接线与独立存储隔离（不触碰宠物存档与 P4 提醒链）；④ 隔离存储验证证据的可复现性。
复核通过后由监理关闭，开发方不自行标 done。

---

# R1 整改段（2026-11-26，对应 [监理R1](reviews/P6-001-review.md) F-01～F-04）

按报告路径+问题编号记录整改；监理报告与 `docs/reviews/evidence` 未改动。

## R1-F-01 生命周期挂起门控与暂停档初始化

- Collector：`SetPaused/SetLocked/SetSleeping` 三原因独立设置，`IsSuspended`=任一为真；
  挂起时 `SampleTick` 早退（零累计、不读前台、不记事件）；解除一个原因不解除其他原因。
- Service：`OnPauseToggledCore/OnSessionSwitchCore/OnPowerModeChangedCore` 全部改接新 API；
  `OnUnlock`/`OnResume` 各自只清对应原因并记自身事件（unlock 与 resume 分开，不作为无条件恢复采集）；
  挂起时 `DrainPending` 照常（保留期清理不中断）。
- App：`InitializeUsageStats` 在 Enable+OnAppStart 之后按已读 `_isPaused`（L555 先于 L611 初始化，顺序已核实）同步
  `OnPauseToggled(true)`，初始全局暂停生效。
- 反例：挂起两次活跃 tick 后触发暂停/锁屏/睡眠/交叠，随后 tick 零累计；"暂停+锁屏→仅解锁"仍挂起（组合原因部分解除）。
  Core 反向用例（挂起期间零累计 + UnlockOrResume 部分解除）与 App 测试 5/11 覆盖；监理验证器
  "No accumulation while pause/lock/sleep/overlap" 全部 pass。

## R1-F-02 采样有效性与已确认区间/有界检查点

- Collector 重写为**逐 tick 已确认区间**模型（`_lastSampleMs/_lastSampleAppId/_lastSampleLocal` 双状态）：
  相邻成功 tick 之间的可验证区间才提交；换应用时上一 tick 区间已按旧应用提交；
  无效输入（null/NaN/Infinity/未来值）、识别失败、空闲、长缺口、空 appId、挂起＝`ResetBaseline` 丢弃未确认尾部，
  不补计、不丢已验证时长、不双计；跨午夜拆分在 `CommitVerifiedInterval` 循环内。
- 有界持久化检查点：每次 drain 提交当前已提交快照（有效采样即有 stats.json），不再等换应用/停止才落盘。
- 测试布景修正：App Harness 默认活跃（LastInput=CurrentTick=0，同值年龄 0），不再以 null 绕过活动判定。
- 反例：Core 参数化 4 型无效输入零累计、识别失败不延展旧应用、长缺口保留已验证 10000ms、检查点正例、
  挂起零累计；监理验证器对应项全部 pass。

## R1-F-03 清除损坏副本与待写副本

- Service 新增 `DeleteSideArtifacts()`：仅删本存储精确路径 `_statsFilePath+".corrupted"` 与 `_statsFilePath+".tmp"`；
  `ClearAll` 先删副本再 `PersistSnapshot`；不扩大到任意目录、不触碰开关/宠物存档/用户其他文件；清理失败仅 Trace 不外抛。
- 反例（App 测试 7b）：预写损坏 stats.json + 残留 `.corrupted`/`.tmp` → Start → ClearAll →
  目录无两种副本、无任何含旧 sentinel 内容的文件、落盘清空快照；重启不复生。
  监理验证器 "Clear removes preserved historical payload" pass。

## R1-F-04 写盘异常隔离与失败可见

- `SafeWrite` 边界 try/catch（IOException/UnauthorizedAccessException/NotSupportedException）→ bool；
  失败设置 `LastWriteError`（internal，可见）+ `_persistRetryPending`，下次 drain 强制有界重试一次，成功清除错误状态；
  `Stop/Dispose/Disable/ClearAll` 路径均不外抛统计 I/O 异常；`TryLoadArchive` 的 `.corrupted` 保护 catch 同步补齐三类异常。
- 反例（App 测试 7c）：有效采样后把 stats.json 替换为同名目录使写失败 → Disable 不抛、`LastWriteError` 可见、
  内存段保留 → 移除障碍 → 下次 drain 重试成功并清错、失败期间段随重试补写。
  监理验证器 "Optional statistics I/O failure contained" pass。

## 描述修正（监理点名）

开发原报告称 File.Replace/单一异步写入队列；按最终实现改正：安全写为**同目录 temp 文件 + 同步
`File.Move(overwrite: true)`**。同步 Dispatcher 串行是合法方案，未新增异步队列；generation 字段用于清除后
丢弃过期代次写单元，不宣称已验证旧异步写回场景。

## R1 验证结果（2026-11-26 整改后全绿）

| 项目 | 结果 |
| --- | --- |
| Core `UsageStatsCollectorTests` 专项 | 26/26 通过（含 7 条新监理反例等价测试） |
| App `UsageStatsServiceTests` 专项 | 15/15 通过（测试 2/3/5/7/8/9/11/12/13 按逐 tick 语义改写 + 新增 7b/7c） |
| Core 全量 `DesktopPet.Core.Tests` | 589/589 通过 |
| App 全量 `DesktopPet.App.Tests` | 625/625 通过 |
| `dotnet build DesktopPet.slnx` | 0 警告 0 错误 |
| 监理 R1 隔离验证器（新隔离目录实跑，读 JSON pass 字段） | 13/13 全部 `"pass": true` |

测试基础设施修复：`MutableIdleSampler` 覆写 `GetLastInputMs` 使用受控 now 域（与 collector 时钟同域）；
`InputIdleSampler.GetLastInputMs(double)` 按既有可测性设计补 `virtual`（无行为变化）。
原因：全量运行（进程已启动数十秒）下真实时钟 now 被 collector 的"未来值拒绝"当成无效输入，导致零累计偶发。

工作目录 `C:\Users\mrrdxx\Desktop\pet`；git HEAD `0aa994d` 未变；本任务整改未提交/推送，全部已有修改保留。
状态保持 `awaiting-verification`，请监理复核；不自行标 done，不进入 P6-002。

---

# R2 整改段（2026-11-26，对应监理 [R2](reviews/P6-001-review.md) 剩余边界；已接受实现未重做）

按报告路径+问题编号记录整改；监理报告与 `docs/reviews/evidence`（含 R2 增强验证器源码/JSON）未改动。
R2 验证器在隔离临时目录只读复制后实跑核对（开发侧自验 4/13 → 13/13），原文件与产物未触碰。

## R2-F-01 Service 挂起期间不得读前台采样器 + App 默认关闭暂停档初始同步

- Service `SampleTick()`（`UsageStatsService.cs`）：门控前置为 `_collector.IsEnabled && !_collector.IsSuspended`
  才读 idle/前台采样器并喂 `SampleTick`；挂起期间（暂停/锁屏/睡眠/交叠，任一组合）零采样器调用；
  `DrainPending()` 无条件照常执行（保留期清理不中断）。此前挂起仍读前台采样器（验证器观察到 Calls 2→4）。
- App `InitializeUsageStats()`（`App.xaml.cs`）：`_isPaused` 同步**无条件**执行（移出 `UsageStatsEnabled` 开关分支）——
  统计默认关闭时暂停启动也必须让服务挂起；`SetUsageStatsEnabled(true)` 内 `Enable` 后按当前 `_isPaused` 再同步一次
  （幂等：暂停期间开启统计不解除挂起）。
- 反例：pause/lock/sleep/overlap 四种挂起在两次活跃 tick 后，随后两次 tick 前台调用计数不变且总时长保持 5000ms
  （验证器 8 条）；App 新增测试 `AppWiring_PausedStart_ToggleOff_EnablingKeepsSuspended`
  （TestSetupIsolatedSave → 反射 `_isPaused=true` → 生产 `InitializeUsageStats` → 开关开启后 `collector.IsSuspended` 仍为真）。

## R2-F-02 单调回退防重复累计

- Core `SampleTick()`（`UsageStatsCollector.cs`）：坏基线判定前增加
  `if (_lastSampleMs.HasValue && nowMs < _lastSampleMs.Value) { ResetBaseline(); return; }`——
  时钟回退视为断链：不降低已验证时间边界、不提交当前区间、返回；恢复后首个 tick 仅重建基线，不补计、不双计。
- 反例：`SampleTick("editor",0)→5000→1000→5000` 总时长保持 5000ms；恢复正向推进后仅在相邻成功 tick 间提交新区间
  （Core 新增 `MonotonicRollback_DropsTail_NoReaccumulation_ResumeForward` 与正例 `MonotonicForward_NormalTicks_AccumulateExactly`）。
  监理验证器 "Clock rollback does not duplicate verified interval" pass。

## R2-F-03/F-04 清除副本失败可见未完成 + 有界重试闭环

- `DeleteSideArtifacts()` 改为返回 bool（全部成功或文件不存在=true）；失败时设置 `LastWriteError`（不再仅 Trace）。
- `ClearAll()`：副件删除失败置 `_clearRetryPending=true`；随后 `PersistSnapshot()` 成功仅在
  `!_clearRetryPending` 时清 `LastWriteError`/`_persistRetryPending`——快照落盘成功不得掩盖未完成的清除。
- `DrainPending()` 开头若 `_clearRetryPending` 重试副件删除，成功则清 `_clearRetryPending` 与 `LastWriteError`
  （有界重试闭环，不静默遗留损坏副本；占用期间删不了是 OS 正常行为，只要求可见+后续重试，不绕过文件锁强删）。
- 反例（App 新增测试 `ClearAll_SideArtifactLocked_FailureVisible_RetriedOnDrain`，对齐验证器）：
  预写损坏 stats.json → Start 保留为 `.corrupted` → `FileShare.Read` 锁该副件 → `ClearAll` 后
  `LastWriteError` 非空、被锁副件存在、stats.json 落盘空快照；解锁 → `DrainPending` 重试删除成功、错误状态清除。
  监理验证器 "Failed backup deletion is observable" / "Retained history exposes incomplete clear status" /
  "Clear retry removes remaining historical backup" 三条全部 pass。

## R2 验证结果（2026-11-26 整改后全绿）

| 项目 | 结果 |
| --- | --- |
| Core `UsageStatsCollectorTests` 专项 | 28/28 通过（26 存量 + 2 新增单调回退正/反例） |
| App `UsageStatsServiceTests` 专项 | 18/18 通过（15 存量 + 3 新增：挂起前台计数、副件锁清除、暂停启动开启仍挂起） |
| Core 全量 `DesktopPet.Core.Tests` | 591/591 通过 |
| App 全量 `DesktopPet.App.Tests` | 628/628 通过（App 接线 11/12/13 与 7/7b/7c 不受影响） |
| `dotnet build DesktopPet.slnx -c Debug` | 0 警告 0 错误 |
| 监理 R2 增强验证器（隔离临时目录实跑，读 JSON pass 字段） | 13/13 全部 `"pass": true`（整改前基线 4/13） |

工作目录 `C:\Users\mrrdxx\Desktop\pet`；git HEAD `0aa994d` 未变；R2 整改未提交/推送，全部已有修改保留。
状态保持 `awaiting-verification`，请监理 R3 复核；不自行标 done，不进入 P6-002，不做实机验证。

---

# R3 整改段（2026-11-26，对应 [监理R3](reviews/P6-001-review.md) 唯一剩余 F-02 连续回退窗口）

按报告路径+问题编号记录整改；监理报告与 `docs/reviews/evidence`（含 R3 验证器源码/JSON）未改动。
R3 验证器在隔离临时目录只读复制后实跑核对（复跑前清空输出目录，避免 `Start()` 恢复上轮遗留 `stats.json` 造成假失败），原文件与产物未触碰。

## R3-F-02 保留已计时单调上界：连续低于上界的样本不得重建可重复累计的基线

- Core `UsageStatsCollector`（`UsageStatsCollector.cs`）新增独立字段 `_monotonicRollbackFloorMs`
  （自上次 `ClearAll` 起已接受的最高单调上界），与确认区间基线 `_lastSampleMs` 分离：
  - `SampleTick` 回退判定由 `< _lastSampleMs` 改为 `< _monotonicRollbackFloorMs`——低于上界即拒绝并
    `ResetBaseline()`（丢弃未确认尾部、**保留 floor**），连续低于上界的 tick 持续拒绝，无法以较低 tick
    重建可重复累计的基线；
  - 确认 tick 末尾 `floor = Math.Max(floor ?? nowMs, nowMs)`：被接受的样本单调抬高已计时上界
    （回退分支已保证不会低于当前 floor，只会上升）；
  - `Enable/Disable/ResetBaseline/SetSuspensionReason`（pause/lock/sleep 任一进入或解除）均保留 floor——
    同一单调时间域内已提交数据必须持续受防重计保护；`ClearAll` 重置 floor（清除后是新时间域，新记录
    从当前时间点正常建立，不受旧上界阻挡）；`Restore` 不设 floor（跨进程重启不恢复）。
  - 恢复语义：回到/越过上界的 tick 走既有缺测重建路径（首个样本仅建基线，下一相邻成功采样才提交），
    只累计未计过的可靠区间；实现未新增固定忽略 tick 数量，未重写已接受逐 tick 模型。
- 生命周期边界（监理 R2/R3 要求明确）：开启/关闭/暂停/锁屏/睡眠/挂起组合/缺口/idle/识别失败均保留上界；
  仅清除重置。对应边界测试见下。
- 测试（`UsageStatsCollectorTests.cs` 新增 4 条，位于 R2 单调回退段后）：
  1. `RollbackWindow_ConsecutiveBelowFloor_NeverRebuildsReaccumulatingBaseline`——监理 R3 反例序列
     `0→5000→1000→2000→5000`，期望总时长 5000ms（回退窗口不得重复累计成 8000ms）；
  2. `RollbackWindow_PersistentlyBelowFloor_MultipleTicksStayRejected`——持续低于上界多 tick
     （1000/2000/3000/4000）全部拒绝不建基线，回到上界 5000 重建基线，[5000,6000) 才提交；
  3. `RollbackWindow_JumpAboveFloor_ResumesAsOrdinaryGapRebuild`——越过上界（直接跳到 6000）按既定
     缺口重建规则恢复，只累计未计过的 [6000,7000)；
  4. `RollbackFloor_Lifecycle_EnableDisableKeeps_ClearResets`——disable→enable 保留已计时上界、
     `ClearAll` 重置上界后新时间域正常建立。
  正常单调正例沿用 R2 既有 `MonotonicForward_NormalTicks_AccumulateExactly`（未改，仍通过）。

## R3 验证结果（2026-11-26 整改后全绿）

| 项目 | 结果 |
| --- | --- |
| Core `UsageStatsCollectorTests` 专项 | 32/32 通过（28 存量 + 4 新增 R3 连续回退/生命周期测试） |
| 监理 R3 验证器（隔离临时目录，清空输出目录后实跑，读 JSON pass 字段） | 14/14 全部 `"pass": true`（含 "Multiple post-rollback ticks do not re-count pre-high-water interval"） |

自验说明：验证器复跑会向输出目录写入 `stats.json`；残留会导致 `Start()` 恢复上轮已计时长造成假失败，
故复跑前必须清空输出目录。监理报告与证据原件未触碰，自验用的临时副本在 `%TEMP%`，未引入仓库。

工作目录 `C:\Users\mrrdxx\Desktop\pet`；git HEAD `0aa994d` 未变；R3 整改未提交/推送，全部已有修改保留。
已关闭项 F-01/F-03/F-04 未重做；App 层零改动（本次整改仅限 Core collector 与 Core 测试）。
状态保持 `awaiting-verification`，请监理复核唯一剩余 F-02；不自行标 done，不进入 P6-002，不做实机验证。

---

# R5 整改段（2026-11-26，对应 [监理R5](reviews/P6-001-review.md) 唯一剩余 F-05 隔离实机入口缺口）

按报告路径+问题编号记录整改；监理报告与 `docs/reviews/evidence`（含 R5 反例证据）未改动。
本次只修最小隔离场景与操作清单：不重做已关闭 F-01～F-04 产品逻辑，不建大型驱动，不做实机（不占鼠标）。

## R5-F-05-1 重启必须从既有独立 settings/state 生产加载（不再 seed 覆盖）

- `App.xaml.cs` 新增 internal 窄包装（生产逻辑零改动）：
  - `LoadArchivesForIsolatedProbe()`：走生产 `LoadState → LoadSettings → _isPaused=_state.IsPaused`，不覆盖磁盘已有
    settings/state；文件缺失返回默认（首轮新目录可 seed）；
  - `TestEnsureProbeRuntimeObjects()`：只补 RuntimeTracker/RareEventScheduler/SocialCoordinator/ActivitySession/Emotion
    等宠物窗口依赖的运行对象，不覆盖已加载存档；
  - `TestSettings` 只读访问器（断言磁盘实际加载值）。
- `Program.cs` `UsageStatsLiveVerification` 开头按 `settings.json/state.json` 是否存在区分：
  重启 → `LoadArchivesForIsolatedProbe` + `TestEnsureProbeRuntimeObjects`；首轮 → 原 `TestSetupIsolatedSave` + seed。
- `Program.cs` 新增 `UsageStatsWiringVerification`（无窗口场景）Phase 1-2：仅在**全新目录**用生产
  `SaveSettings/SaveState` 播种 `settings.UsageStatsEnabled=true`，随后统一走生产
  `LoadArchivesForIsolatedProbe` → 断言 `TestSettings.UsageStatsEnabled==true` 且初始化后 `stats.IsEnabled==true`；
  同目录重跑不覆盖上一轮已落盘的 settings/state。
  R5 反例（预存 On→setup+生产包装后变 false）已闭环。

## R5-F-05-2 补生产系统订阅共享接线（退出走既有退订）

- `UsageStatsLiveVerification` 与 `UsageStatsWiringVerification` 均经 **`StartForIsolatedProbe`** 启动共享
  `InitializeActivitySession`（含 `SubscribeSystemEvents`，与生产 `OnStartup` 同一接线）；
  退出统一走既有 `ShutdownMainWindow`（`UnsubscribeSystemEvents` + `ActivitySessionService.Stop/Dispose` +
  `UsageStatsService.Stop/Dispose` + 关窗）。
- 无窗口接线证据：wiring 场景 `subscribedAfterStart=true`，`usage-stats-wiring-cleanup-1.json` 记录
  `subscribedAfterCleanup=false`、`serviceNullAfterCleanup=true`、`shutdownCalled=true`。

## R5-F-05-3 sidecar 收尾时机 + 按 session/run 编号保留证据（不覆盖）

- sidecar 从"启动时预写"改为 **shutdownTimer 内真正收尾时刻** 从服务当前值读取（`IsEnabled/Generation/Segments/Events/LastWriteError` 等）；
  删除操作前预写 `usedRealContextMenuDispatch=true`，改如实记录 `realContextMenuDispatchConfirmed=false`
  （探针无法自行确认真实右键菜单分发，由操作清单人工记录）。
- 观测/sidecar/cleanup 文件按 session/run 编号命名（`usage-stats-observations-<n>.jsonl`、
  `usage-stats-sidecar-<n>.json`、`usage-stats-cleanup-<n>.json`）；`NextSessionNumber` 枚举目录现有
  `<prefix><n>.jsonl` 取 max+1。数据目录可共享、观察文件不覆盖。
- 无窗口证据：同一 run 目录连跑两次 wiring 场景，session-1 与 session-2 的 obs/sidecar/cleanup 三件套并存不覆盖
  （见下表）。

## R5-F-05-4 操作清单拆清状态与语义 + app-start 语义 + 标记应用归属

- 操作清单（`docs/history/P6-001-live-entry.md`）状态拆分：
  - **E「关闭保留」**：`enabled=false`、已有数据保留并冻结（零增长、不清除）；
  - **F「重新开启后清除保留 On」**：清除后记录 generation/时间并存 sidecar，旧记录消失（不无条件要求空列表持续），
    toggle 保持 **On**，允许之后新记录（重新开启路径保持 On）；
  - **同目录重启**：settings/state 从磁盘恢复（开关保留）、已清除历史**不恢复**、允许新记录，写入新 session 编号文件。
- **app-start 语义**：只在启动时 settings 已启用才记录一次（`InitializeUsageStats` 门控），运行中不补造；
  wiring 场景以**初始化前后增量 `appStartDelta==1`** 断言（同一目录重启时，上次启动已持久化的 app-start 被生产恢复保留，
  本次启动再新增一条，累计条数会从 1 增到 2，增量恒为 1）；live 场景首轮默认 OFF 不记录。
- **标记应用归属**：前台标记应用由**操作者**自己创建并精确归属（如标题含 `PET-STATS-MARKER-A` 的记事本）；
  探针不创建、不借用用户未保存窗口、不以注入/假前台充当实机证据。

## R5 无窗口接线验证证据（2026-11-26，本机实跑）

构建验证：

| 命令 | 结果 |
| --- | --- |
| `dotnet build src/DesktopPet.App/DesktopPet.App.csproj -c Debug --no-dependencies` | 成功，0 警告 0 错误 |
| `dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj -c Debug` | 成功，0 警告 0 错误 |

wiring 场景（`DesktopPet.DiagnosticProbe.exe <base> usage-stats-wiring <result>`，无窗口、无鼠标），
同一 base 连跑两次，证据在 `%TEMP%\codefree-agent\p6-001-r5-wiring3\usage-stats-wiring\`：

| 断言 | 证据 | 结果 |
| --- | --- | --- |
| 隔离预存 On → 生产加载保持 On | obs/sidecar-1/2 `toggleOnAfterLoad=true`、`enabledAfterInit=true` | 通过 |
| app-start 仅在启动已启用时记录，不补造 | session1 `appStartEventCount=1`；session2 `appStartEventCount=2`（累计恢复+新增），两轮 `appStartDelta=1` | 通过 |
| 系统事件已订阅 → 正常清理后退订 | sidecar `subscribedAfterStart=true`；cleanup `subscribedAfterCleanup=false`，且探针退出前主动读取 cleanup 断言 | 通过 |
| 独立 session 文件不覆盖 | 连跑两次产出 `*-1.*` 与 `*-2.*` 三件套并存；session2 主动读取 session1 obs 并断言 `session==1` 未被覆盖 | 通过 |

限制：真实右键菜单点击、真实前台切换、真实锁屏/睡眠仍须监理与用户约时段实机执行；本轮仅无窗口/无鼠标接线验证，
不标 done、不提交/推送、不进入 P6-002～004。F-01～F-04 保持关闭未重做。
# R8 整改段（2026-11-26，对应 [监理R8](reviews/P6-001-review.md) 唯一剩余 F-06 隔离重启退出异常）

## R8-F-06 隔离探针首轮/重启统一按生产 OnExplicitShutdown 建立生命周期 + 真实菜单 Exit 子进程回归

### 根因（与 R8 定位一致，已由对照实验复现）

- 生产 `App.xaml` 配置 `ShutdownMode=OnExplicitShutdown`；探针 `new App()` 未加载 XAML，WPF 取默认 `OnLastWindowClose`。
- 首轮 `TestSetupIsolatedSave` 内部会调用既有测试设施 `ResetWpfLifecycleForTest()`（反射）对齐该模式；
  **重启分支** `LoadArchivesForIsolatedProbe + TestEnsureProbeRuntimeObjects` 不做同样对齐。
- `App.ShutdownMainWindow` 先 `Close()` 所有宠物窗口（L1599-1601），之后才在 L1609 设置 `ShutdownMode`。
  `OnLastWindowClose` 下关闭最后一扇窗即触发 WPF 自动关闭，setter 抛
  “正在关闭或已经关闭应用程序时无法设置ShutdownMode”，进程以 -532462766 结束、无正常 cleanup/sidecar。
- 探针此前用 `Dispatcher.Run()` 自建消息循环（不是 `Application.Run()`），生产 `Shutdown(0)` 无法结束该循环；
  这也是先前脚本收尾只能紧跟 `Environment.Exit(0)` 的原因。

### 修法（限定隔离探针初始化/收尾，未改产品业务逻辑）

1. `tests/DesktopPet.DiagnosticProbe/Program.cs` `UsageStatsLiveVerification` 与新增
   `UsageStatsExitVerification`：在**创建/关闭窗口之前**，对首轮与重启两条分支统一
   `s_app!.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;`（公开属性，值同生产 App.xaml）。
   不新增 WPF 私有字段反射；不 catch 吞 `ShutdownMode` setter 异常；不使用 `Environment.Exit(0)` 伪装成功。
   首轮仍由 `TestSetupIsolatedSave` 的既有反射兜底，二者取同值，不影响其他测试设施。
2. `src/DesktopPet.App/MainWindow.xaml.cs`：`MenuIdExit` 由 `private const` 改 `internal const`（仅可见性，
   与既有 `MenuIdPerchOnWindow` 等探针缝一致），使回归能驱动**同一生产菜单分发**而非复制命令。
   生产行为零变化（`ShutdownMainWindow` 顺序、产品服务、采样、清除逻辑均未改）。
3. 新增探针场景 `usage-stats-exit`：真实宠物窗口 + 生产 `InitializeUsageStats` + 真实
   `Application.Run()` 生命周期（`_probeIsolatedMode` 下 `OnStartup` 早退，不建生产窗口/托盘），
   0.8s 后经 `TestOpenContextMenuLifecycle(MenuIdExit)` 走
   `HandleContextMenuCommand → Dispatcher.BeginInvoke → App.ShutdownMainWindow`；
   进程以 `Application.Run()` 正常返回结束（**不是** `Environment.Exit(0)`），OS 取真实退出码；
   30s 挂起守卫仅用于“生产退出没能结束循环”时显式失败 exit 1。
   同目录跑两次分别覆盖首轮与重启分支；证据按 session 编号写 `usage-stats-exit-<n>.jsonl`（pre-exit/post-exit 两行）。

### 对照实验（证明回归能检出 F-06，非假通过）

在 `usage-stats-exit` 中临时移除上述 `ShutdownMode` 赋值、同目录跑第二次（重启分支）：
进程 `Unhandled exception. System.InvalidOperationException: 正在关闭或已经关闭应用程序时无法设置ShutdownMode`，
栈顶 `App.ShutdownMainWindow`（`App.xaml.cs:1609`），经 `MainWindow.HandleContextMenuCommand`
（`MainWindow.xaml.cs:995`）菜单分发，退出码 **-532462766**，无 result/cleanup——与 R8 现象一致。
随后恢复赋值。首轮对照不报错，与 R8“首轮经既有测试初始化已对齐”的判断相符。

### 验证结果（本机实跑，2026-11-26）

| 检查项 | 实测 | 结论 |
| --- | --- | --- |
| 构建 | App 与 DiagnosticProbe Debug 均 0 警告 0 错误 | 通过 |
| 首轮（session 1，isRestart=false）退出码 | 0（`Application.Run()` 正常返回） | 通过 |
| 重启（session 2，isRestart=true）退出码 | 0 | 通过 |
| 退出方式 | `shutdownViaMenuDispatch=true`、`usedEnvironmentExitZero=false` | 通过 |
| 服务停止 | `usageStatsNullAfterExit=true`、`activitySessionNullAfterExit=true` | 通过 |
| 系统通知解绑 | `unsubscribedAfterExit=true` | 通过 |
| 异常日志 | `logs/diagnostic-*.log` 0 个文件、`exceptionLoggedInDiagnosticLog=false` | 通过 |
| 证据不覆盖 | `usage-stats-exit-1.jsonl` 与 `-2.jsonl` 并存 | 通过 |
| App 单元测试 | 628/628 通过（可见性改动无回归） | 通过 |

证据目录（本机，临时）：`%TEMP%\codefree-agent\p6-001-r8-final\usage-stats-exit\`（两轮 jsonl + result-1/2.json + settings/state/stats）；
对照目录 `%TEMP%\codefree-agent\p6-001-r8-control3\`。临时目录不入库。

### 限制与范围

- 已通过的菜单启停、前台统计、清除与恢复**未重做**；F-01～F-05 关闭结论不变。
- 真实锁屏/睡眠仍未验证；本轮未触及真实用户目录，未启用真实统计。
- 未改 `docs/reviews` 及原始失败证据；未提交/推送；不进入 P6-002。
- 请监理按 R8 F-06 复核：是否符合“首轮与重启建窗前对齐、公开属性、无吞异常/无伪装退出码”，以及子进程回归是否足以关闭该阻断项。

---

# R11 接收引用（2026-11-27）

[监理R11](reviews/P6-001-review.md) 已最终接收 P6-001（F-01～F-06 关闭，含 R10 真实锁屏、R11 真实睡眠生命周期证据），任务已标 done。开发侧本次仅追加本接收引用，未重测、未改监理报告与 `docs/reviews/evidence`。
