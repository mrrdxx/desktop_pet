# P5A-002 监理审查 R1

2026-11-18（Asia/Shanghai）。对象为 HEAD `f59e7e2` 加当前未提交的栖息晃动实现、测试、隔离探针与 M-15 驱动。[版本摘要](evidence/P5A-002-R1/versions.json)

**暂缓接收，保持 `awaiting-verification`。** F-01～F-03 阻断本小步结案。P5A-001 接收结论保持。

- 独立执行 `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p5a-002-r1/build --filter 'FullyQualifiedName~PerchWobbleTests|FullyQualifiedName~FootStabilityTests|FullyQualifiedName~WindowPerchOwnershipTests' --logger 'trx;LogFileName=p5a-002-r1.trx' --results-directory artifacts/p5a-002-r1`，23/23，通过且无跳过。暂停、拖动和遮挡脱离的单测末态归零。[TRX](evidence/P5A-002-R1/app-wobble.trx)
- 原始记事本清单 18/18，探针退出 0，记录移动后角度峰值 7.489°、静止后归零。测量的脚点为窗口底部锚点。未在本轮重跑实机或全量，也没有进行画面观感确认。[清单](evidence/P5A-002-R1/notepad-checklist.txt) · [事件](evidence/P5A-002-R1/notepad-events.jsonl)

## F-01 旋转中心未固定实际素材脚点（中，未关闭）

