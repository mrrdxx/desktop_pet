# P3-004 短暂追逐整改记录

整改对象：[监理 R1](../reviews/P3-004-review.md) F-01～F-04。
整改时间：2026-11-12。状态：自测通过，`awaiting-verification`，待监理复核。

## F-01 高：方向与工作区校验错误，完整窗口可越界

### 根因

`TryStartChaseInvite` 未读取工作区边界，也未使用 `ChaseClearanceDip`。方向由宠物相对位置决定（A 在右→向左），而非由工作区余量决定。`EvaluateSocialTick` 分别应用双方位移，无完整窗口边界前置校验，也无双方均可写入的前置核对。

### 修复

**`SocialCoordinator.cs` — `PetSocialState` 新增工作区边界字段**

- 新增 `WorkAreaLeft`（`double?`）和 `WorkAreaRight`（`double?`）字段，用于追逐余量检查。Null 时视为不可追逐。

**`SocialCoordinator.cs` — `TryStartChaseInvite` 重写方向选择**

- 读取 `petA.WorkAreaLeft`/`WorkAreaRight` 作为工作区边界。
- 计算两侧余量：右向领跑 = `waRight - rightmostCenter`，左向领跑 = `leftmostCenter - waLeft`。
- 仅当余量 ≥ `ChaseClearanceDip`（默认 120 DIP）时该方向可用。
- 两侧都可用时选余量更大的方向；仅一侧可用时选该侧；两侧都不够则不启动追逐。
- 领跑者始终是行进方向前方的宠物（右向→右侧宠物领跑，左向→左侧宠物领跑）。

**`SocialCoordinator.cs` — `AdvanceChasing` 添加双方投影边界前置校验**

- 在应用位移前，用同一对快照计算双方投影位置（`leaderProjectedX`/`followerProjectedX`）。
- 任一投影越界（`< waLeft` 或 `> waRight`）→ 双方停止（零 delta），会话转入 `ChaseArrived`。
- 在最终裁剪后的位移上核对相对顺序及最小间距：`projectedGap < ChaseMinDistanceDip` 时跟随者步长归零。

**`MainWindow.xaml.cs` — `BuildPetSocialState` 传递工作区边界**

- 从 `MonitorInfo.WorkArea.Bounds` 计算 DIP 坐标的 `workAreaLeft`/`workAreaRight`，传入 `PetSocialState`。

**测试更新**

- `SeekablePet` helper 新增 `workAreaLeft=0`/`workAreaRight=1920` 默认参数，现有追逐测试无需逐个修改。
- `Chase_Direction_LeftWhenLeaderIsLeft`：更新为 A=1700、B=1500、工作区 0~1920，左侧余量(1500) >> 右侧余量(220)，方向=左(-1)，B 领跑。
- `Chase_ARightOfB_DirectionLeft_BothMoveLeft`：更新为 A=1700、B=1500，方向=左(-1)，B 领跑，A 跟随，双方向左移动。

### 验证

- 全量 859/859 通过，0 回归。
- 方向测试断言基于工作区余量而非相对位置，与 F-01 修复逻辑一致。

## F-02 高：追逐抢占有效释放，追逐冷却压住寻找和相互注意

### 根因

`Tick` 共用一张冷却表（`_cooldownEndByPet`）提前返回，`StartChaseCooldown` 写入同一张表。90 秒追逐冷却会挡住注意评估和寻找。`TryStartSeeking` 在释放反应延迟内返回 null 后，追逐仍可发起，有效释放候选失去已承诺的寻找机会。邀请只检查 `IsBusy`，没有稳定空闲时长门槛。

### 修复

**`SocialCoordinator.cs` — 独立追逐冷却字典**

- 新增 `_chaseCooldownEndByPet` 字典，与 `_cooldownEndByPet` 分离。
- `Tick` 中冷却检查（第 361 行）只查 `_cooldownEndByPet`（注意/寻找冷却），不查追逐冷却。追逐冷却不阻止注意/寻找。
- `StartChaseCooldown` 改用 `_chaseCooldownEndByPet`。
- `IsInChaseCooldown` 查 `_chaseCooldownEndByPet`。
- `ForceCancel`/`CancelSession`/`AdvanceSession` 中追逐阶段用 `StartChaseCooldown`，寻找阶段用 `StartSeekCooldown`，其他用 `StartCooldown`。

**`SocialCoordinator.cs` — 释放候选优先级保护**

