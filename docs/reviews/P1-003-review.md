# P1-003 Core 情绪模型阶段检查

日期：2026-10-23（Asia/Shanghai）。监理：本助手。本次未修改应用代码。
版本：`b7212f3`（实现提交 `4882efc`），开始工作区干净。
范围：四个 Emotion 文件、PetStateArchive、ArchiveService.LoadState、相关测试及 PRODUCT R-02。不是完整 P1-003 验收，App 尚未接入。

## 结论

总体分层可沿用。F-01 须在相关日期逻辑接入前修正，阻断 P1-003 最终完成；不阻断无关的抚摸检测等工作。G-01/G-02 为接入约束，不是已运行 App 的确定缺陷。不重开 P1-002。
开发方报告全量 228/228；本轮运行结果以本文后续记录为准，不将开发报告写成本监理独立全量通过。

本轮尝试 `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj -c Debug --no-build --no-restore`，filter 限定四个情绪测试类。测试启动后长时间无结果，已用 Ctrl+C 取消，退出码 1；没有通过/失败计数，记为本轮运行未完成，不据此判定代码测试失败。未重新构建、未执行全量测试。上述发现来自源码静态检查，日期反例为可直接追踪的分支结果，未声称运行反例通过。

## F-01：日期回拨被当成次日（未关闭）

- 位置：`src/DesktopPet.Core/Emotion/GrievanceExpressionState.cs` 的 ShouldExpress。
- 当前实现 `IsPending && MarkedDate != currentLocalDate` 只比较不相等。标记 2026-10-23 后传入 2026-10-22，也返回 true，与“次日”和回拨不重复的时间约定不符。
- 修正：按真实有效日期比较，只有当前日期晚于标记日期才可表达；明确缺失/非法日期的恢复策略，不把无效日期直接当次日。可使用 DateOnly 或严格解析，不要求特定实现。
- 验证：同日不触发、次日和更晚日期触发、回拨不触发；存档恢复仍遵循相同判断，补充缺失/非法日期用例。现有测试只有相同日期及向后推进，没有回拨反例。

## G-01：衰减接入须保留累计精度

EmotionDecay 每次输出 int。如果 OnUpdateTick 把短间隔衰减结果写回整数，并推进基准时间，每次小于 0.5 的变化会被舍弃，在线衰减可能永远不发生。当前 App 尚未接入，不能声称已出现该运行缺陷。
接入时保留浮点内部状态或使用固定基值及累计时间，合理处理存档精度；用许多小步累计 12 小时/7 天与一次推进相同总时长对照，不能只测一次半衰期。

## G-02：角落停留必须对应最后一次释放

CornerTracker.RecordCornerDrag 不清除旧 _lastDragStayStart，IsConditionMet 不检查其是否早于最后释放。必须定义新拖动、离开角落、再次释放时的停留取消/重置约定，避免用前一次停留替代最后 30 秒。可在 Core 保证或以明确调用协议配合 App 验证。
用例：先开始旧停留，再发生第二/第三次拖动；第三次释放不足 30 秒不计分，连续满 30 秒才计一次；离开角落终止停留。计分后 Clear，后续 tick 不重复加分。现有测试每次手动设置最新停留，不能证明接入后的中断链正确。

## 接续

开发 AI 在 `docs/remediation/P1-003-response.md` 按本报告路径和 F-01 记录整改、自测及复核请求；G 项在对应接入小步给出覆盖，不为建议另设无关审批。TASKS 继续 in-progress。维持现有存档高版本只读保护，不开始 P1-004。

## R1 复核：2026-10-23

对象：`5d5055f`，实现 `ea0f9ec`。开始仅本报告尚未跟踪，未修改应用代码。读取整改记录、实际实现及对应新增测试。

- F-01 **关闭**：ShouldExpress 使用 DateOnly 解析并比较 current > marked，回拨/同日/无效日期不触发，补充恢复路径测试。TryParse 为日期解析，不是限定 yyyy-MM-dd 的 TryParseExact；本次要求的日期先后关系已满足，不另扩大范围。
- G-01 精度方案可沿用：内部 double 累计并在快照取整，小步与大步一致性已有覆盖；App 尚未接入，不签运行时通过。新增累计器的回拨恢复缺陷另列 F-02。
- G-02 Core 修正认可：新角落释放取消旧停留、检查停留起点、支持 CancelStay；App 接入时仍须验证按下即取消、离开终止、释放重建及计分后清理。
- 独立运行 `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj -c Debug --no-build --no-restore --nologo -v minimal`：**199/199 通过、0 失败/跳过，退出码 0**。本轮在获准的沙箱外执行测试后正常完成。未重建、未重跑 App 58 项，257/257 全量仍为开发方证据。

