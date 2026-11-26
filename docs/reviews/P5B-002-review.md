# P5B-002 审查与验证记录

## R1：2026-11-22 M-18 正式入口实机确认

- 用户请求：“这一步验证你来做”。本轮专门执行M-18，不接管产品实现，不改产品或测试代码。
- 对象：HEAD `0aa994d` 加当前工作区P5B-001整改与P5B-002正式入口；执行前重新构建当前DiagnosticProbe（含Core/App依赖），`--artifacts-path artifacts/m18-supervisor-20261122 -warnaserror`，0警告0错误。源码摘要见 [SHA256](evidence/P5B-002-R1/source-sha256.json)，运行后核对未变。
- 执行：`tools/Invoke-P5B002EntryPreview.ps1 -DurationSec 600 -ProbePath artifacts/m18-supervisor-20261122/bin/DesktopPet.DiagnosticProbe/debug/DesktopPet.DiagnosticProbe.exe`，独立STA驱动、独立WinForms目标夹具，不操作正式存档。
- 本轮运行目录：`.temp/p5b-002/m18/run-20261122-173402-fixture/`，**18/18 ALL PASS，驱动exit=0，探针exitCode=0**。本轮不是转述开发历史152310轮；采用已核对的既有驱动执行。

### 实际覆盖

1. 本轮目标启动PID27800、句柄0x308B0，探针PID28244、宠物句柄0x108E0；归属核对通过。
2. 真实宠物右键→生产TrackPopupMenuEx→UIA只读定位「Climb on Window」→键盘选择；真实左键点击目标后进入started，事件targetHwnd与夹具一致。命令日志没有pick-entry/menu-entry替代入口。
3. 路线20个climb-step进度递增，顶部脚点[690,160]，随后栖息跟随至[790,210]；「Perch on Window」真实菜单平行回归保持selected/perchActive且未触发攀爬。
4. 点击Shell_TrayWnd拒绝且零位移；超距715px拒绝且零位移；自身实体按压取消；真实15秒超时；开菜单、暂停/恢复、拾取中真实拖动均取消且不续跑。
5. 攀爬中真实拖动产生climb-drag-preempted，之后移动旧目标无回拉；探针正常完成测试退出；外部诱饵窗口标题/矩形/文件及无窗口进程保持到断言，清理范围检查通过。

### 口径与限制

- **M-18 本轮实机验证通过。** 此记录仅确认本轮验证覆盖；尚未据此完成P5B-002整体代码及自动测试交付审查，TASKS保持awaiting-verification，不自动签done。
- 第10项清单标题仍写cancelled-self，实际输入优先触发实体按压，真实终态为cancelled；两者对用户都是取消拾取且不攀爬，原始detail保留，不伪称执行到测试缝的cancelled-self分支。
- 第13项是探针pause/resume命令经生产暂停函数验证取消，不声称实际点击了Pause菜单；第16项是探针quit/Environment.Exit，不声称本轮走了生产保存退出链。
- 第5项驱动noJump比较落位事件与后续稳态；本轮不以该字段额外证明所有动画或每帧路径连续性，路线与交接机制已有P5B-001基线。
- Shell拒绝用例自身触发系统搜索面板，驱动按前台标题Esc关闭后继续；未关闭用户应用或文件。单屏1920×1080@96DPI；跨屏、混合DPI、Notepad/Edge新轮未测。
- 结束后查询本轮PID27800/28244/27672均已不存在，屏幕/鼠标交还；原有修改保留，未提交/推送。开发方专项22项及全量563+582报告本轮未重跑，不冒充监理实跑结果。

### 稳定证据

[原始清单](evidence/P5B-002-R1/m18-checklist.txt)、[驱动实机日志](evidence/P5B-002-R1/driver-transcript.log)、[原始事件](evidence/P5B-002-R1/climb-live-events.jsonl)、[命令日志](evidence/P5B-002-R1/climb-live-commands.txt)、[探针结果](evidence/P5B-002-R1/probe-result.json)。

下一步：监理结合实际代码、专项/全量证据完成P5B-002整体交付审查；没有新变更或具体风险，不重复运行本轮绿色M-18。开发不重做P5B-001，不进入新功能。


## R2：2026-11-22 整体交付审查——一项待整改

- 对象：当前P5B-002菜单/共享拾取分派及测试，源码摘要见 [R2版本](evidence/P5B-002-R2/source-sha256.json)。本轮只读产品代码，反例写在隔离副本，不接管实现。
- 实际运行：正式攀爬入口与栖息入口专项22/22通过；全量Core563/563、App582/582，合计1145/1145，0失败0跳过。TRX均留存于 [R2目录](evidence/P5B-002-R2/)。Core专项筛选无匹配，不计为Core入口测试通过；全量Core覆盖正常。
- 真实菜单及M-18保持R1的18/18通过，本轮未重跑屏幕操作。没有重复检查已接收P5B-001的实现范围。

