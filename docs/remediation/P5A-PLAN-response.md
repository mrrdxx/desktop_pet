# P5A-PLAN 整改记录（监理 R1 → 开发整改）

2026-11-18（Asia/Shanghai）。对象：[监理报告 R1](../reviews/P5A-PLAN-review.md) 的 F-01、F-02 阻断项及交付复跑要求。保留工作区未提交修改，未提交/推送。

> **R2 更正**：R1 段原文把两轮清单记作 21/21，实际各为 **16 项**（当前驱动为记事本 17 项、Edge 16 项，新增项见下文 R2 段）；实测环境为**单屏、探针记录主屏 96 DPI**（此前写的"2560×1440、DPI=144"未经独立量取，最大化外框 1920×1080/1920×1032 只是窗口外框）。事件日志位于各 run 目录的 `window-perch/` 子目录。R1 段的 run 证据路径不变，其余结论（F-01/F-02 Core 整改、两轮 PASS）经 R2 复核确认。

## R2 整改（F-02 收尾、F-03，2026-11-18）

### F-02 移除探针自动重选回退

**根因**：Core 的 `TryReacquire` 确实拒绝 `WindowGone` 后同值句柄，但探针在拒绝后的同一次轮询里仍用原标题筛选结果自动 `Reset → Select`，旧筛选条件可直接使同值句柄重新绑定，无独立新选择输入。

**修复**：
- `tests/DesktopPet.DiagnosticProbe/WindowPerchScenario.cs`：删除该回退分支。`WindowGone/HandleReused` 后同值句柄被拒时，探针记一条 `reacquire-denied` 事件并保持 Cleared；重新绑定只能来自明确的显式新选择（重开探针指定新目标）。最小化/隐藏还原仍自动 reacquire 同句柄。
- 新增 `src/DesktopPet.Core/Windows/WindowPerchSelectionPolicy.cs`（探针轮询选择决策的纯逻辑）及 `PerchTargetTracker.ClearedTargetHwndValue`（清除目标句柄暴露）。
- 确定性补证（`tests/DesktopPet.Core.Tests/ExternalWindowTests.cs` 新增 5 项，Core 529/529）：
  - `WindowGone`/`HandleReused` 后同值句柄（身份字段全部一致）出现在原筛选结果中，**持续轮询 25 次不重绑**（状态恒 Cleared、无 Target）；
  - **显式新选择才成功**：独立的 `Reset + Select` 后重新绑定；
  - 最小化/隐藏还原（窗口未销毁）仍自动重获（正例保留）；`WindowGone` 后新句柄窗口自动重获允许（身份不同）。

### F-03 重开分支先归属校验后写入

**根因**：驱动重开记事本时按默认标题发现候选后，在归属校验（宿主 PID/启动时间）之前先 `SetWindowText`；其他记事本若在测试过程中打开也可能命中并被修改。且重开检查在未找到新窗口时也能通过。

**修复**（`tools/Invoke-M14WindowPerch.ps1` 第 9 步）：
- 归属校验前置：逐候选先 `GetWindowThreadProcessId` 校验宿主进程 StartTime ≥ t0 且**非驱动进程自身**（run-20261118-014959 教训：驱动自建窗口 StartTime 落在 t0±2s 容差内，仅按 StartTime 判定会误过），通过后才允许改标题/置前/移动/输入；归属不明的外部候选只读跳过，全部无法确认则该检查失败。
- 零写入对照（记事本轮）：重开前先显示驱动自建的诱饵窗口（标题"外部候选对照 - Notepad"，匹配发现模式但归属校验必失败），发现循环必须跳过它且零写入；新增检查"外部候选出现时零写入（诱饵对照）"断言诱饵标题不变且确有外部候选被跳过。
- 重开检查收紧：必须已找到并验证本轮新窗口（`owned=True`），且探针事件句柄与其一致；没找到/归属不明时检查失败（旧条件在 hwnd2 为空时也能通过）。

### R2 复跑实机证据（单屏、探针主屏 96 DPI；多屏/混合 DPI 未测）

- 记事本轮 `run-20261118-015936-notepad/`：**17/17 ALL PASS**（含诱饵对照 PASS：`unchanged=True externalSkipped=1`；重开窗口先验证归属后写入；真实鼠标拖动 updateCount=1；探针 exit=0）。
- Edge 轮 `run-20261118-020304-edge/`：**16/16 ALL PASS**（重开窗口先验证归属后写入；真实鼠标拖动 updateCount=3；遮挡/解除、探针 exit=0）。
- 过程轮 `run-20261118-014959-notepad/`：诱饵对照 FAIL 的失败轮，根因即上述"驱动进程 StartTime 在容差内"教训，按当时版本保留；该轮未触碰任何用户窗口（其后用户在遗留的空记事本中打开自己的文件，该窗口未被驱动操作）。

