# P6-001 最小隔离实机入口与操作清单（监理 R4；R5 整改后）

> 状态：入口与接线已就绪（无鼠标构建 + 无窗口 wiring 验证通过）；真实实机由监理与用户约时段执行。
> 本轮未启动实机窗口、未占鼠标/屏幕、未触碰真实统计目录、未提交推送。

## 目的

P6-001（本地统计）F-01～F-04 已全部关闭，剩隔离实机验收。本入口提供**最小隔离实机预览**：

- 独立 run 目录 + 独立实例锁；`settings.json / state.json / stats.json`（含 `.corrupted`/`.tmp` 副本）全部限定该目录，不触碰 `%LOCALAPPDATA%\DesktopPet\`。
- 走生产路径：真实宠物窗口（`CreatePetWindow` + `Show`）、生产 `UsageStats` 初始化（真实 5 秒 DispatcherTimer、真实 `GetLastInputInfo` 空闲采样、真实 `ForegroundAppSampler` 前台采样）、`SessionSwitch`/`PowerModeChanged` 系统事件转发、真实窗口右键菜单分发（"Statistics: On/Off" / "Clear Statistics"）。
- 探针只观测（jsonl）与提示，操作由人工驱动真实菜单与真实前台切换；不用假前台、手工 SampleTick、注入 AppId。

## 无鼠标/无窗口接线检查（R5 整改后，证据）

| 检查 | 结果 |
| --- | --- |
| `dotnet build src/DesktopPet.App/DesktopPet.App.csproj -c Debug --no-dependencies` | 成功，0 警告 0 错误 |
| `dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj -c Debug` | 成功，0 警告 0 错误 |
| `usage-stats-wiring` 无窗口场景（连跑两次，同一 run 目录） | 三断言通过 + session 不覆盖，证据见下 |

wiring 场景断言（无窗口、无鼠标、不创建宠物窗口）：

1. 隔离预存 `settings.UsageStatsEnabled=true` → 生产 `LoadArchivesForIsolatedProbe` → 开关保持 On
   （R5 反例：setup+生产包装后变 false 已闭环）。
2. `StartForIsolatedProbe`（共享 `InitializeActivitySession` → `SubscribeSystemEvents`）后
   `TestSystemEventsSubscribed=true`；`ShutdownMainWindow` 后 cleanup sidecar
   `subscribedAfterCleanup=false`、`serviceNullAfterCleanup=true`、`shutdownCalled=true`。
3. 观测/sidecar/cleanup 文件按 session 编号命名，同一 run 目录第二次运行产出 session-2，与 session-1 并存不覆盖。

可复制命令（wiring，无窗口无鼠标）：

```powershell
dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj -c Debug
$runBase = Join-Path (Resolve-Path ".temp") "p6-001-live\wiring-check"
New-Item -ItemType Directory -Force -Path $runBase | Out-Null
& ".temp\p6-001-live\probe\DesktopPet.DiagnosticProbe.exe" $runBase "usage-stats-wiring" (Join-Path $runBase "result1.json")
# 再次运行同一命令 → session-2 文件
& ".temp\p6-001-live\probe\DesktopPet.DiagnosticProbe.exe" $runBase "usage-stats-wiring" (Join-Path $runBase "result2.json")
```

接线证据（代码路径）：

- 真实菜单项已存在于窗口右键菜单：`MainWindow.xaml.cs` L914-929（`MenuIdStatsToggle=9`、`MenuIdStatsClear=10`，经 `TrackPopupMenuEx` → `HandleContextMenuCommand` L1002-1017 → `App.SetUsageStatsEnabled` / `App.ClearUsageStats`）。
- 生产初始化入口：`App.xaml.cs` `InitializeUsageStats`（L685）→ new `UsageStatsService(saveDirectory)` + `Start()` + 暂停同步 + settings 门控（默认关闭）。新增 internal 探针包装 `InitializeUsageStatsForIsolatedProbe(saveDirectory)`，生产逻辑零改动。
- R5 F-05 新增 internal 窄包装（生产逻辑零改动）：`LoadArchivesForIsolatedProbe()`（生产 `LoadState→LoadSettings→_isPaused` 恢复，不覆盖磁盘）、`TestEnsureProbeRuntimeObjects()`（只补运行对象）、`TestSettings`（只读断言）。
- 服务：`UsageStatsService(string? saveDirectory, bool autoStartTimer = true)`（internal，生产采样器/时钟/Dispatcher）；新增 internal 只读 `IsSuspended` 转发（观测挂起/恢复基线）。
- 系统订阅：live 与 wiring 场景均经 `StartForIsolatedProbe`（共享 `InitializeActivitySession` → `SubscribeSystemEvents`）启动；退出走既有 `ShutdownMainWindow(cleanupSidecarPath)`（Stop+flush stats、退订系统事件、关窗、`Shutdown(0)`）。
- 实测注册：`Program.cs` switch 新增 `"usage-stats-live"` → `UsageStatsLiveVerification`、`"usage-stats-wiring"` → `UsageStatsWiringVerification`。

## 可复制命令

前置：`cd` 到仓库根 `C:\Users\mrrdxx\Desktop\pet`（PowerShell）。

```powershell
# 1) 构建（如需重新构建）
dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj -c Debug

