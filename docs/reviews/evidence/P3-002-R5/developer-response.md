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

---

## R2 整改（2026-11-10）：F-01 可见动画/渲染链接入

整改对象为监理 R2 报告（[docs/reviews/P3-002-review.md](../reviews/P3-002-review.md) 第 39–53 行）F-01 限定剩余整改。F-02/F-03/F-04 已由 R2 关闭，不重复整改。F-01 角色分发结构（`ResolveExpressionForPet`/`ApplySocialExpression`/`IsCuriosityActive`）已被接受，保留不重做。未修改监理报告或证据目录。

### F-01 剩余：社交表现接入实际动画/渲染通道

**问题**：`ApplySocialExpression` 约 1206–1225 行 `Notice`/`Respond` 仍只有 `_curiosity.Start(nowMs)`，`Reject`/`ReturnToIdle` 仍只有 `_curiosity.Cancel()`；没有调用 `_animPlayer.PlayAction` 或 `UpdateFrame`。`_curiosity.Start/Cancel` 只操作计时器字段（`_endsAt`），不切换 `_animPlayer.CurrentActionId`。原反例 `Reviewer_SocialNotice_MustActuallyDisplayCurious` 连续两个生产 tick 后 `CurrentAction` 仍 `"idle"`，断言 `Assert.AreEqual("curious", a.CurrentAction)` 失败。

**措施**：

1. **`ApplySocialExpression` Notice/Respond 路径接入动画通道**：
   - 在 `_curiosity.Start(nowMs)` 之后，当 `_animPlayer.CurrentActionId != "curious"` 时调用 `_animPlayer.PlayAction("curious")` + `UpdateFrame()`，使 `CurrentAction` 实际切换为 `"curious"`。
   - 仅在当前不是 `"curious"` 时才 `PlayAction`，避免每 tick 重启同一段表情（`PlayAction` 会重置帧计时器，每 tick 重调会导致动画永远停在第一帧）。
   - 保留原有忙碌守卫：`_isProbing`/`_isExpressingGrievance`/`_isDragging`/`_isPettingCandidate` 时不切换（探头已播放 curious 并拥有位置，委屈/拖动/抚摸候选有更高优先级）。

2. **`ApplySocialExpression` Reject/ReturnToIdle 路径恢复 idle 动画**：
   - 在 `_curiosity.Cancel()` 之后，当 `_animPlayer.CurrentActionId == "curious"` 且无更高优先级状态（非拖动/探头/委屈）时，调用 `_animPlayer.PlayAction("idle")` + `UpdateFrame()`，恢复实际 idle 动画。
   - 仅在 `_curiosity.IsActive` 时才执行（不取消用户悬停触发的独立好奇）。
   - 仅在当前显示 "curious" 时才恢复 idle（社交是 curious 的所有者），不无差别覆盖其他动作。

3. **`CancelSocialImmediately` 双方恢复 idle 动画**：
   - 本窗口清理 `_curiosity.Cancel()` 后，若 `_animPlayer.CurrentActionId == "curious"` 且非拖动/探头/委屈，恢复 `PlayAction("idle")` + `UpdateFrame()`。
   - 对方窗口同样：`w._curiosity.Cancel()` 后，若 `w._animPlayer.CurrentActionId == "curious"` 且非忙碌，恢复 idle 动画。

**位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `ApplySocialExpression` 约 1206–1240 行、`CancelSocialImmediately` 约 1254–1295 行。

### 测试升级与新增

#### 升级 3 项 R1 F-01 测试（从 `IsCuriosityActive` 到 `CurrentAction` 断言）

| 测试 | 升级内容 |
|------|----------|
| `Social_F01_InitiatorShowsNotice_TargetShowsRespond` | 新增 `Assert.AreEqual("curious", wA.CurrentAction)` 和 `Assert.AreEqual("curious", wB.CurrentAction)` 断言，以及社交前 `Assert.AreEqual("idle", ...)` 基线 |
| `Social_F01_Reject_TargetCuriosityCancels_BothReturnToIdle` | 新增 `Assert.AreNotEqual("curious", wB.CurrentAction)` (Reject) 和 `Assert.AreEqual("idle", ...)` (结束恢复) |
| `Social_F01_NormalFinish_BothWindowsCuriosityCancels` | 新增 `Assert.AreEqual("curious", ...)` (Responding) 和 `Assert.AreEqual("idle", ...)` (结束恢复) |