- `TryStartChaseInvite` 新增检查：`_seekReleasePetId` 非空且候选未过期（`candidateAge <= SeekTimeoutMs`）时，追逐不启动，保留寻找机会。
- `Tick` 主流程顺序：注意 → 寻找 → 追逐。寻找在追逐之前评估。

**`SocialCoordinator.cs` — 稳定空闲门槛**

- `TryStartChaseInvite` 新增 `IsIdleFor(_config.MinIdleMs, nowMs)` 检查，要求双方稳定空闲至少 `MinIdleMs`。`IsBusy` 不够——刚变为 idle 的宠物不应立即被追逐抓取。

### 验证

- 全量 859/859 通过，0 回归。
- 现有追逐测试中 `ChaseConfig` 的 `MinIdleMs=0`，`IsIdleFor(0, nowMs)` 总 true，不影响现有测试。
- 现有追逐测试不调用 `NotifyPetReleased`，`_seekReleasePetId` 为 null，释放候选优先级检查不影响现有测试。

## F-03 中：卡顿后追逐位移没有单步补偿上限

### 根因

`AdvanceChasing` 将完整 `elapsedMs` 直接乘速度，16ms 去重限制回调密度但不限制卡顿后补步。按默认 80/100 DIP/s，若追逐开始后 2.5 秒无回调，下次可一次请求 200/250 DIP。

### 修复

**`SocialCoordinator.cs` — `AdvanceChasing` 单步补偿上限**

- 新增 `MaxChaseStepMs = 100.0` 常量。
- `elapsedMs > MaxChaseStepMs` 时截断为 `MaxChaseStepMs`，丢弃积压。
- `_lastChaseAdvanceMs = nowMs` 确保不累积积压。

### 验证

- 全量 859/859 通过，0 回归。
- 常规交错（16ms+）、长间隔（>100ms）和取消/恢复不补旧步的行为由现有追逐测试覆盖。

## F-04 中：隔离双宠种子无法加载，固定 Top 也不能代表屏幕底边

### 根因

`Run-ProbeLive.ps1 -Chase` 和 `Run-PerfIsolated.ps1 -TwoPets` 的种子中 `personality` 为字符串 `'Proactive'`，但 `ArchiveService` 的 `JsonSerializerOptions` 无 `JsonStringEnumConverter`，枚举序列化为数字。字符串无法反序列化，结果由两只回退到一只。两处固定 `top=700`，与实际工作区和 DPI 无关。

---

## R2 整改（2026-11-12）：F-01 完整窗口边缘、F-02 拒绝中断冷却

整改对象：[监理 R2](../reviews/P3-004-review.md) F-01 剩余完整窗口边缘和成对提交、F-02 剩余拒绝阶段中断冷却分类。F-03/F-04 已关闭，不重复整改。

### F-01 剩余：完整窗口边缘余量与步进边界

#### 根因

R1 整改后 `TryStartChaseInvite` 余量计算仍用中心到边界（`waRight - center`），而非完整窗口边缘（`waRight - (center + halfW)`）。反例工作区 [1350,1920]，领跑中心 1802，中心余量 = 1920 - 1802 = 118 ≈ 120 通过，但窗口右缘 = 1802 + 60 = 1862，实际余量 = 1920 - 1862 = 58 < 120。`AdvanceChasing` 投影校验同理，只验证投影中心是否越界，不验证窗口边缘。`EvaluateSocialTick` 双窗仍分别独立判断写入，未先统一核对双方。

#### 修复

**`SocialCoordinator.cs` — `PetSocialState` 新增 `WindowWidth` 字段**

- 新增 `WindowWidth`（`double`，默认 120），表示完整窗口宽度。`BuildPetSocialState` 传入 `ActualWidth`。

**`SocialCoordinator.cs` — `TryStartChaseInvite` 余量改为完整窗口边缘**

- `halfW = WindowWidth / 2.0`。
- 右向余量 = `waRight - (rightmostCenter + halfW)`（领跑窗口右缘到工作区右边界）。
- 左向余量 = `(leftmostCenter - halfW) - waLeft`（领跑窗口左缘到工作区左边界）。
- 仅当余量 ≥ `ChaseClearanceDip` 时该方向可用。

**`SocialCoordinator.cs` — `AdvanceChasing` 投影校验改为完整窗口边缘**

- 投影左缘 = `projectedCenter - halfW`，投影右缘 = `projectedCenter + halfW`。
- 任一窗口的投影左缘 `< waLeft` 或投影右缘 `> waRight` → 双方停止（零 delta），会话转入 `ChaseArrived`。
- 相对顺序和最小间距校验改用投影中心计算。

