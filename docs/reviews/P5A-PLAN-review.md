# P5A-PLAN 监理审查 R1

2026-11-17（Asia/Shanghai）。对象为 HEAD `f59e7e2` 加当前未提交的 Core 窗口逻辑、只读探针及 M-14 驱动。[文件摘要](evidence/P5A-PLAN-R1/versions.json)

**结论：暂缓接收。** F-01、F-02 阻断 P5A-PLAN 结案。阶段仍限只读探针与验证。

## 已核对的证据

- 记事本原窗口 `0x1806DE` 在 10948 ms 报 `WindowGone`，11610 ms 以 `0x50FE0` 重获。Edge 原窗口 `0xD0ADE` 在 10556 ms 报 `WindowGone`，11199 ms 以 `0x290E0A` 重获。两轮也均记录了最小化清除、还原后原句柄重验，以及受控遮挡翻转。Edge 最终 `Hidden` 证明失去可跟踪资格，单凭该事件无法确认窗口销毁。[记事本事件摘录](evidence/P5A-PLAN-R1/notepad-events.jsonl) · [Edge 事件摘录](evidence/P5A-PLAN-R1/edge-events.jsonl)
- 独立执行 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p5a-r1/build --filter 'FullyQualifiedName~ExternalWindowTests' --logger 'trx;LogFilePrefix=p5a-r1' --results-directory artifacts/p5a-r1`，Core 外部窗口专项 24/24 通过。PowerShell 驱动解析无错误。本轮只审查已有实机原始日志，未重跑实机和全量测试。[独立 TRX](evidence/P5A-PLAN-R1/core-external-window.trx)
- 原实机轮使用单屏 144 DPI，自建覆盖窗可复验遮挡。跨屏、混合 DPI、真实应用间动态遮挡仍无实测证据。

## F-01 外框更新基线与快速移动判定（高，未关闭）

[探针](../../tests/DesktopPet.DiagnosticProbe/WindowPerchScenario.cs#L180)把最新外框与选中时的固定外框比较。窗口移走后静止仍会重复记 `update`，移回初始位置反而不记。Edge 日志中相同外框 `[1709,930,2491,1421]` 重复 11 次。驱动只要求 `update` 至少两条，重复上报即可满足快速移动检查。

整改条件：以上一次有效观测为基线，选中和重获时重新建立基线。补证移动后静止、再次移回初始位置、快速移动结束时记录正确外框。M-14 对照实际读取到的可见外框和不同位置，明确区分 `SetWindowPos` 连续位移与真实鼠标快速拖动，并补齐后者的实机记录。

## F-02 已确认销毁的句柄仍可自动续接（中，未关闭）

[跟踪器](../../src/DesktopPet.Core/Windows/PerchTargetTracker.cs#L127)在 `WindowGone` 后仍允许同数值 HWND、同 PID 和同类名的合格快照直接恢复。浏览器等同进程同窗口类的场景下，这些字段无法证明已经销毁的窗口恢复了。现有测试覆盖身份字段不同的复用，两个实机重开案例取得的是新数值 HWND。

整改条件：确认销毁后，同数值句柄只能经明确的新目标选择重新取得。补自动反例覆盖同 HWND/PID/类名复用，同时保留最小化后原窗口重验和新目标选择的正例。说明纯轮询无法识别完全发生在两次采样之间且身份字段相同的复用。

## 交付复跑

把正式 M-14 入口置于可保留的验证位置，并在 TESTING 写明命令；现入口位于未跟踪的 `.temp`。保留足以对照的窗口事件和检查结果。归档可复核证据时省去临时浏览器配置和无关窗口标题。本报告的事件摘录只保留时间、事件、句柄、外框、原因及遮挡比例。

## 监理复核 R2（2026-11-18，Asia/Shanghai）

**F-01 关闭，F-02 部分通过，新立 F-03。P5A-PLAN 保持待验收。** 复核对象为 HEAD `f59e7e2` 加当前未提交修改。[文件摘要](evidence/P5A-PLAN-R2/versions.json)

- **F-01 通过。** 探针在选择和重获时重置外框基线，后续仅在外框改变时推进和上报。Core 自动用例覆盖原反例。开发实机原始清单显示两轮静止均为 `extraUpdates=0`，回到初始位置有更新，连续程序位移后末条事件与 DWM 实测一致。另以鼠标事件注入驱动实际标题栏拖动，记事本和 Edge 分别收到 1 条和 4 条更新，末条均等于实测且不同于拖动前。Edge 遮挡比例从 0.333 回到 1。[记事本清单](evidence/P5A-PLAN-R2/notepad-checklist.txt) · [Edge 清单](evidence/P5A-PLAN-R2/edge-checklist.txt)
- **F-02 部分通过，未关闭。** Core 确实会拒绝 `WindowGone` 后同 HWND/PID/类名的 `TryReacquire`，并保留最小化还原的正例。但 [探针](../../tests/DesktopPet.DiagnosticProbe/WindowPerchScenario.cs#L239)在拒绝后的同一次轮询，仍会对同一标题筛选结果自动 `Reset → Select`。此处没有独立的新选择输入，旧筛选条件可直接使同值句柄重新绑定。两份实机日志只覆盖新句柄重开。整改时在拒绝后等待独立的显式选择信号（重新启动探针并明确指定新目标也可），或取消自动回退；交探针级确定性补证，验证同值拒绝后持续轮询不重绑，显式新选择才成功，最小化还原仍可自动重获。[记事本事件](evidence/P5A-PLAN-R2/notepad-events.jsonl) · [Edge 事件](evidence/P5A-PLAN-R2/edge-events.jsonl)
- **F-03 正式驱动重开分支在归属校验前修改窗口（高，阻断正式驱动实机复跑与本任务结案）。** [驱动](../../tools/Invoke-M14WindowPerch.ps1#L578)用默认标题找重开的记事本，在校验宿主 PID/启动时间之前先 `SetWindowText`。其他记事本若在测试过程中打开，也可能命中；之后即使校验中止，窗口已经被修改。请先确认本轮启动的目标归属，再对它改标题、置前、移动或发送输入。默认标题和进程启动时间不足以单独证明归属；无法确认则退出该测试。加一条外部候选出现或归属未能确认时零写入的对照。重开检查必须已找到且验证本轮新窗口，并核对探针事件句柄；现条件在没找到新窗口时也能通过。
- **实机记录校正。** 两轮各 16 项 PASS，探针出口码均为 0，均为单屏且探针记录主屏 96 DPI。最大化外框分别为 1920×1080 和 1920×1032；它们是窗口外框，本轮没有独立量取显示器分辨率。请开发同步修正整改记录及 TESTING 的证据路径（事件日志位于 `window-perch/` 子目录）。跨屏、混合 DPI、真实用户窗口动态遮挡仍为未测。[记事本摘要](evidence/P5A-PLAN-R2/notepad-summary.json) · [Edge 摘要](evidence/P5A-PLAN-R2/edge-summary.json)
- **独立回归。** 使用 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p5a-r2/build --logger 'trx;LogFilePrefix=p5a-r2' --results-directory artifacts/p5a-r2`。Core 524/524；App 458/511，53 项失败，多处表现为测试预期 `idle` 或 `curious`、实际进入 `sleep`。这是夜间窗内跑出的全量结果，尚未用旧基线独立归因，也不扩大本轮 P5A 整改范围。驱动 PowerShell 解析通过。本轮核对原日志，未在桌面重跑。[Core TRX](evidence/P5A-PLAN-R2/core.trx) · [App TRX](evidence/P5A-PLAN-R2/app.trx)