### 未测项（不变）

多屏与混合 DPI；真实用户窗口动态遮挡；纯轮询无法识别两次采样之间且身份字段完全相同的复用。App 全量测试在夜间窗跑出的 53 项 sleep 相关失败按监理意见不扩大本轮范围，未做归因。

### 复核请求（R2）

请监理复核 F-02（回退移除 + 策略确定性补证 + 两轮实机）与 F-03（归属前置 + 零写入对照 + 检查收紧）是否满足 R2 整改条件，并核对上述证据路径更正。任务状态保持 `awaiting-verification`。

## F-03 收尾补证（窗口归属识别与跨进程对照，2026-11-18）

按 R3 结论"F-03 仍须用本轮启动的确切身份排除新进程的无关同类窗口"完成收尾，全部改动在驱动 `tools/Invoke-M14WindowPerch.ps1`，探针与 Core 未改。

### 标记绑定：初开/重开都用与本次启动绑定的标记确认目标

- 每轮在 run 目录内创建本轮独有临时文件 `P5AM14NP-<ts>.txt`，`Start-Process notepad` 直接打开它，窗口标题自然含标记；初开与重开都打开同一标记文件。
- 识别与归属不再使用 `SetWindowText` 改名（已全部删除）：候选须标题含本轮标记，且宿主进程 `ProcessName='Notepad'`、`StartTime >= t0-2s`、pid≠驱动、pid≠诱饵。Win11 记事本启动器进程把窗口交给另一子进程宿主，不能按 `Start-Process` 返回的 pid 精确匹配（`run-20261118-024622` 教训）。
- 归属不明一律只读跳过。新增两道防线：notepad 轮启动时清理历史 run 目录的旧标记文件（防 Win11 记事本"继续上次会话"把旧标记恢复为标签页造成标题漂移，`run-20261118-080353` 教训）；归属确认后 1.5s 内标题脱离本轮标记即中止，不碰窗口。

### 不同 PID 外部候选对照

- 诱饵改为独立 pwsh 子进程创建的 WinForms 窗口（标题`外部候选对照 - Notepad`，符合旧发现规则 `*- Notepad`；pid 不同于驱动，且在驱动启动后出现）。信息落盘 `decoy-window-info.txt` / `decoy-window.ps1`。
- 终局断言"外部候选不被改标题/移动/关闭（不同 PID 诱饵）"：`IsWindow` 存活 + 标题不变 + `GetWindowRect` 基线不变 + `decoyPid != driverPid`。

### 实机补跑证据（单屏、主屏 96 DPI；多屏/混合 DPI 未测）

- 记事本轮 `.temp/p5a-plan/m14/run-20261118-090518-notepad/`：**17/17 ALL PASS**（含：标记绑定 select/reacquire；真实鼠标快速拖动 `updateCount=4`，`lastEvent==measured`，窗口离开注入前位置；不同 PID 诱饵 `titleUnchanged=True rectUnchanged=True alive=True`；探针 exit=0）。
- Edge 轮 `.temp/p5a-plan/m14/run-20261118-090839-edge/`：**16/16 ALL PASS**（共享发现循环改动无回归；真实鼠标拖动、遮挡/解除、重开 reacquire 新句柄、探针 exit=0）。

### 过程教训（按当时版本保留）

- `run-20261118-085006-notepad/`、`run-20261118-085703-notepad/`：真实鼠标拖动项注入完成后窗口零位移（前置校验全过）。根因：Win11 记事本标签页占用标题栏中部，标题栏中心抓取点不是 `HTCAPTION`，LEFTDOWN 不启动窗口拖动。修复：注入前以 `WM_NCHITTEST` 确认抓取点返回 `HTCAPTION`，否则沿标题栏从右向左扫描可拖动点，找不到则安全跳过；修复后 `run-20261118-090518-notepad` 该项 PASS（扫描命中 `grabX=1990`）。
- `run-20261118-080353-notepad/`：旧标记文件被记事本会话恢复为标签页导致标题漂移断链（环境问题），已由历史标记清理 + 标题漂移中止防线关闭。
- `run-20261118-024622-notepad/`：pid 精确匹配过严导致初开发现 abort，已改为 ProcessName+StartTime 归属规则。

### 复核请求（F-03 收尾）

请监理复核 F-03 收尾是否满足 R3 要求：初开/重开均以本轮启动绑定的标记确认目标（不再改标题），归属不明跳过，不同 PID 外部候选零写入/零移动/零关闭对照成立。任务状态保持 `awaiting-verification`。