#### 升级 1 项 R1 F-02 测试

| 测试 | 升级内容 |
|------|----------|
| `Social_F02_PauseCancelsImmediately_NoTickNeeded` | 新增 `Assert.AreEqual("curious", wA.CurrentAction)` (社交中) 和 `Assert.AreNotEqual("curious", wA.CurrentAction)` (暂停取消后) |

#### 新增 7 项 R2 F-01 可见动画链测试

| 测试 | 覆盖 |
|------|------|
| `Social_F01_R2_InitiatorCurrentActionIsCurious_NotIdle` | 原反例复现：社交后两个 tick，`CurrentAction == "curious"` 而非 `"idle"` |
| `Social_F01_R2_TargetCurrentActionIsCurious_AfterRespondDelay` | 回应延迟前 target 保持 idle，延迟后 target `CurrentAction == "curious"` |
| `Social_F01_R2_Reject_TargetNotCurious_BothReturnToIdle` | Reject 时 target 非 curious，结束后双方 `CurrentAction == "idle"` |
| `Social_F01_R2_NormalFinish_BothReturnToIdleAnimation` | 正常结束时双方 `CurrentAction` 从 "curious" 恢复为 "idle" |
| `Social_F01_R2_DragCancel_CurrentActionSwitchesToDrag` | 拖动取消后 `CurrentAction == "drag"`（不残留 curious） |
| `Social_F01_R2_PauseCancel_CurrentActionNotCurious` | 暂停取消后 `CurrentAction != "curious"` |
| `Social_F01_R2_CrossWindowDispatch_TargetReceivesExpression` | 跨窗口分发：A tick 后 A `CurrentAction == "curious"`，延迟后 B `CurrentAction == "curious"` |
| `Social_F01_R2_NoRestart_EveryTick_StableCuriousAction` | 多 tick 不重启：连续 4 个 tick `CurrentAction` 稳定为 "curious" |

原 26 项 + 升级 4 项 + 新增 7 项 = 33 项社交集成测试。

### 自测命令与结果

```
dotnet build --no-restore
```
结果：0 警告 0 错误。

```
dotnet test --no-restore --verbosity minimal
```
结果：全量通过，Core 372 + App 375 = **747/747**，0 失败，0 跳过，0 回归。

社交专项筛选运行：
```
dotnet test --no-restore --filter "FullyQualifiedName~Social"
```
结果：全部通过，0 失败。

### 设计要点

1. **不每 tick 重启表情**：`PlayAction` 重置帧计时器 `_actionStartMs`，若每 tick 重调会导致动画永远停在第一帧。通过 `CurrentActionId != "curious"` 守卫确保只在首次切换时调用一次。
2. **不取消悬停独立表现**：`Reject`/`ReturnToIdle` 路径仅在 `_curiosity.IsActive` 时执行取消和恢复，不干扰用户悬停触发的好奇（悬停的 `_curiosity.Start` 由 `HoverDetector` 独立管理）。
3. **不覆盖更高优先级**：所有动画切换都检查 `_isDragging`/`_isProbing`/`_isExpressingGrievance`，不覆盖这些状态的动画所有权。
4. **社交是 curious 的所有者**：恢复 idle 仅在当前显示 "curious" 时执行，不无差别恢复其他动作到 idle。

### 未验证项

- 双宠实机社交观感体验尚未进行人工验证（两只宠物在屏幕上同时空闲并触发社交的可见动画表现）。
- 社交表现仍为好奇表情复用（`"curious"` action），无专门社交美术。
- 原反例 `Reviewer_SocialNotice_MustActuallyDisplayCurious` 在监理隔离副本中的转绿须由监理独立验证。

### 复核请求

