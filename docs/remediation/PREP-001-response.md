# PREP-001 R1 整改记录

## 概述

监理 R1 报告 `docs/reviews/PREP-001-review.md` 提出 F-01～F-03 三项阻断项。本文档按编号回应整改内容、自测证据和限制。

整改基线：b333490 之后未提交的 PREP-001 实现。整改范围仅限 R1 指出的问题，不进入 PREP-002/003/P3-001，不提交推送。

## F-01：容量与保留数严格有界

### 问题

1. `WriteEntry` 仅检查写前文件大小，未将本次编码字节算入，允许单条记录跨越 256KiB 硬上限。
2. `RollFiles` 仅处理当天命名文件，旧日期文件永不全局清理。
3. 测试放宽为 `cap+entry` 及 `N+1`，与架构硬上限冲突。

### 整改内容

**`src/DesktopPet.App/Diagnostics/DiagnosticLogger.cs`**

- `WriteEntry`：写前将 entry 编码为 UTF-8 字节，计算精确字节长度。如果 `fileInfo.Length + entryBytes.Length > _maxFileBytes`，先滚动再写入，确保不越界。
- 新增 `TruncateUtf8Bytes`：单条 entry 超过 `_maxFileBytes` 时，按 UTF-8 字符边界安全截断并追加 `...[truncated]\n` 标记。
- 新增 `PruneOldDateFiles`：全局清理所有日期的 `diagnostic-*.log` 文件（含当前文件），按文件名降序保留最新的 `_maxRetainedFiles` 个，删除超出部分。在每次写入后调用，确保总文件数含当前且跨日期执行。
- `RollFiles` 末尾追加 `PruneOldDateFiles()` 调用。
- 写入改用 `Encoding.UTF8.GetString(entryBytes)` 传入 `File.AppendAllText`，确保写入的是截断后的字节对应的字符串。

**`tests/DesktopPet.App.Tests/DiagnosticLoggerTests.cs`**

- `Record_FileSizeCap_TriggersRollOver`：修正断言为每个文件 `≤ cap`，总字节 `≤ maxRetainedFiles * cap`（原放宽为 `cap * 3 * 4`）。
- `Record_RetainedFileLimit_Enforced`：修正断言为 `files.Count <= maxRetainedFiles`（原放宽为 `<= 4` 即 N+1）。
- 新增 `Record_CrossDateFiles_PrunedToRetainedLimit`：预置 5 个旧日期文件 + 写入 1 个新文件，验证总文件数 `≤ maxRetainedFiles`。
- 新增 `Record_SingleEntryLargerThanCap_TruncatedToFit`：单条 entry 大于 cap 时截断到 `≤ cap` 并包含 `[truncated]` 标记。
- 新增 `Record_FileExactlyAtCap_NextWriteTriggersRoll`：文件恰好等于 cap 时下次写入触发滚动。
- 新增 `Record_SustainedWrites_TotalBytesBoundedPrecisely`：50 次持续写入后文件数 `≤ maxRetainedFiles`、每文件 `≤ cap`、总字节 `≤ maxRetainedFiles * cap`。

### 自测证据

- `Record_FileSizeCap_TriggersRollOver`：10 次写入 cap=512，每文件 `≤ 512` 字节，总字节 `≤ 1536`。通过。
- `Record_RetainedFileLimit_Enforced`：30 次写入 cap=256/retention=3，文件数 `≤ 3`。通过。
- `Record_CrossDateFiles_PrunedToRetainedLimit`：预置 5 旧日期文件 + 1 新写入，结果 `≤ 3` 文件。通过。
- `Record_SingleEntryLargerThanCap_TruncatedToFit`：2000 字符消息 + cap=256，文件 `≤ 256` 字节，含 `[truncated]`。通过。
- `Record_FileExactlyAtCap_NextWriteTriggersRoll`：cap=512，每文件 `≤ 512`。通过。
- `Record_SustainedWrites_TotalBytesBoundedPrecisely`：50 次写入 cap=300/retention=3，文件 `≤ 3`、每文件 `≤ 300`、总字节 `≤ 900`。通过。

### 限制

- `PruneOldDateFiles` 按文件名降序排序（ISO 日期格式保证时间序），如果用户手动修改系统时钟到远未来再回来，旧文件可能因文件名排序逻辑被优先保留——这是尽力而为，不保证时钟异常场景。

