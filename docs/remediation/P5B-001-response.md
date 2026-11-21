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