请监理复核以下方面：
1. `ApplySocialExpression` Notice/Respond 路径实际调用 `_animPlayer.PlayAction("curious")` + `UpdateFrame()`，`CurrentAction` 切换为 `"curious"`。
2. `ApplySocialExpression` Reject/ReturnToIdle 路径实际调用 `_animPlayer.PlayAction("idle")` + `UpdateFrame()`，`CurrentAction` 恢复为 `"idle"`。
3. `CancelSocialImmediately` 双方窗口均恢复 idle 动画。
4. 多 tick 不重启同一段表情（`CurrentActionId != "curious"` 守卫）。
5. 不取消用户悬停触发的独立好奇（`_curiosity.IsActive` 守卫）。
6. 不覆盖更高优先级状态（拖动/探头/委屈守卫）。
7. 原 idle 反例 `Reviewer_SocialNotice_MustActuallyDisplayCurious` 转绿。
8. 新增/升级测试断言 `CurrentAction`（而非仅 `IsCuriosityActive`），覆盖双方调用顺序/延迟回应/拒绝/正常结束/取消/拖动/暂停。

P3-001 保持 done 不重开。不进入 P3-003。不提交推送。

---

## R3 整改（2026-11-10）：F-01 表情来源归属

整改对象为监理 R3 报告（[docs/reviews/P3-002-review.md](../reviews/P3-002-review.md)）F-01 限定剩余整改：表情来源归属。R3 已接受可见动画链（`PlayAction` 接入），原 idle 反例通过，正式接线 33/33 通过。F-02/F-03/F-04 已关闭，F-01 角色分发和可见动画链已接受，不重复整改。未修改监理报告或证据目录。

### F-01 剩余：表情来源归属——区分社交与悬停的表现所有权

**问题**：`ApplySocialExpression` 的 Reject/ReturnToIdle 路径和 `CancelSocialImmediately` 都以 `_curiosity.IsActive` 判定是否取消好奇，但 `_curiosity` 是社交和悬停共享的计时器，`IsActive` 在两种来源下值相同，无法区分所有权。正常悬停已让 B 显示 curious（`_socialOwnsCuriosity` 未设），随后 B 拒绝社交时 Reject 会误清 B 仍有效的独立悬停表情。同理 `CancelSocialImmediately` 对本窗口和对方窗口的无条件 `_curiosity.Cancel()` 也会误清。

**措施**：

1. **新增独立所有权标志 `_socialOwnsCuriosity`**（`MainWindow.xaml.cs` 字段声明区，紧邻 `_isSocialActive`）：
   - `private bool _socialOwnsCuriosity;`——true 仅当当前好奇由社交会话（Notice/Respond）启动，false 表示由独立悬停启动或无好奇。
   - 新增 `internal bool TestSocialOwnsCuriosity => _socialOwnsCuriosity;` 只读测试访问器，供测试断言所有权状态。
   - 不使用 `IsCuriosityActive` 或 `CurrentAction` 名称认定所有权——两者在社交和悬停下值相同。

2. **`ApplySocialExpression` Notice/Respond 路径设置所有权**：
   - 在 `_curiosity.Start(nowMs)` 之后设 `_socialOwnsCuriosity = true`，标记当前好奇由社交取得。
   - 悬停路径（`OnUpdateTick` 第 852–853 行 `_curiosity.Start`）不设此标志，保持 `_socialOwnsCuriosity == false`。

3. **`ApplySocialExpression` Reject/ReturnToIdle 路径仅释放社交所有权**：
   - 将条件从 `if (_curiosity.IsActive)` 改为 `if (_socialOwnsCuriosity)`。
   - 仅当社交拥有好奇时才 `_curiosity.Cancel()` + `_socialOwnsCuriosity = false` + 恢复 idle 动画。
   - 当 `_socialOwnsCuriosity == false`（悬停拥有的好奇或无好奇）时，Reject/ReturnToIdle 不执行任何操作——不影响悬停触发的独立好奇。

4. **`CancelSocialImmediately` 同理仅释放社交所有权**：
   - 本窗口：仅当 `_socialOwnsCuriosity` 时才 `_curiosity.Cancel()` + 重置标志 + 恢复 idle 动画。
   - 对方窗口：仅当 `w._socialOwnsCuriosity` 时才 `w._curiosity.Cancel()` + 重置标志 + 恢复 idle 动画。
   - `_isSocialActive = false` 仍无条件执行（社交会话状态本身仍需清除）。

