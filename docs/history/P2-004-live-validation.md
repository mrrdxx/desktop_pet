# P2-004 第一轮实机验收（2026-11-07）

结论：部分通过，P2-004 保持 awaiting-verification，不作完整手感通过结论。实际执行者为监理，通过 computer-use/cua_repl 操纵系统鼠标；当前代码与 P2-003 R7 接受版本的 App/MainWindow 摘要一致，未改产品代码或参数，未提交/推送。原始日志、观察宿主源码及版本见 [证据](evidence/P2-004-live-100pct/)。

## 环境与观察宿主边界

单显示器1920×1080，工作区(0,0)-(1920,1032)，实际宠物 HWND DPI=96（100%），窗口120×120。引用当前 R7 构建 DLL/资源，创建实际生产 MainWindow，不替换 TestGetCursorPos/TestGetElapsedMs/物理参数或输入事件；位置/物理由生产 DispatcherTimer 推进。仅标题、初始位置(550,300)、ShowInTaskbar 为诊断宿主设置，便于工具发现。

宿主使用普通 WPF Application，不调用正式 App.OnStartup、单实例、托盘或 ArchiveService，**不访问正式存档**；Pause/Resume/Recover 控制按钮调用生产同名方法，是真实 UI 按钮点击，但不等同 Shell 托盘点击。每约33ms记录窗口/活动状态，实际PreviewMouseDown/Up另记日志。各次启动 ms 从0重计，analysis.json 按时间倒退分段，原始文件保留两次会话。

## 实际结果

- 零位移轻放：实际鼠标点击身体后松开，ShouldThrow=false/RawSpeed=0，位置维持(1435,912)。仅证明静止轻放，不把它扩展为已验证所有慢速移动/停住后释放手感。
- 右向甩出：实际身体短拖40px，DOWN39552→UP39573ms；ShouldThrow=true、Vx=2。36个活动采样中有横向反向，最终active=false，停在(1435,912)。
- 左向甩出：实际反向短拖，Vx=-2，进入真实窗口物理运动；通过控制按钮在运动中暂停。
- 暂停/恢复：active=true且paused=true的345个采样跨16253ms，窗口位置唯一为(367,472)，即空中冻结。实际点击 Resume 后继续移动、反弹并停稳。没有把暂停错误记为取消运动。
- 斜向甩出：最后一次真实斜拖记录速度约(1.8727,-0.7022)，最终(1077,912)静止。该次后续追抓操作工具报截图失效，因此只接受甩出/落地日志，不接受追抓结果。
- 有界与收敛：两会话共462个active采样（包含暂停），完整窗口均在上述工作区内；记录到方向反转和落地后active=false。采样结果不证明采样间每个瞬间，也不代替人类对丝滑程度的主观判断。
- 正常退出：实际点击观察宿主 Exit test 按钮，之后两窗口消失，无 Live 进程残留。它不是生产托盘退出/真实存档重启验证；生产隔离退出/恢复沿用 P2-003 R7。

## 未完成及工具/宿主问题

- 飞行中重新抓住身体/头部、抓取点位移一致性未可靠完成。自动操作出现 unknown screenshotId；部分追抓前的短拖本身未触发物理，所以不能把其后点击记为“飞行中抓取通过”。
- 同样40px的快速自动短拖并非每次都触发抛掷；有成功和 ShouldThrow=false 的记录。当前没有细粒度鼠标移动事件日志，不能归因产品缺陷或工具时序，保留为待核实体验现象，不擅自修改参数。
- 工具不允许拖动终点超出当前窗口截图，且会拒绝透明点/位于其他进程上的终点；因此大幅抛掷、慢拖与停住后松手、头部连续转拖动未覆盖完整手感集合。后续需要可靠、可控持续时间的实际输入路径或用户补体验，不以单元测试代签。
- 150%、混合DPI/多屏、真实锁屏/休眠、本轮生产启动脚本/托盘/存档重启未验证；ART既有未验证项保留。
- 第一会话观察宿主的 Recover 反射只找NonPublic方法，未找到实际公开方法，导致宿主退出；仅在 artifacts 中修正反射查找 Public|NonPublic 后重新构建，生产 Recover 实现未改。后续 Recover 按钮正常。不能归因为产品崩溃。
- 最初编译宿主有 WPF/WinForms Button 名称歧义，仅限定宿主Button类型后构建成功；原产品未改。