### F-02：回拨后恢复时间会重复衰减（未关闭）

位置：`src/DesktopPet.Core/Emotion/EmotionDecay.cs` 中 EmotionDecayAccumulator.AdvanceTo。
负间隔被改为零后仍无条件执行 `_lastDecayUtc = currentUtc`，将基准倒退。后续时钟恢复会把已经处理过的区间再次衰减。

本轮使用 PowerShell .NET 10.0.13 加载当前实际 Core DLL 执行：

```text
new EmotionDecayAccumulator(50, 90, 2026-10-23T12:00:00Z)
AdvanceTo(2026-10-23T00:00:00Z)
afterRollback=90; reference=2026-10-23T00:00:00.0000000Z
AdvanceTo(2026-10-23T12:00:00Z)
afterReturningToOriginalTime=70; expected=90
```

整改目标：回拨时不倒退有效时间基准（或采用等价的去重时间方案）；恢复到原处理时刻时不发生额外衰减，超过原时刻只计算新间隔。恢复存档时沿用同样的时间约定。
验证：起点→回拨→恢复原时刻→超过原时刻，与没有回拨的对照数值一致，覆盖亲近/委屈及重复回拨。现有测试只断言回拨这一调用数值不变，漏掉恢复过程。
阻断范围：本累计器相关时间推进逻辑的接入放行及 P1-003 最终完成；抚摸手势等无关接入可继续，不要求停工或重做 F-01。

## R2 复核：2026-10-23

对象：`680befe`；开始仅本监理报告未跟踪，未修改应用实现。对照整改记录、AdvanceTo 实现及九项新增 F02 测试。

- **F-02 关闭**：负间隔直接返回快照，不移动有效时间基准。恢复到原时刻无额外衰减，超过原时刻只计算新时间。
- 独立运行 Core 测试（与 R1 相同命令）：**208/208 通过，0 失败/跳过，退出码 0**。未重新构建，未运行 App 58 项，不将开发方 266/266 写成本轮独立全量通过。
- 用 .NET 10 PowerShell 加载当前实际 Core DLL 重跑原反例，并加亲近度及无回拨对照：起点亲近 80/委屈 90、回拨 12 小时后为 80/90，基准仍为 `2026-10-23T12:00:00Z`；恢复原时刻仍为 80/90；再推进 12 小时，委屈 70 与对照相同、亲近度与对照差值 0。
- 九项新增测试覆盖连续回拨/恢复/继续推进、两种情绪、重复回拨及基准不倒退，断言有实际区分度。

F-01/F-02 当前均已关闭，允许继续 P1-003 App 接入。G-01/G-02 的运行时接线、抚摸/拖动手势冲突、暂停/打断、存档恢复仍需在相应小步验证；此结论不是 P1-003 整体完成。TASKS 保持 in-progress，不进入 P1-004。开发方在整改记录追加本轮关闭依据，无需重复申请已关闭问题。

## R3 App 接入检查：2026-10-23

对象：`74ba984`（含 `5c13485`、`25ded04` 接入），开始工作区干净。本轮未改应用代码。
独立执行 `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj -c Debug --no-build --no-restore --filter FullyQualifiedName~EmotionRuntimeTests --nologo -v minimal`：**15/15 通过，退出码 0**。未重建/未重跑全量 290 项；以下为对实际生产分支及测试覆盖的检查，不声称已经执行真实鼠标用例。

G-01 浮点累计器已接入 tick，G-02 的计分后 Clear/离开 CancelStay 方向认可，但整体 App 接入尚不能验收。F-01/F-02 保持关闭。新增 F-03～F-05 阻断 P1-003 最终完成，不阻断其余无关实现，不进入 P1-004。

### F-03：抚摸累计包含静止时间，且未在离开头部时结束（未关闭）