# 2) 创建本轮 run 目录（用实际时间戳，避免覆盖旧证据）
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runBase = Join-Path (Resolve-Path ".temp") "p6-001-live\run-$stamp"
New-Item -ItemType Directory -Force -Path $runBase | Out-Null

# 3) 第一段运行：默认关闭 → 启用 → 前台累积/切换 → 关闭 → 清除
& ".temp\p6-001-live\probe\DesktopPet.DiagnosticProbe.exe" `
  $runBase "usage-stats-live" (Join-Path $runBase "result.json")
```

说明：

- 探针二进制位于 `tests/DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe`（构建产物；建议复制到 `.temp\p6-001-live\probe\` 固定路径以便复用，如上示例）。
- `result.json` 参数当前为占位（场景主要证据在 jsonl + sidecar）；退出码 0 为正常。
- **第二段（同目录重启）**：在第一段退出后，用**同一个 `$runBase`** 再次运行同样命令。用于观察"清除后重启不恢复旧历史、settings 开关保留"。
- 两段必须串行（同一 stats.json 写路径），不得并行运行两个探针实例。

## 实际绝对数据路径（以构建产物位于 `C:\Users\mrrdxx\Desktop\pet` 为例）

| 内容 | 路径 |
| --- | --- |
| run 根（baseDir） | `C:\Users\mrrdxx\Desktop\pet\.temp\p6-001-live\run-<时间戳>` |
| 场景目录（全部数据） | `...\run-<时间戳>\usage-stats-live\` |
| 统计存档 | `...\usage-stats-live\stats.json`（及 `.corrupted`/`.tmp` 副本同目录） |
| 宠物设置（含统计开关） | `...\usage-stats-live\settings.json` |
| 宠物状态 | `...\usage-stats-live\state.json` |
| 观测 jsonl | `...\usage-stats-live\usage-stats-observations-<n>.jsonl`（按每次启动编号，不覆盖） |
| 阶段小结 sidecar | `...\usage-stats-live\usage-stats-sidecar-<n>.json`（收尾时刻从服务当前值写） |
| 退出清理 sidecar | `...\usage-stats-live\usage-stats-cleanup-<n>.json`（`ShutdownMainWindow` 内写） |
| wiring 检查证据 | `...\usage-stats-wiring\usage-stats-wiring-{observations|sidecar|cleanup}-<n>.{jsonl\|json}` |
| 诊断日志 | `...\usage-stats-live\logs\` |

## 进程归属与正常退出清理

- 运行进程：`DesktopPet.DiagnosticProbe.exe`（WPF 单窗口预览，无托盘）。
- 实例锁：`ProbeSetupIsolated` 创建唯一 `Global\DesktopPet.DiagnosticProbe.<8hex>` mutex，不争用生产锁。
- 正常退出（场景自动，t≈285s 或手动关闭窗口）：`ShutdownMainWindow` → 退订 `SessionSwitch/PowerModeChanged` → `UsageStatsService.Stop()+Dispose()`（flush 待写+保留期清理）→ 关窗 → `Shutdown(0)`，退出码 0。
- `usage-stats-cleanup-<n>.json` 记录 `serviceNullAfterCleanup / shutdownCalled / subscribedAfterCleanup`，验证系统事件订阅已释放。
- 若异常退出（崩溃/杀进程）：stats 可能保留部分待写数据；重复运行同一目录前先人工确认无残留探针进程（`Get-Process DesktopPet.DiagnosticProbe`）。

## 操作阶段清单（第一段运行，约 285 秒）

| 时间 | 阶段 | 操作者动作 | 预期观察（jsonl） |
| --- | --- | --- | --- |
| 0-30s | A 默认关闭 | 不操作宠物 | `enabled=false`，`segments=0`，`events=0` |
| ~45s 提示 | B 启用 | 右键宠物 → 点击 **Statistics: Off** | ≤10s 内 `enabled=true`；本轮启动时默认 OFF，**不会补记 app-start**（仅重启且启动已启用时新增一条） |
| ~115s 提示 | C 前台累积 | 打开标记应用（如记事本，标题含 `PET-STATS-MARKER-A`）保持前台并活动 | `segments` 增长，`recentSegments[].appId` 为该应用可执行文件名（base name，如 `notepad`） |
| ~175s 提示 | D 前台切换 | 切换到**不同**应用（如 `mspaint` 或计算器）并活动 | 新段以新 `appId` 开启；旧段闭合，时长不再增长 |
| ~220s 提示 | E「关闭保留」 | 右键宠物 → 点击 **Statistics: On**（即关闭收集） | `enabled=false`；已收集数据保留并冻结（`segments` 数量/时长零增长，不清除） |
| ~255s 提示 | F「重新开启后清除保留 On」 | 右键宠物 → 点击 **Clear Statistics** | 旧记录消失（清除代次/generation 与时间记入 `usage-stats-sidecar-<n>.json`；不无条件要求此后列表长期为空），菜单仍显示 **Statistics: On**（开关保留），之后新记录允许（重新开启路径保持 On） |
| ~278s 提示 | 重启说明 | 无（提示） | 退出后同目录重跑命令：开关恢复上次状态、已清除历史**不恢复**、允许新记录、写入新 session 编号文件 |
| ~285s | 退出 | 无（自动） | `shutting down`；sidecar 在收尾时刻从服务当前值写；stats.json 为操作后的实际存档 |

## 第二段运行（同目录重启，验证"关闭/清除后状态语义"）

第一段退出后，用**同一 `$runBase`** 再运行一次命令：

- 启动即观察：settings/state 从磁盘生产恢复（`UsageStatsEnabled` 开关保留上次状态）；已清除的统计**不因重启复生**
  （`stats.json` 恢复为空存档——`Restore` 不恢复 generation，R3/R5 语义）；若上一段以"关闭"结束，`enabled=false`；
- 本段可顺带复测 启用→累积→关闭→退出 一小轮（约 1-2 分钟），然后正常退出；
- 本段观测写入 `usage-stats-observations-<n+1>.jsonl` 等新 session 编号文件，不覆盖第一段证据。
  同一 run 目录连跑两段后，目录中 `*-1.*` 与 `*-2.*` 三件套（obs/sidecar/cleanup）并存。

## app-start 语义与标记应用归属

- **app-start**：只在**启动时** settings `UsageStatsEnabled=true` 才记录一次（`InitializeUsageStats` 门控）；运行中
  启用、禁用、清除均不补造 app-start 事件。首轮默认关闭时 `events` 初始为 0，启用后从新事件开始。
  同一 run 目录重启时，上一次启动已持久化的 app-start 会被生产恢复保留，本次启动再新增一条（每进程启动恰好一条），
  因此累计条数随重启增加，增量恒为 1；wiring 场景以「初始化前后 app-start 增量」断言，不要求累计恒为 1。
- **标记应用**：C/D 阶段的前台标记应用（如标题含 `PET-STATS-MARKER-A` 的记事本、`mspaint`/计算器）由**操作者自己**
  创建并精确归属（标题含唯一标记）；探针不创建、不借用用户未保存窗口，不以假前台/手工 SampleTick/注入 AppId
  充当实机证据。

## 生命周期（锁屏/睡眠）观测

- 探针已在 jsonl 持续输出 `isSuspended`（pause/lock/sleep 任一即 true）与 `events`（真实 `unlock`/`resume` 事件）。
- 真实锁屏/睡眠按**用户可用时段由监理另行安排**；到时操作者锁屏（Win+L）后等待 15-30s → 解锁；或让系统/手动睡眠后唤醒。预期：锁屏后 `isSuspended=true`、segment 停止增长；解锁后 `events` 出现 `unlock`。睡眠唤醒对应 `resume`。
- 本轮未执行真实锁屏/睡眠，缺失项如实列出。

## 未验证 / 不做

- 真实菜单点击、真实前台切换、真实锁屏/睡眠 → **待监理与用户约时段实机执行**（本轮只做无鼠标构建与无窗口 wiring 接线验证）。
- `usage-stats-wiring` 无窗口检查已通过（三断言 + session 不覆盖），证据见上表；live 实机仍待监理执行。
- 不再整改已关闭的 F-01～F-04；不进入 P6-002～004；不提交/推送；不改 `docs/reviews` 及监理证据。