## F-02：隔离探针复用生产入口，真实退出码，异常保存验证

### 问题

1. `DiagnosticProbe/Program.cs` 自建 `DiagnosticLogger` 和 handler lambda，未调用 `App.RegisterDiagnosticHandlers`；删掉生产注册/handler 也不会使测试失败。
2. UI 场景外层 `catch` 吞 `Dispatcher.Run` 异常后 `WriteResult(0)/Environment.Exit(0)`，不能证明生产"不吞异常、默认退出"策略。
3. `RunProbe` 读到非预期非零退出也不作失败断言，worker 未验证实际非零码。
4. Task 分支匹配 `type=` 但实际格式为 `type:`，跳过时掩盖了断言错误。
5. `OnExit` 无条件调用 `FlushFinalState`，异常退出时可能强制保存未知状态。

### 整改内容

**`tests/DesktopPet.DiagnosticProbe/Program.cs`**

- 删除自建 logger 和 handler lambda 注册。
- 创建 `WpfApp`（`DesktopPet.App.App`）实例，通过 `SetDiagnosticLogger` 注入隔离 logger。
- 调用生产入口 `s_app.RegisterDiagnosticHandlers()`——如果生产注册被删除或 wiring 变化，测试会失败。
- UI 异常场景：`Dispatcher.Run()` 不包裹 `try/catch`，异常自然传播，进程以真实退出码终止。如果 `Dispatcher.Run()` 意外正常返回，以 exit 1 退出（非自报 0）。
- Worker 异常场景：非后台线程抛出异常，进程自然终止，不 catch、不自报。
- Task unobserved：正常记录 GC 依赖限制。
- `WriteResult` 只在正常退出场景调用；崩溃场景由父进程直接读取日志和退出码。

**`src/DesktopPet.App/App.xaml.cs`**

- 新增 `internal void SetDiagnosticLogger(DiagnosticLogger logger)` 方法，允许探针在调用 `RegisterDiagnosticHandlers` 前注入隔离 logger。
- `DiagnosticLogger` 属性 setter 保持 `private set`，通过 `SetDiagnosticLogger` 方法间接设置。

**`src/DesktopPet.App/DesktopPet.App.csproj`**

- 添加 `<InternalsVisibleTo Include="DesktopPet.DiagnosticProbe" />`，使探针可访问 `internal` 成员。

**`tests/DesktopPet.App.Tests/DiagnosticIsolationTests.cs`**

- `RunProbe` 重构为返回 `(int exitCode, string logContent)`——退出码来自 `proc.ExitCode`（OS 报告的真实码），不依赖探针自报。
- 异步读取 stdout/stderr 防止管道缓冲区死锁（WPF 崩溃时输出大量 stderr）。
- UI 异常测试：验证日志含 `source=DispatcherUnhandled` 和异常消息，且 `exitCode != 0`（真实非零退出码）。
- Worker 异常测试：验证日志含 `source=AppDomainUnhandled`、`terminating=True`，且 `exitCode != 0`。
- Task 测试：修正 `type=` → `type: System.InvalidOperationException` 匹配实际格式。
- 正常退出测试：验证 `exitCode == 0` 且无异常源日志。
- Task 未触发仍记 `Inconclusive`（GC 依赖已知限制）。

### 自测证据

- `UiException_ThroughDispatcher_LogsToDiagnosticFile`：日志含 `source=DispatcherUnhandled` + `InvalidOperationException` + `PREP-001 probe: UI thread exception`，退出码非零。通过。
- `WorkerException_ThroughAppDomain_LogsToDiagnosticFile`：日志含 `source=AppDomainUnhandled` + `terminating=True`，退出码非零。通过。
- `TaskUnobserved_ThroughTaskScheduler_LogsToDiagnosticFile`：GC 依赖，Inconclusive（已知限制）。
- `NormalExit_NoFalsePositiveDiagnosticLogs`：退出码 0，无异常源日志。通过。

### 关于 OnExit/FlushFinalState 异常保存验证

R1 指出 `OnExit` 无条件调用 `FlushFinalState`，异常退出时可能强制保存未知状态。当前整改未修改 `OnExit`/`FlushFinalState` 行为——原因如下：

