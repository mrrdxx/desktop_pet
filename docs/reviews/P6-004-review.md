# P6-004 第一小步：气泡与统计查看入口审查

## R1：部分通过，F-01/F-02 待整改（2026-11-27，Asia/Shanghai）

**结论：规则主体与已有隔离接线部分通过；F-01/F-02 阻断第一小步接收。** P6-004 整体保持 in-progress，“靠近”仍未定，不因此标 done；不重新打开 P6-001/002/003。修复复核通过后再安排真实关闭按钮、焦点/输入及观感验收。

### 对象、证据及实际验证

- HEAD `0aa994dd371ac7e804ca863836b71eeadf877788` 加未提交工作区；本轮读取实际目录/差异、STATUS、TASKS、PRODUCT R-09、ARCHITECTURE §7、REVIEW_PROCESS 与[开发交付](../remediation/P6-004-response.md)。[源码摘要](evidence/P6-004-R1/source-hashes.json)限定实际审查对象，既有未提交修改保留。
- 独立 artifacts 目录重新编译运行：Core `UsageStatsBubblePlannerTests` **25/25**（[TRX](evidence/P6-004-R1/core-bubble.trx)）；App `UsageStatsBubbleAppTests` **7/7**（[TRX](evidence/P6-004-R1/app-bubble.trx)），均 0 失败/跳过。这些是规则/程序驱动的 WPF 测试，不是实际鼠标操作。
- 核对开发既有预览 JSONL/result 原件，复制留存为[开发预览](evidence/P6-004-R1/developer-preview.jsonl)、[开发 result](evidence/P6-004-R1/developer-result.json)，未重跑该整套预览。首条展示/同日去重/查看/关闭/清除记录符合其已声明范围，exit 0、无日志；配置 `autoHideConfiguredMs=8000` 本身不证明实际定时隐藏。
- 监理补充验证器调用当前实际 Core/Service/MainWindow/StatsBubbleWindow：[源码](evidence/P6-004-R1/checks/Program.cs)、[原始观测](evidence/P6-004-R1/observations.json)。两个缺陷场景 `pass=false`，社交结束正例 `pass=true`。仅离屏 Show、没有鼠标/键盘注入；真实 WPF Dispatcher 泵 8504ms 后气泡已隐藏，定时隐藏正例通过。验证器 exit 0 只表示采集完成，不代表所有检查通过。
- 本轮未重复无关全量；Core 638/App 644 及 App/DiagnosticProbe 构建 0/0 为开发自测结果。未触碰真实统计目录，未改产品/正式测试，未提交/推送。

### F-01（中，阻断第一小步）：选窗空闲判定没有读取共享社交会话

**位置**：`MainWindow.xaml.cs:243` 的 `IsAvailableForStatsBubble`；调用链 `App.SelectStatsBubblePet→TryShowUsageStatsBubble`。可复用现有 `MainWindow.IsInSharedSocialSession`（约 2423 行）。

**触发与证据**：双宠共享协调器已在一个窗口的 tick 中建立社交会话，另一只还未 tick，本地 `_isSocialActive` 仍 false。新的可展示判定只检查本地旗，没有检查共享会话。本轮用实际 `SocialCoordinator.Tick` 开始 `Noticing`，将同一协调器注入参与方 b 的 `StateContext`，b 尚未执行自身 tick：

- 共享参与状态 `sharedActive=true`；本地旗 `localSocialFlag=false`；
- `availableForBubble=true`，把此结果传给实际 Planner 得到 `plannerWouldShow=true`；
- 共享会话结束后可展示返回 true，为正向对照。

这是权威状态与局部缓存之间的时序缺口。补证范围是实际协调器启动、窗口谓词及 Planner；没有把这项隔离组合验证冒称为真实 App 定时器已弹出的人工观察。既有生产社交流程本就允许“一窗更新共享会话、另一窗随后 tick”，因此不是任意捏造字段的不可达状态。

**影响**：社交忙碌参与方仍可能被当作空闲窗口选中，违反已登记“社交任一进行中不可用/不覆盖高优先级交互”的门槛。它不直接改变社交位置，不据此宣称已有位置回归。

**限定整改/验收**：复用共享权威参与状态排除忙碌窗口，保留本地旗与现有其他守卫。补 App 接线正反例：共享会话活跃且 B 本地旗未更新时，不选 B、不展示、不消费每日标记；会话结束恢复；如有真正空闲且不参与会话的窗口可按原规则选取。不要重写社交状态机或仅在测试中提前 tick B 遮蔽缺口。

### F-02（中，阻断第一小步）：查看汇总首次读取计入已过期桶，依赖后续清理才正确