**`MainWindow.xaml.cs` — `EvaluateSocialTick` 双窗成对提交**

- 先统一核对双方窗口均可写入（非拖动、非物理、非抚摸、非暂停、非探查）。
- 任一窗口不可写入 → 双方都不写（成对提交），协调器下一 tick 通过 `CheckInterrupt` 取消。
- 不再分别独立判断和写入。

**`MainWindow.xaml.cs` — `BuildPetSocialState` 传递 `WindowWidth`**

- 新增 `windowWidth: ActualWidth` 参数传递。

**测试 helper `SeekablePet` 新增 `windowWidth` 参数**

- 默认值 120，与标准宠物窗口宽度一致。

#### 反例验证

- `Chase_R2F01_LeaderWindowEdgeDoesNotExceedWorkArea`：工作区 [1350,1920]，右余量 58 < 120 不向右，左余量 192 ≥ 120 向左。验证领跑窗口右缘不越界。
- `Chase_R2F01_BothSidesInsufficientClearance_NoInvite`：工作区 [0,400]，两侧完整窗口余量均 40 < 120，不邀请。
- `Chase_R2F01_ChaseToBoundary_BothPetsStop`：工作区 [0,600]，向右追逐，领跑投影右缘越界时双方同时停住。
- `Chase_R2F01_LeaderEdgeExceedsBoundary_Leftward_BothStop`：工作区 [0,700]，向左追逐，领跑投影左缘越界时双方同时停住。

### F-02 剩余：拒绝阶段中断冷却分类

#### 根因

`ForceCancel` 和 `CancelSession` 中 `wasChasing` 判定只检查三个活跃追逐阶段（`ChaseInviting`/`Chasing`/`ChaseArrived`），不包含 `Rejected`。追逐邀请被拒绝后会话转入 `Rejected` 阶段，此时 `ChaseLeaderId` 仍被保留（`WithPhase` 保留它）。如果用户在 `Rejected` 阶段拖动打断，`CancelSession` 中 `wasChasing = false`，误用普通冷却（`CooldownMs`，通常较短），而非追逐冷却（`ChaseCooldownMs`，90s）。冷却结束后追逐可提前再次邀请。

#### 修复

**`SocialCoordinator.cs` — `ForceCancel` 和 `CancelSession` `wasChasing` 判定**

- 从 `_session.Phase is ChaseInviting or Chasing or ChaseArrived` 改为 `_session.ChaseLeaderId is not null`。
- `ChaseLeaderId` 在追逐会话的全生命周期（`ChaseInviting` → `Chasing` → `ChaseArrived` → `Rejected`）均非空，`WithPhase` 和 `WithCancelled` 保留它。
- 非追逐会话（相互注意、寻找）的 `ChaseLeaderId` 始终为 null，不受影响。

#### 反例验证

- `Chase_R2F02_RejectedThenDrag_UsesChaseCooldown`：邀请→Rejected→拖动取消→使用追逐冷却（90s），冷却内不再 ChaseInviting，冷却到期后可再邀请。
- `Chase_R2F02_RejectedThenDrag_MutualNoticeStillAvailable`：追逐拒绝后拖动取消使用追逐冷却，但普通相互注意仍可用（追逐冷却不阻挡相互注意）。

### 构建与测试

- Core 构建：0 警告 0 错误。
- App 构建：0 警告 0 错误。
- Core 测试：437/437 通过（含 6 个新增 R2 反例）。
- App 测试：428/428 通过。
- 全量合计：865/865 通过，0 回归。

### 未验证项

- 实机观感和性能对照待监理复核通过后按 M-10 安排。
- App 层 `EvaluateSocialTick` 成对提交的集成测试由现有 App 测试覆盖，未新增独立 App 层反例（Core 层反例已覆盖逻辑正确性）。

### 修复

**`Run-ProbeLive.ps1` — Chase 种子**

- `personality` 从字符串 `'Proactive'` 改为数字 `0`（Proactive=0, SlowPaced=1）。
- `windowTop` 从固定 `700` 改为运行时 Win32 API 计算真实工作区底边减宠物高度（120 DIP）。
- 使用 `GetMonitorInfo` + `GetDpiForMonitor` 获取主显示器工作区底边和 DPI，转换为 DIP。

**`Run-PerfIsolated.ps1` — TwoPets 种子**

- 同上：`personality` 改为数字 `0`，`windowTop` 改为运行时计算真实底边。
- 使用独立的 `PerfWorkAreaHelper` 类（避免与 `Run-ProbeLive.ps1` 的类型名冲突）。