位置：MainWindow.PetCanvas_MouseMove、EndPetting、IsInHeadZone。
按下后 `_pettingStartMs` 记录起点；随后只要单次 MouseMove 距离 <=8，就把整个事件间隔加入累计。按住静止两秒再微移 1 DIP，便累计两秒；moveDist=0 也满足条件。捕获后没有检查是否仍在有效头部区域，连续小步离开宠物也可继续累计。违背 R-02“按住未移动不算、离开结束”。头部还被硬编码为上 1/3，未落实角色资源中的区域契约。
整改：按有效小幅移动累计，排除零位移及长静止间隔；移出有效头部实体区域结束/取消，区分转拖动；明确区域来源并落实已有资源契约。验证静止后单次微移不计一次、持续有效微移达一秒只加 5、移出头部停止、拖动转换及冷却。不要只预置 accumulatedMs 来替代实际手势链。

### F-04：委屈表达缺少首次相遇条件和完整动作结束/打断（未关闭）

位置：MainWindow.OnUpdateTick 的 ShouldExpress 分支、PetCanvas_MouseLeftButtonDown、EndDrag。
当前只检查日期和非拖动/菜单/抚摸，不检查鼠标是否进入交互范围；次日鼠标远处也启动并消耗待表达。达到十秒仅清标志/数值，不恢复 idle，curious 是循环动作，仍保留同一画面。拖动中断逻辑放在 EndDrag，按住拖动时表达标志可持续存在，甚至先由 tick 完成，而非拖动开始立即打断。
整改：按 R-02 首次相遇启动；表达动作有明确优先级和结束恢复，拖动开始即取消表达并按约定清状态，避免悬停/甩动覆盖表达。占位素材可以保留，但不能用它掩盖生命周期缺失。
验证远处不启动/近处首次相遇启动、10 秒前后动作及状态、拖动按下即打断、后续 tick 不重复表达，包含暂停/恢复与菜单协调。

### F-05：所述抚摸增量与重启恢复尚无实际 App 链证据（未关闭）

EmotionRuntimeTests 的 G01_RestartRestore_EmotionStateFromArchive/G01_V1ArchiveFallback_NeutralEmotionAndCurrentTime 只是以字面量构造累计器和表达对象，没有读写文件或执行 App 恢复映射。G01_ManySmallTicks_MatchOneLargeTick 也明确只调用 Core，不能描述为通过 OnUpdateTick 的累计一致性测试。
PettingDetectionTests.Petting_AccumulatedTime_TriggersIncrement 在无 App.Current 情况仅验证候选状态清理，未断言实际亲近度 +5；注入情绪对象只供 tick 使用，EndPetting 仍只访问 App.Current。这不足以证明报告声称的真实计分/保存链。
整改：用隔离目录实际完成加载→运行时互动计分→保存→重新加载/恢复，覆盖 v1 默认值、v2 情绪及待表达日期/基准。可抽出生产共用映射或提供最小生产依赖入口，不复制启动代码到测试。补实际手势驱动的 +5/冷却断言；校正测试说明，仅将真正经过 tick 的用例称为 App 接线验证。保持真实存档不变。

### G-03：暂停时间语义需准确记录

目前 Pause 停止 tick，但 Resume 后 AdvanceTo(DateTime.UtcNow) 会补算暂停时段；表达和角落停留又使用继续流逝的 nowMs。现有测试只检查暂停期间未更新，不能证明“暂停时段不计入衰减/持续时间”。按 PRODUCT/ARCHITECTURE 确定统一语义并补暂停→长间隔→恢复断言，必要时明确细化规格；不要仅把当前实现写成正确规格。本条为接入语义待明确，不单独扩大阻断范围。

接续：开发方按本报告路径和 F-03～F-05 在原整改记录追加修复/自测；建议先手势，再表达，最后真实隔离存档链，分小步验证。TASKS 保持 in-progress，不以这 15 项通过替代整体验收。

## R4 整改复核：2026-10-23

对象 `4ea4462`，开始工作区干净。独立运行 `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj -c Debug --no-build --no-restore --nologo -v minimal`：**App 104/104 通过，0 失败/跳过，退出码 0**。未重建或重跑 Core 208，312 全量为开发方证据。本轮未修改应用代码、未执行真实鼠标。

### F-03：部分通过，未关闭

