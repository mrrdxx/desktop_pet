# 当前状态

更新时间：2026-11-27（Asia/Shanghai）。任务状态唯一出处为[TASKS](TASKS.md)。

P6-001已由[监理R11](reviews/P6-001-review.md)最终接收、任务done，F-01～F-06关闭。用户真实锁屏/解锁由R10接收；本轮监理按用户要求实际发起非强制Windows S3睡眠、用户唤醒登录，OS事件42及真实resume/unlock齐全，睡眠及醒后锁定期间零区间归属、无补计，恢复后约5秒采样。[睡眠证据](reviews/evidence/P6-001-R11/)、[锁屏证据](reviews/evidence/P6-001-R10/)。

最新隔离PID84136自动正常exit0，退订/服务释放、无异常日志，旧PID65588/87228亦已结束；当前无后台测试。R8菜单/前台/清除恢复、R9退出修复已接受，不重测；早期失败轮保留不改写。

**阶段 6 本版已完成**：P6-001～004 均 done，当前状态见 [TASKS](TASKS.md)；P6-004 由[监理 R4](reviews/P6-004-review.md)最终接收，F-01/F-02 关闭保持。用户按 [D-020](DECISIONS.md) 确认本版纯气泡，主动靠近另记 FUT-001（todo，未排期）。默认统计关闭保持。R3 监理夹具隔离事故与恢复限制见下，不概括为“正式文件从未触碰”。

## P6-002 聚合：7 天原始 → 30 天聚合（2026-11-26）

