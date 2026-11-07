# P2-004 R1 整改记录

日期：2026-11-07。对应审查报告：[P2-004-review](../reviews/P2-004-review.md) R1。
开发方独立整改，不修改监理报告，不自行关闭问题，不提交/推送。

## 整改范围

仅 R1 的 F-01 阻断项：真实快拖按下/Move 时间戳碰撞导致位移丢失。
释放快照已获监理接受，不重复整改。
不进入阶段 3，不重做 P2-003 已关闭问题，不恢复已取消功能。

## F-01 中：真实快拖的按下/Move 时间戳碰撞

### 问题

生产 `GetElapsedMs()` 使用 `Environment.TickCount64` 差值，该计数器在 Windows .NET 10 上更新节奏约 10-16ms。真实快拖中按下和首次 MouseMove 可能在同一 `TickCount64` 值发生（监理实机确认 t=12375），导致 `RecordSample` 走 `timeMs <= newest.TimeMs` 拒绝路径（`rejected_same_or_older_time`），Move 样本被丢弃。窗口确实移动了 40px，但采样器只剩基准样本 1 个 → `insufficient_samples` → 不抛掷。

这不是"非产品逻辑问题"——`Environment.TickCount64` 的粗粒度是 App 时间源选择的后果，Core 正确拒绝非递增时间，App 时间源/事件采样适配丢失了首段位置。

### 修复

将 `GetElapsedMs()` 从 `Environment.TickCount64` 改为 `Stopwatch.GetTimestamp()` 按 `Stopwatch.Frequency` 换算 double 毫秒。

位置：`src/DesktopPet.App/MainWindow.xaml.cs` — `GetElapsedMs()` 方法（第 258-269 行）。

```csharp
// Before (coarse, ~10-16ms granularity):
private static double GetElapsedMs()
{
    if (_startTimeMs < 0)
        _startTimeMs = Environment.TickCount64;
    return Environment.TickCount64 - _startTimeMs;
}

// After (high-resolution, sub-microsecond):
private static double GetElapsedMs()
{
    if (_startTimeMs < 0)
        _startTimeMs = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
    return Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency - _startTimeMs;
}
```

新增 `using System.Diagnostics;`。

### 为什么这解决问题

- `Stopwatch.GetTimestamp()` 基于 Windows Query Performance Counter (QPC)，分辨率远高于毫秒（通常 ~100ns 级别）。
- 换算为 double 毫秒后保留小数精度，按下和首次 Move 即使只隔几十微秒也会得到不同的 timeMs。
- `RecordSample` 的 `timeMs <= newest.TimeMs` 比较正确处理 double 小数——只要 timeMs 严格大于 newest.TimeMs 就接受。
- 不伪造 epsilon、不放开零 dt、不降低 `MinTimeSpanMs` 或 `ThrowThresholdPxPerMs`。

### 时间域一致性

所有时间使用方通过同一入口 `TestGetElapsedMs?.Invoke() ?? GetElapsedMs()` 获取时间：

| 调用方 | 位置 | 说明 |
|--------|------|------|
| `OnUpdateTick` | 第 636 行 | 物理 tick、hover、情绪、罕见事件 |
| `BeginCapturedPointerInteraction` | 第 1083 行 | `_pointerPressTimeMs`（按下时间） |
| `ProcessCapturedPointerMove` | 第 1173 行 | MouseMove 的 `nowMs` |
| `EndDrag` | 第 1447 行 | 释放时间 `releaseMs` |
| `UpdatePhysics` | 通过 `nowMs` 参数 | 物理 `elapsedMs = nowMs - _lastPhysicsUpdateMs` |
| `PauseAnimation` | 第 1880 行 | `_pauseStartMs` |
| `ResumeAnimation` | 第 1920/1927/1978/1983 行 | 恢复后重置 `_lastPhysicsUpdateMs` |
| `AnimationPlayer` | 构造函数传入 `GetElapsedMs` 委托 | 动画帧时间 |

改为 `Stopwatch` 后，所有调用方自动获得高分辨率时间，无需逐个修改。`Stopwatch` 在暂停期间不停止（不像 `DispatcherTimer`），但暂停补偿机制（`PauseAnimation`/`ResumeAnimation` 中调整 `_grievanceExpressionStartMs`、`_probeStartMs`、`_lastPhysicsUpdateMs` 等）基于时间差计算，不受时间源精度影响。

### 测试注入兼容性

`TestGetElapsedMs` 注入机制完全不变——测试通过 `w.TestGetElapsedMs = () => now` 注入可控时钟，生产代码走 `GetElapsedMs()`。现有 599 个测试不受影响（已验证 0 回归）。

### 新增回归测试

`tests/DesktopPet.App.Tests/HighResolutionTimingTests.cs`，5 个测试：

