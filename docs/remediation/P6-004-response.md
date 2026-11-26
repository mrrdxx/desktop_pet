# P6-004 交付记录（开发独立记录）

任务：P6-004 展示：气泡反馈与统计入口（第一小步「气泡反馈与统计查看入口」）
状态：`awaiting-verification`（第一小步开发实现与自动验证已完成；[监理 R1](../reviews/P6-004-review.md) 的 F-01/F-02 已按第 6 节限定整改并自测通过，待监理复核；**P6-004 整体仍为 `in-progress`**，不自行标 done，「靠近」表达保留待讨论；未提交/推送）
日期：2026-11-27
依赖：P6-003（done，监理 R2 接收）。本记录只读复用已验收的频次判定结果，不重测、不改写 P6-001/P6-002/P6-003 结论与报告。

## 1. 口径（已先写入权威文档）

- 已先同步 [TASKS P6-004 第一小步](../TASKS.md#第一小步气泡反馈与统计查看入口2026-11-27-起)、[PRODUCT R-09](../PRODUCT.md#r-09-习惯学习阶段-6)、[ARCHITECTURE §7 P6-004](../ARCHITECTURE.md#p6-004-展示气泡与统计入口约定第一步已实现)，再进入实现。
- **查看入口**：右键菜单「Usage Statistics」打开只读查看窗，分两段。第一段「已记录的统计」沿用 R-09 准确名称：有记录的日期数、**近似前台活跃时长**、**会话次数**、涉及应用数；缺失数据不表述为“没有使用”。第二段「有样本支持的时间偏好」列出 P6-003 候选（应用、时段、支持日期数、会话次数），标明是统计依据而非准确率。
- **气泡门槛**：只使用 P6-003 已达标且 `SlotStart` 等于当前本地半小时时段的候选；无候选保持安静，不输出“样本不足”，不提示打开应用。
- **文案**：「过去有记录的日期里，你有 {N} 天在 {HH:mm–HH:mm} 这个时段使用 {app}」；不含“你现在该打开它了”等指令式措辞，不包装准确率或“知道用户意图”。
- **保守初值**：约 8 秒自动消失、可关闭；每本地日期全局最多展示一次，去重日期持久化在独立 `stats.json`，跨重启生效；多宠只选一只“可接受展示”的空闲宠物。
- **抑制与撤销**：暂停、锁屏、睡眠期间不展示，恢复后不补播积压（判定每次即时计算，无排队）；统计关闭立即隐藏并保留当天标记；清除统计立即隐藏并重置标记。
- **不抢焦点/不阻挡**：独立透明置顶、不激活、不占任务栏的气泡窗，放在宠物矩形外侧且不重叠；关闭按钮只关气泡。
- **明确不做**：本小步不移动宠物、不做图标定位、不自动启动外部应用；「靠近」表达保留待讨论。

## 2. 实现范围

### Core 纯逻辑（新增 `src/DesktopPet.Core/Events/UsageStatsBubblePlanner.cs`）

- `UsageStatsBubblePlanner.Decide(result, localNow, lastShownLocalDate, isSuspended, hasAvailablePet)`：统计关闭/结果为空、挂起、无可接受展示宠物、当日已展示、当前时段无达标候选时均返回不展示，且不消费当日额度。
- `GetCurrentSlotStart` / `FormatSlotLabel`（如 `09:30–09:59`）/ `FormatBubbleMessage`；`SummarizeRecorded` + `FormatRecordedSummaryLines`（近似前台活跃时长、会话次数等准确名称）；`FormatPreferenceLine`；`FormatDuration`。
- 无状态、无持久化、无 WPF 依赖。

### 持久化字段

- `UsageStatsArchive.BubbleLastShownLocalDate`（附加字段，v2 旧文件缺失为 null）；`UsageStatsService` 在 `Start` 恢复、`RecordStatsBubbleShown` 记录并立即落盘、`ClearAll` 重置；`Disable` 不重置。

### App 接线

- `src/DesktopPet.App/Platform/StatsBubbleWindow.xaml(.cs)`：8 秒自动隐藏（`DefaultAutoHideMs=8000`）、关闭按钮、`WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`、优先宠物左右两侧且夹到工作区。
- `src/DesktopPet.App/Platform/StatsViewerWindow.xaml(.cs)`：两段 + 状态行 + 刷新；空态占位「暂无已记录的统计。」/「暂无可显示的时间偏好。」。
- `MainWindow`：`MenuIdStatsView = 11`、「Usage Statistics」菜单项；`IsAvailableForStatsBubble`（拖动/抚摸/物理/委屈/探头/休息提醒/菜单/攀爬/社交/暂停任一进行中即不可用）与宠物/工作区矩形访问器。
- `App.xaml.cs`：30 秒判定定时器、`TryShowUsageStatsBubble`、`SelectStatsBubblePet`（多宠按 PetId 排序只取一只）、隐藏/撤销接线（暂停、锁屏、睡眠、统计关闭、清除、退出）、`ShowUsageStatsViewer`、`BuildStatsViewSnapshot`；测试/预览访问器 `TestStatsBubbleVisible`/`TestStatsBubbleMessage`/`TestStatsBubbleWindow`/`TestStatsViewerWindow`/`TestStatsBubbleLocalNow`。

## 3. 测试与证据（2026-11-27 本机实跑）

### Core 规则测试（新增 `tests/DesktopPet.Core.Tests/UsageStatsBubblePlannerTests.cs`，25 条）

时段截断与标签、文案含依据且无指令/准确率、关闭/空结果安静、挂起抑制、无宠物抑制、当日已展示、无匹配时段、命中匹配、日期格式、汇总与准确名称、时长格式化、空桶/非法桶等。**25/25 通过**。

### App 隔离测试（新增 `tests/DesktopPet.App.Tests/UsageStatsBubbleAppTests.cs`，7 条，`%TEMP%/P6004_*`）

1. 命中当前时段展示 + 文案依据 + 日期落盘 + 同日不重复 + 重启去重；
2. 无匹配时段安静且不消费额度；
3. 暂停/锁屏/睡眠不展示不消费、恢复后正常判定；
4. 无可用宠物不展示不消费，Show 后可展示；
5. 多宠单选、气泡在宠物外侧、复用同一实例；
6. 查看窗两段/准确名称/关闭与清除刷新；
7. 关闭保留标记、清除重置标记。**7/7 通过**。

### DiagnosticProbe 隔离预览（新增场景 `usage-stats-bubble`）

- 预置 notepad 5 个日期 @09:30 + chrome 2 个日期（覆盖 7 个有记录日期）；固定本地时间 `2026-11-25 09:35`；真实宠物窗口 Show（不移动、不注入鼠标、不启动应用）。
- 证据 `usage-stats-bubble-1.jsonl`（会话编号，不覆盖）实测：
  - `first-decide`：启用、恢复桶 7、候选 1（notepad@09:30，支持 5 天/会话 5，WindowDays=29、ObservedDateCount=7）、气泡可见、文案「过去有记录的日期里，你有 5 天在 09:30–09:59 这个时段使用 notepad。」、气泡不与宠物矩形重叠、自动隐藏配置 8000ms、当日标记已落盘 `2026-11-25`。
  - `second-decide-same-day`：不再展示，标记仍为 `2026-11-25`。
  - `viewer-enabled`：已记录行「有记录的日期：7 天 / 近似前台活跃时长：1 分钟 / 会话次数：7 次 / 涉及应用：2 个」；偏好行「notepad　09:30–09:59　支持日期 5 天，会话 5 次」；状态「已开启」；无准确率/百分号。
  - `after-disable`：气泡隐藏，标记与落盘值仍为 `2026-11-25`（关闭保留）。
  - `after-clear`：气泡隐藏，标记与落盘值均为 `null`（清除重置）；查看窗回到空态，状态「已关闭」。
- 进程按生产 `ShutdownMainWindow` 释放窗口/服务后正常退出（exit 0）。日志 `.temp/p6-004-probe-build*.log`、证据目录 `.temp/p6-004-probe-run/`（均不入库）。

### 全量回归与构建

- Core `DesktopPet.Core.Tests` **638/638**、App `DesktopPet.App.Tests` **644/644**，0 失败 0 跳过（日志 `.temp/p6-004-core-full.log`、`.temp/p6-004-app-full.log`，不入库）。
- App 与 DiagnosticProbe 构建 **0 警告 0 错误**（`.temp/p6-004-app-build.log`、`.temp/p6-004-probe-build2.log`）。

## 4. 未验证 / 限制

- 无真实长期使用样本，门槛与文案均为保守起步值，**不宣称预测可靠或准确率**。
- 真实 WPF 下 8 秒自动消失与关闭按钮点击未做自动化断言（自动测试不运行消息泵），靠隔离预览配置与人工观察；建议实机复核时观察一次实际自动消失与关闭按钮。
- 未验证真实多显示器/DPI 组合下气泡夹取，沿用与位置恢复相同的显示器枚举与 DPI 换算，不宣称已覆盖全部多屏组合。
- 本小步**未移动宠物、未定位图标、未启动外部应用**；「靠近」表达保留待讨论，P6-004 整体不因此标 done。

## 5. 纪律声明

- 保留全部已有修改；未修改、删除或搬移 `docs/reviews/` 及证据，未代签监理结论。
- 未提交、未推送；P6-004 在 [TASKS](../TASKS.md) 中保持 `in-progress`，等待监理复核本小步。

## 6. R1 限定整改：F-01 / F-02（2026-11-27）

对应[监理 R1 报告](../reviews/P6-004-review.md)的 F-01、F-02。只做限定整改：不重写社交状态机、不改采集/聚合、不延长保留期、不恢复过期记录、不推进「靠近」、不重测已接收的 P6-001/P6-002/P6-003。

### F-01 选窗复用共享社交权威状态

- **问题**：`MainWindow.IsAvailableForStatsBubble` 只读本地 `_isSocialActive`。窗口 A 的 tick 驱动共享会话并移动 B 时，B 的本地旗要等 B 自己的社交轮询才置位，期间 B 仍被判为可展示。
- **修改位置**：`src/DesktopPet.App/MainWindow.xaml.cs` 的 `IsAvailableForStatsBubble`（约 L243–262），在条件链中追加 `&& !IsInSharedSocialSession`，与既有 `IsPerchWriteAllowed`（约 L2451）同源读取 `SocialCoordinator.CurrentSession.IsActive` 且本 PetId 为 Initiator/Target。**保留**本地 `_isSocialActive` 及拖动/抚摸/物理/委屈/探头/休息提醒/菜单/攀爬/暂停/隐藏等全部既有守卫；不新增第二套社交判定。
- **自测（隔离目录，不注入鼠标）**：
  - 单宠：协调器持有含该宠的活跃会话、本地旗未更新 → `IsAvailableForStatsBubble=false`；`TryShowUsageStatsBubble` 不展示、`BubbleLastShownLocalDate` 与落盘值均为 `null`（未消费每日标记）。会话切到 `Idle`（非活跃）后，同一宠物恢复可用，同一时刻正常展示并消费 `2026-11-25`。
  - 双宠：bear-2 参与共享会话、bear-1 真正空闲且未参与 → bear-2 不可用、bear-1 可用，`TryShowUsageStatsBubble` 正常展示并按原规则消费额度。
- **限制**：未用真实双宠追逐驱动（共享会话由协调器测试种子构造，等价于 A 已建立会话、B 尚未轮询的状态）；真实双宠实机观感仍待后续集中验收。

### F-02 查看汇总从首查即按含今天 30 天保留期过滤

- **问题**：`UsageStatsBubblePlanner.SummarizeRecorded` 直接累加所有已提交桶，过期桶要等下一次 drain 才被物理剪枝，首查会多计 `today−30` 等过期记录。
- **修改位置**：
  - `src/DesktopPet.Core/Events/UsageStatsBubblePlanner.cs`：`SummarizeRecorded` 增加必填 `DateTimeOffset localNow` 与默认 `retentionDays = UsageStatsCollector.AggregatedRetentionDays(30)`；窗口与 `UsageStatsCollector.PruneToRetention` 的 `aggregatedCutoff` 同口径，保留 `[today−(30−1), today]`（含今天），仅对有活跃的桶计入；未来日期同样排除；无法解析日期的桶不做额外过滤（与采集器保留判定一致）。查询不写盘、不延长保留、不复活过期记录，**不**套用 P6-003 预测的“排除今天”。
  - `src/DesktopPet.App/Platform/UsageStatsService.cs`：`GetRecordedSummary()` 传入 `_localNow()`（与采集器同一时间源），查看窗 `BuildStatsViewSnapshot` 与探针因此自动覆盖。
- **自测（Core 规则）**：`today−30` 首查即排除；`today−29` 与今天保留；未来日期排除；第二天窗口右移后 `today−29` 变为 `today−30` 被排除（不因查询延长保留）。含原 25 条共 **28/28** 通过。
- **自测（App 隔离目录，固定本地时间 2026-11-25 09:35）**：预置 `2026-10-26`（today−30，600000ms/9 会话）、`2026-10-27`（today−29，30000ms）、`2026-11-25`（90000ms/2 会话）。
  - 首查（未发生任何 drain）即得 2 天 / 120000ms / 3 会话 / 2 应用，过期桶不计；
  - 查看窗「有记录的日期：2 天」「会话次数：3 次」；
  - 关闭统计触发 drain 后汇总与首查完全一致；关闭状态下查看窗仍显示期限内旧记录；
  - 重启（同一隔离目录重新加载）汇总与首查一致；
  - 清除后为空，空态「暂无已记录的统计。」。
  - App 气泡专项含原 7 条共 **10/10** 通过。
- **限制**：只验证受控时钟与真实 `stats.json` 往返，未真实等待跨日/跨月；时区切换与夏令时未专项验证。关闭统计的物理剪枝仍按既有 drain 行为发生（本整改只保证读取侧首查一致）。

### 回归与构建（2026-11-27 本机实跑）

- Core 全量 **641/641**（+3）、App 全量 **647/647**（+3），0 失败 0 跳过（日志 `.temp/p6-004-f01f02-core.log`、`.temp/p6-004-f01f02-app.log`，不入库）。
- App 与 DiagnosticProbe 构建 **0 警告 0 错误**（`.temp/p6-004-f01f02-build-app.log`、`.temp/p6-004-f01f02-build-probe.log`）。
- DiagnosticProbe `usage-stats-bubble` 场景在干净隔离目录重跑 **exitCode=0**：候选 notepad@09:30 支持 5/会话 5、气泡一次/日、关闭保留标记、清除重置；文案与查看窗分段保持。

### 请求复核

- 请按报告 F-01、F-02 复核上述位置与正反例（含单宠不消费/会话结束恢复、双宠跳过、today−30 首查排除、today−29/今天保留、drain 前后与重启一致、关闭可见、清除为空）。
- 真实关闭按钮点击、无激活、宠物拖动与透明区命中、观感等实机验收，待代码复核通过后再集中安排；届时需要占用鼠标时会先告知用户。
- P6-004 整体仍 `in-progress`；未改 `docs/reviews/`；未提交、未推送。

## 7. R4 最终接收引用（2026-11-27）

依据：[监理 R4](../reviews/P6-004-review.md#r4用户确认纯气泡收尾p6-004-最终接收2026-11-27asiashanghai)（2026-11-27）——**按用户明确调整后的本版范围，P6-004 接收、可标记 done；阶段 6 本版完成**。任务状态由 `awaiting-verification` 更新为 `done`（见 [TASKS](../TASKS.md#p6-004-展示气泡反馈与统计入口)）。

- 接收范围：主动气泡与统计查看入口，复用已接收的本地采集、聚合与有门槛的时段频次判断；默认关闭、可关闭/清除、仅依据文案、低频展示、多宠单选与交互守卫不变，具体行为以 [PRODUCT R-09](../PRODUCT.md#r-09-习惯学习阶段-6) 为唯一规格出处。
- 接收依据：R2 已关闭 F-01/F-02（本记录第 6 节），Core 专项 28/28、App 专项 10/10 及原反例通过；R3 已完成真实关闭、默认自动隐藏、前台焦点、实体拖动/透明区穿透、真实菜单查看与正常退出。移除“靠近”这一未实现的本版门槛后，无其余当前阻断项；不为纯文档范围调整重复测试或启动实机。
- 范围调整授权：用户明确回复“按这个方案收尾”（本版保持纯气泡，“靠近”另拆后续），已登记 [D-020](../DECISIONS.md) 并同步 PRODUCT/ARCHITECTURE/TASKS/STATUS；不是单方删除需求，也**不把延期功能描述为已实现**。
- 延期：“靠近”独立登记 [TASKS](../TASKS.md) 的 FUT-001，`todo`、未排期、未授权开工；精确图标定位仍不在已完成功能中。该待办不阻塞本版阶段 6 收尾。
- 限制保留（不因接收消除）：无长期真实样本，接收的是既定规则与交互实现，不宣称预测可靠、多屏组合全覆盖；R3 监理夹具事故、已恢复主存档及旧备份无法恢复的限制完整保留。
- 开发侧按报告要求仅追加本 R4 接收引用，不重复实现、不重跑测试，未改报告/证据、未启动 FUT-001 或其他新阶段，未改产品代码；已有修改全部保留，未提交、未推送。