[旋转中心](../../src/DesktopPet.App/MainWindow.xaml#L35)使用窗口底边的 `(60,120)`，[小熊素材](../../assets/pets/bear/character.json#L5)声明的脚点是 `(60,110)`。旋转使实际脚点位移，窗口底部坐标不变的测试无法识别。零帧偏移时，由两点差及 7.489° 峰值计算，素材脚点横向约偏移 1.3 DIP。请将旋转锚定在帧偏移补偿后的素材脚点。补证比较非零晃动前后实际变换的素材脚点，并验证窗口位置不变。保持 P5A-001 已验收的栖息位置语义。[计算记录](evidence/P5A-002-R1/observations.json)

## F-02 连续跟随反复重置相位（中，未关闭）

[每次跟随成功](../../src/DesktopPet.App/Platform/PetWindowPerch.cs#L280)都会重置[晃动起点](../../src/DesktopPet.App/MainWindow.xaml.cs#L2242)。轮询间隔 120 ms，260 ms 振荡的首个半周期为 130 ms。持续移动且每次轮询均提交成功时，理想 120 ms 节奏下角度相位始终停留在首半周期，表现为单侧重复回跳。当前实机只有一次 follow。保持持续跟随期间的相位连续，停止后仍可短时回稳。补多个连续 follow 的受控时序验证，在目标仍移动时出现两个方向的角度，停止后归零。[时序计算](evidence/P5A-002-R1/observations.json)

## F-03 实机拖动打断缺少晃动中前置条件（中，未关闭）

已接受拖动、暂停与遮挡即时清理的自动测试结果。M-15 [拖动段](../../tools/Invoke-P5A001Preview.ps1#L594)在目标重新选中后直接拖动，仅在拖动后检查零角度。该轮唯一一次 follow 在 1002 ms，拖动接管在 19781 ms，间隔 18779 ms，远超晃动时长。补一次隔离对照：先确认小熊角度非零，随即走真实拖动入口，并在接管时或衰减自然结束前确认归零。原有移动、回稳证据可沿用，只补这一小段；同步校正实际清单项数。[时序摘要](evidence/P5A-002-R1/observations.json)

## 监理复核 R2（2026-11-18，Asia/Shanghai）

**F-02 关闭，F-01 与 F-03 部分通过。** P5A-002 保持 `awaiting-verification`；P5A-001 验收保持。对象为 HEAD `f59e7e2` 加当前未提交版本。[版本摘要](evidence/P5A-002-R2/versions.json)

- 独立执行 `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p5a-002-r2/build --filter 'FullyQualifiedName~PerchWobbleTests|FullyQualifiedName~FootStabilityTests|FullyQualifiedName~WindowPerchOwnershipTests' --logger 'trx;LogFileName=p5a-002-r2.trx' --results-directory artifacts/p5a-002-r2`，25/25，无跳过。核对已有实机原始证据，未重跑桌面或全量。[专项 TRX](evidence/P5A-002-R2/app-wobble.trx)
- **F-01 固定帧通过，帧切换仍有一帧偏移。** [更新顺序](../../src/DesktopPet.App/MainWindow.xaml.cs#L1273)先更新旋转中心，后应用新帧偏移；从其他入口直接调用 `UpdateFrame` 也会单独改变偏移。独立测试副本在非零角度切到带 9 DIP 偏移的 `curious` 后立即调用生产 `UpdateFrame`，完整变换的素材脚点 X 为 58.8343 DIP，预期 60，原反例 0/1。现有固定帧测试输入已加偏移的脚点到还含平移的变换组，零偏移下看不出问题。请在设置帧偏移的同一入口同步旋转中心，补实际非零帧切换。测试应把原始素材脚点输入整个变换组，与只应用平移的结果比较，并保留窗口坐标不变的断言。[反例差异](evidence/P5A-002-R2/reviewer-frame-transition.diff) · [反例 TRX](evidence/P5A-002-R2/reviewer-frame-transition.trx)
- **F-02 通过。** 连续 follow 保留起始时刻，通过生产跟随触发正负角度的测试，静止后归零。
- **F-03 晃动中抓取已补证，归零仍须在接管时记录。** 原始记事本清单有 19 条 PASS，探针退出 0。晃动前置读数为 4.509°，第二次 follow 在 20174 ms、`drag-preempted` 在 20381 ms，相差 207 ms，确认抓取发生在晃动期。[当前驱动](../../tools/Invoke-P5A001Preview.ps1#L707)在完整拖动、松手并额外等待 1000 ms 后才读零角度，早已越过自然衰减期限。补接管处理完成时的角度记录，或在当前晃动自然截止前读归零，只补这一小段。清单条数按文件记录校正为 19，上一轮为 18。[清单](evidence/P5A-002-R2/notepad-checklist.txt) · [事件](evidence/P5A-002-R2/notepad-events.jsonl)

## 监理复核 R3（2026-11-18，Asia/Shanghai）

**F-01 关闭，F-02 保持关闭，F-03 仅剩接管后立即归零的实机证据。** P5A-002 保持 `awaiting-verification`；P5A-001 接收结论有效。对象为 HEAD `f59e7e2` 加当前未提交版本。[版本摘要](evidence/P5A-002-R3/versions.json)

- **F-01 通过。** `UpdateFrame` 对同一帧应用平移及旋转中心，`UpdatePerchWobble` 只更新角度。独立沿用 R2 原反例，以临时测试编译输入运行当前版本。原反例和开发新增切帧回归均通过，专项 27/27。命令：`dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p5a-002-r3/build -p:CustomAfterMicrosoftCommonTargets=<独立副本>/review.targets --filter 'FullyQualifiedName~PerchWobbleTests|FullyQualifiedName~FootStabilityTests|FullyQualifiedName~WindowPerchOwnershipTests' --logger 'trx;LogFileName=p5a-002-r3.trx' --results-directory artifacts/p5a-002-r3`。[R2 独立反例差异](evidence/P5A-002-R2/reviewer-frame-transition.diff) · [R3 TRX](evidence/P5A-002-R3/app-wobble.trx)
- **F-03 仍部分通过。** 新一轮原始清单共 **19 条 PASS**，探针退出 0。目标位移后角度 6.876°，同目标最近一次 `follow` 在 20619 ms，`drag-preempted` 在 20801 ms，间隔 182 ms。已再次确认接管发生于晃动期。自动用例的即时清理证据有效。[记事本清单](evidence/P5A-002-R3/notepad-checklist.txt) · [事件](evidence/P5A-002-R3/notepad-events.jsonl) · [时序摘要](evidence/P5A-002-R3/observations.json)
- **当前归零读数仍可能来自自然结束后。** [驱动](../../tools/Invoke-P5A001Preview.ps1#L723)在鼠标按下后完成 120 ms 停顿、12×40 ms 移动及 350 ms 释放前停顿，松手后才轮询状态文件。[探针](../../tests/DesktopPet.DiagnosticProbe/WindowPerchLiveScenario.cs#L171)每次覆盖最新心跳，条件 `tMs > dragEv.tMs` 无法取回已经被覆盖的接管后第一条心跳；日志也未记录实际归零样本时间。自然衰减约 708 ms，这条断言仍可在未即时清理的情况下通过。建议只在隔离探针中追加持久事件，记录拖动接管处理前后的角度及时间，或在接管后、继续完整移动与松手前采样并断言时间差。补一次短实机段即可，沿用其余通过证据。

本轮未重新操作桌面或运行全量。最新原始清单的最大化步骤记录屏幕边界 1920×1080；跨屏、混合 DPI、真实用户动态遮挡仍未测。

## 监理复核 R4（2026-11-18，Asia/Shanghai）

**F-03 关闭，F-01～F-03 全部关闭。P5A-002 接收并标记 `done`。** P5A-001 接收结论保持。对象为 HEAD `f59e7e2` 加当前未提交的探针与驱动。[版本摘要](evidence/P5A-002-R4/versions.json)

- **F-03 即时归零证据通过。** 探针在生产 `CancelForDrag` 的同步事件回调中追加前态，通过 Dispatcher 排队在该处理返回后追加后态。原始事件流依次为 `drag-preempted` 15702 ms，前态 15703 ms（晃动中，−4.19°），后态 15707 ms（晃动结束、0°）。前后态相差 4 ms，距接管事件 5 ms，满足即时打断取证。探针退出 0。[事件](evidence/P5A-002-R4/notepad-events.jsonl) · [时间及清单摘要](evidence/P5A-002-R4/observations.json)
- 该轮清单按原始结果为 **18 PASS、1 FAIL**。远端遮挡失败时，驱动的测试覆盖窗从 x=1170 起，落脚区为 x=1040–1160，另一个外部窗口矩形覆盖落脚区，事件显示落脚区可见比例为 0。此轮的外部遮挡脱离符合规格，远端覆盖窗本身不触发脱离的证据沿用 `run-20261118-212632-notepad`（PASS）。该轮原始清单为 19/19。[本轮清单（省略外部窗口标题）](evidence/P5A-002-R4/notepad-checklist.txt) · [此前清单](evidence/P5A-002-R3/notepad-checklist.txt)
- 独立核验原始事件、计数及驱动语法，核对 `MainWindow.xaml.cs` 和晃动用例摘要与 R3 一致。沿用 R3 独立专项 27/27 和开发全量 1083/1083，本轮未重跑程序或操作桌面。暂停打断仅自动测试覆盖；跨屏、混合 DPI 与真实用户窗口动态遮挡仍未系统实测。主动靠近与攀爬留待后续。