1. `ProductionClock_TwoImmediateCalls_ReturnDifferentMs`：不注入时钟，连续调用生产 `GetElapsedMs()` 应返回不同值（证明 Stopwatch 分辨率高于 TickCount64）。
2. `ProductionClock_ShortIntervalDrag_ProducesTwoAcceptedSamples`：不注入时钟，真实快拖（按下→40px Move→释放）应产生至少 2 个 accepted 样本。
3. `InjectedClock_SameTimestampMove_StillRejected`：注入相同时间戳时 Move 仍被拒绝（证明未放松单调序要求）。
4. `InjectedClock_SubMillisecondDelta_Accepted`：注入 0.1ms 差值时 Move 被接受（证明 double 小数精度正确传递）。
5. `ProductionClock_PauseResume_PhysicsTimeDomainConsistent`：不注入时钟，暂停/恢复前后 `GetElapsedMs()` 单调递增。

### 验证

- 构建 0 警告 0 错误。
- 全量 Core 351 + App 253 = 604/604 通过，0 回归（新增 5 个测试）。
- **未修改任何判定逻辑、阈值或参数。**
- 未修改 `RecordSample` 的拒绝条件。
- 未修改 `ReleaseSpeedConfig` 任何阈值。
- 未伪造时间差、未放开零 dt 除法。

### 保留的原日志作为失败证据

监理 R1 报告中的原始失败记录（baseline t=12375 accepted + move t=12375 rejected_same_or_older_time × 2）保留在 [P2-004-review](../reviews/P2-004-review.md) R1 和 [实机记录](../history/P2-004-live-validation.md) 中，作为 `Environment.TickCount64` 粗粒度碰撞的证据。

### 未验证项

- ~~真实鼠标短拖复测须由监理执行（开发方不自行签关闭）。~~ → 已由监理 R2 复验，F-01 关闭。
- 慢拖、停住释放、飞行中身体/头部重新抓取仍待后续补测。
- 150%/混合DPI/多屏未实测。
- 未提交/推送。

---

## R2 监理复核关闭依据（2026-11-07）

监理 R2 报告：[P2-004-review](../reviews/P2-004-review.md) R2。

### F-01 关闭

监理独立重建 DLL 后以同一观察宿主进行真实 CUA 鼠标短拖复验（HWND DPI96）：

- 独立高分辨率 5 项 + 快照 6 项 + 释放接线 32 项 = **43/43 通过**，0 编译警告/错误。
- 原 40px 短拖真实复验两次，baseline/move 均为不同小数时间戳，所有 Move 为 accepted，无 `rejected_same_or_older_time` 或 `insufficient_samples`：
  - 右向：baseline 13478.8323 → move 13489.2114（40px/约 10.3791ms）→ 同位置 move 13501.7541 → release 13506.5469。3 个样本被接受，末段零位移，reason=`no_motion_segment_stopped`。
  - 左向：baseline 28953.6025 → move 28954.1978（约 0.5953ms）→ 同位置 move 28956.9789 → release 28973.5550。全部接受，末段零位移，reason=`no_motion_segment_stopped`。实际移动段低于既定 5ms，不能要求必定甩出。
- 既有同时间拒绝回归仍通过。
- 未放宽 Core 时间拒绝/5ms 最小段/速度阈值；未把两次未抛出改写为抛掷成功；未为使自动脚本通过而改判定规则。

### P2-004 状态

P2-004 保持 awaiting-verification。F-01 已关闭，不重复申请关闭。剩余实机验收项：
- 真实慢拖。
- 快拖后停住释放。
- 飞行中身体/头部追抓及抓取点一致性。
- 正常可控甩出重复性。

不标 done、不进入阶段 3、不提交/推送。不继续围绕工具极短固定动作增补诊断或调参数。

---

## R3 监理最终验收依据（2026-11-07）

监理 R3 报告：[P2-004-review](../reviews/P2-004-review.md) R3。

### P2-004 通过

用户在本对话反馈"5项均完美测试通过"，覆盖：
1. 慢拖松手。
2. 快拖后停住松手。
3. 正常甩出与反弹收敛。
4. 飞行中身体抓取。
5. 飞行中头部抓取及原抓取点稳定。

依据为用户本人的实际体验反馈，不冒充监理实际操作或全环境实测。逐项操作和执行边界见 [第六轮实机记录](../history/P2-004-live-validation.md)。

合并前轮实际 100% 单屏暂停/恢复、R2 独立 43/43 与 F-01 关闭、P2-003 R7 生产隔离保存/恢复证据，当前 P2-004 范围验收条件满足，**P2-004 通过，标 done**。P2-001～P2-004 当前计划内验收闭环。

### 保留限制

- 150%/混合 DPI/多屏、真实休眠/锁屏和 ART 历史未验证项保留。
- 旧自动短拖失败/工具中止保留历史，不借本轮结果改写。
- 桌面旧启动脚本待后续交付收尾更新。
- 不自动开始阶段 3。

### 开发方操作

仅在原整改记录追加 R3 验收依据，不重复申请关闭/测试，不修改监理报告。本轮交付收尾包含启动入口更新和 Git 提交推送，按用户明确授权执行。