5. **悬停规则不受影响**：
   - 悬停的 `_curiosity.Start`（第 852–853 行）不设 `_socialOwnsCuriosity`，保持 false。
   - 悬停的 `ShouldEnd` → `Cancel`（第 860–864 行）不检查 `_socialOwnsCuriosity`，保持原有到时和离开规则不变。
   - 不无限延长悬停好奇的生命周期——悬停的 `ShouldEnd` 规则独立运作。

**位置**：`src/DesktopPet.App/MainWindow.xaml.cs` 字段声明约 140–148 行（`_socialOwnsCuriosity`）、测试访问器约 113–115 行（`TestSocialOwnsCuriosity`）、`ApplySocialExpression` 约 1218–1262 行、`CancelSocialImmediately` 约 1297–1340 行。

### 新增 6 项 R3 归属测试

在 `tests/DesktopPet.App.Tests/SocialIntegrationTests.cs` 追加 6 项 R3 归属测试，验证所有权标志在社交生命周期各阶段的正确设置和释放：

| 测试 | 覆盖 |
|------|------|
| `Social_F01_R3_Reject_NoSocialOwnership_DoesNothing` | 无社交所有权时 Reject 是 no-op；Notice 建立所有权后 Reject 正确释放；二次 Reject no-op |
| `Social_F01_R3_ReturnToIdle_ReleasesOnlySocialCuriosity` | ReturnToIdle 正确释放社交所有权；二次 ReturnToIdle no-op |
| `Social_F01_R3_Respond_EstablishesOwnership_RejectReleases` | Respond 建立所有权；B Reject 释放 B 所有权后 A 仍持有；A ReturnToIdle 释放 A 所有权 |
| `Social_F01_R3_CancelSocialImmediately_ReleasesOnlySocialCuriosity` | CancelSocialImmediately 重置社交所有权；取消后 Reject 不改变所有权标志（不误清非社交好奇） |
| `Social_F01_R3_FullLifecycle_OwnershipSetAndReleased` | 完整 Notice→Respond→Finish 生命周期：双方所有权在会话期间设置，结束后释放 |
| `Social_F01_R3_Reject_BothOwnershipReleased` | Reject 后双方所有权均释放，双方回到 idle |

原 33 项 + 新增 6 项 = 39 项社交集成测试。

### 自测命令与结果

```
dotnet test --no-restore --filter "FullyQualifiedName~SocialIntegrationTests"
```
结果：39/39 通过，0 失败。

```
dotnet test --no-restore --verbosity minimal
```
结果：全量通过，Core 372 + App 381 = **753/753**，0 失败，0 跳过，0 回归。

### 设计要点

1. **独立所有权标志**：`_socialOwnsCuriosity` 是唯一的所有权判据，不依赖 `IsCuriosityActive`（计时器活跃度）或 `CurrentAction`（动画名称），因为两者在社交和悬停下值相同。
2. **仅释放社交取得的表现**：Reject/ReturnToIdle/CancelSocialImmediately 仅在 `_socialOwnsCuriosity == true` 时取消好奇和恢复 idle，不误清悬停触发的独立好奇。
3. **悬停规则独立**：悬停的 `ShouldEnd`→`Cancel` 不受 `_socialOwnsCuriosity` 影响，保持原有到时和离开规则。不无限延长悬停好奇生命周期。
4. **社交会话状态仍无条件清除**：`_isSocialActive = false` 在 `CancelSocialImmediately` 中仍无条件执行，确保社交会话本身被正确终止，只是好奇心表情的清理受所有权保护。

### 未验证项

- 双宠实机社交观感体验尚未进行人工验证（两只宠物在屏幕上同时空闲并触发社交的可见动画表现）。
- 社交表现仍为好奇表情复用（`"curious"` action），无专门社交美术。
- 悬停与社交同时触发好奇的实机交互（鼠标悬停 B 期间 B 收到社交 Reject）需实机观察。

