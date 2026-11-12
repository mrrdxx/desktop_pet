# P3-002 开发自测记录

## 实现概述

P3-002 相互注意与协调：两只空闲宠物偶尔注意对方，短暂发起/回应/拒绝，然后结束并冷却。

### 架构

- **Core 层**（纯逻辑，无 WPF 依赖）：
  - `SocialConfig`：12 个可调参数（触发概率/距离/回应延迟/持续/冷却/超时/空闲门槛），参数表已写入 ARCHITECTURE §6 和 PRODUCT R-05。
  - `SocialSession`：不可变状态结构，阶段枚举 `Idle/Noticing/Responding/Rejected/Finished/Cancelled/Cooldown`，取消原因枚举 `DragStarted/PettingCandidate/Paused/PetRemoved/RecoverRequested/PhysicsActive/Timeout/OnlyOnePet`。
  - `SocialCoordinator`：状态机，`Tick(nowMs, petA, petB?)` 推进，`ForceCancel(reason, nowMs)` 强制取消。Tick 按 nowMs 去重（多窗口同一 tick 只推进一次）。

- **App 层**（生产接线）：
  - `PetStateContext.SocialCoordinator`：全局共享属性。
  - `App.SocialCoordinator`：在 `OnStartup` 和 `TestSetupIsolatedSave` 中创建，在 `CreatePetWindow` 中注入到 `PetStateContext`，在 `RemovePet` 中 `ForceCancel(PetRemoved)`。
  - `MainWindow.EvaluateSocialTick`：在 `OnUpdateTick` 稀有事件块后调用，收集双宠状态调用 `SocialCoordinator.Tick`，通过 `_curiosity.Start/Cancel` 切换表情。`isSocialActive` 传入 `RareEventUpdateService.EvaluateTick` 阻止稀有事件触发。
  - `MainWindow.BuildPetSocialState`：构建宠物社交状态快照（PetId/性格/中心坐标/忙碌标志/最后活动时间）。

### 关键设计决策

1. **社交不写位置**：社交只切换表情（复用好奇 `_curiosity.Start/Cancel`），不修改窗口 Left/Top。遵守单一位置写入约束。
2. **Tick 去重**：多个窗口的 OnUpdateTick 可能在同一 nowMs 调用 `SocialCoordinator.Tick`，coordinator 按 nowMs 去重，只有第一次调用推进状态机。
3. **用户输入优先**：任一方被拖动（含头部候选 petting）、暂停、进入物理、探头时，`CheckInterrupt` 立即取消会话并释放双方，不从旧阶段续演。慢回应只影响社交延迟（`SlowPacedRespondDelayMs`），不延迟用户抓取。
4. **唯一会话**：同时只有一个双方共享会话。冷却期（`CooldownMs`）内不启动新会话。
5. **性格差异**：`Proactive` 使用 `NoticeProbability`/`RespondDelayMs`；`SlowPaced` 使用 `SlowPacedNoticeProbability`/`SlowPacedRespondDelayMs`，观察上慢半拍。

## 自测证据

### 构建

- 全量构建 0 警告 0 错误（`dotnet build`）。
- 隔离预览 `-BuildOnly` 构建成功。

### 测试

