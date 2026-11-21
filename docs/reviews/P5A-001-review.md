# P5A-001 监理审查 R1

2026-11-18（Asia/Shanghai）。审查对象为 HEAD `f59e7e2` 加当前未提交的栖息实现、隔离探针和 M-15 驱动。[文件摘要](evidence/P5A-001-R1/versions.json)

**结论：暂缓接收，保持 `awaiting-verification`。** F-01～F-04 阻断 P5A-001 结案。P5A-PLAN 已接收的结论有效。

## 证据

- 独立运行 `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --artifacts-path artifacts/p5a-001-r1/build --filter 'FullyQualifiedName~ExternalWindowTests|FullyQualifiedName~Perch' --logger 'trx;LogFileName=core-perch.trx' --results-directory artifacts/p5a-001-r1`，46/46 通过。[TRX](evidence/P5A-001-R1/core-perch.trx)
- 两轮原始清单各有 **14 条 PASS**（含退出检查），探针退出 0。事件与脚点数据支持普通落位及跟随、最小化/关闭脱离、受控遮挡脱离，以及真实输入拖动接管。两轮松手后脚点均保持 `[2018,920]`，目标再移动无 follow。最大化的 PASS 只检查了目标外框，实际宠物出屏，见 F-01。[记事本清单](evidence/P5A-001-R1/notepad-checklist.txt) · [Edge 清单](evidence/P5A-001-R1/edge-checklist.txt)
- 本轮读取开发实机记录，未操作桌面或重跑 App 全量。跨屏、混合 DPI、真实用户窗口动态遮挡仍未测。

## F-01 最大化时宠物完全离开可见区域（高，未关闭）