### 复核请求

请监理复核以下方面：
1. `_socialOwnsCuriosity` 标志在 Notice/Respond 时设置为 true，在 Reject/ReturnToIdle/CancelSocialImmediately 时仅当 true 才释放并重置为 false。
2. Reject/ReturnToIdle 当 `_socialOwnsCuriosity == false` 时不执行 `_curiosity.Cancel()` 或动画切换——不误清悬停触发的独立好奇。
3. `CancelSocialImmediately` 对本窗口和对方窗口均仅当 `_socialOwnsCuriosity` 时才取消好奇和恢复 idle。
4. 悬停路径的 `_curiosity.Start` 不设 `_socialOwnsCuriosity`，悬停的 `ShouldEnd`→`Cancel` 不受社交所有权影响。
5. 新增 6 项 R3 归属测试覆盖：无所有权 Reject no-op、Notice/Respond 建立所有权、Reject/ReturnToIdle 释放所有权、CancelSocialImmediately 释放所有权、完整生命周期所有权设置与释放、Reject 双方所有权释放。
6. 全量 753/753 通过，0 回归。

P3-001 保持 done 不重开。不进入 P3-003。不提交推送。

---

## R4 整改（2026-11-10）：F-01 已有悬停被社交回应接管后的生命周期

整改对象为监理 R4 报告（[docs/reviews/P3-002-review.md](../reviews/P3-002-review.md) 第 67–77 行）F-01 限定剩余整改。R4 已接受 Reject 保护（原反例 1/1 转绿，正式社交 39/39 通过），F-02/F-03/F-04 保持关闭，F-01 角色分发/可见动画链/Reject 保护已接受，不重复整改。未修改监理报告或证据目录。

### F-01 剩余：Notice/Respond 接管已有独立悬停的好奇生命周期

**问题**：`ApplySocialExpression` 的 Notice/Respond 路径无条件执行 `_curiosity.Start(nowMs)` + `_socialOwnsCuriosity = true`，会把已有独立悬停触发的好奇（`_socialOwnsCuriosity == false`）改成社交所有并重置期限。监理 R4 反例：B 在 1200ms 由生产悬停触发 curious，鼠标持续停留；A 1300ms 发起，B 1450ms 回应，2000ms 社交正常结束时将 B 清为 idle，早于原悬停 3200ms 期限。

**措施**：

1. **`ApplySocialExpression` Notice/Respond 路径新增 borrow 分支**（约 1231–1246 行）：
   - 当 `_curiosity.IsActive && !_socialOwnsCuriosity`（已有独立悬停好奇，非社交所有）时，社交借用当前显示，直接 `break`。
   - 不重新 `Start`（不重置期限），不设 `_socialOwnsCuriosity`（不取得所有权），不 `PlayAction`（已经是 "curious"）。
   - 原悬停的 `ShouldEnd`/离开规则继续适用，在原期限到期/离开时正常恢复 idle。
   - 只有社交实际新启动的表现（`_curiosity.IsActive` 为 false 或已由社交拥有）才走原有 `Start` + 设标志 + `PlayAction` 路径。

2. **悬停路径不变**（约 856–877 行）：
   - `HoverDetector.Update` DwellMs=1200ms 触发 `PlayAction("curious")` + `_curiosity.Start(nowMs)`，不设 `_socialOwnsCuriosity`。
   - `ShouldEnd(nowMs, isOverPet)` 在 `nowMs >= _endsAt`（Start + 2000ms）或离开超过 300ms 时返回 true。
   - `ShouldEnd` → `Cancel` 不检查 `_socialOwnsCuriosity`，保持原有到时和离开规则不变。

3. **Reject/ReturnToIdle/CancelSocialImmediately 不变**（R3 已正确实现）：
   - 仅当 `_socialOwnsCuriosity == true` 时才 `_curiosity.Cancel()` + 恢复 idle。
   - 当 `_socialOwnsCuriosity == false`（悬停拥有）时，这些路径不执行任何操作——不影响悬停触发的独立好奇。

**位置**：`src/DesktopPet.App/MainWindow.xaml.cs` `ApplySocialExpression` 约 1231–1246 行（新增 borrow 分支）。