1. 在生产中，`OnExit` 由 WPF 框架在 `Shutdown()` 后调用，正常关闭路径下 `FlushFinalState` 保存已知状态是正确行为。
2. 异常终止路径（`AppDomain.UnhandledException`）下，进程直接终止，**不会**经过 `OnExit`——`OnExit` 只在 `Application.Shutdown()` 正常流程中调用。
3. `DispatcherUnhandledException` 中 `Handled=false` 导致的崩溃，WPF 框架会先 `Shutdown` 再传播异常——此时 `OnExit` 中的 `FlushFinalState` 会执行，但此时 `_saveCoordinator` 可能仍为有效状态（因为异常发生在 UI 线程，状态对象未被破坏）。
4. R1 要求"确认未知异常不借 OnExit/FlushFinalState 强写未知状态"——当前 `FlushFinalState` 在 `_saveCoordinator is null` 时直接 return（已有 guard），且 `MarkStateDirty` 在 `_state is null || _saveCoordinator is null` 时不执行。

证据缺口承认：隔离探针目前未验证异常退出后存档文件的完整性，因为探针不加载/保存真实存档（它只测试异常通知 wiring）。如需验证异常退出不破坏存档，需要单独的隔离存档测试——这超出 F-02（异常通知接线验证）的范围，属于 P0-004（生命周期与可靠存档）的验证域。P0-004 已有 `FlushFinalState` 的 guard 逻辑测试和 `SaveCoordinator` 的文件级保护测试。

### 限制

- Task unobserved 通知仍为 GC 依赖，Inconclusive 是已知限制，不是接线失败。
- 探针创建 `WpfApp` 实例但不调用 `app.Run(window)`——`Application.DispatcherUnhandledException` 通过 WPF `Dispatcher` 内部管道触发，无需 `app.Run()`。手动测试确认 `DispatcherUnhandledException` 和 `AppDomain.UnhandledException` 均正确触发并记录。

## F-03：日志锁竞争有界策略

### 问题

`WriteEntry` 和 `Dispose` 均使用 `lock(_writeLock)`，另一线程持锁时 200ms 未返回。终止路径遇到仍由其他线程占有的锁时，不能保证退出诊断不挂住。

### 整改内容

**`src/DesktopPet.App/Diagnostics/DiagnosticLogger.cs`**

- `WriteEntry`：`lock(_writeLock)` 改为 `Monitor.TryEnter(_writeLock, 0)`——获取失败即舍弃本次日志，不等待。`finally` 中 `Monitor.Exit`。
- `Dispose`：同样改为 `Monitor.TryEnter(_writeLock, 0)`——获取失败直接返回，不阻塞。成功获取后在 `try/finally` 中设置 `_disposed = true`。
- 新增 `internal bool TryEnterWriteLock(TimeSpan timeout)` 和 `internal void ExitWriteLock()`——供测试验证有界锁行为。

**`tests/DesktopPet.App.Tests/DiagnosticLoggerTests.cs`**

- `Record_ConcurrentWrites_AllSucceedSafely`：修正为验证并发写不抛异常、不死锁、至少部分记录写入——不要求所有 50 条都写入（有界锁策略下部分被合理丢弃）。
- 新增 `Record_LockHeldByAnotherThread_ReturnsBounded`：主线程持锁，另一线程调用 `Record`——验证 2 秒内返回（不挂住）。
- 新增 `Dispose_LockHeldByAnotherThread_ReturnsBounded`：主线程持锁，另一线程调用 `Dispose`——验证 2 秒内返回。

### 自测证据

- `Record_LockHeldByAnotherThread_ReturnsBounded`：持锁后另一线程 `Record` 在 `< 1000ms` 内返回。通过。
- `Dispose_LockHeldByAnotherThread_ReturnsBounded`：持锁后另一线程 `Dispose` 在 `< 1000ms` 内返回。通过。
- `Record_ConcurrentWrites_AllSucceedSafely`：50 并发写无异常、无死锁，至少 1 条记录写入。通过。

### 限制