## 下一步

优先补飞行中身体/头部重新抓取、慢移/停住释放，以及重复甩出可靠性；本轮不调参数、不恢复已取消功能。P2-003代码验收仍有效，P2-004尚未完成，不推进阶段3。桌面启动脚本 tools/Start-DesktopPet.cmd 仍指向旧 affinity-removal 构建，本轮未使用也未修改它；正式交付前应按当前构建更新入口。

---

## 诊断补充（2026-11-07）：短拖未触发抛掷的最小诊断

### 背景

首轮验收中，同样 40px 的自动短拖并非每次都触发抛掷（ShouldThrow=false）。当时没有细粒度采样日志，不能归因产品缺陷或工具时序。

### 改动（仅诊断，不改判定逻辑/参数）

- **`ReleaseSpeedSampler` 新增诊断快照**：`ReleaseDiagInfo` 结构记录 `EvaluateRelease` 被调用时的完整状态——采样列表（X/Y/TimeMs）、运动段索引、段 dt、段 rawSpeed、判定原因（reason 字符串）、配置阈值、是否注入了静止探测样本。
  位置：`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs` — `ReleaseDiagInfo` 结构 + `CaptureDiagnostics` 属性 + `MarkStationaryProbeInjected` 方法 + `FinishResult` 辅助方法。
- **`EvaluateRelease` 各返回路径标记原因**：`invalid_release_time` / `release_before_newest_sample` / `insufficient_samples` / `no_motion_segment_stopped` / `segment_dt_below_min` / `speed_below_throw_threshold` / `throw`。判定逻辑和返回值完全不变，仅附加诊断。
- **`MainWindow` 暴露 `LastReleaseDiag`**：public 只读属性，观察宿主可读取。采样器初始化时 `CaptureDiagnostics = true`。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `LastReleaseDiag` 属性 + `_speedSampler` 初始化。
- **观察宿主 `Program.cs` 追加 `releaseDiag` 字段**：timeline.jsonl 和 state.json 每帧记录诊断快照，含 reason、sampleCount、gapMs、segStart/End、segDt、segRawSpeed、throwThreshold、stopSpeed 和完整 samples 数组。
  位置：`artifacts/p2-004-live/Program.cs`。

### 诊断能力说明

下一轮验收时，短拖未触发抛掷可通过 `releaseDiag.reason` 直接归因：
- `insufficient_samples`：采样不足 2 个（MinSamplesForThrow=2），说明 MouseMove 事件太少——工具时序问题。
- `no_motion_segment_stopped`：末尾采样对速度低于 StopSpeed（0.02 px/ms），说明拖动已停止——工具在释放前停住了。
- `segment_dt_below_min`：运动段时间跨度 < 5ms，速度估算不可靠。
- `speed_below_throw_threshold`：速度低于 ThrowThreshold（0.15 px/ms），说明拖动太慢——可能工具移动速度不均匀。
- `throw`：触发了抛掷，与其他成功记录对比。

`samples` 数组提供完整的物理像素坐标和时间戳，可重建实际拖动轨迹进行对比。

### 验证

- 构建 0 警告 0 错误。
- 全量 App 242/242 + Core 351/351 = 593/593 通过，0 回归。
- 诊断捕获默认启用（`CaptureDiagnostics = true`），但 `FinishResult` 在 `CaptureDiagnostics = false` 时直接返回原结果，零开销。
- **未修改任何判定逻辑、阈值或参数。**

### 限制

- 诊断仅在观察宿主中有效；生产运行时 `LastReleaseDiag` 有值但无消费方。
- `samples` 数组最多保留 SampleWindowMs(100ms) 内的样本，通常 3-5 个。
- 未提交/推送。
- 不标 P2-004 done，不进入阶段 3。

## 第二轮：诊断短拖（2026-11-07，监理实际执行）