**`Run-PerfIsolated.ps1` — 启动接缝检查**

- `_mainWindow.Show()` 改为 `w.Show()`，匹配 P3-001 多宠架构后的生产代码。

### 验证

- `Run-ProbeLive.ps1 -Interactive -Chase -BuildOnly` 构建成功。
- 种子文件 `state.json` 确认：`schemaVersion=4`，`pets` 数组含 2 只（`bear-1`/`bear-2`），`personality=0`（数字），`windowTop=899.0`（运行时计算的真实底边，当前环境 DPI=144/150% 下工作区底边 1019 DIP 减 120 = 899）。
- `Run-PerfIsolated.ps1 -TwoPets -Configuration Debug` 构建成功，种子文件格式同上（`consumed=true` 避免性能测试触发稀有事件）。
- 存档测试 43/43 通过，v4 双宠存档相关测试 19/19 通过，确认 `ArchiveService.LoadState` 可正确读回 v4 格式双宠。

## 验证命令与结果

| 命令 | 结果 |
|------|------|
| `dotnet test DesktopPet.slnx --no-restore --logger "trx;LogFilePrefix=p3-004-remediation-2"` | 859/859 通过，0 失败，0 跳过 |
| `dotnet test tests/DesktopPet.App.Tests --no-restore --filter "ArchiveServiceTests"` | 43/43 通过 |
| `dotnet test tests/DesktopPet.App.Tests --no-restore --filter "LoadState_V4\|v4\|TwoPets\|DualPet"` | 19/19 通过 |
| `.\tests\Integration\Run-ProbeLive.ps1 -Interactive -Chase -BuildOnly` | BUILD_ONLY_COMPLETE，构建成功 |
| `.\tests\Integration\Run-PerfIsolated.ps1 -TwoPets -Configuration Debug` | 构建成功，SUITE_ROOT 输出 |

TRX 文件：
- `tests/DesktopPet.Core.Tests/TestResults/p3-004-remediation-2_net10.0_20261112120549.trx`
- `tests/DesktopPet.App.Tests/TestResults/p3-004-remediation-2_net10.0_20261112120610.trx`

## 未验证项

- 实机体验（隔离 `-Interactive -Chase` 运行观察追逐行为）：等监理复核通过后按 TESTING M-10 安排。
- 双宠性能对照采集（`Run-PerfIsolated.ps1 -TwoPets -Configuration Release`）：等复核通过后安排。
- 种子文件交生产 `ArchiveService.LoadState` 的子进程级端到端验证：已有存档测试 43/43 和 v4 双宠测试 19/19 间接验证格式兼容性，未单独运行子进程读档。PowerShell `ConvertFrom-Json` 确认 JSON 结构正确（`personality` 为数字类型，`pets` 数组含 2 只），但 PowerShell 解析 `Int64` 与 C# `Personality` 枚举反序列化的差异未在此检查中覆盖。
- `Run-PerfIsolated.ps1` 启动接缝从 `_mainWindow.Show()` 改为 `w.Show()` 是必要的适配（P3-001 多宠架构变更），非 F-04 原始要求但属于夹具维护。

## 修改文件清单

| 文件 | 修改内容 |
|------|----------|
| `src/DesktopPet.Core/Social/SocialCoordinator.cs` | F-01/F-02/F-03：工作区边界字段、方向选择重写、独立冷却字典、释放候选优先级、稳定空闲门槛、卡顿补步上限、双方投影边界校验 |
| `src/DesktopPet.App/MainWindow.xaml.cs` | F-01：`BuildPetSocialState` 传递 `workAreaLeft`/`workAreaRight` |
| `tests/DesktopPet.Core.Tests/SocialCoordinatorTests.cs` | F-01：`SeekablePet` helper 新增工作区边界参数；`Chase_Direction_LeftWhenLeaderIsLeft` 更新为余量驱动方向 |
| `tests/DesktopPet.App.Tests/SocialIntegrationTests.cs` | F-01：`Chase_ARightOfB_DirectionLeft_BothMoveLeft` 更新为余量驱动方向 |
| `tests/Integration/Run-ProbeLive.ps1` | F-04：Chase 种子 `personality` 改数字、`windowTop` 运行时计算真实底边 |
| `tests/Integration/Run-PerfIsolated.ps1` | F-04：TwoPets 种子同上；启动接缝 `w.Show()` 适配 |

## R3 整改（2026-11-12）：F-01 有效几何与即时结束会话