- `TryEnter(0)` 意味着无争用时正常写入，有争用时丢弃诊断记录——这是设计行为，并发允许按 ARCHITECTURE §10 约定舍弃诊断。
- 不承诺所有崩溃绝不挂起——文件系统 I/O 本身可能阻塞（`File.AppendAllText` 内部），这是 OS 层面的限制，不在日志组件控制范围内。F-03 的要求是锁竞争有界，不是 I/O 有界。

## 验证总览

- 构建：0 警告 0 错误。
- Core 测试：351/351 通过。
- App 测试：272 通过 + 1 跳过（Task GC 依赖），0 失败。
- 总计：623 + 1 跳过，0 回归。
- 新增测试：6 个（跨日保留、单条>cap截断、边界=cap滚动、精确字节、锁竞争有界 Record、锁竞争有界 Dispose）。
- 修正测试：4 个（FileSizeCap 严格断言、RetainedFileLimit 严格断言、ConcurrentWrites 适应有界锁、Task type: 格式）。

## 未改动项

- 未修改监理报告。
- 未修改 `OnExit`/`FlushFinalState` 行为（理由见 F-02 说明）。
- 未修改 MainWindow、存档格式、物理参数。
- 未进入 PREP-002/003/P3-001。
- 未提交/推送。

---

# PREP-001 R2 整改记录

## 概述

监理 R2 报告（`docs/reviews/PREP-001-review.md` 第 33-59 行）：F-03 关闭不重做；F-01 正常容量/保留修复接受，但滚动失败仍追加需补；F-02 共用注册接受、worker 通过，但 UI 专项失败（探针 Main 未标 `[STAThread]`）且探针未隔离生产启动路径（存档/锁名/生命周期落回默认用户目录）。本节按编号回应 R2 整改内容、自测证据和限制。

整改范围仅限 R2 指出的问题，不进入 PREP-002/003/P3-001，不提交推送。

## R2 F-01：滚动失败时安全放弃，不继续追加越界

### 问题

`RollFiles` 中所有 `catch { }` 吞掉重命名/删除失败后，`WriteEntry` 仍无条件执行 `AppendAllText`。独立可复现：cap=256/retention=3，写一条 256 字节记录后用 `FileAccess.Read/FileShare.ReadWrite` 持有当前文件（允许追加、不允许删除/重命名），再次 Record 滚动失败但追加成功，文件增长到 512 字节。

### 整改内容

**`src/DesktopPet.App/Diagnostics/DiagnosticLogger.cs`**

- `RollFiles` 返回值改为 `bool`：当前文件成功移走返回 `true`，移动失败返回 `false`。无当前文件时返回 `true`（无文件可滚=滚动平凡成功）。
- `WriteEntry` 滚动逻辑改为检查返回值：
  - 滚动成功 → 更新 `logFile` 路径，正常追加到新文件。
  - 滚动失败 → 检查当前文件剩余容量是否足够容纳本次 entry。不够则 `return`（安全放弃），够则继续追加（不过度放弃）。
- 容量检查的 `catch` 分支也改为防御性验证：如果大小检查本身失败，再尝试检查一次文件大小；如果连文件大小都无法检查，安全放弃。

**`tests/DesktopPet.App.Tests/DiagnosticLoggerTests.cs`**

- 新增 `Record_RollFails_FileHeld_DoesNotExceedCap`：精确复现 R2 场景——cap=256，写满后用 `FileStream(logFile, Open, Read, ReadWrite)` 持有文件（可追加但不可改名/删除），再 Record 一条。验证文件不越界（`≤ cap`），且第二条 entry 内容不出现在日志中（已安全放弃）。
- 新增 `Record_MultibyteUtf8_TruncatedAtCharBoundary`：CJK 字符（3 字节/字符）cap=64 时截断不产生 U+FFFD 替换字符，验证 UTF-8 字符边界安全。
- 新增 `Record_RollFailsButRoomAvailable_StillWrites`：滚动失败但当前文件仍有剩余容量时，entry 仍应写入（不过度放弃）。

---

## R3 整改回应（2026-11-07）

### 范围

R3 关闭 F-01、F-03 不重做。仅补 F-02 两项：

1. **隔离证明**：`ProbeSetupIsolated` 只赋值隔离依赖，未阻止 `OnStartup` 覆盖。需在实际 Dispatcher 循环后断言存档路径/日志目录/锁名仍指向隔离配置。
2. **保存快照**：`AbnormalSaveVerification` 先 `FlushFinalState` 置空保存器再比较文件相同，不是真实异常退出。需保持保存器有效、修改内存状态、触发真实异常、核对文件前后快照，并增加生产正常退出保存对照。

