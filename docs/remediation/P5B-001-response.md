# P5B-001 整改记录

## 上缘卡死修复（M-17 实机发现，先行完成）

2026-11-21（Asia/Shanghai）。对象为 M-17 实机验证轮 run-20261121-141126-notepad 暴露的真实产品缺陷（非监理报告）。状态：已修复、回归测试与 M-17 复验通过，`awaiting-verification` 待监理复核。未提交/推送。

## F-01 爬上上缘后卡死：fraction 停 0.29 震荡不前进（已整改）

- 现象（run-20261121-141126-notepad）：起攀 PASS，随后 climb-step 只到 `steps=8` 且 `fracInc=False`，fractions 0.209→0.290 卡住；无 `climb-completed`。事件流显示爬上上缘后 foot.y=160、foot.x 在 296~314 间往返震荡 15 秒以上；目标记事本 bounds=[295,160,1085,655]，上缘中点 x=690。
- 根因：`WindowClimbPolicy.BuildRoute` 每 tick 用当前脚点重建路线，脚点上缘阶段 (313,160) 距左缘 295 更近，`sideX` 被选为左缘，路线被反复拉回侧边再折返，净位移互相抵消，卡死在上缘左侧。
- 修复位置：`src/DesktopPet.Core/Windows/WindowClimbPolicy.cs` `BuildRoute` 新增 `onTopEdge` 水平分支——脚点已在顶部且水平在外框跨度内时，直接返回水平走向落位脚点的路线（w1=w2=(footX, topY)），不再绕回最近侧边。脚点仍在跨度外时保持原“最近侧边→上缘”折线语义不变（目标移动按最新快照重建的语义不受影响）。
- 回归测试：`tests/DesktopPet.Core.Tests/WindowClimbTests.cs` 新增 3 例——`Route_OnTopEdgeInsideSpan_GoesHorizontalStraightToLanding`（跨度内直行）、`Route_OnTopEdgeOutsideSpan_StillTurnsToNearestSide`（跨度外仍走侧边）、`Route_RebuiltEachTickOnTopEdge_AdvancesMonotonicallyToLanding`（逐 tick 重建模拟调度循环，断言 x 单调不减收敛到上缘中点 690）。
- 自测证据：
  - 全量构建 0 警告 0 错误；
  - Core **563/563**（560 + 新增 3）、App **556/556** 全绿；
  - M-17 复验轮 run-20261121-141812-notepad **15/15 ALL PASS**，探针 exitCode=0：③ 路线推进 `steps=8 fracInc=True fractions=[0.171…0.339]`、④ 顶部交接 `foot=[690,160] expected=[690,160] dx=0 dy=0 perch=True noJump=True`、⑤~⑮ 跟随/拖动脱离/三类拒绝/路线中移动收敛/最小化/遮挡/拖动抢占/暂停/退出全部 PASS。

## 说明