## F-03 Edge 收尾（R4 意见：旧固定标题两轮并行会互相误选，2026-11-18）

按 R4 意见收尾 Edge：旧识别规则是固定标题 `P5AM14Browser`，初开/重开仅靠它识别，另一轮同时启动时会误选。全部改动在驱动 `tools/Invoke-M14WindowPerch.ps1`，探针与 Core 未改。

### 修复

- **本轮唯一标记**：页面标题改为 `P5AM14Browser-<ts>`（run 目录内 `p5am14-page.html` 的 `<title>`），页面、探针（`targetTitleSubstr`）、驱动（初开/重开发现）共用同一标记；初开与重开打开同一 html，窗口标题自然含本轮标记，两轮并行时互不命中。
- **宿主归属确认**：初开/重开发现循环内、任何写入之前，Edge 宿主除 `StartTime >= t0-2s`、pid≠驱动外，还须命令行含本轮 profile 路径 `--user-data-dir="<runDir>\edge-profile"`（带引号精确匹配，排除其他 profile）；其他轮/其他 profile 的 msedge 窗口即使标题命中也按归属不明只读跳过（`Test-EdgeHostOwned`）。
- **其他轮对照窗口**：驱动启动后由另一个真实 msedge 进程（独立 profile `<runDir>\edge-profile-other`，页面标题仍为旧固定规则 `P5AM14Browser`）创建对照窗口。全程断言它被跳过、零写入：标题不变、`GetWindowRect` 基线不变、`IsWindow` 存活、探针事件流中从未出现其句柄（`neverSelected=True`），同时本轮标记窗口通过全部检查（select/重开 reacquire 均为本轮句柄）。

### 实机补跑证据（单屏、主屏 96 DPI；多屏/混合 DPI 未测）

- Edge 轮 `.temp/p5a-plan/m14/run-20261118-092951-edge/`：**17/17 ALL PASS**（新增项"其他轮窗口被跳过（零写入）本轮窗口被选中"：`otherPid=31708 driverPid=46672 titleUnchanged=True rectUnchanged=True alive=True neverSelected=True`；本轮窗口 select/reacquire 句柄 0xF41374/0x3210FA 均为标记窗口；真实鼠标拖动 `updateCount=4`；探针 exit=0）。
- 记事本轮证据不变（`run-20261118-090518-notepad/` 17/17 ALL PASS）；Core/探针未改，无需重跑。

### 复核请求（F-03 Edge 收尾）

请监理复核 F-03 Edge 收尾是否满足 R4 要求：本轮唯一标记（页面/探针/驱动共用）、宿主 profile 归属确认前置、其他轮旧标题窗口被跳过且本轮窗口被选中的对照成立。任务状态保持 `awaiting-verification`。

## F-01 外框更新基线与快速移动判定

### 根因

`WindowPerchScenario.cs` 原实现把最新外框与选中时的固定外框比较：窗口移走后静止仍会重复上报 `update`，移回初始位置反而不记；驱动只要求 `update`≥2 条，重复上报即可满足快速移动检查。

### 修复位置

- `tests/DesktopPet.DiagnosticProbe/WindowPerchScenario.cs`：`update` 判定改为与上一次已上报外框比较（基线在 select 与 reacquire 时重置，静止不重复上报、移回初始位置有更新）。
- `src/DesktopPet.Core/Windows/PerchUpdateGate.cs`（新增）：Core 纯逻辑门控，含正例/反例单测。
- 驱动 `tools/Invoke-M14WindowPerch.ps1`：快速移动检查改为「updateCount≥1 + 最终事件外框与驱动 DWM 同口径实测一致」；新增「移动后静止不重复上报」「移回初始位置仍有 update」「真实鼠标快速拖动」检查。真实鼠标拖动用 `mouse_event` 注入，注入前做三重前置校验（SetCursorPos 回读 ±2px、WindowFromPoint 命中本窗口、目标前台；非前台时先在自建窗口上做经验证的安全激活点击），任一失守跳过注入并留痕。

### 补证

- 静止不重复：两轮 `extraUpdates=0` PASS。
- 移回初始位置仍有 update：两轮 PASS。
- 快速移动最终外框与实测一致：两轮 `lastEvent == measured` PASS（程序位移 updateCount=7，F-01 去重语义下无重复）。
- 真实鼠标快速拖动：记事本轮 updateCount=1、Edge 轮 updateCount=4，`lastEvent == measured`，且窗口确实离开注入前位置，两轮 PASS。
- 抓取点从 GetWindowRect 顶部 +12 改 +25（DWM 阴影使 +12 落在 HTTOP 不可见边框，按下只触发缩放）。