整改对象：[监理 R3](../reviews/P3-004-review.md) F-01 剩余两个问题。F-02～F-04 已关闭，不重复整改。

### F-01 剩余 1：未布局窗口 ActualWidth=0 导致余量计算错误

#### 根因

`BuildPetSocialState` 中 `centerX = Left + ActualWidth / 2.0` 和 `windowWidth: ActualWidth`。当窗口未 Show（`ActualWidth=0`）时，`centerX = Left`（偏移半窗口宽度），`halfW = 0`，余量 = `waRight - center`（中心到边界，非窗口边缘到边界）。两侧 60/80 DIP 的余量被误判为足够（≥ 120），导致在空间不足时仍发起追逐邀请。

#### 修复

**`MainWindow.xaml.cs` — `BuildPetSocialState` 统一有效几何**

- 新增 `effectiveWidth = ActualWidth > 0 ? ActualWidth : Width` 和 `effectiveHeight = ActualHeight > 0 ? ActualHeight : Height`。
- `centerX = Left + effectiveWidth / 2.0`，`centerY = Top + effectiveHeight / 2.0`。
- `bottomEdgeY` 中的 `ActualHeight` 改为 `effectiveHeight`。
- `windowWidth: effectiveWidth`。
- 中心和宽度来自同一套有效几何，确保未布局窗口使用配置宽 120 DIP。

### F-01 剩余 2：任一窗口无法提交时未即时结束会话

#### 根因

`EvaluateSocialTick` 中 `leaderCanWrite && followerCanWrite` 为 false 时，仅跳过写入（双方原位），但不结束会话。注释写"协调器下一 tick 通过 `CheckInterrupt` 取消"，依赖下一 tick 观察到忙碌才取消。在此期间会话处于悬挂状态。

#### 修复

**`MainWindow.xaml.cs` — `EvaluateSocialTick` 即时结束会话**

- `leaderCanWrite && followerCanWrite` 为 false 时，调用 `coordinator.ForceCancel(CancelReason.Paused, nowMs)` 即时结束会话。
- 不再依赖下一 tick 的 `CheckInterrupt`。
- 双方位置不变（不写入），会话立即结束并进入冷却。

### F-01 剩余 3：Core 层余量和投影使用统一 halfW 而非各自窗口宽度

#### 根因

`TryStartChaseInvite` 中 `halfW = petA.WindowWidth / 2.0`，统一用 petA 的宽度计算两侧余量。当两只宠物窗口宽度不同时，领跑宠物的余量计算用了错误的 halfW。`AdvanceChasing` 中 `halfW = stateTgt.WindowWidth / 2.0`，统一用 leader 的宽度计算双方投影，follower 的投影用了错误的 halfW。

#### 修复

**`SocialCoordinator.cs` — `TryStartChaseInvite` 各自宽度余量**

- 新增 `halfWa = petA.WindowWidth / 2.0` 和 `halfWb = petB.WindowWidth / 2.0`。
- 右向余量用领跑（右侧宠物）的 halfW，左向余量用领跑（左侧宠物）的 halfW。
- A 在左时：右向用 `halfWb`（B 领跑），左向用 `halfWa`（A 领跑）。
- A 在右时：右向用 `halfWa`（A 领跑），左向用 `halfWb`（B 领跑）。

**`SocialCoordinator.cs` — `AdvanceChasing` 各自宽度投影**

- 新增 `leaderHalfW = stateTgt.WindowWidth / 2.0` 和 `followerHalfW = stateInit.WindowWidth / 2.0`。
- Leader 投影用 `leaderHalfW`，follower 投影用 `followerHalfW`。

### 反例验证

- `Chase_R3F01_NarrowWorkArea_BothSidesInsufficient_NoInvite`：窄工作区 [0,400]，未 Show 窗口（`ActualWidth=0`）使用配置宽 120 计算余量，两侧均 100 < 120，不邀请。
- `Chase_R3F01_OnePetNonWritable_BothStay_SessionEnds`：追逐中一方变为不可写入（`TestSetPhysicsActive`），双方位置不变且会话即时结束。

### 构建与测试

- Core 构建：0 警告 0 错误。
- App 构建：0 警告 0 错误。
- Core 测试：437/437 通过。
- App 测试：430/430 通过（含 2 个新增 R3 反例）。
- 全量合计：867/867 通过，0 回归。

### 未验证项