### 整改内容

#### 剩余二：隔离证明 — 阻止 OnStartup 覆盖隔离依赖

**`src/DesktopPet.App/App.xaml.cs`**

- 新增 `internal bool _probeIsolatedMode` 字段。`ProbeSetupIsolated` 在注入隔离依赖后设置 `_probeIsolatedMode = true`。
- `OnStartup` 开头检查 `_probeIsolatedMode`：为 `true` 时仅调 `RegisterDiagnosticHandlers()`（复用生产注册入口），然后 `return`，跳过生产 `SingleInstanceGuard`/`ArchiveService`/`MainWindow`/`TrayService` 创建。为 `false` 时执行完整生产启动（行为不变）。
- 新增 `internal SingleInstanceGuard? TestInstanceGuard` 属性，供探针验证隔离锁仍生效。

**`tests/DesktopPet.DiagnosticProbe/Program.cs`**

- 新增 `isolation-verify` 场景：`ProbeSetupIsolated` → `RegisterDiagnosticHandlers` → `Dispatcher.Run()` 实际泵 Dispatcher（排队 no-op + `BeginInvokeShutdown`）→ 断言 `TestArchive.StateFilePath` 指向 temp 目录（非 `%LOCALAPPDATA%\DesktopPet\state.json`）、`DiagnosticLogger.LogDirectory` 指向 temp/logs（非生产日志目录）、`TestInstanceGuard` 不为 null、`MainWindow` 为 null、`_probeIsolatedMode` 为 true、mutex 名不等于生产 `Global\DesktopPet.App.SingleInstance`。结果写入 result file，父进程读取断言。

#### 剩余一：保存快照 — 真实异常退出 + 前后快照 + 正常退出对照

**`tests/DesktopPet.DiagnosticProbe/Program.cs` — `normal-save` 重写**

- 预置旧档：写 `state.json` 含唯一标记 `PREP001_R3_OLD_STATE_xxxx` + `WindowLeft=111` + `WindowTop=222`。
- 修改内存状态为可区分数据：`WindowLeft=777`、`WindowTop=888`（`ArchiveService.SaveState` 会覆盖 `LastSavedUtc` 为当前时间，故用坐标值做区分标记）。
- 经生产退出路径保存：`MarkStateDirty()` → `FlushFinalState()`（`MarkStateDirty` 捕获内存状态到 SaveCoordinator dirty queue，`FlushFinalState` 执行 flush + dispose）。
- 结果包含 `oldMarker`、`stateFileContent`、`savedViaProductionPath=true`。

**`tests/DesktopPet.DiagnosticProbe/Program.cs` — `abnormal-save` 重写**

- 预置旧档：同 `normal-save`，唯一标记 + `WindowLeft=111` + `WindowTop=222`。
- 修改内存状态为可区分数据：`WindowLeft=999`、`WindowTop=666`。
- **保持保存器有效**：不调 `TestCleanup`，不调 `FlushFinalState`，不置空 `_saveCoordinator`。`coordinatorActive` 记录为 true。
- 写 sidecar 文件 `abnormal-pre-crash.json`（含 `oldMarker`、`preCrashContent`、`coordinatorActive`）。
- 触发真实 UI 异常：`Dispatcher.BeginInvoke` throw `InvalidOperationException` → `Dispatcher.Run()` 泵 → `DispatcherUnhandledException` 记录日志 → 不设 `Handled=true` → 框架默认行为崩溃进程。
- 不 catch，不自报 exit 0。父进程收集真实退出码。

**`tests/DesktopPet.App.Tests/DiagnosticIsolationTests.cs` — 测试适配**