## F-02 已确认销毁的句柄仍可自动续接

### 根因

`PerchTargetTracker.cs` 在 `WindowGone` 后仍允许同数值 HWND、同 PID、同类名的合格快照直接 reacquire；这些字段不能证明已销毁窗口恢复。

### 修复位置

- `src/DesktopPet.Core/Windows/PerchTargetTracker.cs`：`WindowGone` 后同数值句柄一律拒绝自动恢复，必须 `Reset` 后经明确的新选择（`select(NewSelectionAfterClear)`）才能重新取得；`TryReacquire` 对同值句柄返回拒绝。
- 自动反例单测：同 HWND/PID/类名复用被拒绝；正例保留：最小化/Hidden 还原后原句柄重验（reacquire 同句柄）、新数值句柄 reacquire、销毁后明确新选择。

### 补证（两轮实机）

- 最小化 → cleared(Minimized) → 恢复 → reacquire 同句柄：两轮 PASS。
- 关闭 → cleared 且窗口确认销毁（Edge 先 Hidden 后销毁，驱动以 WindowGone/IsWindow=false 确认）：两轮 PASS。
- 重开 → reacquire 新数值句柄（`sameValue=False`）并重新通过归属校验：两轮 PASS。
- 探针单测覆盖同数值句柄拒绝 + Reset+Select 后重取。

### 限制说明

纯轮询无法识别完全发生在两次采样之间且身份字段（HWND/PID/类名）完全相同的复用；本实现以 200ms 轮询 + 同值句柄拒绝将该风险降到「采样间隙内同值复现」这一不可区分情形。

## 实机证据（单屏 2560×1440、DPI=144/150%，驱动线程 Per-Monitor V2，物理像素）

- 记事本轮 `.temp/p5a-plan/m14/run-20261117-222604-notepad/`：21/21 ALL PASS（含真实鼠标拖动 updateCount=1，lastEvent==measured）。
- Edge 轮 `.temp/p5a-plan/m14/run-20261118-010338-edge/`：21/21 ALL PASS（含真实鼠标拖动 updateCount=4、遮挡 occluded fraction=0.333 coverHit=True、解除 unoccluded fraction=1、探针 exitCode=0）。
- 驱动目标归属校验：目标窗口宿主进程 StartTime ≥ 脚本启动时刻（Win11 记事本启动进程立即退出、窗口由另一宿主进程创建，故不按 Start-Process pid 判定）；启动前断言系统无既有 `p5am14` 标题窗口。全程只操作自建测试窗口。

## 驱动脚本整改过程中的修复（影响检查有效性）

- `Wait-Event` 水位比较 `-ge` 改 `-gt`；reacquire 水位提前到还原动作前采样；unoccluded 水位提前到覆盖窗 `Hide()` 前采样（探针 200ms 轮询在 Hide 后约 430ms 即上报，Hide 后的 Raise-Top 耗时会使水位晚于事件导致漏检——run-20261118-004822-edge 唯一 FAIL 项的根因，修复后 run-20261118-010338-edge PASS）。
- 覆盖窗遮挡改为 TopMost + 显式 `SetWindowPos` 把目标插到覆盖窗正下方，并以 WindowFromPoint 命中覆盖窗作双保险。
- 记事本单实例预检闸门：存在任何 `*- Notepad` 窗口即中止，避免并入用户进程（run-20261117-210809 事故整改）。

## 单元测试

- Core 524/524、App 511/511 通过；构建 0 警告 0 错误（含新增 PerchUpdateGate 与 tracker 反例）。

## 交付复跑

- 正式 M-14 入口移入仓库 `tools/Invoke-M14WindowPerch.ps1`；[TESTING.md](../TESTING.md) M-14 段已写明 pwsh 7 复跑命令（Windows PowerShell 5.1 读 UTF-8 脚本乱码，不可用）。
- 每轮证据目录含 `m14-checklist.txt`、`window-perch/window-perch-events.jsonl`、`window-perch/candidates.jsonl`、`probe-result.json`、`driver-transcript.log`；Edge 独立 user-data-dir 仅存 `.temp`，未入仓。

## 未测项

- 多屏与混合 DPI 未测；遮挡由自建覆盖窗模拟，真实用户窗口动态遮挡未测。
- 记事本轮 `run-20261117-210809` 曾误占用用户窗口（单实例并入），用户自行关闭，无数据损失；该事故已由预检闸门 + 归属校验整改，历史证据按当时版本保留。

## 复核请求

请监理复核 F-01、F-02 是否满足 R1 整改条件，并对两轮实机证据（`run-20261117-222604-notepad`、`run-20261118-010338-edge`）按需独立复跑。任务状态保持 `awaiting-verification`。