[落位策略](../../src/DesktopPet.Core/Windows/WindowPerchLandingPolicy.cs#L20)只校验外框宽高，随后按目标上缘放置脚点。[M-15 最大化断言](../../tools/Invoke-P5A001Preview.ps1#L364)只检查目标外框原点。两轮最大化时脚点都是 `[1280,0]`，实际宠物矩形为 `[1190,-180,1370,0]`，在该单屏环境中完全不可见。修复初选与跟随的落位校验，使用完整宠物矩形和可见区域。没有可用落点则解除绑定并保持或恢复到可见区域。补最大化或顶边反例，以及最大化后上方仍容得下宠物时的正例。驱动以实际宠物矩形与绑定状态断言，不能凭目标原点通过。[记事本事件](evidence/P5A-001-R1/notepad-events.jsonl) · [Edge 事件](evidence/P5A-001-R1/edge-events.jsonl)

## F-02 栖息绕过已有位置所有权（高，未关闭）

[栖息](../../src/DesktopPet.App/Platform/PetWindowPerch.cs#L55)用独立的 DispatcherTimer 写位置，现有更新循环仍能推进物理和其他位移。[写入守卫](../../src/DesktopPet.App/MainWindow.xaml.cs#L2179)只覆盖拖动、抚摸、暂停。物理活动期间显式选中即可落位，之后物理循环仍能改写位置。暂停中显式选中时，`ApplyPerchFoot` 提前返回，但调用方仍报告 `landed` 和成功。让初选、跟随和实际位置提交通过已有的受守卫更新边界，明确高优先级位移的互斥条件；无法提交时不得报告落位或消费跟随。加 App 级反例，至少覆盖暂停及物理活动中拒绝落位、成功时位置只由一个行为提交，并保留实机拖动接管的证据。按现有更新入口做有限接入。

## F-03 关闭宠物窗口未停止栖息轮询（中，未关闭）

[窗口关闭](../../src/DesktopPet.App/MainWindow.xaml.cs#L3259)只停止原有更新计时器。栖息计时器订阅持有窗口引用，关闭宠物时若目标仍存活，跟随仍可继续执行；双宠移除会经过相同关闭路径。关闭时释放栖息绑定及轮询，验证外部目标存活时关闭宠物，后续不再读目标或写宠物位置。顺带核对退出保存的坐标语义：[保存](../../src/DesktopPet.App/App.xaml.cs#L1516)直接读取窗口当前坐标，栖息自身不标脏并不能保证临时位置从未落盘。整改记录应说明并验证实际保留的坐标和重开无旧绑定，校正与实现不符的注释。

## F-04 遮挡判定与落脚点不一致，写入先于校验（中，未关闭）

[遮挡采样](../../src/DesktopPet.App/Platform/WindowPerchInterop.cs#L111)对上缘九点取样，任一点不可见就脱离，即使中间的落脚点无遮挡。[跟随](../../src/DesktopPet.App/Platform/PetWindowPerch.cs#L155)先移动宠物，再检查遮挡；初选在首次轮询前也能落到已被遮挡的位置。应在落位及跟随提交前校验实际落脚处。补证只遮上缘远端时仍可栖息，落脚处被盖时初选拒绝，跟随不会先写入被遮挡位置。

## 监理复核 R2（2026-11-18，Asia/Shanghai）

**F-01 关闭，F-02～F-04 部分通过，P5A-001 保持 `awaiting-verification`。** 对象为 HEAD `f59e7e2` 加当前未提交的整改版本。[版本摘要](evidence/P5A-001-R2/versions.json)

- 独立执行 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p5a-001-r2/build --filter 'FullyQualifiedName~ExternalWindowTests|FullyQualifiedName~Perch' --logger 'trx;LogFilePrefix=perch-r2' --results-directory artifacts/p5a-001-r2`，Core 54/54、App 9/9，均无跳过。Core 548/548 与 App 520/520 为开发全量自测。本轮核对开发原始实机记录，未重跑桌面或全量。[Core TRX](evidence/P5A-001-R2/core-perch.trx) · [App TRX](evidence/P5A-001-R2/app-perch.trx)
- **F-01 通过。** Core 补了完整矩形贴边正例及不容纳反例；四轮实际最大化均记录 `detached(no-landing)`，宠物完整可见、绑定已解除，恢复后重选正常。四轮清单均 15/15、探针均退出 0，保留真实输入拖动接管及松手不回拉证据。实测单屏 144 DPI。[四轮摘要](evidence/P5A-001-R2/runs.json) · [记事本清单](evidence/P5A-001-R2/163221-notepad-checklist.txt) · [Edge 清单](evidence/P5A-001-R2/163417-edge-checklist.txt)
- **F-02 部分通过。** 暂停、物理活动的显式选择回归通过，提交被拒时不发落位事件且不消费跟随基线。仍由 [独立计时器](../../src/DesktopPet.App/Platform/PetWindowPerch.cs#L80) 直接经 `ApplyPerchFoot` 写位置，现有更新链没有统一的所有权处理。[找回](../../src/DesktopPet.App/MainWindow.xaml.cs#L2878) 不释放绑定，随后移动目标会重新拉走已找回的宠物。把栖息提交接到已有位置更新边界，保留低频观测即可；加一个已绑定后找回、随后目标移动不再写位置的对照，并保留拖动抢占。另一个可直接从代码推出的反例在 [重选](../../src/DesktopPet.App/Platform/PetWindowPerch.cs#L100)：已有绑定时重选一个合格但不可落位的目标，会把旧跟踪器清掉，失败又只重置跟踪器，活跃标志和计时器仍保留；下一轮对空目标解引用。重选失败时应保留原绑定或完整脱离，两种结果均需状态一致，补一次失败后继续推进的反例。此反例为静态复核，本轮未在桌面触发。
- **F-03 部分通过。** `OnClosed` 已停用栖息，独立通过的用例确认关闭时关计时器及解绑。退出恢复钩子的坐标用例通过；现有测试仅用反射设置状态后调用恢复钩子，未经过正常保存与新窗口恢复。补隔离存档往返，核对正常退出落盘的栖息前坐标、新窗口不续跑旧绑定；关闭用例若更新驱动方式，保留关闭后无观测、无写入的断言。
- **F-04 部分通过。** 代码已把实际落脚区采样放在初选及跟随提交之前，四轮证实远端遮挡保持绑定、静态中心遮挡脱离。现行 M-15 的中心遮挡是在已经落位后加盖，尚未观测初选被挡后零位移、跟随到被盖位置前零写入。补这两个小步的 App 或隔离证据即可，无需重跑四轮全部步骤。[Edge 事件](evidence/P5A-001-R2/163417-edge-events.jsonl)

跨屏、混合 DPI、真实用户窗口动态遮挡维持未测范围。P5A-PLAN 已接收结论保持。

## 监理复核 R3（2026-11-18，Asia/Shanghai）

**F-03 关闭，F-02 与 F-04 仍部分通过。** 保留 F-01 关闭及 P5A-PLAN 接收结论，P5A-001 仍为 `awaiting-verification`。审查对象为 HEAD `f59e7e2` 加当前未提交版本。[版本摘要](evidence/P5A-001-R3/versions.json)

- 独立运行 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p5a-001-r3/build --filter 'FullyQualifiedName~ExternalWindowTests|FullyQualifiedName~Perch' --logger 'trx;LogFilePrefix=perch-r3' --results-directory artifacts/p5a-001-r3`，Core 54/54、App 14/14，均无跳过。沿用 R2 四轮实机记录，本轮未重跑实机或全量。[Core TRX](evidence/P5A-001-R3/core-perch.trx) · [App TRX](evidence/P5A-001-R3/app-perch.trx)
- **F-02 找回与失败重选通过。** 找回先解除绑定，后续轮询不写位置；失败重选用原目标恢复跟踪。新增反例继续推进真实轮询方法，读取旧绑定并在旧目标实际不可读时以 `WindowGone` 一致清除。**位置所有权仍待收尾。** `ApplyPerchFoot` 是栖息专用提交入口，独立计时器仍调用它写 `Left/Top`。另一个宠物的社交 tick 可以直接提交当前宠物的寻找或追逐位移，[提交](../../src/DesktopPet.App/MainWindow.xaml.cs#L1355)的守卫不查询栖息，`_isSocialActive` 只由当前处理该 tick 的窗口先更新。因此本地六旗并不能保证两条路径互斥。请将栖息提交并入现有更新中的位置选择，或提供等效的共享所有权判定且两侧提交都遵守。低频观察和其他行为内部实现可沿用。补 App 对照在同一宠物同时有栖息和社交位移候选时仅获准路径写位置。
- **F-03 通过。** 关闭停轮询与解绑的原验证通过。新增隔离用例实际经过退出保存链、从磁盘重载，坐标为栖息前的 222/333，新建窗口无目标且推进轮询零写入。用例以反射设定初始栖息，退出钩子绕开 WPF 进程终止；新窗口使用生产工厂，未 Show。此处接受的是实际落盘与绑定不跨窗口恢复。
- **F-04 初选通过，跟随脱离补证尚缺。** 初选反例走真实选择路径，拒绝时无位移或落位事件。跟随用例在 [测试](../../tests/DesktopPet.App.Tests/WindowPerchOwnershipTests.cs#L386)中直接反射调用 `TryApplyLanding("follow")`，断言 `Occluded` 和零位移。这个测试即使删掉 `OnTick` 的遮挡脱离分支也能通过。补一次已有绑定、目标外框变化且新落脚处被遮挡的轮询，验证原位置不动、无 `follow` 且解除绑定；可用受控观测驱动生产决策，不必重跑四轮 M-15。

跨屏、混合 DPI、真实用户窗口动态遮挡仍未测。

## 监理复核 R4（2026-11-18，Asia/Shanghai）

**F-04 关闭，F-02 仍有跨窗口位置所有权阻断。** F-01、F-03 保持关闭，P5A-001 保持 `awaiting-verification`。复核对象为 HEAD `f59e7e2` 加当前未提交修改。[版本摘要](evidence/P5A-001-R4/versions.json)

- 独立运行 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p5a-001-r4/build --filter 'FullyQualifiedName~ExternalWindowTests|FullyQualifiedName~Perch|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=perch-r4' --results-directory artifacts/p5a-001-r4`，Core 54/54、App 104/104。沿用四轮 M-15，未重跑实机或全量。[Core TRX](evidence/P5A-001-R4/core-perch.trx) · [App TRX](evidence/P5A-001-R4/app-perch-social.trx)
- **F-04 通过。** 新用例只替换快照输入，完整推进生产轮询。目标身份保持一致、外框变化，触发被遮挡的跟随分支。断言宠物原位、无 `follow`、恰一条 `occluded-detach`、目标清空且轮询停止。初选及远端遮挡的已通过证据一并接受。
- **F-02 栖息先取得位置时的反向守卫已接入。** 寻找及成对追逐写入检查目标宠物的 `IsSocialPositionWriteAllowed`，对应追逐取消与测试钩子仍保留。当前所有权用例只读取社交允许属性并手工设置本地 `_isSocialActive`，未执行跨窗口生产社交提交。
- **F-02 社交先取得位置的反例已独立复现。** 复用既有双宠追逐测试，只有 A 推进真实 `OnUpdateTickTest`。确认追逐实际移动 A 与 B 后，在 B 自己 tick 前调用 B 的实际栖息提交 `ApplyPerchFoot`，它返回 true。预期拒绝，反例 0/1。原因是 [A 的社交更新](../../src/DesktopPet.App/MainWindow.xaml.cs#L1314)只刷新 A 的本地标志，虽可[直接写 B 的位置](../../src/DesktopPet.App/MainWindow.xaml.cs#L1415)，[B 的栖息入口](../../src/DesktopPet.App/MainWindow.xaml.cs#L2180)却仍查询 B 尚未更新的字段。反例通过独立编译的测试副本执行，未修改产品或仓库测试源码。[反例差异](evidence/P5A-001-R4/reviewer-cross-window.diff) · [反例 TRX](evidence/P5A-001-R4/reviewer-cross-window.trx)
- **唯一剩余项。** 初选及跟随都须依据及时生效的共享持有状态，或由实际社交写入路径同步登记所涉及窗口的所有权。要覆盖 A 提交 B 的社交位移后、B 尚未 tick 的间隙，并验证社交结束后归还权限。沿用现有实机、F-04 与其他已关闭证据。

跨屏、混合 DPI、真实用户窗口动态遮挡仍未测。

## 监理复核 R5（2026-11-18，Asia/Shanghai）

**F-02 关闭，F-01～F-04 全部关闭。P5A-001 接收并标记 `done`。** 范围为窗口栖息首版的落位、跟随和脱离。复核对象为 HEAD `f59e7e2` 加当前未提交版本。[版本摘要](evidence/P5A-001-R5/versions.json)

- **F-02 通过。** 初选与跟随共用的守卫、实际落位提交均读取共享协调器的活动会话并核对该宠物是否参与。A 的追逐更新影响 B 时，B 的本地标志尚未更新也拒绝栖息。开发新增用例使用生产追逐 tick，确认 A 确实写入 B 位移，随后 B 的初选、提交与跟随都让出，且在同一用例中验证追逐结束后的权限恢复。
- **独立复核。** 使用当前测试源码的独立副本追加 R4 原反例方法，以 MSBuild 的 `CustomAfterMicrosoftCommonTargets` 替换 App 测试编译输入，产品源码和仓库测试源码保留原样。命令为 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p5a-001-r5/build -p:CustomAfterMicrosoftCommonTargets=<独立副本>/review.targets --filter 'FullyQualifiedName~ExternalWindowTests|FullyQualifiedName~Perch|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=perch-r5' --results-directory artifacts/p5a-001-r5`。Core 54/54、App 106/106，含原反例 1/1 与开发新增 1/1。[R4 原反例差异](evidence/P5A-001-R4/reviewer-cross-window.diff) · [Core TRX](evidence/P5A-001-R5/core-perch.trx) · [App TRX](evidence/P5A-001-R5/app-perch-social.trx)
- 沿用已核对的四轮 M-15（各 15/15，单屏 144 DPI、探针退出 0）。本轮未重跑桌面或全量。跨屏、混合 DPI、真实用户窗口动态遮挡仍未测。晃动、主动靠近和攀爬不在本任务验收内。[M-15 原始记录摘要](evidence/P5A-001-R2/runs.json)