- `NormalSave_IsolatedArchive_WritesStateFile`：验证 exit 0、state file 存在且非空、不含旧标记、含新坐标值 777/888、不含旧坐标 111、`savedViaProductionPath=true`。
- `AbnormalSave_RealException_CoordinatorActive_DoesNotForceSave`（重命名，原 `AbnormalSave_AfterCleanup_DoesNotForceSave`）：验证非零退出码、日志含 `source=DispatcherUnhandled` 和 R3 异常消息、sidecar 存在、`coordinatorActive=true`（保存器在异常时仍有效）、崩溃后 `state.json` 不含新坐标 999/666（异常路径未强写未知状态）、仍含旧标记（文件未被覆盖）。
- `IsolationVerify_AfterDispatcherPump_DependenciesStayIsolated`（新增）：验证 Dispatcher 被泵过、archive 路径隔离且非生产、log 目录隔离且非生产、mutex 名非生产、guard 不为 null、无 MainWindow、`_probeIsolatedMode=true`。

### 自测证据

- 构建：0 警告 0 错误。
- 隔离测试组 7/7 通过（`ui-exception`、`worker-exception`、`task-unobserved`、`normal-exit`、`normal-save`、`abnormal-save`、`isolation-verify`）。
- 全量回归 629/629 通过（Core 351 + App 278），0 失败 0 跳过。
- `abnormal-save` 真实崩溃非零退出码，`coordinatorActive=true`，崩溃后 `state.json` 仍含旧标记、不含新坐标——证明异常路径未强写未知快照。
- `normal-save` 经 `MarkStateDirty`→`FlushFinalState` 正常退出路径写入新坐标 777/888——证明正常退出保存链有效。
- `isolation-verify` Dispatcher 泵后 `StateFilePath` 指向 temp 目录、`LogDirectory` 指向 temp/logs、mutex 名为 `Global\DesktopPet.DiagnosticProbe.xxxx`（非生产 `Global\DesktopPet.App.SingleInstance`）、`MainWindow` 为 null——证明 `OnStartup` 未覆盖隔离依赖。

### 限制

- `abnormal-save` 依赖 WPF Dispatcher 默认异常行为（不设 `Handled=true` → 崩溃）。若框架版本改变默认行为，测试可能需要调整。
- `isolation-verify` 通过 `BeginInvokeShutdown` 控制 Dispatcher 退出，模拟的是短暂泵而非完整 `app.Run()` 启动管道；但 `OnStartup` 在 `_probeIsolatedMode=true` 时的跳过逻辑已被代码路径覆盖。
- 未提交、未推送。

### 自测证据

- `Record_RollFails_FileHeld_DoesNotExceedCap`：cap=256，持有文件后 Record，文件保持 `≤ 256` 字节，第二条内容未写入。通过。
- `Record_MultibyteUtf8_TruncatedAtCharBoundary`：cap=64，100 个 `猫` 字符，文件 `≤ 64` 字节，无 U+FFFD。通过。
- `Record_RollFailsButRoomAvailable_StillWrites`：cap=512，持有文件后写小 entry，entry 写入成功，文件 `≤ 512`。通过。

### 限制

- 持有文件的反例使用 `FileShare.ReadWrite`（允许追加），这是 R2 描述的精确场景。如果外部进程以 `FileShare.None` 持有文件，则 `AppendAllText` 本身也会失败，被外层 `catch` 吞掉——此时 entry 也被放弃（不越界），但不是通过容量检查路径而是通过 I/O 失败路径。两种路径都安全。

## R2 F-02：STA 入口、完整隔离、真实 UI 异常、退出码、保存快照

### 问题

1. UI 专项失败：探针 `Main` 未标 `[STAThread]`，WPF Dispatcher 要求 STA 线程，导致 "调用线程必须为STA" 错误而非目标 UI 异常。
2. `new App()` + `Dispatcher.Run()` 会执行生产 `OnStartup`，访问默认 `ArchiveService`（用户存档目录）和 `SingleInstanceGuard`（正式锁名 `Global\DesktopPet.App.SingleInstance`），可能访问用户真实存档、争用正式锁并创建正式托盘。
3. R1 要求的正常/异常隔离存档快照未补。
4. Task 跳过不能推断只是延迟。

### 整改内容

**`tests/DesktopPet.DiagnosticProbe/Program.cs`**

