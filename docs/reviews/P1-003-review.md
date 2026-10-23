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