认可 ProcessPettingMove 与生产事件处理器共用、最小移动/长间隔过滤及实际增量注入断言。剩余：IsInHeadZone 仍只检查 `Y <= height/3`，没有 X/Y 下界或 X 上界，故捕获后横向逐步移出图像、或移动到上方负 Y，仍算头部。比如任意大的 X 且 Y=20，仍返回 true。头部区域仍硬编码，资源区域契约未落实。
只需补齐有效区域边界和资源约定，测试横向移出/负坐标/透明空白不继续累计；现有下方移出用例保留，不要求重做全部手势。

### F-04：部分通过，未关闭

认可远处不启动、完成回 idle、按下调用 InterruptGrievanceExpression 的修正。剩余原 R3 明确要求的行为优先级：tick 的悬停/甩动分支仍没有 `_isExpressingGrievance` 条件；表达开始后，近处快划仍可 PlayAction("dodge")，而表达状态继续为 true，十秒生命周期与实际动作脱节。
验证：先实际启动表达，再驱动真实 tick 的近处快划，确认按文档定义维持表达或明确结束该次表达，不能“动作被换走但表达计时继续”。包含正常结束/拖动打断后的输入恢复。无需重做已通过的相遇与结束测试。

### F-05：部分通过，未关闭

隔离文件 I/O 和 +5 断言认可。但 App.MapArchiveToEmotionObjects/MapEmotionObjectsToArchive 只有定义，OnStartup/MarkStateDirty 仍保留各自独立的内联映射；`rg -n 'MapArchiveToEmotionObjects|MapEmotionObjectsToArchive' src/DesktopPet.App/App.xaml.cs` 仅命中定义行 38/58。因此测试调用的是生产未使用的另一份映射，未达到原报告要求的生产共用路径。
整改：使实际启动及保存调用已测共用映射，或让测试实际走生产原路径，避免两套维护。现有 v2 隔离用例直接 acc.AddAffinity，准确描述为数值变化/映射持久化；至少补一项将真实 ProcessPettingMove/EndPetting 的计分结果接入隔离保存恢复，闭合报告所声称的互动链。不要增加第二套测试专用映射。

### G-03：规格细化和补偿方案认可，验证范围需补齐

PRODUCT 已明确排除暂停时段。SkipInterval 与各计时器平移方案可继续沿用。PauseSemanticsTests 中存在 Thread.Sleep 推进真实 UTC、另用 clockMs 推进不同长度单调时间的用例；不能据此证明精确排除暂停段。建议统一可控 UTC/单调时间来源，或用同一真实间隔记录两者，断言恢复后只计新增活动间隔，避免仅判断恢复后数值继续下降。并覆盖暂停中退出/启动仍暂停后恢复是否重复或漏算暂停间隔；此为暂停/存档接续边界，不扩展到锁屏多屏。

结论：F-01/F-02 保持关闭；F-03～F-05 保持未关闭，仅针对上述剩余范围整改。TASKS 继续 in-progress，不要求用户现在重复体验，不进入 P1-004。

## R5 结论补录及 R6 复核：2026-10-23

本轮发现当前报告止于 R4，上轮监理已写入的 R5 不在当前文件中，原因尚未核实。这里补录上轮结论，不改写原 R4：R5 对象 6fee5a0，App 120/120 独立通过；关闭 F-04/F-05，F-03 只保留透明像素与规格一致性，G-03 只保留真正跨对象恢复。版本对比已证实完整 X/Y 边界在 6fee5a0 新增，4ea4462 没有；开发方本轮更正归因认可。报告历史须完整保留，接手方检查是否存在旧副本覆盖，不能用提交覆盖监理新增段落。

### R6 对象及结果

对象 1a2a8c7。开始有开发方未跟踪脚本 check_coords.py/check_coords2.py/check_coords3.py/check_png.py，未修改或删除。独立运行 App 测试（同 R4 命令）：**124/124 通过，0 失败/跳过，退出码 0**。未重建或重跑 Core，不称为本轮独立 332/332 通过。未改应用代码、未操作鼠标。

### F-03：关闭

IsInHeadZone 在矩形边界后调用 PetImageGeometry.Contains 检查当前帧 Alpha；透明/不透明对照及透明处停止累计已补，PRODUCT/ARCHITECTURE 同步固定区域方案。F-01～F-05 当前均已关闭，已认可部分不返工。

### F-06：暂停状态退出期间仍计入衰减（未关闭）

