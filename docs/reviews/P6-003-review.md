# P6-003 频次判断审查

## R1：部分通过，F-01 待修（2026-11-27，Asia/Shanghai）

**结论：门槛主体、无缓存计算以及关闭/清除/重启接线通过；日期窗口与既有保留边界尚未统一，P6-003 保持 awaiting-verification。** F-01 仅阻断本任务接收及依赖它的 P6-004，不重新打开 P6-001/P6-002。

### 审查对象与实际验证

- HEAD `0aa994dd371ac7e804ca863836b71eeadf877788` 加当前未提交工作区。实现并未包含在该 HEAD 全部内容中；审查五份源码/测试 SHA-256 见 [source-hashes.json](evidence/P6-003-R1/source-hashes.json)。已有其他阶段修改原样保留。
- 对照 PRODUCT R-09、ARCHITECTURE §7、TASKS、[开发交付记录](../remediation/P6-003-response.md)及实际 Judge/Collector/Service 接线；沿用 REVIEW_PROCESS。
- 重新编译：Core `UsageStatsFrequencyJudgeTests` **11/11**；App `UsageStatsServiceTests` **25/25**（包括本次 4 项频次测试），均 0 失败/跳过。见 [Core TRX](evidence/P6-003-R1/core-frequency.trx)、[App TRX](evidence/P6-003-R1/app-usage.trx)。Core 613/App 635 全量为开发侧报告，本轮未重复运行无关全量。
- 另建无窗口隔离验证器，调用生产服务 `Start→Enable→GetFrequencyPredictions→DrainPending→GetFrequencyPredictions`，固定 Collector 与 Service 同一本地时钟，无真实时间流逝、无新增活动；[源码](evidence/P6-003-R1/checks/Program.cs)、[原始观测](evidence/P6-003-R1/observations.json)。进程 exit 0 表示采集完成，**不代表边界检查通过**：观测明确记录 `stableAcrossNormalDrain=false`。
- 未改产品或正式测试，未占屏幕、未使用正式统计目录、未提交/推送。

### F-01（中，阻断 P6-003）：预测窗口与既有保留边界冲突，最旧日期在正常清理前被错误采纳

**位置**：`UsageStatsFrequencyJudge.cs:136–164`、`UsageStatsService.cs:200–202/486`；现有边界测试 `UsageStatsServiceTests.cs:1231`。参照既有正确保留实现 `UsageStatsCollector.cs:505–513`。

**事实**：

1. P6-002 已接收的 30 天保留是 `[today−29,today]`，不是 `[today−30,today−1]`。后者是本次新增频次规则宣称的“之前 30 个完整日期”。排除当天以后，已保留数据最多只有 **29 个已完成日期**。
2. `GetFrequencyPredictions` 直接把恢复的桶交给 Judge；Judge 又允许 `today−30`。因此启动恢复后、第一次正常 drain 清理前，会使用已经超出保留期的桶补足覆盖/支持门槛。正常采样 timer 的 drain 随后删除它，同一时刻重新查询却改变结果。
3. 固定 `2026-11-27 12:00 +08:00`，种入 notepad 日期 `today−30/−1/−2/−3/−4`，另用两个日期其他应用补到 7 个覆盖日期。实际观察：首次查询 **覆盖 7、候选 1**；仅调用 `DrainPending` 后 **覆盖 6、候选 0**，最旧 `2026-10-28` 消失。无新增数据、无时钟推进。对照组将最旧桶改为 `today−5`，正常 drain 前后均为 **覆盖 7、候选 1**。两组关闭与清除后输出均为空。
4. 现有 `Frequency_TodayAndExpiredBuckets_AreIgnored` 在 `Start→Enable` 后立即断言 `today−30` 有效，没有推进清理路径，因此虽然测试通过，不能证实生产中该日期在完整生命周期中可参与预测。

**影响**：启动后可能暂时输出由过期桶凑足的候选；文档“30 个完整日期”与实际可用范围不符，现有边界正例遗漏清理后的行为。正常保留期内的门槛与关闭/清除逻辑没有因此被否定。

**监理指令更正**：上一轮由本监理提出“之前 30 个完整日期”，没有与已经接收的“含今天保留 30 天”核对，这是本监理起步口径遗漏，开发按该指令实现不构成擅改需求。此次更正以保留既有数据期限为前提，不通过延长数据保留或恢复已过期记录来满足新窗口。

**限定整改**：

- 保持 P6-002 的 30 天保留、7 天原始、默认关闭及 7/5/5 门槛不变。不重写计时/聚合、不迁移存档。
- P6-003 查询应从第一次调用起就遵守与保留期一致的有效日期交集，**不依赖后续 drain 才排除过期桶**。默认排除当天时实际可用区间为 `[today−29,today−1]`，最多 29 个已完成日期；缺失日期仍不得当作未使用。实现可保持纯过滤，不要求为了查询而写盘或清理文件。
- 对应更新 PRODUCT/ARCHITECTURE/TASKS/交付记录，明确名义回看窗口与保留上限的交集，或者直接明确首版有效完整日期数；输出 `WindowDays` 的含义也须一致，不冒称完整可用 30 日历史。如果确实要保留之前完整 30 日，属于改变既定保存期限，须另请用户决定，本轮不采用。
- 修正 App 边界测试，增加生产 `DrainPending` 前后查询正反例：`today−30` 从首次查询起不补足门槛，`today−29` 有效；当天/未来/更旧日期排除；同一时刻正常 drain 与重启不改变有效样本集及候选。Collector/Judge 使用同一注入日期。
- 运行相关 Core/App 专项及构建，独立整改记录追加位置/证据/复核请求；不修改本报告及原始证据。无需真实等待跨月或重跑 P6-001 的菜单/锁屏/睡眠。