- 当前验收：P6-002 已接收（[TASKS](TASKS.md#p6-002-聚合7-天原始--30-天聚合)）。[监理 R1](reviews/P6-002-review.md)、[整改记录](remediation/P6-002-response.md)。
- 频次口径：一次使用＝一个会话（同 AppId 无中断的连续已验证前台区间链），不把 5 秒采样当一次使用；中断（缺样/空闲/锁屏/睡眠/暂停/关闭/生命周期事件/应用切换）结束会话；会话只计入起始桶，跨桶/跨午夜只分裂时长。已写入 [PRODUCT R-09](PRODUCT.md#r-09-习惯学习阶段-6)。
- 实现：`UsageStatsArchive` schemaVersion=2 新增 `buckets`/`UsageStatsBucket`；`UsageStatsCollector` 新增 `BucketMinutes=30`/`AggregatedRetentionDays=30`、`AddBuckets`/`BeginOrContinueSession`/`PruneBuckets`、`Restore`/`ClearAll` 处理桶、`ResetBaseline` 关会话；`UsageStatsService` 转发 `Buckets` 并由 `PersistSnapshot` 整份快照覆盖写。沿用单一全局开关、默认关闭、generation guard、独立 `stats.json`，不碰宠物存档。
- 保留：原始段/事件 7 天、聚合桶 30 天，均按被统计 `Date` 独立判定；`PruneToRetention` 每次落盘执行，采集关闭时仍清理。
- 验证（2026-11-26 本机实跑）：Core `DesktopPet.Core.Tests` **602/602**、App `DesktopPet.App.Tests` **631/631**，0 失败 0 跳过；App/DiagnosticProbe 构建 0 警告 0 错误（日志 `.temp\p6-002-*-test.log`、`.temp\p6-002-*-build.log`，不入库）。新增 Core 7 条、App 3 条隔离目录用例（`%TEMP%/DP6Stats_*`）。
- 监理实跑（2026-11-27）：重新编译 Core 统计专项 **39/39**、App **21/21**；补充隔离检查 **6/6**（6000 次采样逐桶核对、精确边界、重启新会话、失败重试、清除、v1 兼容），[证据](reviews/evidence/P6-002-R1/)。未重跑无关全量、未占屏幕、未触碰正式统计目录。
- 限制：未真实等待跨月，受控时钟与真实文件往返已满足本步验收；v1 旧原始记录保留、不补造旧聚合频次。时区切换/夏令时未专项验证，不宣称已实现习惯预测。
- 下一步：开发侧只追加 R1 接收引用；P6-003 随后按用户安排推进，见下节。不重复实现或重测、不提交推送，已有修改全部保留。

## P6-003 频次判断：样本不足不预测（2026-11-27）

- 当前验收：[监理 R2](reviews/P6-003-review.md)关闭 F-01、接收 P6-003（[TASKS](TASKS.md#p6-003-频次预测样本不足不预测)、[交付记录](remediation/P6-003-response.md)）。
- 口径（首版保守起步值，未经真实效果验证）：名义窗口＝当天之前 30 个完整日期，与“含今天保留 30 天”聚合保留上限取交集，排除当天后实际可用 `[today−29, today−1]`，最多 29 个已完成日期（不冒称完整 30 日）；过期桶从第一次查询起即排除，不依赖 drain；覆盖 ≥7 个有记录日期；同一应用同一半小时时段 ≥5 个不同日期且会话 ≥5；输出仅应用标识/时段/支持日期数/会话数（另附 `ObservedDateCount` 与表示实际生效完整日期数的 `WindowDays`），不作准确率。已写入 [PRODUCT R-09](PRODUCT.md#r-09-习惯学习阶段-6)、[ARCHITECTURE §7](ARCHITECTURE.md#p6-003-频次判断约定已实现)。
- 实现：Core `UsageStatsFrequencyJudge`（纯逻辑、无缓存，只读 P6-002 `UsageStatsBucket`）；App `UsageStatsService.GetFrequencyPredictions()` 按需计算，关闭/清除立即为空、重启由恢复桶重算；沿用单一全局开关，仅内部输出（无气泡/移动/图标定位/启动应用）。
- 验证（本机实跑，含 F-01 整改后）：Core 频次专项 11/11、App 统计专项 27/27（含 6 项频次）；全量 Core **613/613**、App **637/637**，0 失败 0 跳过；App 与 DiagnosticProbe 构建 0 警告 0 错误（日志 `.temp\p6-003-f01-*.log`，不入库）。
- 未验证：无真实长期使用数据，不宣称预测可靠；未真实等待跨月；时区/夏令时未专项验证。
- 监理实跑：Core 频次专项 **11/11**、App 统计专项 **25/25**；隔离反例固定日期下正常 drain 前覆盖7/候选1，后覆盖6/候选0，对照保留期内数据不变。[证据](reviews/evidence/P6-003-R1/)。未改产品/正式测试、未占屏幕、未使用真实统计目录、未重跑无关全量。
- R2 监理实跑：重新编译 Core **11/11**、App **27/27**；原反例首次查询及 drain 后均覆盖6/候选0，对照均覆盖7/候选1，正式测试含清理与重启正反例。[证据](reviews/evidence/P6-003-R2/)。日期交集与 WindowDays=29 口径一致，F-01 关闭；无产品/正式测试修改、无实机操作、未重跑无关全量。
- 接续：P6-003 已闭环，不重复实现/重测；P6-004 随后已按用户安排开工，见下节。不宣称规则门槛已获得真实预测效果验证。

P6-001 开发侧无需继续整改或重测（R11 已接收），仅在 [P6-001 整改记录](remediation/P6-001-response.md#r11-接收引用2026-11-27) 追加了接收引用。监理未改产品/探针/正式测试、未提交推送，全部已有修改保留；旧阶段接收保持。
## P6-004 第一小步：气泡反馈与统计查看入口（2026-11-27）

- 当前验收：[监理 R4](reviews/P6-004-review.md)按用户纯气泡范围最终接收 P6-004；R3 真实关闭按钮/焦点/拖动/透明区及菜单入口通过。[TASKS](TASKS.md#p6-004-展示气泡反馈与统计入口) 为 done，[交付记录](remediation/P6-004-response.md#6-r1-限定整改f-01--f-022026-11-27)，未提交/推送。
- 实现：Core `UsageStatsBubblePlanner`（当前半小时时段匹配、每日去重、抑制、仅依据文案、准确名称汇总）；`UsageStatsArchive.BubbleLastShownLocalDate` 独立持久化。App `StatsBubbleWindow`（8 秒自动隐藏、可关闭、无激活工具窗、放宠物外侧）、`StatsViewerWindow`（已记录统计 / 有样本支持的时间偏好两段）、`MainWindow`「Usage Statistics」入口、`App` 30 秒判定与暂停/锁屏/睡眠/关闭/清除/退出撤销接线。
- 验证（本机实跑）：Core 规则专项 **25/25**、App 隔离专项 **7/7**；DiagnosticProbe 新增 `usage-stats-bubble` 隔离预览（真实宠物窗口 Show，不移动/不注入鼠标/不启动应用），证据显示候选 notepad@09:30 支持 5 天/会话 5、气泡仅展示一次且文案含依据、查看窗两段准确名称、关闭保留标记、清除重置标记。全量 Core **638/638**、App **644/644**，0 失败 0 跳过；App 与探针构建 0 警告 0 错误（日志 `.temp/p6-004-*.log`、证据 `.temp/p6-004-probe-run/`，不入库）。
- R1 F-01/F-02 整改（2026-11-27）：F-01 `IsAvailableForStatsBubble` 追加 `!IsInSharedSocialSession`（与 `IsPerchWriteAllowed` 同源，保留本地社交旗及全部守卫）；F-02 `SummarizeRecorded` 增加本地时间参数，从首查即按含今天 `[today−29, today]` 过滤（与采集器 `PruneToRetention` 同口径，查询不写盘、不延长保留），`GetRecordedSummary` 传入 `_localNow()`。新增 Core 4 条边界、App 3 条正反例（共享会话不选/不消费/会话结束恢复/双宠跳过；today−30 首查排除、today−29 与今天保留、drain 前后与重启一致、关闭可见、清除为空）。专项 Core **28/28**、App **10/10**；全量 Core **641/641**、App **647/647**；App/探针构建 0 警告 0 错误；探针场景干净目录重跑 exit 0（日志 `.temp/p6-004-f01f02-*.log`，不入库）。
- 保留限制：真实长期样本、多屏/DPI 组合、真实双宠共享会话实机观感未专项验证，不宣称预测可靠。8秒自动隐藏与真实关闭按钮已由R3覆盖。习惯反馈未实现靠近/图标定位/启动应用；用户按D-020将靠近明确延期，并非验收了移动。
- 监理实跑：重新编译 Core **25/25**、App **7/7**；隔离补证复现两项，原始观测与源码见[证据](reviews/evidence/P6-004-R1/)。离屏真实 WPF Dispatcher 泵 8504ms 后自动隐藏通过，无真实输入注入；未重跑无关全量、未触碰正式统计目录。
- R2 监理实跑：重新编译专项 Core **28/28**、App **10/10**；原隔离验证器四组全部通过，共享状态守卫/空闲对照与汇总首查/清理一致；离屏消息泵 8493ms 后自动隐藏。[证据](reviews/evidence/P6-004-R2/)。未重跑无关全量、无真实输入注入、未改产品/正式测试，F-01/F-02 关闭。
- R3 实机：正确隔离的两轮真实输入通过，默认隐藏采样跨度约7995/7925ms；关闭按钮轮仅夹具使用 autoHide=false；气泡展示保留前台，真实拖动位置540/410→660/460→760/500，透明角落2次落到底板，真实菜单打开查看窗并确认两段内容，正常退出。[证据](reviews/evidence/P6-004-R3/)。仅接收第一小步，未宣称预测可靠或多屏已测。
- 监理夹具事故：首轮未设 `_probeIsolatedMode` 而误入正式启动，曾一次保存正式 state.json；已精确停止该测试PID，用自动备份恢复测试前主文件并核对SHA一致，最终仍一致。更早一版 `.bak` 无法恢复，当前主/备份均为测试前主文件；原件保存在本机 `.temp/p6004-live-r3-recovery/`，详情/哈希见报告R3。随后先 ProbeSetupIsolated + 保护断言，两轮正常结束，无测试残留。不是开发产品缺陷。
- R4 收尾：用户明确“按这个方案收尾”，已同步 PRODUCT/ARCHITECTURE/DECISIONS/TASKS，当前版 P6-004 done、阶段6完成；靠近独立待办 FUT-001，不自动开工。纯文档核对，未改代码/资源、未重测、未触碰正式存档、未提交推送。
- 下一步：开发只在整改记录追加 R4 最终接收引用，不重复实现/重测，不改监理报告或证据。新开发与 Git 操作等用户另行安排，既有修改及 R3 事故证据保留。

## 本次 Git 备份

2026-11-27 用户明确授权“先做一次 Git 远程备份”。已将当前已接收 P5B/P6 实现、正式资源、测试、权威文档及已留存监理/整改证据保存为快照 `3ea9f4503c46df83f5b0e56b7c5a7c8348907313`（323 个变更文件），普通推送至 `origin/main`（`git@github.com:mrrdxx/desktop_pet.git`），并通过 `git ls-remote` 核对远端 SHA 完全一致。起点 `0aa994d` 无分叉，身份沿用本地配置；无强推/远端设置变更。临时 `.temp/artifacts/bin/obj/TestResults`、Python 缓存及真实用户存档未纳入，原件保留本机；凭据文件名/内容模式扫描无命中，不宣称所有本机历史实机附件都已上传。本备份成功说明随后独立提交/推送，最终同步 SHA 在交付回复报告，不在文档递归写自身 SHA。本轮未改产品实现或重测，不改变 P6 验收和 FUT-001 未排期状态。

以下保留上次备份记录：

2026-11-21 用户明确授权将当前状态提交并推送至 origin/main。本次保存既有开发与监理工作，不代表 R1 阻断已关闭或实机复验通过；验收状态不变。源码、资源、测试、权威文档及 docs/reviews/evidence 留存证据纳入；.temp、artifacts、bin/obj 和临时 TestResults 排除且原件保留本机。当前 M-17 开发实机 run 原件仍仅在本机 .temp；报告中已留存的证据副本随文档备份，不宣称所有历史实机附件均已远程备份。

备份结果：代码/文档快照提交 `8354f61` 已普通推送至 `git@github.com:mrrdxx/desktop_pet.git` 的 `main`，已读取远端 refs/heads/main 确认 SHA 与该快照一致；原先未推送的 P3 提交一并同步。此条同步说明随后独立提交，最终同步 SHA 由本轮交付回复记录。

