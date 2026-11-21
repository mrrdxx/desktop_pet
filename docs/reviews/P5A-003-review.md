# P5A-003 监理审查 R1

2026-11-19（Asia/Shanghai）。对象为 HEAD `f59e7e2` 加当前未提交的菜单、拾取、隔离探针与 M-16 驱动。[版本摘要](evidence/P5A-003-R1/versions.json)

**暂缓接收，保持 `awaiting-verification`。** F-01、F-02 阻断本任务结案，P5A-001/P5A-002 已接收结论保持。

## 核验结果

- 独立运行 `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p5a-003-r1/build --filter 'FullyQualifiedName~Perch|FullyQualifiedName~FootStabilityTests' --logger 'trx;LogFileName=perch-entry.trx' --results-directory artifacts/p5a-003-r1`，36/36，无跳过。菜单同分发、按键释放、有效选窗、不合格目标拒绝、超时及主动离开等现有用例通过。[专项 TRX](evidence/P5A-003-R1/app-perch-entry.trx)
- 两轮 M-16 原始清单各 **12/12 PASS**，探针均退出 0，记录真实左键点取及拖动。支持正式拾取逻辑对真实目标落位、跟随、遮挡或关闭脱离、主动离开后不回拉。菜单动作通过 `pick-entry/leave-entry → TestOpenContextMenuLifecycle` 分发。本轮读取原日志，未在桌面操作或重跑全量。[记事本清单](evidence/P5A-003-R1/notepad-checklist.txt) · [Edge 清单](evidence/P5A-003-R1/edge-checklist.txt)

## F-01 暂停没有即时结束拾取（中，未关闭）

拾取依赖 `OnUpdateTick`。[暂停](../../src/DesktopPet.App/MainWindow.xaml.cs#L3246)停止更新计时器，未调用 `EndPerchPick`。因此 `UpdatePerchPick` 的暂停兜底无法执行，待选状态跨越暂停保留，短暂停后恢复可能继续处理原拾取。独立反例 `StartPerchPick → PauseAnimation → IsPerchPickActive` 仍为 true，0/1。请在暂停入口同步结束拾取，补托盘或应用级暂停路径以及恢复后不续接旧拾取的验证；保留正常选窗与边沿触发回归。[反例差异](evidence/P5A-003-R1/reviewer-pause.diff) · [反例 TRX](evidence/P5A-003-R1/reviewer-pause.trx)

## F-02 正式菜单端到端证据与文档行为不一致（中，未关闭）

M-16 未打开和选择真实原生菜单，尚未覆盖新增菜单显示及命令选中链；本任务新增的正式入口需要一个短操作记录。补一次自建测试窗口场景，通过真实右键选择 Perch on Window，真实点击目标，再通过真实菜单选择 Leave Perch 并记录原位解绑。已有两轮选窗和跟随记录可沿用。

同时同步实际行为及覆盖，避免以下误读。

- M-16 原始清单每轮为 12 条，未包含文档所列最小化/恢复、无效点击、超时实机步骤。把自动测试和已执行实机步骤据实标注。
- ARCHITECTURE §9 与 TESTING M-16 写无效点击保持拾取，当前代码与单测则在点中自身或不合格目标时结束拾取，分别为 `cancelled-self/rejected-ineligible`，落位失败为 `rejected-landing`。该小步原验收要求无效点击不误选，未规定一定保持拾取；接受当前结束模式时直接校正文档即可。若开发选择保留模式则同步实现及验证，不能只改报告。

多屏与混合 DPI 仍未测，已验收的栖息行为不重开。

## 监理复核 R2（2026-11-19，Asia/Shanghai）

**F-01、F-02 关闭，P5A-003 接收并标记 `done`。** 对象为 HEAD `f59e7e2` 加当前未提交的暂停清理、菜单遮挡处理与 M-16 驱动。[版本摘要](evidence/P5A-003-R2/versions.json)

- **F-01 通过。** 暂停入口在停止更新前同步取消拾取及释放等待标记。独立复跑 R1 原反例转绿；新用例还验证恢复不续接旧拾取、恢复后的新选择仍有效。
- **F-02 通过。** 真实菜单补证轮 `run-20261119-192641-notepad` 原始清单 12/12，探针退出 0。驱动以真实右键触发生产原生菜单，检测 `#32768` 后按菜单项索引注入方向键和 Enter。转录记录四次 Perch on Window 和一次 Leave Perch，随后真实左键点取目标、落位和 `detached(user-left)` 原位解绑均有相应记录。无效点击与超时的文档已按结束拾取统一，单测与实机步骤归属已校正。Edge 实机沿用测试分发入口轮。[原生菜单转录摘录](evidence/P5A-003-R2/real-menu-transcript.txt) · [记事本清单](evidence/P5A-003-R2/notepad-checklist.txt) · [事件](evidence/P5A-003-R2/notepad-events.jsonl)
- 菜单打开时挂起稳态遮挡采样，目标状态重验仍执行；关菜单后恢复遮挡检查。独立通过 `OnTick_ContextMenuOpen_SteadyOcclusionSuspended_CloseResumes`，覆盖菜单开时不脱离、关闭后实际遮挡恢复脱离。已有窗口跟随和遮挡结论继续有效。
- **独立验证。** 当前仓库测试副本加入 R1 原暂停反例，使用独立 MSBuild 测试编译输入运行：`dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p5a-003-r2/build -p:CustomAfterMicrosoftCommonTargets=<独立副本>/review.targets --filter 'FullyQualifiedName~Perch|FullyQualifiedName~FootStabilityTests|FullyQualifiedName~PauseActionSwitchTests' --logger 'trx;LogFileName=perch-entry-r2.trx' --results-directory artifacts/p5a-003-r2`，43/43，无跳过。驱动语法解析通过。全量 Core 548/App 546 为开发证据，本轮未重跑全量或操作桌面。[TRX](evidence/P5A-003-R2/app-perch-entry.trx) · [原反例差异](evidence/P5A-003-R1/reviewer-pause.diff)
- 真实菜单轮环境为单屏 1920×1080、96 DPI。前两轮为单屏 144 DPI，跨屏与混合 DPI 仍未测。前台锁拒绝打开菜单的失败轮保留，成功轮只证明所记录前台条件下菜单正常工作。正式主动靠近、攀爬留待后续任务。