- 第二轮 run-20261121-141036-notepad 的 `start-rejected RejectedNoLanding` 判定为偶发（同环境第三轮起攀成功），本次复验轮未复现，不单独列为修复点；若后续复现再单独分析。
- 复跑命令：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B001ClimbPreview.ps1 -TargetKind notepad -DurationSec 600`。

## R1 整改（监理 [R1 报告](reviews/P5B-001-review.md) F-01～F-04）

2026-11-21。对象为监理 R1 四项阻断项。状态：整改完成、构建与专项/全量测试通过、文档已同步，`awaiting-verification` 待监理 R2 复核。未提交/推送。

### F-01（高）：攀爬位置所有权及找回中断（已整改）

- `MainWindow.xaml.cs` `IsClimbWriteAllowed`（2299-2304）首分支即拒绝 `IsPerchOwned`/`_isSocialActive`/`IsInSharedSocialSession` 的起攀与写入；第二分支攀爬持有时让位拖动/抚摸/暂停/物理/探头/社交。
- `ApplyPerchFoot`（2574-2588）新增 `fromClimb` 参数：`(!fromClimb && IsClimbOwned)` 拒绝一切非攀爬写入，攀爬步进以 `fromClimb: true` 提交不自我阻塞。保留既有 chase paired-commit 与栖息跟随语义。
- `RecoverPet`（3284-3295）调用 `PetWindowClimb.CancelForRecover`（273-280）：找回同步取消路线并锁定向后 Tick 不再推进；之后移动旧目标/推进 Tick 均不回拉。
- 测试：`WindowClimbAppTests.cs` 原样纳入监理 5 反例（`Reviewer_Climb_AlreadyPerched/LocalSocialActive/Recovery/MustNotShortcutPolyline/LongStall`，384-442）并新增 4 项正反例（447-567）：已栖息拒绝起攀不破坏绑定、本地/共享社交占用拒绝起攀、找回取消路线后 Tick/移动零写入、提交与找回后零写入。

### F-02（高）：直线到达判定、积压与时间域（已整改）

- `WindowClimbPolicy.cs` 新增 `PolylineLengthPx`（31）按真实折线求长；`PetWindowClimb.Tick`（167-248）到达判定改基于剩余折线长度，交接前脚点先到达落点再绑定（不提前跳跃）。
- 有限补步：`MaxStepTimeMs=200.0`（21），`stepMs = min(dt, 200)`，dt≤0 不推进；10s 停顿只补 200ms 预算、积压丢弃，回退不产生后续重复消费。
- 时间域统一：App 生产 Tick 与探针起攀均使用同一单调时间源（攀爬控制器在起攀时采用传入时钟起始点，更新与起攀同域）。
- 测试：新增 `Climb_FixedStep_AdvancesAlongPolyline_NoShortcutOnStall` 与 `Climb_ClockRollback_NoAdvanceOnNonPositiveDt` 反例（572-666）；末步→交接前后脚点由 `Climb_FixedStep...` 断言（脚点到达落点后绑定，不把落位后稳定称作交接无跳动）。

### F-03（高）：M-17 驱动清理范围（已整改）

- `tools/Invoke-P5B001ClimbPreview.ps1`：`Find-OwnedTarget`（342-360）只接受本轮启动时记录 PID+句柄归属的对象；`Stop-OwnedOrphanProcess`/`Invoke-CleanupOwnedTarget`（252-303）仅清理归属对象；`otherRunMarkerFiles` 只快照不删；预检仅在有用户 Notepad 窗口时中止不杀。
- 诱饵对照（305-328）：构建 lure 文件/窗口/无窗口进程，断言存活且标题/位置/文件内容不变；finally 只清本轮归属（803-835），其他轮、无窗口外部进程、同进程用户标签页不写不关不杀。
- 不修改真实用户文件；本轮未实际运行实机驱动（属 R2 复核选择）。

### F-04（中）：完整退出/关闭验收缺证据（已整改）

- App 测试新增 `Climb_ExitArchiveRoundTrip_SavesPreClimbPosition_NewWindowNoOldBinding`（678-749）：生产 `ShutdownMainWindowForTest` → 落盘爬行前坐标 → 重载 → 新窗口零写入（不续跑旧路线）。生产退出→FlushFinalState→state.json→加载恢复链由隔离单测覆盖；驱动不宣称该项（探针 quit 走 `Environment.Exit`，不经生产退出链）。
- 新增 `Climb_TargetClosedDuringRoute_DeadSnapshot_CancelsAndStopsWrites`（761-803）：目标真实关闭（`IsWindow=false` 死快照）→ 取消并回可见区、后续 Tick 零写入。
- 驱动固定 `$expectCount=18`，finally 校验条目完整 + 无异常 + 探针成功，提前抛错显式 FAIL（841-848）。
- `docs/TESTING.md` M-17 链接已更正为 `remediation/P5B-001-response.md`。

### R1 整改验证证据

- 构建：`dotnet build DesktopPet.slnx -c Debug` 0 警告 0 错误。
- 专项：`dotnet test --filter "FullyQualifiedName~WindowClimbTests|FullyQualifiedName~WindowClimbAppTests"` —— Core **15/15**、App **23/23**，含 5 个 `Reviewer_Climb_` 反例全过。
- 全量：Core **563/563**、App **569/569**（0 失败 0 跳过）。
- 未变更：M-17 实机驱动未重跑（本轮零实机操作），旧 15/15 证据保留为 R1 前状态；跨屏、混合 DPI 仍未测。

## M-17 首轮复验失败定位与驱动适配（run-20261121-151128-notepad）

2026-11-21。R1 整改后首次实机复验，驱动输出 17/18（⑪~⑭、⑯、⑱ FAIL，其余 PASS），证据 `*.temp\p5b-001\m17\run-20261121-151128-notepad`。逐条定位后均为**驱动脚本自身问题，非产品缺陷**：

- ⑪ 路线中最小化 / ⑫ 遮挡 / ⑬ 拖动 / ⑭ pause 全部 `start failed: start-rejected RejectedBusy`：F-01 修复后「已栖息/社交占用起攀 → `RejectedBusy`」是正确产品行为（`PetWindowClimb.TryStart` 107-110）；⑩ 收敛成功后宠物已 `perchActive`，而原驱动序列沿用 R1 前「栖息中可直接再 start」的产品缺陷行为（F-01 正是封禁该行为），未在起攀前脱离栖息。
- 修复：`Invoke-P5B001ClimbPreview.ps1` 新增 `Ensure-IdleForClimb`（`perchActive` 时真实拖动脱离——复用 ⑥ 的真实鼠标拖动；`climbActive` 时等待路线结束），`Invoke-ClimbStart` 起攀前先调用，使各「路线中」场景真正处于爬行状态。
- ⑯ 诱饵文件存活断言 FALSE（`lureFile=False lureWin=True`，窗口标题/位置未变）：`Set-Content -Encoding UTF8` 写入会追加 CRLF，`Get-Content -Raw` 保留换行，与期望字符串比较恒不等（实证：内容 53 字符 + CRLF = 55 字节）。修复：比较前 `Trim()`。
- 验证：脚本 `Parser.ParseFile` 解析通过；修复后重跑实机待安全环境（需用户静止鼠标、电脑不休眠）。

## M-17 第二轮复验失败定位（run-20261121-155443-notepad）

2026-11-21。R1 整改 + 首轮驱动适配后第二次实机复验：② 起攀 `start-rejected RejectedOccluded` 触发脚本 fatal（4/18），⑯ lure 窗口 rect 漂移。证据 `.temp\p5b-001\m17\run-20261121-155443-notepad\`。逐条定位结果：

- ② `RejectedOccluded`（t≈1230ms，目标 hwnd=0x980510）：**产品拒绝正确，属运行环境干扰，非产品/驱动缺陷**。
  - 产品判定链：`PetWindowClimb.TryStart`（105-122）→ `IsLandingOccluded`（343-352）→ `WindowPerchInterop.SampleLandingOcclusion`（WindowPerchInterop.cs 139-167）：对落点 `[footCenterX±petWidthPx/2] × (BoundsTop+2)` 等距采样，`WindowFromPoint` 命中非目标根窗口即计为遮挡者。
  - 实机取证（本机实时复测，`WindowFromPoint` + 顶层窗口枚举）：运行期间屏幕存在真实**全屏 Chrome 窗口**（0x56116C，pid=33452，标题「【英雄联盟】高光时刻与难忘记忆丨2026全球总决赛」，rect=[-8,-8,1928,1040]），另有 CodeFreeSpace 全屏页面（0x2090E，pid=4036）、「设置」全屏窗口（0x1290ABE，pid=76716）等覆盖屏幕；在落点采样区 (630..750, 162) `WindowFromPoint` 全部命中 Chrome `Chrome_RenderWidgetHostHWND`，`GetAncestor` 根=0x56116C ≠ 目标 → 采样全部判遮挡 → `RejectedOccluded`。此遮挡在窗口存在期间确定可复现（与 run-20261121-141036 偶发 `RejectedNoLanding` 性质不同）。
  - 驱动增强：`Get-LandingOccluder` 按产品相同采样规则实时列出遮挡者标题，② 失败时输出遮挡者清单与环境处理提示；**不修改产品判定，不改断言口径**。
- ⑯ lure 窗口 rect 漂移（快照 [60,60,360,180] → 断言 [−32000,−32000,−31763,−31961]）：**判定为外部窗口管理干扰，未定位代码触发源**。
  - 脚本核查：lure 仅创建/快照/断言/Dispose；全部 `SetWindowPos`/`ShowWindow`/`Raise-Top` 只作用于目标 `$hwnd` 与 ⑫ cover 窗口，**无任何代码路径移动 lure**。[−32000,−32000] 为窗口被最小化后 `GetWindowRect` 的典型返回值（`IsIconic` 为真时）；lure 为无消息泵 WinForms Form，上一轮（151128）同位置 8 秒实证稳定，本轮漂移发生在伴随多个全屏窗口的会话中，疑系统/用户窗口管理操作引起。
  - 驱动增强：⑯ 断言增加 `iconic` 明细输出，区分「窗口存活但被系统/用户最小化」与「被越界清理」；**断言口径不变**（标题/位置/文件内容须原样），是否豁免由监理裁决。
- 验证：脚本 `Parser.ParseFile` 解析通过。重跑前提：用户桌面无覆盖落点与 lure 区域的窗口、静止鼠标、电脑不休眠。

## 复核请求

请监理复核 F-01 上缘卡死修复及 R1 整改（F-01～F-04）是否可关闭；本记录为开发自述，复核结论以监理为准。

## 2026-11-22 用户授权本助手直接整改（R1 F-01～F-04 补完）

授权来源：用户明确要求“这一步，请你自己直接做”。本轮角色为直接实现与自测，不代签独立复核，不修改监理报告及其证据。

基线为远程已备份 `0aa994d`，不是最初 R1 缺陷版；磁盘已有 F-01 所有权/找回、F-02 折线/统一时钟、F-04 保存读档与死快照测试。本轮保留这些修复，补完实际遗漏：

- F-02：`PetWindowClimb.Tick` 对 dt≤0 直接返回且不移动时间基线，防止100→50→100重复消费；到达时先由攀爬持有者提交最终脚点，发 `climb-arrived` 后再释放并交接栖息。新增回退后恢复到旧基线零位移及 arrived/completed 脚点一致回归。
- F-03：驱动默认/唯一目标为本轮独立 WinForms 夹具（`-TargetKind fixture`），有真实消息泵、精确启动PID，不借用记事本共享标签进程。删除孤儿进程强杀与Alt+N不保存路径；清理前重验句柄、精确标题、启动对象PID与存活状态，仅关闭本轮窗口。诱饵进程仍只在存活断言后由其创建者清理。
- F-04：原脚本加入第18项前要求已有18项，完整运行亦会失败；修为17个顺序唯一的检查，加完整性第18项。生产 `Test-M17Complete` 函数同时要求到达结束、无异常、唯一固定顺序、全部PASS。从AST提取真实生产函数验证完整/提前退出/异常/重复编号四类，4/4通过，Parser错误0。
- 准确口径：已有目标关闭用例注入 null 死快照驱动生产取消，并非实际销毁一个真实目标窗口；已有退出保存用例走 `ShutdownMainWindowForTest`/FlushFinalState/真实文件读档，不声称执行了 `Shutdown(0)` 进程终止。原记录中“目标真实关闭”的字面陈述按本段澄清。

本轮自动验证（独立 artifacts 目录，未强杀其他 testhost）：

- `dotnet build DesktopPet.slnx --artifacts-path artifacts/p5b-r1-direct-20261122 --no-restore -warnaserror`：0警告0错误。
- 攀爬专项：Core15/15、App24/24，0失败0跳过。
- 全量：Core563/563 + App570/570 = 1133/1133，0失败0跳过。TRX位于 `artifacts/p5b-r1-direct-20261122/results/`。
- 新实机复验待下段记录，旧15项PASS不替代新版本验证；不提交/推送，任务保持 awaiting-verification。


### 本轮实机驱动排障（保留失败轮）

- run-20261122-044725-fixture：独立夹具进程以Hidden启动，窗口未被枚举，流程在目标确认前停止；诱饵存活检查通过，完整性检查正确FAIL。改为显式可见测试夹具（用户已授权屏幕/鼠标）。
- run-20261122-044813-fixture：17个基础检查中16项通过，真实攀爬中拖动一项失败；完整性汇总正确拒绝整体通过。根因为驱动用200ms心跳位置匹配移动窗口句柄，20px容差不足。探针增加观测字段petHwnd，驱动核对实际所属PID后取实时窗口矩形进行抓取，产品输入与位移判定不改。
- 该轮新时间域首步16ms约7.51px，不再出现11ms约91.95px；5次climb-arrived与climb-completed的首对脚点均[690,160]。最终复验结果另行追加。


### 最终复验（2026-11-22）

`run-20261122-045010-fixture`：18/18 ALL PASS，驱动exit=0、探针exitCode=0。含真实鼠标栖息拖离、攀爬中拖动抢占及松手不回拉、移动目标重建、最小化回可见区、遮挡取消、暂停后不续接；诱饵文件/窗口位置标题/无窗口进程均保持至断言。目标为本轮独立WinForms夹具，不宣称新的Notepad/Edge实测。原失败轮原样保留。

证据稳定副本见 [本轮自测目录](evidence/P5B-001-direct-20261122/)，含清单、原始事件、汇总、结果与源码SHA256；逐对检查climb-arrived→climb-completed脚点，所有交接对位移为0。全量测试是在最终增加探针petHwnd观测字段前完成，之后仅探针/驱动定位适配及注释/文档变动；最终全方案warnaserror构建和完整实机复验均通过。

F-01～F-04直接整改及自测交付完成；因本轮由同一助手直接实现，本段不构成独立复核，不关闭原报告阻断、不将任务标done。未提交/推送，未更改监理报告及其证据。

### R2 复核请求（2026-11-22）

请监理按 [R1 报告](reviews/P5B-001-review.md) F-01～F-04 顺序独立复核本轮整改：修复位置与口径见本段（时钟回退基线、交接前最终脚点提交、独立 WinForms 夹具、清单完整性、实时 HWND 抓取）；自动验证证据见 [自测目录](evidence/P5B-001-direct-20261122/)（warnaserror 构建 0 警告 0 错误、专项 Core15+App24、全量 1133/1133、M-17 `run-20261122-045010-fixture` 18/18 ALL PASS、驱动与探针 exit=0）。开发侧核对发现以下遗留（均无行为影响，未擅自修改）：驱动脚本产 `tools/Invoke-P5B001ClimbPreview.ps1` 第 258 行注释仍写"目标题材（仅 notepad）"与现行 fixture 目标不符；`AltN()` P/Invoke 包装已无调用残留。本记录为开发自述，不代签监理结论；复核结论以监理为准。

## R2 接收依据引用（2026-11-22）

监理 [R2 报告](../reviews/P5B-001-review.md)（64-85 行）按用户指定同一助手复核已接收：F-01～F-04 全部关闭，P5B-001 可标 done，明确"无需开发重复实现或重跑既有绿色验证"；核对证据见 [R2 目录](../reviews/evidence/P5B-001-R2/)（verification.json 与各 TRX），接受实机轮 `run-20261122-045010-fixture` 18/18 ALL PASS（稳定副本见 [自测目录](evidence/P5B-001-direct-20261122/)）。本段仅为登记 R2 接收依据，不重复实现或重测；限制按 R2 原文保留（本轮新实机目标为独立夹具，跨屏、混合 DPI、多宠攀爬、专用素材及正式用户入口不因接收自动完成）。后续开发任务 P5B-002 已登记，由用户另行安排实施。