## 监理复核 R3（2026-11-18，Asia/Shanghai）

**F-02 关闭，F-03 部分通过，P5A-PLAN 保持待验收。** F-01 关闭结论不变。复核对象为 HEAD `f59e7e2` 加当前未提交的探针、策略与 M-14 驱动。[文件摘要](evidence/P5A-PLAN-R3/versions.json)

- **F-02 通过。** [探针轮询](../../tests/DesktopPet.DiagnosticProbe/WindowPerchScenario.cs#L232)直接使用新增策略；已知销毁或复用后的同值候选写 `reacquire-denied` 后返回，原 `Reset → Select` 回退已删除。共享策略用例覆盖持续 25 次轮询、显式选择与最小化还原。实机两轮也分别记录最小化后原句柄重验、关闭后新句柄重开；同值复用的证据来自开发侧确定性测试。两次轮询之间且所有可见身份字段相同的未观测复用仍为已记录限制。[记事本事件](evidence/P5A-PLAN-R3/notepad-events.jsonl) · [Edge 事件](evidence/P5A-PLAN-R3/edge-events.jsonl)
- **F-03 时序与检查通过，归属识别仍未通过。** 重开现在先读候选归属，再写入，且必须发现候选并核对事件句柄才能 PASS。记事本原始清单也确有同进程诱饵被跳过且标题不变。但 [重开候选校验](../../tools/Invoke-M14WindowPerch.ps1#L608)仍依据通用标题匹配、PID 非驱动自身及进程启动时间；[Start-Target](../../tools/Invoke-M14WindowPerch.ps1#L216)没有把本次启动的身份交给窗口识别。若用户在驱动启动后另开一个独立的记事本进程，该窗可同时满足三个条件。枚举先拿到它时，驱动会认领并改标题，随后可能移动、关闭它。首次打开分支也沿用启动时间判定。本轮同进程诱饵会被明确的 PID 排除项拦下，未覆盖这个跨进程对照。
- **F-03 收尾条件。** 初开和重开都用与本次启动关联的可核验身份才允许操作。可在每次启动前生成唯一测试标记，例如记事本打开本轮独有的临时测试文件，Edge 使用本轮独有页面标记或独立 profile 归属；缺少标记或归属不明确时结束该项。补一个驱动启动之后、不同 PID、标题仍匹配旧规则的无关测试候选，验证它零写入；有确切归属的目标仍能通过。允许用确定性对照验证归属决策与写入拦截，保留现有 M-14 行为证据。
- **实际核验。** 读取最新两轮原始清单、事件和结果，记事本 17/17、Edge 16/16，均为单屏 96 DPI，探针退出码均为 0。Core 529/529 是开发自测，本轮沿用现有证据，未重新执行测试或操作桌面。[记事本清单](evidence/P5A-PLAN-R3/notepad-checklist.txt) · [Edge 清单](evidence/P5A-PLAN-R3/edge-checklist.txt)

## 监理复核 R4（2026-11-18，Asia/Shanghai）

**F-03 记事本路径通过，Edge 归属校验仍待收尾。P5A-PLAN 保持待验收。** F-01、F-02 保持关闭。对象为 HEAD `f59e7e2` 加当前未提交的 M-14 驱动。[文件摘要](evidence/P5A-PLAN-R4/versions.json)

- **记事本通过。** 驱动初开和重开均打开本轮标记文件，只接纳标题匹配且宿主为新记事本进程的候选。开发实机记录显示，不同 PID 的诱饵在驱动启动后出现，符合旧标题规则，测试期间标题和外框保持原值，最终仍存活。探针从 `0x14055C` 正常切换到重开的 `0x40065E`。修正抓取点后，鼠标注入实际拖动产生 4 条更新，最终外框与实测一致。[清单](evidence/P5A-PLAN-R4/notepad-checklist.txt) · [事件](evidence/P5A-PLAN-R4/notepad-events.jsonl)
- **Edge 行为实机通过，归属身份还未与本轮绑定。** [启动分支](../../tools/Invoke-M14WindowPerch.ps1#L217)为每轮启动独立 profile，但初开和重开只凭固定页面标题 `P5AM14Browser`、非驱动 PID 和启动时间判定归属。profile 路径只用于启动和清理。若另一轮在预检快照之后先创建了同名测试窗口，两轮候选可同时满足现有检查，驱动可能操作并关闭另一轮窗口。当前实跑证明单轮下行为正常，尚未覆盖这条归属边界。只需让 Edge 页面及两处发现共用每轮独有标记，或在操作之前验证宿主确属本轮 profile。补同标题其他轮窗口被跳过、真正本轮目标通过的对照即可，其中标记方案的对照可用同前缀但不同轮标记。沿用记事本、Core、现有 M-14 行为证据。[Edge 清单](evidence/P5A-PLAN-R4/edge-checklist.txt) · [Edge 事件](evidence/P5A-PLAN-R4/edge-events.jsonl)
- **原始记录校正。** 记事本 17/17、Edge 16/16，探针退出码均为 0。两份最新结果与转录均为单屏 144 DPI（150%）；R3 的旧轮为 96 DPI。两组各自是单屏记录，跨屏和混合 DPI 仍未测。本轮做了脚本语法检查、代码与既有原始证据核对，未重跑程序或操作桌面。[记事本摘要](evidence/P5A-PLAN-R4/notepad-summary.json) · [Edge 摘要](evidence/P5A-PLAN-R4/edge-summary.json)

## 监理复核 R5（2026-11-18，Asia/Shanghai）

**F-03 关闭，F-01～F-03 全部关闭，P5A-PLAN 接收并标记 `done`。** 对象为 HEAD `f59e7e2` 加当前未提交的 M-14 驱动。探针轮询及选择策略文件摘要与 R4 一致。[文件摘要](evidence/P5A-PLAN-R5/versions.json)

- Edge 页面标题、探针配置和初开/重开的发现条件共用本轮标记。两处发现均在操作前核验宿主命令行的本轮 profile 路径，带结束引号的路径检查挡住 `edge-profile-other` 前缀碰撞。
- 核对 `run-20261118-092951-edge` 原始清单、转录、事件和结果。17/17 PASS，探针退出 0；原窗口 `0xF41374` 最小化还原后仍以原句柄重验，关闭重开后跟踪 `0x3210FA`；不同 profile 的旧标题窗口 `0x2014F0` 在对照检查时仍存活，标题及外框不变，事件流中无其目标事件。标题栏拖动后的末条外框与实测一致。记事本沿用 R4 的 17/17 证据。[Edge 清单](evidence/P5A-PLAN-R5/edge-checklist.txt) · [Edge 事件](evidence/P5A-PLAN-R5/edge-events.jsonl) · [跨轮对照摘要](evidence/P5A-PLAN-R5/edge-summary.json)
- 最新记事本和 Edge 两轮原始结果均为单屏 **144 DPI（150%）**。开发整改记录里的 96 DPI 是早期轮次值。多屏、混合 DPI、真实用户窗口动态遮挡仍未测。驱动目录和标记精确到秒，同一秒启动同一目标的两次驱动会共享目录，正式 M-14 复跑按单轮运行。对照覆盖不同 profile 的窗口同时存在。
- 本轮执行静态语法检查与原始证据交叉核对，未重跑程序或操作桌面。Core 529/529 沿用开发自测，R2 全量 App 夜间失败记录保留。结论限于只读探针和 M-14，后续正式栖息与攀爬按各自任务验收。