### 已通过及限制

主体按 7 个不同有记录日期、5 个支持日期且 5 会话判定，使用已聚合数据而非采样次数；排序和输出未伪装成准确率。Service 没有预测缓存，关闭、清除即时空输出、重启从桶重算的既有专项通过。无需为本问题新增预测模型、主动反馈或 UI。

无真实长期数据，当前参数与真实预测效果尚无验证；时区切换/夏令时未专项测试。P6-004 保持 todo，待 F-01 复核关闭后再由用户安排。

复现（仓库根目录；输出目录使用全新 GUID，不覆盖证据）：

```powershell
$reviewOutput = Join-Path '.temp' ('p6003-r1-' + [guid]::NewGuid().ToString('N'))
dotnet run --project docs/reviews/evidence/P6-003-R1/checks/Review.csproj --artifacts-path artifacts/p6003-checks-r1 -- $reviewOutput
```

## R2：F-01 关闭，P6-003 接收（2026-11-27，Asia/Shanghai）

**结论：F-01 复核通过并关闭，P6-003 接收，可标记 done。** R1 原始反例及结果保留，以下结论适用于本次修复版本。P6-001/002 接收保持，P6-004 不自动开工。

### 对象与验证

- HEAD 仍为 `0aa994dd371ac7e804ca863836b71eeadf877788` 加未提交工作区；[本轮源码摘要](evidence/P6-003-R2/source-hashes.json)记录实际对象。读取开发[整改记录 F-01 段](../remediation/P6-003-response.md#f-01-整改2026-11-27)，核对 Judge 日期交集实现、正式测试和 PRODUCT/ARCHITECTURE/TASKS。
- 独立 artifacts 目录重新编译并运行：Core `UsageStatsFrequencyJudgeTests` **11/11**（[TRX](evidence/P6-003-R2/core-frequency.trx)），App `UsageStatsServiceTests` **27/27**（[TRX](evidence/P6-003-R2/app-usage.trx)），0 失败/跳过。App 包括 `today−30` 首查拒绝与 `today−29` 保留的生产 drain 前后及真实文件重启正反例。
- 不修改 R1 验证器，重新编译链接当前产品代码、使用新隔离目录复跑。[原始观测](evidence/P6-003-R2/observations.json)：最旧日期反例从首次查询就是**覆盖 6/候选 0**，drain 后仍 6/0，`stableAcrossNormalDrain=true`；保留期内对照从首次查询到 drain 后保持**覆盖 7/候选 1**。两组 Disable/ClearAll 空输出均保持，采集进程正常 exit 0。
- 本轮命令：

```powershell
dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --artifacts-path artifacts/p6003-core-r2 --filter "FullyQualifiedName~UsageStatsFrequencyJudgeTests" --logger "trx;LogFileName=core-frequency.trx" --results-directory docs/reviews/evidence/P6-003-R2
dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p6003-app-r2 --filter "FullyQualifiedName~UsageStatsServiceTests" --logger "trx;LogFileName=app-usage.trx" --results-directory docs/reviews/evidence/P6-003-R2
dotnet run --project docs/reviews/evidence/P6-003-R1/checks/Review.csproj --artifacts-path artifacts/p6003-checks-r2 -- .temp/p6-003-review-r2-20261127
```

以上输出目录已用过；后续复跑另选新目录和结果路径，不覆盖现有证据。

### F-01 接收依据

- `Evaluate` 在读取桶前计算名义窗口与保留区间交集；默认 `[today−29,today−1]`，过期桶从首查即排除。没有靠写盘/清理副作用保证查询正确，没有延长既定保留或更改采集/聚合模型。
- 结果 `WindowDays` 默认报告实际生效完整日期数 **29**；7 个有记录日期、5 个支持日期且 5 会话的门槛不变。可调 options 的 `RetentionDays` 为 init-only 配置，生产服务使用默认既有保留常量，不额外设置更长保存期。
- PRODUCT、ARCHITECTURE、TASKS 已一致说明“名义回看与保留上限取交集”，不承诺存在完整 30 日历史；正式边界测试不再把过期日当作正例。无状态按需计算及关闭/清除/重启语义保持。

本轮未重跑无关全量；Core 613/App 637 与 App/DiagnosticProbe 构建 0/0 是开发自测，本轮不冒称复跑。未占屏幕或运行实机窗口、未触碰正式数据、未改产品或正式测试、未提交/推送，已有修改保留。无长期真实样本下只接收既定规则实现，不宣称预测准确；时区/夏令时和真实长期效果限制继续保留。

下一步：开发侧仅追加本 R2 接收引用，不重复实现/重测。P6-004 维持 todo，等待用户另行安排。