**位置**：`UsageStatsService.cs:214–215` 的 `GetRecordedSummary`，`UsageStatsBubblePlanner.SummarizeRecorded`，经 `App.BuildStatsViewSnapshot` 渲染“已记录的统计”。

**触发与证据**：隔天启动恢复磁盘后、首次采样/drain 清理前主动打开统计查看窗。`GetRecordedSummary` 直接汇总全部恢复桶，没有按当前保留期过滤。固定同一本地时间 `2026-11-27 09:35 +08:00`，独立文件预置 `today−30` 与 `today−1` 各 5000ms/1 会话：

- `Start` 后首次汇总：**2 个日期、10000ms、2 会话**；
- 只调用生产 `DrainPending` 后：**1 个日期、5000ms、1 会话**；
- 没有新增统计或时间推进，差值全部来自本来就已过期的 `today−30`。查看窗只在请求/刷新时取快照，过期汇总还可能停留在已经渲染的内容中直到下一次刷新。

P6-003 已有查询交集防护，但只覆盖预测；这次新“已记录”读取入口未复用同样的日期纪律。P6-002 原存储清理规则仍正确，不重新打开旧任务。

**限定整改/验收**：从首次查询起过滤保留期外汇总数据，使用与 Collector 相同的当前日期及**含今天**的聚合保留窗口 `[today−29,today]`。不要把预测的“排除今天”误套到已记录汇总；不因查询而延长保留、不恢复过期记录，不重写采集/聚合。补服务/查看快照测试：`today−30` 首次排除，`today−29` 和今天保留；同一时刻 drain 前后及重启汇总一致，关闭仍可查看期限内旧记录，清除后为空。名称继续沿用“近似前台活跃时长/会话次数”。

### 已通过的部分与后续实机边界

- Core 当前时段匹配、安静失败、仅依据文案、每日标记规则及 App 单窗展示/标记正常持久化、关闭保留、清除重置、查看两段等既有专项通过。不扩大为故障磁盘下绝对去重承诺。
- 气泡真实 Dispatcher 定时隐藏已由本轮离屏程序驱动验证，不再只依赖配置数值。8.5 秒时确认隐藏，不声称精确测得隐藏发生的毫秒或已完成实屏视觉观察。
- 真正点击关闭按钮、无激活、宠物实体拖动与透明区命中仍未实机执行；静态 `ShowActivated=false/WS_EX_NOACTIVATE` 与窗口矩形不重叠不能替代全部真实输入体验。待修复代码通过后集中做一次隔离实机，不要求真实等待长期统计数据。占用鼠标须按既有纪律先约时段。
- 本轮没有确定多屏/DPI 兼容结论，也不开始“靠近”、图标定位或应用启动。

复现（仓库根目录，结果路径使用全新目录，不覆盖留存证据）：

```powershell
dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --artifacts-path artifacts/p6004-core-r1 --filter "FullyQualifiedName~UsageStatsBubblePlannerTests" --logger "trx;LogFileName=core-bubble.trx" --results-directory docs/reviews/evidence/P6-004-R1
dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p6004-app-r1 --filter "FullyQualifiedName~UsageStatsBubbleAppTests" --logger "trx;LogFileName=app-bubble.trx" --results-directory docs/reviews/evidence/P6-004-R1
$reviewOutput = Join-Path '.temp' ('p6004-review-' + [guid]::NewGuid().ToString('N'))
dotnet run --project docs/reviews/evidence/P6-004-R1/checks/Review.csproj --artifacts-path artifacts/p6004-checks-r1 -- $reviewOutput
```

开发 AI 按本报告 F-01/F-02 限定整改、运行相关专项及构建，在独立整改记录追加位置/结果/限制后请求复核。保留报告与原始证据，不提前消耗真实数据、不重做已接收任务、不提交/推送。

## R2：F-01/F-02 关闭，第一小步待隔离实机（2026-11-27，Asia/Shanghai）

**结论：两项限定代码整改复核通过，F-01/F-02 关闭。** 第一小步仍需真实关闭按钮、无激活、宠物拖动/透明区命中与观感验收，不签最终接收；P6-004 整体保持 in-progress，“靠近”仍待讨论。R1 失败事实/原件保留，P6-001/002/003 接收不变。

### 对象与本轮实际运行