独立专项32/32通过。为避免旧DLL，重新构建到artifacts/p2-004-diag/build，并为观察宿主改用本轮DLL；开发更新的宿主编译遇到CS8600（object diagObj可能为null），只在本轮artifacts副本改为object?后构建通过。未修改产品代码或参数。仍使用真实CUA鼠标、生产时钟和窗口，DPI96，非正式App启动链，不访问真实存档。

[原始证据](evidence/P2-004-diag-short-drag/)保留timeline、输入、诊断宿主及摘要。实际短拖正反例：

- 成功：向左40px，reason=throw，sampleCount=2，样本(610,420,33313)→(570,420,33328)，dt=15ms，rawSpeed≈2.6667，钳位Vx=-2。
- 未抛出：反向向右40px，窗口Left由645变685；reason=insufficient_samples，sampleCount=1，仅样本(705,1032,49656)，releaseTime=49672、gap=16ms，无静止探测注入。实际按下/释放输入日志49521→49541（宿主Stopwatch时间，与产品时间零点不同，不能直接相减）。
- 最早一次操作后没有产生新的releaseDiag，不能算任何判定分支的通过证据。

因此诊断链有效，已确定本次未抛出为采样不足，**不是速度低于阈值**。但尚不能归因为工具，原因码仅解释当前判定。生产GetElapsedMs使用Environment.TickCount64，按下/首次Move时间相同导致样本拒绝是待验证假设；当前诊断只保存被接收样本，没有Move入口及被拒样本时间，无法直接证明该假设。

下一步只补入口级时间/位置、RecordSample接受/拒绝原因的有限诊断，然后用同一真实短拖确认。不要仅修改阈值或伪造时间差。慢拖、停住释放和飞行中追抓仍未完成，不标P2-004 done，不进入阶段3。本轮正常点击Exit test后两窗口消失，未提交/推送。

---

## 入口级诊断补充（2026-11-07）：RecordSample 接受/拒绝日志

### 背景

第二轮诊断确认短拖未抛出为 `insufficient_samples`（sampleCount=1），但当前诊断只保存被接收的样本，没有记录 `RecordSample` 入口的拒绝事件和 Move 事件的时间/位置。无法直接证明"按下与首次 Move 同时间戳导致样本被拒绝"这一假设。

### 改动（仅诊断，不改判定逻辑/参数）

- **`ReleaseSpeedSampler` 新增 `SampleAttempt` 结构**：记录每次 `RecordSample` 调用的输入参数（x, y, timeMs, source）、调用前缓冲区状态（newestBeforeTimeMs, countBefore）、接受/拒绝结果（result 字符串）、调用后 count。
  位置：`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs` — `SampleAttempt` 结构 + `AttemptLog` 属性 + `LogAttempt` 辅助方法。
- **`RecordSample` 各路径记录尝试日志**：
  - `accepted`：样本被接受
  - `rejected_invalid_time`：时间戳 NaN/Infinity
  - `rejected_invalid_coord`：坐标 NaN/Infinity
  - `rejected_same_or_older_time`：时间戳 ≤ 最新样本时间（**关键路径**——按下与 Move 同时间戳时走此路径）
  判定逻辑和返回值完全不变，仅附加日志记录。
  位置：`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs` — `RecordSample` 方法。
- **`RecordSample` 新增 `source` 参数**：调用方标记样本来源——`"baseline"`（StartDragging 按下基准）、`"move"`（ProcessCapturedPointerMove 鼠标移动）、`"probe"`（EndDrag 静止探测）。默认 `"move"`，不影响行为。
- **`MainWindow` 暴露 `LastSampleAttempts`**：public 只读属性，返回采样器的 `AttemptLog`。`RecordFootSample` 接受可选 source 参数。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `LastSampleAttempts` 属性 + `RecordFootSample` 签名。
- **观察宿主 `Program.cs` 追加 `sampleAttempts` 字段**：timeline.jsonl 和 state.json 每帧记录完整尝试列表，含 source、x、y、timeMs、newestBeforeMs、countBefore、result、countAfter。
  位置：`artifacts/p2-004-live/Program.cs`。