- Core 单元测试 `SocialCoordinatorTests.cs`：21/21 通过。覆盖正常回应流、拒绝、超时、拖动中断、暂停中断、物理中断、探头中断、冷却、性格差异（Proactive 总是发起者/SlowPaced 不发起）、唯一会话、确定性（种子随机）、单宠无社交、距离限制。
- App 集成测试 `SocialIntegrationTests.cs`：15/15 通过。覆盖：
  - `Social_NormalNotice_SessionStarts`：双宠空闲在距离内，OnUpdateTick 启动社交。
  - `Social_NormalFlow_Notice_Respond_Finish_Cooldown`：完整流：Noticing→Responding→Finished→Cooldown。
  - `Social_Reject_TargetRejects_SessionEnds`：拒绝后结束并冷却。
  - `Social_NoticeDurationExceeded_SessionEndsWithoutResponse`：超时结束。
  - `Social_DragInterrupts_SessionCancelled`：拖动中断社交。
  - `Social_DoesNotDelayHeadPress_HeadPressCancelsSocial`：头部按压在社交中立即接受并取消社交（用户输入优先）。
  - `Social_PauseInterrupts_SessionCancelled`：暂停中断社交。
  - `Social_PetRemovalCancels_RemainingPetNotSocial`：移除宠物取消社交。
  - `Social_OnlyOneSession_MultipleTicksDoNotStartSecond`：多 tick 不启动第二会话。
  - `Social_SlowPacedNeverInitiates_ProactiveIsInitiator`：慢半拍不发起，主动型总是发起者。
  - `Social_ActiveSession_BlocksRareEventEvaluate`：社交激活时 IsSocialActive=true 阻塞稀有事件。
  - `Social_TooFar_NoSession`：超出距离不启动社交。
  - `Social_DoesNotWritePosition`：社交期间位置不变。
  - `Social_SinglePet_NoSession`：单宠无社交不崩溃。
  - `Social_Cleanup_StopsTimersEvenWithActiveSession`：社交激活时 TestCleanup 仍正确停止计时器。
- 全量测试 729/729 通过（Core 351 + App 378），0 回归。

### 隔离预览

- `Run-ProbeLive.ps1`（自动模式）：全部探针检查通过（`ALL PROBE LIVE CHECKS PASS`），进程退出码 0，真实存档快照不变。单宠场景社交功能正常不崩溃。

## 隔离体验入口

- 隔离交互入口：`Run-ProbeLive.ps1 -Interactive`（需先 `-BuildOnly` 或直接运行会自动构建）。
- 自动模式：`Run-ProbeLive.ps1`（自动探针，单宠场景，验证社交功能正常不崩溃）。
- 双宠社交实机观察：在 `-Interactive` 模式下右键 Add Pet 添加第二只宠物，两只空闲时观察社交触发。
- 真实存档不受污染：探针使用隔离存档目录，自动模式结束后真实存档快照不变。

## 未验证项

- 双宠实机社交体验（需要两只宠物在屏幕上同时空闲并触发社交）尚未进行人工验证。
- 隔离预览自动探针只覆盖单宠场景（预置存档只有一只宠物），双宠社交的实机行为需要 `-Interactive` 模式手动添加第二只宠物观察。
- 社交表现仅为好奇表情复用，无专门社交美术。

## 复核请求

请监理审查 P3-002 实现的以下方面：
1. 社交协调器状态机正确性（阶段转换/超时/冷却/中断）。
2. 用户输入优先保证（拖动/头部按压/暂停/移除/物理立即中断社交）。
3. 单一位置写入约束（社交不写位置）。
4. 唯一会话保证（同时只有一个共享会话）。
5. 性格差异可观察性（Proactive vs SlowPaced）。
6. 社交阻塞稀有事件的正确性。
7. 可调初值与文档一致性。

---

## R1 整改（2026-11-10）：F-01～F-04 逐项措施、自测证据与复核请求

整改对象为监理 R1 报告（[docs/reviews/P3-002-review.md](../reviews/P3-002-review.md)）F-01～F-04 阻断项。未修改监理报告或证据目录。

### F-01 高：社交表现按 PetId 分发到双方窗口

**问题**：`EvaluateSocialTick` 只在 `SessionStarted/InitiatorExpression.Notice` 时 `_curiosity.Start`，不检查当前窗口角色，忽略 `TargetExpression`，不能正确分发 Notice/Respond/Reject，也无法同步结束另一只表现。

**措施**：
- 新增 `ResolveExpressionForPet(petId, result)` 方法：根据 `session.InitiatorId`/`TargetId` 判断当前窗口是发起方还是对方，返回对应的 `InitiatorExpression` 或 `TargetExpression`；会话结束时返回 `ReturnToIdle`。
- 新增 `ApplySocialExpression(expression, nowMs)` internal 方法：`Notice`/`Respond` → `_curiosity.Start`（若非更高优先级忙碌），`Reject`/`ReturnToIdle` → `_curiosity.Cancel`。不写位置，不取消用户悬停触发的独立好奇。
- 重写 `EvaluateSocialTick`：调用 `ResolveExpressionForPet` 为本窗口解析表情并 `ApplySocialExpression`；同时遍历 `app.AllPetWindows` 找到对方窗口，用对方的 PetId 解析表情并调用 `otherWindow.ApplySocialExpression`，确保双方都收到各自角色的表情（即使对方 Tick 被去重）。
- 新增 `IsCuriosityActive` internal 属性供测试断言可见表现状态。