- HEAD 仍为 `0aa994dd371ac7e804ca863836b71eeadf877788`，审查当前未提交工作区，相关源码/测试 [SHA-256](evidence/P6-004-R2/source-hashes.json)。读取[开发整改第 6 节](../remediation/P6-004-response.md#6-r1-限定整改f-01--f-022026-11-27)及实际实现和新增测试，没有仅按自述关闭问题。
- 独立 artifacts 重新编译：Core `UsageStatsBubblePlannerTests` **28/28**（[TRX](evidence/P6-004-R2/core-bubble.trx)），App `UsageStatsBubbleAppTests` **10/10**（[TRX](evidence/P6-004-R2/app-bubble.trx)），0 失败/跳过。
- 不修改 R1 验证器，新目录重新编译链接当前代码复跑，四组观测均 `pass=true`：[原始输出](evidence/P6-004-R2/observations.json)。本轮没有实机鼠标输入，离屏窗口及测试 WPF 调用不冒称真实点击验收。

### F-01：通过并关闭

`IsAvailableForStatsBubble` 追加 `!IsInSharedSocialSession`，保留本地旗、其他忙碌守卫，复用既有共享会话参与判断，不重写社交模型。

原验证器通过生产 `SocialCoordinator.Tick` 开始 Noticing、参与方 b 未执行自身 tick：`sharedActive=true`、`localSocialFlag=false`，现在 `availableForBubble=false`、`plannerWouldShow=false`；会话取消后的空闲对照为 true。

正式 App 测试还覆盖：预置共享会话且窗口本地旗未更新时，`TryShowUsageStatsBubble` 不展示且不消费内存/磁盘每日标记；会话结束可展示；另有不参与会话的空闲窗口可按规则展示。此处正式测试采用 `TestSeedSession` 构造会话，原验证器的生产 Tick 补证保留，两层证据区分说明。

### F-02：通过并关闭

`SummarizeRecorded` 必须传入本地时间，以含今天的聚合保留窗口过滤；`GetRecordedSummary` 传 `_localNow()`。查询只读，不依赖 drain、不套用预测排除今天规则，不更改既有保留常量。

原反例首次读取现在即 **1 日期/5000ms/1 会话**，生产 drain 后仍相同；过期 `today−30` 不再计入。Core 测试覆盖 `today−29` 与今天保留、未来排除、日期右移后旧边界过期；App 测试覆盖查看内容、关闭仍看保留期内旧数据、drain/重启一致与清除空态。

沿用采集器对无法解析日期不额外删除的既有策略，不在本轮增加损坏数据整治需求；当前接收针对有效生产桶与所列日期边界。

### 验证命令、范围与下一步

```powershell
dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --artifacts-path artifacts/p6004-core-r2 --filter "FullyQualifiedName~UsageStatsBubblePlannerTests" --logger "trx;LogFileName=core-bubble.trx" --results-directory docs/reviews/evidence/P6-004-R2
dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p6004-app-r2 --filter "FullyQualifiedName~UsageStatsBubbleAppTests" --logger "trx;LogFileName=app-bubble.trx" --results-directory docs/reviews/evidence/P6-004-R2
dotnet run --project docs/reviews/evidence/P6-004-R1/checks/Review.csproj --artifacts-path artifacts/p6004-checks-r2 -- .temp/p6004-review-r2-20261127
```

目录已使用，后续复跑另选新目录，不覆盖证据。原验证器内离屏 WPF Dispatcher 泵 8493ms 后自动隐藏仍通过，静态关闭按钮 handler/无激活样式不等同于实际鼠标确认。

开发报告的 Core 641/App 647 全量及 App/探针构建 0/0 未由本轮机械重跑。监理未改产品或正式测试、未使用正式统计目录、未移动鼠标、未提交/推送，已有修改保留。

下一步集中一次隔离实机：展示气泡→真实关闭按钮；再次隔离展示→观察自动隐藏；确认前台焦点保持、宠物实体拖动/透明区穿透及查看入口；关闭/清除撤销保持。仅使用测试数据和独立运行目录，不为复验修改正式统计或伪造长期真实样本；占用鼠标前与用户约时段。开发侧可仅追加本 R2 代码问题关闭引用，不重复实现/重测，不将第一小步或整项标 done。

## R3：真实输入验收，第一小步接收（2026-11-27，Asia/Shanghai）

**结论：气泡反馈与统计查看入口第一小步接收。** F-01/F-02 关闭保持；P6-004 整体仍 in-progress，“靠近”表达尚待用户讨论，不代填整项 done。下面同时记录本监理夹具的隔离事故及恢复，不能概括本轮为“从未触碰正式存档”。

### 实机范围与有效证据

用户在本轮明确回复“现在可以”，授权占用鼠标/屏幕。本监理通过 Computer Use 的真实点击/拖动操作，不用测试方法伪造点击。产品源码未改；[源码摘要](evidence/P6-004-R3/source-hashes.json)、[汇总](evidence/P6-004-R3/summary.json)。测试样本是固定日期/应用桶，不是真实使用习惯，也没有更改系统时间。

现有 `usage-stats-bubble` 场景连续自验后立即退出，不适合交互；另建[监理交互夹具](evidence/P6-004-R3/live/Program.cs)调用生产 App/气泡/查看窗/宠物路径。修正隔离保护后的有效两轮分别为 `.temp/p6004-live-r3-final-20261127`（PID 64464）和 `.temp/p6004-live-r3-close-20261127`（PID 99116）：

1. **气泡可见、文案可读、自动隐藏**：真实点击底板展示按钮，调用 `TryShowUsageStatsBubble` 取得当前时段候选并展示。生产默认 8000ms 计时两轮可见采样跨度约 **7995ms/7925ms**（100ms 采样精度，非精确计时），之后隐藏。[auto-events](evidence/P6-004-R3/auto-events.jsonl)。最初两次关闭点击均晚于自动隐藏，实际落到底板；未把它们算作关闭按钮通过。
2. **真实关闭按钮**：为避免截图往返超过 8 秒，在第二个有效夹具里仅调用已有 `ShowNear(...,autoHide:false)` 延长该关闭测试气泡停留；产品默认值不改。观察可见气泡后实际点击叉号，气泡隐藏，未触发底板点击计数。此项证明真实关闭路径，自动隐藏另由第 1 项覆盖，不混为同一测试。
3. **不抢前台**：展示前后 GetForegroundWindow 相同（自动轮 593024、关闭轮 78126004）；气泡可见采样期间前台保持底板。关闭后亦仍在底板，没有切到气泡。截图/UIA 显示底板文本框仍为输入区。主动打开查看窗后其获得焦点属正常用户操作，不据此判气泡抢焦点。
4. **实体拖动**：真实拖动小熊从 `(540,410)` 到 `(660,460)`；再在气泡仍可见时从 `(660,460)` 到 `(760,500)`，位移与输入一致。没有通过给窗口赋坐标替代拖动断言。
5. **透明区穿透**：分别点击移动后宠物左上透明角落，底板实际收到两次 PreviewMouseLeftButtonDown，文本框获得焦点，小熊位置保持；第二次发生时气泡仍可见。两次坐标在新截图中重新核对，未使用旧位置。
6. **真实菜单查看**：对小熊实体真实右键，原生菜单可见，点击 `Usage Statistics`，出现正式查看窗；两段分别显示已记录统计与有样本支持的时间偏好。7 个有记录日期/7 分钟近似前台活跃/7 会话/2 应用、notepad 09:30–09:59 支持 5 日期/5 会话与合成样本一致，无准确率/命令式文案。[查看窗截图](evidence/P6-004-R3/viewer.png)、[UIA 文本](evidence/P6-004-R3/viewer-accessibility.txt)、[气泡及宠物](evidence/P6-004-R3/bubble-and-pet.png)。
7. **正常收尾**：真实点击关闭查看窗及夹具“结束验收”，经生产 `ShutdownMainWindow` 退出；两轮 [auto-exit](evidence/P6-004-R3/auto-exit.json)/[close-exit](evidence/P6-004-R3/close-exit.json) 均 `normalExit=true/statsServiceNull=true`。Computer Use 重新枚举确认测试窗口已消失，精确 PID 核对无测试进程残留。

[close-events](evidence/P6-004-R3/close-events.jsonl)保留关闭/拖动/透明点击/查看状态与前台时间序列。关闭、清除、挂起撤销仍沿用已复核 App 专项，**本轮未通过真实菜单再次切换统计隐私开关或清除数据**，不冒称这些已实机点击。多屏/DPI 组合及长期真实预测效果未专项验证。

### 监理夹具事故与恢复（不是产品缺陷，不新增 F 编号）

首个夹具（PID 77288，`.temp/p6004-live-r3-20261127`）只调用 `TestSetupIsolatedSave` 后执行 `App.Run`，遗漏 `ProbeSetupIsolated` 设置的 `_probeIsolatedMode`。WPF 随后进入正式 OnStartup，加载正式状态并额外创建宠物；一次 debounce 保存于本地 **04:55:44** 更新了正式 `state.json`。变化为保存时间、少量运行累计与衰减时间，宠物位置/身份/性格/委屈等比较未变。该轮气泡未能按测试数据展示，不作为有效验收。

发现后，本监理校验 exe 归属并仅强制终止 PID 77288，避免正常退出再保存。保留误写主文件和当时 `.bak` 到本机 `.temp/p6004-live-r3-recovery/`；其 `.bak` 正是本次自动替换前的主存档（时间 02:11:34）。主存档已按该备份**逐字节恢复**，SHA-256 为 `AB3211C70FAA89CD341BDF6DE331A9FD217D8D4F2CE36F06C70F6F4A543B5F58`；有效测试完成后再次核对仍相同。[恢复记录](evidence/P6-004-R3/recovery.json)。恢复内容不写入仓库原始数据附件，仅保存哈希与结果。

**保留限制**：误写时原来的更早 `.bak` 被自动备份机制替换，无法证明或恢复更早一版备份；当前主存档与 `.bak` 均为测试前主存档。不能宣称全部历史备份链未受影响。本监理已在对话向用户说明事故、恢复主文件与这项限制；没有把夹具问题转嫁给开发 AI。

后续夹具先调用 `ProbeSetupIsolated(root,uniqueMutex)`，再设置测试依赖，并在运行前断言 `_probeIsolatedMode=true`；有效两轮不再进入正式启动。原始失败轮和本地恢复副本保留，不自动清理。初次夹具构建还曾因 `Thickness` 参数错误失败，修正夹具后 warnaserror 构建 0/0，非产品编译缺陷。

### 复现及下一步

归档夹具把两轮的计时差异整理成 `--hold` 参数，产品路径相同：

```powershell
dotnet build docs/reviews/evidence/P6-004-R3/live/Live.csproj --artifacts-path artifacts/p6004-live-r3 -warnaserror
# 以下需要用户可占鼠标时段，目录必须新建，不能复用正式目录。
$liveOutput = Join-Path '.temp' ('p6004-live-' + [guid]::NewGuid().ToString('N'))
& artifacts/p6004-live-r3/bin/Live/debug/DesktopPet.App.Tests.exe $liveOutput
# 单独真实关闭按钮轮：使用另一个全新目录，并添加 --hold；默认 8s 的证据另跑上式。
```

`--hold` 仅对夹具停留时间生效；保守去重通过受控本地日期逐次递增展示，未改系统时间/产品每日标记规则。归档整理后仅重新构建 0 警告/错误，未再启动一轮实机。未改产品/正式测试、未提交/推送，所有工作区修改保留。

开发侧只需追加本 R3 第一小步接收引用，不重复实现/重测、不代改报告；P6-004 整项不标 done。下一步与用户讨论是否保留纯气泡体验以及“靠近”的具体含义，再确定剩余范围，不默认启动移动。

## R4：用户确认纯气泡收尾，P6-004 最终接收（2026-11-27，Asia/Shanghai）

**结论：按用户明确调整后的本版范围，P6-004 接收、可标记 done；阶段 6 本版完成。**

- **授权来源**：监理在 R3 实机后建议“本版保持纯气泡，把靠近另拆后续任务”，用户明确回复“按这个方案收尾”。已登记 [D-020](../DECISIONS.md)，并同步 PRODUCT R-09、ARCHITECTURE §7、TASKS/STATUS。不是监理单方删除需求，也不把延期功能描述为已实现/已验收。
- **当前接收范围**：主动气泡与统计查看入口，复用已接收的本地采集、聚合和有门槛的时段频次判断；默认关闭、可关闭/清除、依据文案、低频展示、多宠单选及交互守卫不变。具体行为以 PRODUCT 为唯一规格出处。
- **依据**：R2 已关闭 F-01/F-02，Core 28/28、App 10/10 及原反例通过；R3 已完成真实关闭、默认自动隐藏、前台焦点、实体拖动/透明区穿透、真实菜单查看与正常退出。移除“靠近”这一未实现的本版门槛后，无其余当前阻断项。不为纯文档范围调整重复测试或启动实机。
- **延期**：主动靠近独立登记 TASKS 的 FUT-001，todo、未排期、未授权开工；其目标/触发和位置所有权须后续明确，精确图标定位仍不在已完成功能中。该待办不阻塞本版阶段 6 收尾。
- **限制保留**：没有长期真实样本，接收的是既定规则和交互实现，不宣称预测可靠、多屏组合全覆盖；R3 本监理夹具事故、已恢复主存档及旧备份无法恢复限制完整保留，不因最终接收消除历史。
- **本轮实际动作**：只修改规划/规格/审查/状态文档，核对状态、引用和链接。未改产品/资源/正式测试、未修改开发独立整改记录、未重跑测试或触碰正式存档、未提交/推送；所有已有修改保留。

开发侧可在 P6-004-response 末尾仅追加 R4 最终接收引用；不重复实现/重测，不改报告/证据，不启动 FUT-001 或其他新阶段。