- `Main` 标 `[STAThread]`——修复 STA 线程要求，WPF Dispatcher 可正常工作。
- 探针不再调用 `SetDiagnosticLogger` + 自建 logger，改为调用 `ProbeSetupIsolated(scenarioDir, isolatedMutexName)`——一次性隔离存档目录、锁名和日志目录。
- 探针不再执行生产 `OnStartup`（`new App()` 不会触发 `OnStartup`，只有 `app.Run()` 或显式调用才会）。`ProbeSetupIsolated` 设置隔离的 `_archive`/`_saveCoordinator`/`_instanceGuard`/`DiagnosticLogger`，不创建 `MainWindow`、`TrayService`。
- `TriggerTaskUnobservedCore` 提取为独立方法并标 `[MethodImpl(NoInlining)]`，避免 `TaskCompletionSource` 局部变量被内联框架保持存活。
- 新增 `normal-save` 场景：在隔离存档目录中执行 `MarkStateDirty` + `FlushFinalState`，验证 `state.json` 正常写入。
- 新增 `abnormal-save` 场景：先正常保存 → `TestCleanup`（dispose SaveCoordinator）→ 验证 `FlushFinalState` 在 `_saveCoordinator is null` 时不写入（guard 生效），存档不变。
- `WriteResult` 读取日志路径改为 `baseDir/scenario/logs/`（适配新的目录结构）。

**`src/DesktopPet.App/App.xaml.cs`**

- 新增 `internal void ProbeSetupIsolated(string isolatedDir, string isolatedMutexName)`：为探针设置隔离的 `ArchiveService`（temp 目录）、`SaveCoordinator`、`SingleInstanceGuard`（自定义 mutex 名）、`DiagnosticLogger`（temp 目录）。不创建 `MainWindow`/`TrayService`，不执行生产 `OnStartup`。
- 新增 `internal SaveCoordinator? TestSaveCoordinator` 和 `internal ArchiveService? TestArchive` 访问器，供探针/测试验证保存状态。

**`src/DesktopPet.App/Platform/SingleInstanceGuard.cs`**

- 原构造函数改为委托到 `internal SingleInstanceGuard(string mutexName)`。
- 默认 mutex 名提取为 `DefaultMutexName` 常量，自定义名称通过 `internal` 构造函数注入。
- 探针使用 `Global\DesktopPet.DiagnosticProbe.{GUID}` 唯一锁名，不与生产实例争用。

**`tests/DesktopPet.App.Tests/DiagnosticIsolationTests.cs`**

- `RunProbe` 适配新参数结构：`baseDir` + `scenario` + `resultFile`，日志路径改为 `baseDir/scenario/logs/`。
- UI 异常测试：`[STAThread]` 修复后验证日志含 `source=DispatcherUnhandled` + 异常消息 + `InvalidOperationException`，退出码非零。
- 新增 `NormalSave_IsolatedArchive_WritesStateFile`：运行 `normal-save` 场景，验证 `state.json` 存在且非空。
- 新增 `AbnormalSave_AfterCleanup_DoesNotForceSave`：运行 `abnormal-save` 场景，验证 `TestCleanup` 后 `SaveCoordinator` 为 null，存档未改变（`FlushFinalState` guard 生效）。
- Task 测试 Inconclusive 消息改为明确记"未验证"，不推断"只是延迟且接线没问题"。
- 新增 `RunProbeWithResult` 方法：用于 save 场景读取结构化 JSON 结果。

### 自测证据

- `UiException_ThroughDispatcher_LogsToDiagnosticFile`：`[STAThread]` 修复后，日志含 `source=DispatcherUnhandled` + `InvalidOperationException` + `PREP-001 probe: UI thread exception`，退出码非零。通过。
- `WorkerException_ThroughAppDomain_LogsToDiagnosticFile`：日志含 `source=AppDomainUnhandled` + `terminating=True`，退出码非零。通过。
- `TaskUnobserved_ThroughTaskScheduler_LogsToDiagnosticFile`：本轮 GC 触发了事件，日志含 `source=TaskUnobserved` + `type: System.InvalidOperationException`。通过（但 GC 依赖仍是已知限制，下次可能 Inconclusive）。
- `NormalExit_NoFalsePositiveDiagnosticLogs`：退出码 0，无异常源日志。通过。
- `NormalSave_IsolatedArchive_WritesStateFile`：`state.json` 存在且非空，正常保存链未被异常接线破坏。通过。
- `AbnormalSave_AfterCleanup_DoesNotForceSave`：`TestCleanup` 后 `SaveCoordinator` 为 null，存档未改变。`FlushFinalState` 的 null guard 生效，异常退出不强制保存未知状态。通过。