### F-01（中，未关闭）：托盘找回未结束待选目标的攀爬拾取

位置：`MainWindow.RecoverPet`（当前约3350行）、`StartClimbPick/UpdatePerchPick`，调用方`App.OnRecoverRequested`直接遍历调用`RecoverPet`。

触发：通过正式菜单进入攀爬拾取，还没点击目标时，从托盘执行找回。`RecoverPet`解除既有栖息、取消已起攀路线，但没有`EndPerchPick`或等价清理。故找回后`IsClimbPickActive`仍为true，旧拾取意图继续等待点击，之后一次普通点击仍可能启动用户已经打断的选窗操作。宠物自身菜单找回会因打开菜单先行清理而掩盖此问题，不能代替托盘直接入口。

运行证据：隔离反例`Reviewer_RecoverDuringClimbPick_MustEndPendingSelection`，前置`StartClimbPick`成功，调用生产`RecoverPet`后预期拾取结束，实际仍活跃，1/1失败、0跳过。见 [反例TRX](evidence/P5B-002-R2/recover-counterexample_net10.0_20261122173813.trx)、[副本源码](evidence/P5B-002-R2/ClimbEntryTests.reviewer-copy.cs.txt)。

整改目标：找回入口同步结束待选择的攀爬意图，重置其边沿/超时等待选状态；之后释放/点击/推进Tick不启动旧攀爬、不再次移动宠物。补直接生产找回入口的反例及找回后旧点击零起攀/零位移，不能通过先打开宠物菜单来隐式取消。检查共享栖息拾取的找回语义，避免引入平行入口回归；限定当前任务，不以此重开P5A历史验收。

### 当前结论

**P5B-002暂不接收，保持awaiting-verification；F-01仅阻断P5B-002关闭。** 既有菜单、正常拾取及M-18实机链均已通过，修复后无需机械重跑18项实机，仅对受影响的找回/拾取链补证并运行相关回归。代码或环境若有新的实质变化，再评估重测范围。

开发在`docs/remediation/P5B-002-response.md`记录对应R2 F-01的措施、自测与复核请求。监理报告由本助手维护；不进入后续功能、不提交/推送。复核通过后再讨论专用攀爬动作，不提前实现。


## R3：2026-11-22 R2 F-01复核——接收

- 对象：R2之后的`RecoverPet`同步取消待选拾取及两条新增正式测试。读取[开发整改记录](../remediation/P5B-002-response.md)，核对实际代码，版本见[R3摘要](evidence/P5B-002-R3/source-sha256.json)。本轮未改产品实现、未操作屏幕。
- 代码：`RecoverPet`在取消旧栖息/攀爬路线后、任何显示器查询提前返回之前执行`EndPerchPick("cancelled")`；共享拾取活跃、awaitRelease和超时基线一起被清理，不依赖打开宠物菜单。托盘直接调用路径同样生效。
- 复核实跑：保留R2隔离副本中的原始反例，只更新副本生产MainWindow源码，原反例1/1转绿；当前正式`ClimbEntryTests`14项及`PerchEntryTests`10项合计24/24通过，0失败0跳过。TRX见[R3证据目录](evidence/P5B-002-R3/)。
- 两条新测试分别验证攀爬/栖息意图找回后取消、旧释放与点击零起攀/零落位/零位移；攀爬用例额外推进Tick确认不续跑。测试前置断言拾取确实活跃，没有通过先开菜单隐式取消来绕过反例。
- 全量App584/584和warnaserror构建0警告0错误为开发本轮自测；监理本轮限定复跑原反例和入口回归，没有重复全量或Core。R2监理已实跑全量563+582通过；本次新增2项测试且单点同步清理不改变Core或菜单/位移实现。R1真实M-18的18/18证据继续适用于未变更的菜单关键链，不伪称本轮重跑了实机托盘找回。

**结论：R2 F-01通过、关闭；P5B-002窗口攀爬正式入口接收，TASKS可标done。** 当前无未关闭阻断项；R2暂不接收及F-01未关闭保留为历史版本，当前以本R3为准。

限制沿用R1：单屏夹具实机；跨屏、混合DPI、新Notepad/Edge实测未覆盖。本修复的托盘入口同函数自动验证不等于实际点击托盘找回。上述限制不扩大为当前任务的新门槛。无需机械重跑M-18或重复实现；开发只追加R3关闭引用即可。后续动作表现待用户安排，不提交/推送。