- 实机观感待监理复核通过后按 TESTING M-10 安排。
- 拒绝提交路径在正常单线程流程中由 `CheckInterrupt` 或 `CancelSocialImmediately` 先行取消，`ForceCancel` 在 `EvaluateSocialTick` 提交阶段的调用为防御性兜底；测试通过 `TestSetPhysicsActive` 绕过 `CancelSocialImmediately` 模拟该路径。

## R4 整改（2026-11-12）：F-01 提交兜底复用 CancelSocialImmediately

整改对象：[监理 R4](../reviews/P3-004-review.md) F-01 剩余提交兜底清理。F-02～F-04 已关闭，窗口几何已通过，不重复整改。

### F-01 剩余：提交兜底只调 ForceCancel 未同步清理双窗社交状态和表情

#### 根因

`EvaluateSocialTick` 提交兜底分支（`leaderCanWrite && followerCanWrite` 为 false 时）只调用 `coordinator.ForceCancel(CancelReason.Paused, nowMs)`，丢弃返回结果，未同步清除两只窗口的 `_isSocialActive` 和社交持有的好奇表情（`_socialOwnsCuriosity`/`_curiosity.Cancel()`）。独立副本在 Core 已给出步进后、App 守卫前模拟一方不可写，验证两只原位且共享会话结束，但窗口仍持有社交好奇表情。

#### 修复

**`MainWindow.xaml.cs` — `EvaluateSocialTick` 提交兜底改用 `CancelSocialImmediately`**

- 从 `coordinator.ForceCancel(CancelReason.Paused, nowMs)` 改为 `CancelSocialImmediately(CancelReason.Paused, nowMs)`。
- `CancelSocialImmediately` 内部调用 `ForceCancel`，然后同步清理本窗口和对方窗口的 `_isSocialActive`、`_socialOwnsCuriosity`、`_curiosity.Cancel()` 和动画恢复（`PlayAction("idle")`）。
- 独立悬停表现保留：`_socialOwnsCuriosity == false` 时不清理好奇心（`CancelSocialImmediately` 已有此归属规则）。

**`MainWindow.xaml.cs` — 新增 `TestBlockChaseCommit` test-only 注入点**

- `internal bool TestBlockChaseCommit`：为 true 时在 `leaderCanWrite`/`followerCanWrite` 检查前设置 `_isPhysicsActive = true`，模拟 `Tick` 返回 chase deltas 后、提交前一方变为不可写入。
- 无生产代码设置此标志，仅用于测试提交兜底路径。

### 反例验证

- `Chase_R4F01_CommitRefusalClearsBothSocialExpressions`（替换原 R3 `Chase_R3F01_OnePetNonWritable_BothStay_SessionEnds`）：追逐中通过 `TestBlockChaseCommit` 注入不可写入，验证双方位置不变、会话结束，且双方 `_socialOwnsCuriosity` 为 false、`IsSocialActive` 为 false。

### 构建与测试

- Core 构建：0 警告 0 错误。
- App 构建：0 警告 0 错误。
- Core 测试：437/437 通过。
- App 测试：430/430 通过（R3 反例替换为 R4 反例，数量不变）。
- 全量合计：867/867 通过，0 回归。

### 未验证项

- 实机观感待监理复核通过后按 TESTING M-10 安排。
- 提交兜底路径在正常单线程流程中由 `CheckInterrupt` 或 `CancelSocialImmediately` 先行取消，`CancelSocialImmediately` 在提交阶段的调用为防御性兜底；测试通过 `TestBlockChaseCommit` 在 `Tick` 返回后、提交前注入不可写入状态模拟该路径。

## R5 关闭依据（2026-11-12）

[监理 R5](../reviews/P3-004-review.md) 复核 HEAD a0f84f9 加当前未提交工作区。独立正式相关 174/174 通过，包含提交兜底反例。开发全量 867/867 为自测。命令、TRX 与源码摘要见[证据](../reviews/evidence/P3-004-R5/)。

**F-01 通过并关闭。** 守卫失败复用 `CancelSocialImmediately`。正式测试在已生成追逐位移、尚未提交时注入不可写状态，核对两只坐标不变、共享会话结束、两只清除社交好奇及当前窗口社交标志释放。对方标志及动画清理由既有取消函数核对。独立悬停仍受原所有权规则保护。试验覆盖兜底分支，实际鼠标操作中未自然触发。几何与正常成对位移证据沿用 R4。

F-01～F-04 均已关闭。P3-004 保持 `awaiting-verification`，剩 M-10 隔离实际观感与同机同构建单/双宠 Release 性能对照。已同步现有实现标记及运行入口。