位置：App.MapArchiveToEmotionObjects/OnStartup、MainWindow.ResumeAnimation，以及测试 G03_R5_IsolatedSaveCloseRestore_ExitPaused_NoDoubleCountDecay。
开发方明确暂停退出期间冻结。跨对象隔离测试确实建立，认可比前轮进步；但恢复仍使用旧 LastDecayUtc，Resume 只补偿本次进程启动后的暂停时长。保持旧基准不会自动免除离线间隔，反而在下次 AdvanceTo 补算。

本轮用 .NET 10 PowerShell 加载实际 Core DLL，按生产恢复构造与 Resume 相同数值调用（不冒称运行了完整 App 进程重启）：

```text
reference=2026-10-23T12:00:01.980Z; initial grievance=90
new EmotionDecayAccumulator(50,90,reference)
SkipInterval(330ms) // 重启后暂停
AdvanceTo(reference + 10s退出 + 330ms暂停 + 1.98s活动)
actual=89.9923119396424
activeOnly(1.98s)=89.9987292503545
error=0.006417310712024 < testTolerance=0.01
同样退出12小时，actual=69.9993646251772，期望约90（仅活动1.98s衰减）
```

现有 10 秒用例错误补算全部退出间隔也能通过。另有 Assert.AreEqual(acc2.GrievanceFloat, grievanceAtResume)，后者刚从同一属性赋值，是自比较，不能证明前后不变。

整改：在生产共用恢复/保存路径依据暂停状态排除约定冻结时段，明确写入 PRODUCT 的跨退出语义；保留未暂停退出的正常离线衰减及 F-02 回拨保护。沿用现有模型的最小修复，不引入框架。
验证：隔离保存→关闭旧对象→12小时等长离线→新对象/窗口恢复仍暂停→恢复→活动时间，与只累计活动区间对照；容差明显小于错误衰减量，移除生产修复时必须失败。用独立前后快照替代自比较，补未暂停退出正常衰减对照，防止一律重置基准吞掉正常衰减。
阻断范围：仅 P1-003 暂停/恢复验收与最终完成。原 G-03 的确定缺陷以 F-06 闭环；在原整改记录追加，不重开已关闭项，不要求用户改系统时间，不进入 P1-004。

## R7 复核：2026-10-24（Asia/Shanghai）

对象 `cabf7ba`，开始工作区干净。独立运行 App 测试（同 R4 命令）：**125/125 通过，0 失败/跳过，退出码 0**。未重建/未运行 Core；开发方移除修复后失败的实验本轮未重复，按开发自测证据引用。未改应用代码，未操作真实鼠标。

F-06 **部分通过，未关闭**：生产恢复确实区分 IsPaused，暂停时冻结正常向前的退出间隔、未暂停保持正常离线衰减；12小时隔离用例及独立快照/0.001容差具有区分度，原宽容差和自比较问题已修复，认可这些部分。

剩余仅回拨保护的恢复边界：MapArchiveToEmotionObjects 中 `if (state.IsPaused) lastDecayUtc = utcNow` 无条件覆盖。当保存基准为 `2026-10-24T12:00:00Z`、恢复当前时间回拨为 `2026-10-24T00:00:00Z`，它把基准从12点倒退至0点；恢复到12点时，累计器已无法知道原基准，会再次衰减这12小时。委屈90将变70。此为读取代码可确定的恢复分支反例，本轮未声称通过完整进程重启运行了该组合。

这绕过了原 F-02 在 AdvanceTo 中的非倒退保护；保留那个方法不变并不等于恢复路径仍有保护。F-06 原整改目标已要求保留 F-02，此处不重开旧问题、不增加无关功能。

只需将暂停恢复设为不早于存档有效基准的当前时间（如有条件前移或 max），未暂停路径不变；补“IsPaused=true、存档基准晚于 restoreUtcNow→生产映射恢复→回到原时刻不衰减→超过原时刻只计新间隔”的连续测试，亲近/委屈对照。保留已通过的正常向前12小时暂停/未暂停测试。此组合通过后再关闭 F-06，其余 F-01～F-05 维持关闭，TASKS 仍 in-progress，不进入 P1-004。

关于报告：本轮提交已保留 R5 补录及 R6，认可保留结果；开发方“本提交纯追加”只说明该次 diff，不足以反推此前 R5 缺失原因。无需为此另设阻断或反复调查，后续保持监理新增内容不丢失即可。