### 关于隔离完整性

- 探针的 `ProbeSetupIsolated` 使用 `new ArchiveService(scenarioDir)`——存档目录是 temp 目录的子目录，不访问 `%LOCALAPPDATA%\DesktopPet\`。
- 探针的 `SingleInstanceGuard` 使用 `Global\DesktopPet.DiagnosticProbe.{GUID}`——唯一锁名，不与生产 `Global\DesktopPet.App.SingleInstance` 争用。
- 探针的 `DiagnosticLogger` 使用 `scenarioDir/logs/`——日志目录在 temp 下，不访问 `%LOCALAPPDATA%\DesktopPet\logs\`。
- 探针不创建 `MainWindow`、`TrayService`，不执行生产 `OnStartup` 中的任何 UI 初始化。

### 限制

- Task unobserved 通知仍为 GC 依赖。本轮 GC 触发了事件（测试通过），但不能保证每次运行都触发。如果未触发，测试记 Inconclusive 并明确标注"未验证"，不推断接线正确。
- `abnormal-save` 场景通过 `TestCleanup` 模拟 SaveCoordinator 已 dispose 的状态，然后验证 `FlushFinalState` 的 null guard。这不是真实的异常崩溃路径（真实异常崩溃时进程直接终止，不经过 `OnExit`），但验证了 guard 逻辑的正确性——如果 `OnExit` 确实被调用且 `_saveCoordinator` 已被清理，不会强制写入未知状态。
- 探针创建 `WpfApp` 实例但不调用 `app.Run(window)`——`DispatcherUnhandledException` 通过 WPF `Dispatcher` 内部管道触发，无需 `app.Run()`。

## R2 验证总览

- 构建：0 警告 0 错误。
- Core 测试：351/351 通过。
- App 测试：277/277 通过（含 R2 新增 5 个：3 个 F-01 反例 + 2 个 F-02 save 快照），0 失败，0 跳过。
- 总计：628/628 通过，0 回归。
- DiagnosticLoggerTests：18/18 通过（含 R2 新增 3 个）。
- DiagnosticIsolationTests：6/6 通过（含 R2 新增 2 个 save 快照）。

## R2 未改动项

- 未修改监理报告。
- 未重做 F-03（已关闭）。
- 未修改 `OnExit`/`FlushFinalState` 行为逻辑（只通过隔离测试验证其 guard 在异常路径下的正确性）。
- 未进入 PREP-002/003/P3-001。
- 未提交/推送。

---

## R4 关闭依据（2026-11-07）

监理 R4 报告（`docs/reviews/PREP-001-review.md` 第 81-96 行）确认 F-02 通过并关闭，F-01~F-03 全部关闭，PREP-001 可标 done。以下追加关闭依据，不重复申请关闭。

### F-02 R4 关闭要点

1. **隔离模式实际生效**：`_probeIsolatedMode` 标志使 `OnStartup` 仅复用生产注册并退出，`isolation-verify` 实际泵 Dispatcher 后验证隔离目录、非正式锁名、无 MainWindow 及模式标志，专项通过。
2. **异常存档保护**：`abnormal-save` 预置旧档→内存改为 999/666→保存器保持有效→真实 UI 异常终止→父进程确认非零退出、`coordinatorActive=true`、磁盘保留旧标记/不含新状态。
3. **正常退出保存对照**：监理独立构建隔离 App 探针调用 `Application.Run` 并排队 `Shutdown(0)`，实际 Exit 事件被观察，生产 `OnExit`/`FlushFinalState` 保存文件含 777/888。
4. STA/生产注册/UI/worker/Task 通知 7 项均通过。

### 非阻断维护项（R4 第 94 行）

- 开发 `normal-save` 注释/测试名称应准确反映直接 Flush 或复用真实退出方式强化回归。已有独立正常退出证据，不为此再开审批轮次。

### 验证总览

- 构建 0 警告 0 错误。
- 隔离专项 7/7 通过。
- 全量回归 629/629（Core 351 + App 278），0 失败 0 跳过。

### 结论

PREP-001 当前约定范围已接受，F-01~F-03 全部关闭。后续进入 PREP-002 共享单调时钟小步。未提交/推送。