### 诊断能力说明

下一轮验收时，短拖未触发抛掷可通过 `sampleAttempts` 直接确认：
- 如果 `sampleAttempts` 中有 `source="baseline"` accepted + `source="move"` rejected_same_or_older_time，且两者 `timeMs` 相同 → **证实按下与首次 Move 同时间戳导致 Move 样本被拒绝**，这是 `Environment.TickCount64` 毫秒精度不足的问题，不是产品逻辑缺陷。
- 如果 `sampleAttempts` 中有 `source="move"` accepted 但 `countAfter` 仍为 1 → Move 样本被接受但后续被 prune 或其他原因导致不足。
- 如果 `sampleAttempts` 中没有 `source="move"` 条目 → MouseMove 事件根本未到达采样器（工具时序问题）。

### 验证

- 构建 0 警告 0 错误。
- 全量 App 242/242 + Core 351/351 = 593/593 通过，0 回归。
- `RecordSample` 的 `source` 参数默认 `"move"`，现有测试不传 source 时行为不变。
- `LogAttempt` 在 `CaptureDiagnostics = false` 时直接 return，零开销。
- **未修改任何判定逻辑、阈值或参数。**

### 限制

- 尝试日志上限 64 条（`MaxAttemptLogSize`），长拖动时旧条目被移除。短拖场景足够。
- 尝试日志在 `Reset` 时清空，每次拖动开始时为空。
- 未提交/推送。
- 不标 P2-004 done，不进入阶段 3。

---

## 诊断生命周期补充（2026-11-07）：释放快照冻结

### 背景

入口级诊断的 `LastSampleAttempts` 原先直接返回 `_speedSampler.AttemptLog` 的引用。`EndDrag` 中 `Reset()` 清空 `_attemptLog`，导致松手后观察宿主无法读取本次拖动的采样尝试记录——`sampleAttempts` 在 release 后变为空数组，无法与 `LastReleaseResult`/`LastReleaseDiag` 对应分析。

### 改动（仅诊断，不改判定逻辑/参数）

- **新增 `_lastSampleAttemptsSnapshot` 字段**：`IReadOnlyList<SampleAttempt>`，存储最近一次完成拖动的冻结快照。初始为空数组。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — 字段声明。
- **`EndDrag` 在 `Reset` 前复制快照**：正常释放路径和取消路径均在 `Reset()` 前执行 `_lastSampleAttemptsSnapshot = _speedSampler.AttemptLog.ToList()`，确保快照与 `_lastReleaseResult` 对应。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `EndDrag` 方法两条路径。
- **`LastSampleAttempts` 属性改为条件返回**：拖动中（`_isDragging`）返回实时 `AttemptLog`，松手后返回冻结快照。
  位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `LastSampleAttempts` 属性。

### 快照替换规则

1. **首次拖动前**：`LastSampleAttempts` 返回空列表。
2. **拖动中**：返回 `_speedSampler.AttemptLog`（实时），观察宿主可逐帧看到 baseline/move/probe 条目。
3. **松手后**（EndDrag → Reset）：返回冻结快照，包含本次拖动所有尝试记录（accepted + rejected），与 `LastReleaseResult`/`LastReleaseDiag` 对应。跨 tick 稳定不变。
4. **下一次拖动开始**（StartDragging → IsDragging=true）：返回新拖动的实时 `AttemptLog`；旧快照在下次 EndDrag 时被新快照替换。
5. **取消路径**（capture loss / menu / closing）：同样冻结快照，不含 EvaluateRelease 结果但保留尝试记录。

### 新增测试

`tests/DesktopPet.App.Tests/SampleAttemptSnapshotTests.cs`，6 个测试覆盖完整生命周期：
- `AttemptLog_LiveDuringDrag_ContainsBaselineAndMove`：拖动中实时返回 baseline + move。
- `AttemptLog_FrozenAfterRelease_SurvivesReset`：松手后快照不被 Reset 清空，跨 tick 稳定。
- `AttemptLog_ReplacedOnNextDrag_OldSnapshotGone`：第二次拖动后快照反映新拖动而非旧拖动。
- `AttemptLog_SameTimestampMove_RejectionRecorded`：同时间戳 Move 的 `rejected_same_or_older_time` 被记录并冻结。
- `AttemptLog_CancellationPath_AlsoFreezesSnapshot`：取消路径也冻结快照。
- `AttemptLog_EmptyBeforeFirstDrag`：首次拖动前返回空列表。