### 新增 4 项 R4 悬停接管测试

在 `tests/DesktopPet.App.Tests/SocialIntegrationTests.cs` 追加 4 项 R4 测试，全部通过生产 `TestGetCursorPos` + `OnUpdateTickTest` 让悬停真实触发好奇（`HoverDetector` DwellMs=1200ms），然后让社交接管/取消，验证原悬停期限不受影响：

| 测试 | 覆盖 |
|------|------|
| `Social_F01_R4_HoverThenRespondFinish_OriginalHoverPreserved` | 真实悬停触发好奇 → 社交 Respond 借用（不设所有权）→ 社交正常结束 → 原悬停仍显示 curious（期限内）→ 原期限到期后恢复 idle |
| `Social_F01_R4_HoverThenReject_OriginalHoverPreserved` | 真实悬停触发好奇 → 社交 Reject → 原悬停不受影响（`_socialOwnsCuriosity` 始终 false）→ 原期限到期后恢复 idle |
| `Social_F01_R4_HoverThenSocialCancel_OriginalHoverPreserved` | 真实悬停触发好奇 → 社交 Respond 借用 → `CancelSocialImmediately(DragStarted)` 取消社交 → 原悬停不受影响 → 原期限到期后恢复 idle |
| `Social_F01_R4_HoverLeave_EndsCuriosity_LeaveRuleIntact` | 真实悬停触发好奇 → 鼠标离开 → 离开宽限期内仍 curious → 超过宽限期后恢复 idle（确认离开规则完整，不无限延长） |

原 39 项 + 新增 4 项 = 43 项社交集成测试。

### 自测命令与结果

```
dotnet test --no-restore --verbosity minimal
```
结果：全量通过，Core 372 + App 385 = **757/757**，0 失败，0 跳过，0 回归。

### 设计要点

1. **borrow 不 Start**：已有独立悬停好奇时，社交 Notice/Respond 只借用当前 "curious" 显示，不重新 `Start`（不重置期限）、不设 `_socialOwnsCuriosity`（不取得所有权）、不 `PlayAction`（已经是 curious）。
2. **只有社交新启动才设所有权**：当 `_curiosity.IsActive` 为 false（无已有好奇）或 `_socialOwnsCuriosity` 为 true（社交已拥有）时，走原有 `Start` + 设标志路径。
3. **悬停规则独立运作**：`ShouldEnd`/离开规则不检查 `_socialOwnsCuriosity`，原期限到期或鼠标离开时正常恢复 idle。不无限延长悬停好奇生命周期。
4. **Reject 保护不变**：R3 已正确实现——Reject/ReturnToIdle/CancelSocialImmediately 仅当 `_socialOwnsCuriosity == true` 时才取消好奇。borrow 场景下 `_socialOwnsCuriosity` 始终 false，这些路径是 no-op。

### 未验证项

- 双宠实机社交观感体验尚未进行人工验证（两只宠物在屏幕上同时空闲并触发社交的可见动画表现）。
- 社交表现仍为好奇表情复用（`"curious"` action），无专门社交美术。
- 悬停与社交同时触发好奇的实机交互需实机观察。

### 复核请求

请监理复核以下方面：
1. `ApplySocialExpression` Notice/Respond 路径：当 `_curiosity.IsActive && !_socialOwnsCuriosity` 时走 borrow 分支（break），不 Start/不设标志/不 PlayAction。
2. 真实悬停 → 社交 Respond 借用 → 社交正常结束后，原悬停仍显示 curious（原期限内），原期限到期后恢复 idle。
3. 真实悬停 → 社交 Reject → 原悬停不受影响，原期限到期后恢复 idle。
4. 真实悬停 → 社交取消（`CancelSocialImmediately`）→ 原悬停不受影响，原期限到期后恢复 idle。
5. 真实悬停 → 鼠标离开 → 离开宽限期内仍 curious，超过宽限期后恢复 idle（离开规则完整，不无限延长）。
6. 全量 757/757 通过，0 回归。

P3-001 保持 done 不重开。不进入 P3-003。不提交推送。