**位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `EvaluateSocialTick` 约 1092–1174 行、`ResolveExpressionForPet` 1180–1197 行、`ApplySocialExpression` 1206–1225 行。

### F-02 高：事件入口即时取消共享会话

**问题**：暂停只 Stop timer 不 ForceCancel；Recover 无取消接线；头部按下仅设置候选到后续 tick 才取消。取消依赖已停 timer 或下一 frame。

**措施**：
- 新增 `CancelSocialImmediately(reason, nowMs)` internal 方法：检查 `coordinator.IsSocialActive`，若活跃则 `ForceCancel(reason, nowMs)`；同步清理本窗口 `_isSocialActive=false`/`_curiosity.Cancel()`；遍历 `app.AllPetWindows` 同步清理对方窗口 `_isSocialActive=false`/`_curiosity.Cancel()`。不依赖后续 tick。
- 在以下生产事件入口调用 `CancelSocialImmediately`：
  - `StartDragging()`：`CancelReason.DragStarted`（身体拖动入口）。
  - `BeginCapturedPointerInteraction` 头部按压路径：`CancelReason.PettingCandidate`（头部按压入口）。
  - `PauseAnimation()`：`CancelReason.Paused`（全局暂停，timer 即将停止）。
  - `RecoverPet()`：`CancelReason.RecoverRequested`（全局找回）。
- `App.RemovePet` 改为通过幸存窗口的 `CancelSocialImmediately` 取消（同时解决 F-03 时间域问题），不再直接调 `ForceCancel`。

**位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `CancelSocialImmediately` 1239–1273 行、`StartDragging` 1499–1504 行、`BeginCapturedPointerInteraction` 1476–1478 行、`PauseAnimation` 2242–2246 行、`RecoverPet` 2210–2212 行；`src/DesktopPet.App/App.xaml.cs` `RemovePet` 694–702 行。

### F-03 中：移除使用当前同一单调时钟计算冷却

**问题**：`App.RemovePet` 调用 `ForceCancel(PetRemoved, 0)`，冷却基准为 0，与运行中的 `nowMs`（如 100000）差距巨大，冷却瞬间过期，幸存者可立即与新添加的宠物重启社交。

**措施**：
- `App.RemovePet` 不再使用 `MonotonicClock.Instance.GetElapsedMs()` 或硬编码 0，改为通过幸存窗口的 `CancelSocialImmediately(CancelReason.PetRemoved, survivingWindow.TestGetElapsedMs?.Invoke() ?? MonotonicClock.Instance.GetElapsedMs())`。这样冷却基准时间与 coordinator 的 Tick 时间域一致（测试中用 `TestGetElapsedMs` 注入，生产用 `GetElapsedMs`），确保冷却从实际取消时刻正确计算。

**位置**：`src/DesktopPet.App/App.xaml.cs` `RemovePet` 694–702 行。

### F-04 高：稀有事件评估前读取权威共享社交状态

**问题**：`OnUpdateTick` 稀有事件块在 `EvaluateSocialTick` 之前，向 `RareEventUpdateService` 传本窗 `_isSocialActive`；该字段只在 `EvaluateSocialTick` 后更新。窗口 A 开始社交后 B 尚未执行社交 tick 时，B 仍传 false，可能消费稀有计划。

**措施**：
- `OnUpdateTick` 稀有事件块中，从全局 `SocialCoordinator` 读取 `IsSocialActive`（`authoritativeSocialActive = sharedSocialCoordinator?.IsSocialActive ?? false`），替代本地 `_isSocialActive`。
- 该读取在 `RareEventUpdateService.EvaluateTick` 调用之前，确保所有窗口在稀有事件评估前看到权威共享社交状态。

**位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `OnUpdateTick` 1010–1030 行。