### 验证

- 构建 0 警告 0 错误。
- 全量 Core 351 + App 248 = 599/599 通过，0 回归（新增 6 个测试）。
- **未修改任何判定逻辑、阈值或参数。**

### 限制

- 快照为 `List<SampleAttempt>` 副本，最多 64 条（`MaxAttemptLogSize`），短拖场景足够。
- 快照在每次 EndDrag 时替换，不保留多轮历史。
- 未提交/推送。
- 不标 P2-004 done，不进入阶段 3。

### 证据判断指引

监理复测时须根据完整证据判断根因，不可预设结论：
- **同时间戳拒绝**（`rejected_same_or_older_time`）不等于"非产品问题"——时间源精度是产品选择，是否需要改进入而根据频率和影响判断。
- **无 move 条目**不能直接归因工具——可能是 MouseMove 事件未到达采样器，也可能是 WPF 输入管线在快速操作中的时序特性。
- 须结合 `sampleAttempts`（入口级）、`releaseDiag`（判定级）、`input.log`（宿主级时间线）和窗口位移量综合判断。

## 第三轮准备核对：入口日志保留缺口（2026-11-07）

本轮仅静态核对和独立可控采样器复现，未执行新一轮真实鼠标，不声称已确认上一轮实机根因。证据见[入口日志保留](evidence/P2-004-attempt-retention/)。新增RecordSample尝试日志可以记录拒绝原因，但生产EndDrag在EvaluateRelease后立即Reset，Reset清空AttemptLog，而LastSampleAttempts直接返回该活动日志。宿主约33ms采样一次，20ms短拖无法保证采到松手前的日志；松手后稳定看到的可能是空列表，不是本次释放的诊断快照。

独立可控序列：baseline(705,1032,t=100) accepted，move(745,1032,t=100) rejected_same_or_older_time；EvaluateRelease(116)给出insufficient_samples，随后执行与EndDrag相同的Reset，attempts从2变0。仅证明同时间戳路径和日志生命周期，不能证明上一轮真实输入就是这一原因。

下一步：在Reset前把本次尝试列表复制成独立释放快照（与LastReleaseResult/会话标识关联），让下一轮拖动/Reset不修改已发布的快照；用生产共享手势验证松手后、下一tick仍能读到baseline/move及拒绝原因。常规取消/下一次有效释放的替换规则要明确，不能把旧日志误配到新结果；不为保留日志改变采样器历史清理/判定规则或调阈值。

归因规则更正：同时间戳拒绝不等于“非产品问题”，时间源分辨率和App事件适配也属于实现边界；没有move日志也不能直接认定工具时序，可能是生产事件/捕获/过滤路径或日志丢失。先保留完整证据，再决定修复。

## 第四轮：释放快照有效，时间戳碰撞确认（2026-11-07）

监理独立专项38/38通过，真实40px短拖后冻结记录baseline和两个move均为12375ms，move位置变化却均被rejected_same_or_older_time拒绝，最终insufficient_samples。直接原因已确认，详见[审查R1](../reviews/P2-004-review.md)和证据。本轮未修改产品代码，不调参数；以新报告的F-01组织整改，先前根因“未确认”的记录保留为当时证据状态。慢拖/停住松手、飞行追抓仍待验收，P2-004不标done。

---

## F-01 整改：高分辨率单调计时（2026-11-07）

### 问题

`GetElapsedMs()` 使用 `Environment.TickCount64` 差值，精度 ~10-16ms。真实快拖中按下和首次 Move 在同一 TickCount64 值（t=12375），Move 样本被 `rejected_same_or_older_time` 拒绝，位移丢失。

### 修复

