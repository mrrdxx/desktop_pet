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