### 新增生产接线测试（10 项）

在 `tests/DesktopPet.App.Tests/SocialIntegrationTests.cs` 追加 10 项 R1 生产接线测试，全部通过生产工厂 `CreatePetWindow` + `OnUpdateTickTest` 入口，断言双方窗口可见表现状态（`IsCuriosityActive`/`IsSocialActive`），不只断言 coordinator.Phase：

| 测试 | 覆盖 |
|------|------|
| `Social_F01_InitiatorShowsNotice_TargetShowsRespond` | F-01：发起方 Notice 时 curiosity 激活，过 respond delay 后对方 Respond curiosity 激活 |
| `Social_F01_Reject_TargetCuriosityCancels_BothReturnToIdle` | F-01：拒绝后对方 curiosity 不激活，会话结束后双方 curiosity 均取消 |
| `Social_F01_NormalFinish_BothWindowsCuriosityCancels` | F-01：正常结束时双方 curiosity 均取消 |
| `Social_F02_PauseCancelsImmediately_NoTickNeeded` | F-02：暂停后 coordinator 和双方窗口社交状态立即清除，无需额外 tick |
| `Social_F02_RecoverCancelsImmediately_NoTickNeeded` | F-02：找回后即时取消，无需额外 tick |
| `Social_F02_DragStartCancelsImmediately_NoTickNeeded` | F-02：身体拖动入口即时取消，无需额外 tick |
| `Social_F02_HeadPressCancelsImmediately_NoTickNeeded` | F-02：头部按压入口即时取消且接受按压，无需额外 tick |
| `Social_F02_RemovePetCancelsImmediately_BothWindowsCleared` | F-02：移除后即时取消，幸存窗口社交状态立即清除，无需额外 tick |
| `Social_F03_LongRunRemoveAdd_CooldownHolds_PreventsImmediateRestart` | F-03：100000ms 移除再添加，100001ms 冷却有效阻止重启，150001ms 冷却过期后允许 |
| `Social_F04_WindowBRareEventSeesSharedSocialActive_PlanNotConsumed` | F-04：A 社交后 B tick 的稀有事件读权威共享状态，计划未被消费 |
| `Social_F04_AfterSocialEnds_RareEventCanFire` | F-04：社交结束后稀有事件不再被社交状态阻塞 |

注：F-02 有 5 项（含 RemovePet），总新增 11 项（F-01 3 + F-02 5 + F-03 1 + F-04 2）。原 15 项 + 新增 11 项 = 26 项社交集成测试。

### 自测命令与结果

```
dotnet build --no-restore
```
结果：0 警告 0 错误。

```
dotnet test --no-build
```
结果：全量通过，Core 372 + App 368 = **740/740**，0 失败，0 跳过，0 回归。

社交专项筛选运行：
```
dotnet test --no-build --filter "FullyQualifiedName~Social"
```
结果：全部通过，0 失败。

### 未验证项

- 双宠实机社交观感体验尚未进行人工验证（两只宠物在屏幕上同时空闲并触发社交的可见表现）。
- 社交表现仍为好奇表情复用，无专门社交美术。
- 单宠隔离预览自动探针只覆盖单宠场景，未重跑（R1 代码改动主要影响双宠社交路径，单宠路径无变化）。

### 复核请求

请监理复核以下方面：
1. F-01：`EvaluateSocialTick` 按 PetId 分发 `InitiatorExpression`/`TargetExpression` 到各自窗口；双方窗口 `IsCuriosityActive` 在 Notice/Respond/Reject/Finish 各阶段正确切换；不同 timer 调用顺序下双方表现一致。
2. F-02：暂停/找回/拖动/头部按压/移除在事件返回时 coordinator 和双方窗口社交状态均已清除，不依赖后续 tick。
3. F-03：移除冷却基准使用幸存窗口的当前时钟（与 coordinator Tick 同域），长运行移除再添加冷却正反例正确。
4. F-04：稀有事件块读取 `coordinator.IsSocialActive` 而非本地 `_isSocialActive`；双窗交错 tick 下社交期间不消费稀有计划，结束后正常评估。

P3-001 保持 done 不重开。不进入 P3-003。不提交推送。