将 `GetElapsedMs()` 改为 `Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency`，保留 double 小数毫秒精度。Stopwatch 基于 QPC，分辨率 ~100ns 级别，按下和 Move 即使只隔几十微秒也得到不同 timeMs。

位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `GetElapsedMs()` 方法 + 新增 `using System.Diagnostics;`。

### 时间域一致性

所有时间使用方通过 `TestGetElapsedMs?.Invoke() ?? GetElapsedMs()` 走同一入口：OnUpdateTick、按下、Move、释放、物理 tick、暂停/恢复、AnimationPlayer。改为 Stopwatch 后自动统一，无需逐个修改。暂停补偿基于时间差，不受时间源精度影响。

### 不变项

- 判定逻辑、阈值、参数完全未改。
- `RecordSample` 拒绝条件不变（`timeMs <= newest.TimeMs` 仍拒绝真正同时间戳）。
- 未伪造 epsilon、未放开零 dt、未降低 MinTimeSpan/ThrowThreshold。
- 测试注入 `TestGetElapsedMs` 机制不变。

### 新增回归测试

`tests/DesktopPet.App.Tests/HighResolutionTimingTests.cs`，5 个测试：
- 生产时钟连续调用返回不同值。
- 生产时钟真实快拖产生 ≥2 accepted 样本。
- 注入相同时间戳仍拒绝（语义保留）。
- 注入 0.1ms 差值被接受（小数精度正确传递）。
- 生产时钟暂停/恢复单调递增。

### 验证

- 构建 0 警告 0 错误。
- 全量 Core 351 + App 253 = 604/604 通过，0 回归（新增 5 个测试）。
- 整改记录见 [P2-004-response](../remediation/P2-004-response.md)。
- 未提交/推送，不标 done，待监理复核。

## 第五轮：高分辨率时钟复核（2026-11-07）

[监理R2](../reviews/P2-004-review.md)关闭F-01，独立专项43/43。两次实际40px短拖的baseline/move时间戳已不同，所有Move为accepted、没有采样不足；本次仍未抛出，原因分别为末段同位置Move判停，另有移动段不足5ms，不能记为抛掷成功。保留这组输入供手感判断，不为脚本特殊动作改阈值，不新增无限诊断轮次。完整慢拖/停住释放及飞行追抓待补；P2-004保持awaiting-verification，未提交推送。

## 第六轮：用户实际体验确认（2026-11-07）

用户在本监理对话明确反馈：“5项均完美测试通过”。对应紧邻前一轮提供的五项操作清单：

1. 慢拖约2秒后松手，小熊留在松手位置，不自动下落。
2. 快拖后按住停约半秒再松手，不沿用旧速度飞走。
3. 正常向左/右甩出，方向合理、反弹后最终停稳。
4. 飞行中按住身体，旧运动立即停止，继续拖动抓取点不跳。
5. 飞行中按住头部后移动，旧运动停止，转拖动时抓取点不跳。

执行者为用户，结果是用户报告的真实体验通过，不冒充监理自动操作或新增原始鼠标日志，不从简短反馈推断具体重复次数。启动入口为上一轮监理直接启动的R2构建 artifacts/p2-004-clock-r2/build/bin/DesktopPet.App/debug/DesktopPet.exe，生产正常启动/正式存档；同轮MainWindow源码摘要与R2一致。用户未报告另行切换缩放，本次不新增150%或多屏覆盖声明。

上述五项补齐剩余体验，暂停/恢复沿用首轮实际100%单屏通过和后续相关回归，生产隔离退出/静止恢复沿用P2-003 R7，时间戳碰撞F-01沿用P2-004 R2关闭。监理据合并证据接受当前范围，P2-004标done；这不是对已失败事项的豁免。原诊断失败/工具中止日志保留，不改写为通过。

未验证项保留：新物理系统150%/混合DPI/多屏、真实休眠/锁屏以及ART历史未验证项。桌面启动脚本仍指向旧构建，尚需后续显式交付收尾，不能把本次“直接启动新构建”说成桌面快捷方式已更新。参数不修改，阶段3未启动，未提交/推送。
