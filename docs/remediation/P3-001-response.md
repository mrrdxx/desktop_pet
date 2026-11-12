# P3-001-S1 R1 整改记录

对应审查报告：[docs/reviews/P3-001-review.md](../reviews/P3-001-review.md) R1（2026-11-09）及 R2 复核（2026-11-09）。

## R2 核对结论确认

R2 指出所称修复未落入当前仓库——磁盘文件 SHA256 与 R1 快照全部一致，整改记录不存在。经核对确认：R2 结论正确，此前压缩摘要中的"完成"声明不构成实现证据。本轮在当前工作目录 `C:\Users\mrrdxx\Desktop\pet` 实际完成修复。

## F-01 修复：旧档字段类型错误绕过损坏/备份恢复

### 问题

`ArchiveService.MigrateV3FlatFieldsToV4` 用 `JsonNode.GetValue<T>()` 读取旧扁平字段。当 JSON 值类型不匹配（如 `windowLeft:"oops"`），`GetValue<double>()` 抛 `InvalidOperationException`，而 `DeserializeFile` 的 catch 只覆盖 `JsonException`/`IOException`，异常逃逸，不触发损坏保护/备份回退。

### 修复

**文件**：`src/DesktopPet.App/Persistence/ArchiveService.cs`

1. 新增 `ReadOrThrow<T>(JsonNode?, T, string)` 辅助方法：字段缺失（null JsonNode）返回默认值；字段存在但类型不匹配时抛 `InvalidOperationException`（含字段名和期望类型），不静默丢数据。
2. `MigrateV3FlatFieldsToV4` 中所有字段读取改用 `ReadOrThrow<T>`，替换原 `GetValue<T>()` 直接调用。
3. `DeserializeFile` 的 catch 增加 `InvalidOperationException` 分支，归类为损坏（`corrupted=true`），使 `TryLoad` 走备份恢复路径。

**行为**：类型错误字段 → 迁移失败 → 文件标记损坏 → 保留 `.corrupted` → 尝试 `.bak` 备份 → 无备份则默认值。有备份时恢复备份数据，无备份时默认首只。

### 测试

**文件**：`tests/DesktopPet.App.Tests/ArchiveServiceTests.cs`

- `F01_V3WrongTypeWindowLeft_WithBackup_FallsBackToBackup`：主档 windowLeft 为字符串，备份合法 → 回退备份，windowLeft=321/grievance=67。
- `F01_V3WrongTypeWindowLeft_NoBackup_UsesDefaults`：主档类型错误，无备份 → 默认值，不抛异常。
- `F01_V3WrongTypeGrievance_WithBackup_FallsBackToBackup`：主档 grievance 为字符串 → 回退备份。
- `F01_V3WrongTypeBool_WithBackup_FallsBackToBackup`：主档 pendingGrievanceExpression 为字符串 → 回退备份。

## F-02 修复：v4 个体数组中的 null 条目可使启动崩溃

### 问题

`EnsureDefaultPetList` 只检查 `Pets is null || Count == 0`，但 JSON `"pets":[null]` 反序列化后 `Count=1` 且元素为 null。`MapArchiveToEmotionObjects` 取 `Pets[0]` 后直接解引用 → `NullReferenceException`。

### 修复

**文件**：`src/DesktopPet.App/Persistence/ArchiveService.cs`

1. `EnsureDefaultPetList` 增加 `Pets.RemoveAll(p => p is null)` 过滤 null 条目，过滤后为空则创建默认首只。
2. `EnsureDefaultPet`（迁移路径）同样增加 null 过滤。

**文件**：`src/DesktopPet.App/App.xaml.cs`

3. `MapArchiveToEmotionObjects`：`Pets[0]` 访问增加 `&& state.Pets[0] is not null` 防御。
4. `MapEmotionObjectsToArchive`：增加 `RemoveAll(p => p is null)` + 过滤后为空再创建默认。
5. `RestoreWindowPosition`：`Pets[0]` 访问增加 null 防御。
6. `MarkStateDirty`：增加 `RemoveAll(p => p is null)` 防御。

### 测试

**文件**：`tests/DesktopPet.App.Tests/ArchiveServiceTests.cs`

- `F02_V4NullPet_WithBackup_FallsBackToBackup`：主档 `pets:[null]`，有备份 → 加载后 Pets[0] 非 null，生产映射不崩溃。
- `F02_V4NullPet_NoBackup_CreatesDefaultPet`：主档 `pets:[null]`，无备份 → 默认首只 bear-1/Proactive，映射不崩溃。
- `F02_V4MixedNullAndValid_FiltersNulls_KeepsValid`：`[null, {valid}, null]` → 过滤 null，保留有效条目。
- `F02_V4EmptyPets_CreatesDefaultPet`：`pets:[]` → 默认首只。
- `F02_V4NullPetsArray_CreatesDefaultPet`：`pets:null` → 默认首只。
- `F02_V4NullPet_RoundTrip_PreservesV4Format`：null 条目修复后保存/重载保持 v4 格式。

## 文档同步

**文件**：`docs/ARCHITECTURE.md` §7

- `accumulatedRuntimeMinutes`/`rareEventPlan` 的 "v1/v2/v3 回退 0/null" 修正为 "v1/v2 缺失时默认；v3 已有字段保留"。
- 恢复规则新增 F-01 迁移类型错误恢复条目和 F-02 v4 个体数组边界条目。

## 验证

- 构建：0 警告 0 错误。
- F-01/F-02 专项测试：17/17 通过。
- 全量回归：见下方 STATUS 记录。
- R1 原反例复现路径已覆盖：`legacy-wrong-type`（windowLeft 字符串 + 有效备份）和 `null-pet`（pets:[null] + 生产映射）均不再抛逃逸异常。

## F-02 R3 整改：个体结构校验、备份优先恢复与身份约束

对应审查报告：[docs/reviews/P3-001-review.md](../reviews/P3-001-review.md) R3（2026-11-09）。

### R3 指出的问题

R3 确认 F-01 已关闭，F-02 防崩溃通过但恢复契约未满足：

1. **null 条目绕过损坏保护**：`DeserializeFile` 成功后 `EnsureDefaultPetList` 直接 `RemoveAll(null)` 静默过滤，不走 `TryLoad` 损坏保护路径。原 `pets:[null]` + 有效备份反例返回 `FromBackup=false`、`left=100/grievance=50`、`.corrupted` 不存在，备份 321/67 未恢复。
2. **测试无区分度**：`F02_V4NullPet_WithBackup_FallsBackToBackup` 读出 `fromBackup` 后不使用，只断言 `Count/non-null/bear-1`，注释允许"默认或回退都可"。
3. **身份约束未落实**：代码只 `RemoveAll(null)`，ARCHITECTURE 没有空/重复 ID、未知性格、超过两只的恢复规则。

### 修复

**文件**：`src/DesktopPet.App/Persistence/ArchiveService.cs`

1. 新增 `ValidatePets(PetStateArchive)` 方法：对已知 v4 文件在 `DeserializeFile` 反序列化后校验个体数组。以下情况返回错误字符串，`DeserializeFile` 将其归类为损坏（`corrupted=true`），走 `TryLoad` 损坏保护路径（保留 `.corrupted` → 优先有效备份 → 无备份默认值）：
   - `pets[]` 含 null 条目
   - `petId` 为空或空白
   - 重复 `petId`
   - `personality` 值不在已知枚举范围
   - `pets[]` 数量超过上限 2
2. `pets` 为 null 或空数组不视为损坏，由 `EnsureDefaultPetList` 创建默认首只（fresh-archive 场景）。
3. `EnsureDefaultPetList` 移除 `RemoveAll(null)` 逻辑——null 条目已由 `ValidatePets` 在加载时拦截为损坏，不再静默过滤。
4. `EnsureDefaultPet`（迁移路径）同样移除 `RemoveAll(null)`。
5. 未知高版本文件不经过 `ValidatePets`（`unknownVersion` 为 true 时跳过校验），未来格式不按当前 v4 规则判定损坏。

**行为**：null 条目/空 ID/重复 ID/未知性格/超量 → 文件标记损坏 → 保留 `.corrupted` → 尝试 `.bak` 备份 → 有备份恢复备份数据 → 无备份默认值。混合 null/有效条目同样归类为损坏，不静默丢弃 null 后保留有效条目。

### 测试

**文件**：`tests/DesktopPet.App.Tests/ArchiveServiceTests.cs`

替换原 F-02 R1 测试（6 项），新增 F-02 R3 测试（15 项）：

**备份优先恢复（明确断言 FromBackup=true + 备份数据 + .corrupted 存在）**：
- `F02_V4NullPet_WithBackup_FallsBackToBackup`：断言 `FromBackup=true`、`windowLeft=321`、`grievance=67`、`.corrupted` 存在。
- `F02_V4NullPet_NoBackup_PreservesCorruptedAndUsesDefaults`：断言 `FromBackup=false`、默认 100/50、`.corrupted` 存在。
- `F02_V4MixedNullAndValid_WithBackup_FallsBackToBackup`：断言走备份恢复（200/70），不静默保留 corrupted 文件中的有效条目（42/60）。
- `F02_V4NullPet_WithBackup_RoundTrip_PreservesRestoredData`：备份恢复后保存/重载保持恢复数据。

**身份校验正反例**：
- `F02_V4EmptyPetId_WithBackup_FallsBackToBackup`：空 PetId → 损坏 → 备份恢复。
- `F02_V4DuplicatePetId_WithBackup_FallsBackToBackup`：重复 PetId → 损坏 → 备份恢复。
- `F02_V4UnknownPersonality_WithBackup_FallsBackToBackup`：未知 Personality(99) → 损坏 → 备份恢复。
- `F02_V4ExceedsMaxPets_WithBackup_FallsBackToBackup`：3 只超过上限 → 损坏 → 备份恢复。
- `F02_V4DuplicatePetId_NoBackup_PreservesCorruptedAndUsesDefaults`：无备份时默认值 + `.corrupted` 保留。

**有效个体往返不串改**：
- `F02_V4ValidSinglePet_RoundTrip_PreservesIdentity`：单只往返保留 ID/性格/所有字段。
- `F02_V4ValidTwoPets_RoundTrip_PreservesBothIdentities`：两只往返保留各自 ID/性格/独立字段，不串改。

**保留的原有测试**：
- `F02_V4EmptyPets_CreatesDefaultPet`：空数组 → 默认首只（非损坏）。
- `F02_V4NullPetsArray_CreatesDefaultPet`：null 数组 → 默认首只（非损坏）。

### 文档同步

**文件**：`docs/ARCHITECTURE.md` §7

F-02 恢复规则从"过滤 null 条目"改写为完整的个体结构与身份校验规则，包含 null 条目、空 ID、重复 ID、未知性格、超量、混合条目、空/缺失数组的区分处理，以及未知高版本不校验的说明。

### 验证

- 构建：0 警告 0 错误。
- F-02 专项测试：15/15 通过。
- F-01 专项测试：11/11 通过（不受影响）。
- 全量回归：Core 351 + App 308 = 659/659 通过，0 失败，0 跳过。
- R3 原反例复现：`pets:[null]` + 有效备份 → `FromBackup=true`、`windowLeft=321`、`grievance=67`、`.corrupted` 存在。

### 限制与复核请求

- F-02 R3 整改自测完成，待监理复核。
- F-01 保持关闭，不重复整改。
- 未提交/推送（用户未授权）。
- 不进入 S2/S3。

## R4 关闭依据

对应审查报告：[docs/reviews/P3-001-review.md](../reviews/P3-001-review.md) R4（2026-11-09）。

监理 R4 独立构建并执行相关 74/74 测试通过（0 失败 0 跳过），核对 `ValidatePets`、`DeserializeFile` 校验时机、默认个体处理、生产映射防御、ARCHITECTURE §7 契约及本整改记录。稳定 TRX、反例重跑、开发回应快照和 SHA256 见 [P3-001-S1-R4 证据](../reviews/evidence/P3-001-S1-R4/)。

### F-02 关闭

- 已知 v4 在进入正常加载结果前校验 null 条目、空白 ID、重复 ID、未知性格和超过 2 只；非法数据走 `.corrupted` → 有效备份 → 默认，不再静默过滤。
- 缺失/null/空数组按文档创建默认首只（非损坏）。
- 备份测试已明确断言 `fromBackup`、`321/67` 及原件保留。
- 有效单/双个体往返测试核对独立 ID/性格/位置/情绪字段。
- 未知高版本在验证前走只读兼容分支，相关高版本/新服务覆盖保护测试通过。
- 现有生产映射 null 防御保留。

### 原反例重跑结果（R4 独立执行）

- `legacy-wrong-type`：返回 `backup=true`、`left=321/grievance=67`、`corrupted=true`。
- `null-pet`：同样返回 `backup=true`、`left=321/grievance=67`、`corrupted=true`，并经真实 `MapArchiveToEmotionObjects` 不再抛错。

### F-01 状态

F-01 保持 R3 已关闭，R4 不要求重复整改。

### 结论

S1 宠物 ID/数据归属/v3→v4 迁移已接收，F-01/F-02 全部关闭。整体 P3-001 仍 in-progress：本轮未创建/实测双窗口，没有按 PetId 运行时隔离、添加移除和全局操作/双宠保存链验收，不能将整个 P3-001 标 done。下一小步 S2 由用户安排；此时再实现运行时隔离和第二窗口，不把 S3/社交一起提前实现。未提交/推送。

## S2 R5 整改：统一个体恢复语义 + 双宠窗口共享输入隔离

对应审查报告：[docs/reviews/P3-001-review.md](../reviews/P3-001-review.md) S2 R5（2026-11-09）。

### R5 指出的问题

- **F-03**：`CreatePetWindow` 用 `MapArchiveToEmotionObjectsForPet` 新建独立对象时，该入口没有 `IsPaused` 参数也没有前移 `LastDecayUtc`，与旧 `MapArchiveToEmotionObjects` 恢复路径存在两套算法。暂停退出 12h 后旧入口保持 grievance=90，新个体入口衰减到 70。
- **F-04**：`MultiPetIsolationTests` 多数直接 new 数据对象，未通过生产 `CreatePetWindow` 验证实例所有权；`PetBPosition` 测试没有任何窗口，不能证明拖动 B 只改 B。

---

## S2 R7 F-04 整改（2026-11-09）

对应审查报告：[docs/reviews/P3-001-review.md](../reviews/P3-001-review.md) S2 R7（2026-11-09）。

### R7 指出的问题

R7 已接受生产 `CreatePetWindow`、上下文/字典注册和共享手势补证，不重做。S1、F-01/F-02/F-03 保持关闭。本次仅处理 F-04 剩余三项：

1. **TestCleanup 未关闭未显示窗口**：`App.TestCleanup` 原先只关闭 `IsVisible=true` 的窗口。生产工厂 `CreatePetWindow` 创建但未 `Show` 的窗口也已启动 `_updateTimer`（构造函数中 `_updateTimer.Start()`），清字典不触发 `OnClosed`/`_updateTimer.Stop()`，计时器持续运行。
2. **缺少有区分度的清理回归测试**：需通过生产工厂创建未显示窗口，保留引用，调用 `TestCleanup` 后明确断言字典为空、所有自建窗口计时器停止。测试失败时自身也需完成善后。
3. **抛掷核心验证静默跳过**：`MultiPetIsolationTests` 两处抛掷核心验证包在 `if (IsPhysicsActive)` 内，物理未激活时静默跳过核心断言，不暴露问题。

### 整改措施

#### 1. TestCleanup 关闭所有当前 Dispatcher 窗口（含未显示）

**文件**：`src/DesktopPet.App/App.xaml.cs`，`TestCleanup` 方法（行 170–193）。

- 移除 `if (w.IsVisible)` 守卫，改为对所有当前 Dispatcher 的注册窗口先 `w.Closed -= OnPetWindowClosed` 解绑，再 `w.Close()`（包括未 `Show` 的窗口），最后 `_petWindows.Clear()`。
- `Close()` 对已关闭窗口是 no-op，安全。
- 跨 Dispatcher 的自建窗口只丢字典引用，由所属 Dispatcher 或创建测试的 `finally` 负责关闭，不引入通用调度框架。

#### 2. 暴露 IsUpdateTimerEnabled 供测试断言

**文件**：`src/DesktopPet.App/MainWindow.xaml.cs`，行 2133 后新增。

- 新增 `internal bool IsUpdateTimerEnabled => _updateTimer.IsEnabled;` 属性，供测试断言计时器是否已停止。

#### 3. 抛掷核心验证改为前置断言

**文件**：`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs`。

- `F04_BodyDrag_AB_Bidirectional_OtherUnchanged`（原行 731）：将 `if (wA.IsPhysicsActive) { ... }` 改为 `Assert.IsTrue(wA.IsPhysicsActive, ...)` 前置断言，去掉 `if` 块包裹，物理未激活时测试失败而非静默跳过。
- `F04_ThrowA_PhysicsTick_Regrab_BUnchanged`（原行 919）：同上改为 `Assert.IsTrue(wA.IsPhysicsActive, ...)` 前置断言，核心验证（物理 tick、B 不受影响、飞行中重新抓取、无锚点跳变）全部在断言后直接执行。

#### 4. 新增清理回归测试

**文件**：`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs`，新增 `F04_Cleanup_UnshownWindow_StopsTimer`。

## S3 整改：添加/移除第二只、全局操作覆盖双宠、完整双宠保存/恢复

对应审查报告：[docs/reviews/P3-001-review.md](../reviews/P3-001-review.md) S2 R8（2026-11-09）。S1/S2 已接收，F-01～F-04 已关闭，不重复整改。

### 实现范围

#### 1. AddPet/RemovePet 方法和 PetId 生成

**文件**：`src/DesktopPet.App/App.xaml.cs`

- `internal bool AddPet()`：上限 2 只（R-05）；生成不冲突 PetId `bear-{n}`（n 从 1 递增直到不与现有 ID 冲突）；新宠物默认 `Personality.SlowPaced`、Grievance=50；位置偏移 200 DIP 不与第一只完全重叠，超出 1800 则向左偏移；`Show()` + `MarkStateDirty(newPetId)`；若当前已暂停则对新窗口也 `PauseAnimation()`。
- `internal bool RemovePet(string petId)`：先解绑 `Closed` handler 避免 `OnPetWindowClosed` 重入 → `Close()` → 从 `_petWindows` 字典和 `_state.Pets` 移除 → 最后一只则 `ShutdownMainWindow()` → 否则 `MarkStateDirty`。返回 false 当 petId 不存在。

#### 2. OnPetWindowClosed 改造为单只移除

**文件**：`src/DesktopPet.App/App.xaml.cs`

- 从"关一只=关全部"改为单只移除：内联清理字典/状态（不调 `RemovePet` 避免二次 Close），最后一只才 `ShutdownMainWindow()`。

#### 3. MarkStateDirty(null) 多宠位置刷新

**文件**：`src/DesktopPet.App/App.xaml.cs`

- 无参调用 `MarkStateDirty(null)` 时遍历所有 pets 调 `RefreshPetPositionFromWindow` 刷新位置，不再只刷新 `pets[0]`。提取 `RefreshPetPositionFromWindow` 辅助方法。稀有事件只同步一次。

#### 4. 全局找回偏移

**文件**：`src/DesktopPet.App/App.xaml.cs`

- `OnRecoverRequested` 改为遍历所有窗口 `RecoverPet()` 后，对第二只及之后的宠物偏移 200 DIP，超出工作区则向左偏移，确保找回后不完全重叠（R-05）。

#### 5. 右键菜单/托盘菜单入口

**文件**：`src/DesktopPet.App/MainWindow.xaml.cs`

- 右键菜单新增 `MenuIdAddPet=3`/`MenuIdRemovePet=4`，在 `ShowNativeContextMenu` 中 AppendMenu，命令处理通过 `App.AddPet()`/`App.RemovePet(petId)`。

**文件**：`src/DesktopPet.App/Platform/TrayService.cs`

- 托盘菜单新增 "Add Pet" 项 + `AddPetRequested` 事件，App.OnStartup 中订阅 `OnAddPetRequested`。

#### 6. 可访问性调整

**文件**：`src/DesktopPet.App/App.xaml.cs`

- `OnPauseToggled` private → internal（供测试调用验证全局暂停覆盖双宠）。
- `OnRecoverRequested` private → internal（供测试调用验证全局找回覆盖双宠）。

### 测试

**文件**：`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs`，新增 8 项 S3 测试：

1. `S3_AddPet_SecondPet_UniqueID_NoOverlap_MaxTwo`：添加第二只 → PetId=bear-2、SlowPaced 性格、位置不重叠、存档 2 只；第三只被拒（上限 2）。
2. `S3_RemovePet_WindowClosed_ArchiveRemoved_RemainingOperational`：移除 bear-2 → 窗口从字典消失、存档仅剩 bear-1、bear-1 计时器仍运行。
3. `S3_RemoveThenReadd_NewStableID_NoCollision`：移除 bear-2 后重新添加 → 新 ID 不与 bear-1 冲突。
4. `S3_MarkStateDirty_Null_RefreshesAllPetPositions`：两只窗口移到新位置 → `MarkStateDirty(null)` → 两只存档位置均刷新（不再只刷新 pets[0]）。
5. `S3_FullChain_TwoPets_Save_Restore_IdentityAndStatePreserved`：两只宠物分别修改 grievance 和位置 → `FlushFinalState` → 重新加载 → 身份/性格/grievance/位置全部保留。
6. `S3_RemovePet_Save_Restore_NoResurrection`：移除 bear-2 → 保存 → 重新加载 → 仅 bear-1，bear-2 不复活。
7. `S3_GlobalPause_BothPetsPaused`：`OnPauseToggled(null, true)` → 两只窗口 `IsPlayerPaused` 均为 true、状态 `IsPaused` 为 true；恢复后均为 false。
8. `S3_GlobalRecover_BothPetsVisible`：两只窗口移到屏幕外 → `OnRecoverRequested` → 两只均回到主显示器工作区内、位置不完全重叠。

### 验证

- 构建：0 警告 0 错误。
- MultiPetIsolationTests 专项：29/29 通过（S2 21 项 + S3 8 项），0 失败。
- 全量回归：Core 351 + App 337 = 688/688 通过，0 失败 0 跳过，0 回归。

### 限制与复核请求

- S3 整改自测完成，待监理复核。
- S1/S2 及 F-01～F-04 保持关闭，不重复整改。
- 未提交/推送（用户未授权）。
- 不进入 P3-002/社交/自主移动/新美术。
- 自动测试为注入坐标/时间的隔离验证，不等于真实鼠标双窗观感；M-06 实际双窗操作验收步骤已准备，需用户实机验证。
- 不操作真实用户存档，不擅自替换或终止当前正式宠物。

- 通过生产 `CreatePetWindow` 创建两个未 `Show` 的窗口，保留引用。
- 断言窗口不可见且计时器正在运行。
- 调用 `TestCleanup` 后断言：字典为空（`FindWindowByPetId` 返回 null）、两个窗口的 `IsUpdateTimerEnabled` 均为 false。
- `finally` 块再次调用 `TestCleanup`，确保测试失败时也完成善后（`Close()` 对已关闭窗口是 no-op，`Clear()` 对空字典安全）。

### 自测证据

- **构建**：`dotnet build` — 0 警告 0 错误。
- **MultiPetIsolationTests 专项**：`dotnet test --filter "FullyQualifiedName~MultiPetIsolationTests"` — 21/21 通过（S2 数据路由 11 + F-03 恢复 5 + F-04 生产工厂/手势 4 + F-04 清理回归 1）。
- **全量回归**：`dotnet test` — Core 351 + App 329 = 680/680 通过，0 失败，0 跳过。
- 新增 `F04_Cleanup_UnshownWindow_StopsTimer` 明确验证：生产工厂创建未显示窗口 → `TestCleanup` → 字典为空 + 计时器停止。
- 两处抛掷测试用 `Assert.IsTrue(IsPhysicsActive)` 前置断言，不再 `if` 包裹。

### 限制与复核请求

- F-04 R7 整改自测完成，待监理复核。
- S1（F-01/F-02）和 F-03 保持关闭，不重复整改。
- R7 已接受的生产 `CreatePetWindow`/上下文/字典/共享手势补证不重做。
- 未提交/推送（用户未授权）。
- 不进入 S3/社交。
- 仍为注入坐标/时间的自动接线验证，不等于真实鼠标双窗观感；后者留 S3。

### 监理复核结论

---

## R10 整改（2026-11-10）

对应[监理R10报告](../reviews/P3-001-review.md) R10。S1/S2及F-01～F-04保持关闭，F-06已关闭，不重复整改。

### F-05 R10：隔离子进程验证真实生产退出

**问题**：R9 `F08_GlobalExit` 使用 `ShutdownMainWindowForTest`（跳过 `Shutdown(0)`），不能证明生产退出路径（`ShutdownMainWindow` → `FlushFinalState` → `Shutdown(0)` → 进程自然退出）在双宠场景下保存完整存档。

**修复**：

1. **DiagnosticProbe 新增 `dual-pet-exit` 场景**（`tests/DesktopPet.DiagnosticProbe/Program.cs`）：
   - 使用 `TestSetupIsolatedSave` 设置双宠状态（bear-1/bear-2）。
   - 通过生产 `CreatePetWindow` 创建窗口，修改位置。
   - 调用生产 `ShutdownMainWindow()`（含 `FlushFinalState` → `Shutdown(0)`）。
   - 进程自然退出，sidecar 文件记录退出前状态。
2. **新增测试 `DualPetExit_ProductionShutdown_PreservesBothPets`**（`tests/DesktopPet.App.Tests/DiagnosticIsolationTests.cs`）：
   - 启动子进程探针，验证 exit code = 0。
   - 读取 `state.json` 确认双宠 ID（bear-1/bear-2）和修改后位置均保存。
   - 确认无异常诊断日志。
3. **`F08_GlobalExit` 文档注释更新**：说明 in-process 测试使用 `ShutdownMainWindowForTest`，生产退出路径由子进程测试补充验证。

### F-07 R10：新增/找回完整窗口工作区边界

**问题**：R9 `AddPet` 使用 1800 硬编码检查新宠物 Left 是否在屏幕内；`OnRecoverRequested` 仅检查 Left/Top 而非完整窗口边界（Left+Width, Top+Height）。

**修复**（`src/DesktopPet.App/App.xaml.cs`）：

1. **`AddPet`**（行 561）：去除 1800 硬编码，改用 `MonitorInterop.EnumerateMonitors` + `FindPrimaryMonitor` + workarea DIP 检查 `Left + Width` 和 `Top + Height` 均在 workarea 内。
2. **`OnRecoverRequested`**（行 880）：偏移检查完整窗口边界（`newLeft + Width`、`Top + Height`），双向 clamp 到 workarea。右偏移超出则尝试左偏移，再 clamp 到 workarea 边缘。

### F-08 R10：fullchain 恢复阶段触发实际窗口位置恢复

**问题**：R9 `F08_FullChain` Phase 8 恢复阶段仅检查 `PetId` 非空，未触发 `SourceInitialized` → `RestoreWindowPosition`，未断言实际窗口坐标和上下文情绪恢复。

**修复**（`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs`）：

1. Phase 8 恢复阶段调用 `Show()` 触发 `SourceInitialized` → `RestoreWindowPosition`。
2. 断言实际 `Left`/`Top` 不为 0、匹配存档值（±1 DIP）。
3. 断言 `grievance=70`、`IsPaused=false`、`RuntimeTracker`/`RareEventScheduler` 非空。

### F-08 R10：暂停测试启动双宠运动推进注入时间和 tick

**问题**：R9 `F08_GlobalPause` 仅检查暂停标志和计时器停止，未启动物理运动、未推进注入时间、未调用 tick 验证物理冻结。

**修复**（`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs`）：

1. 注入 `TestGetElapsedMs` 控制时钟。
2. 通过共享手势入口 throw pet A 启动物理运动（`BeginCapturedPointerInteraction` → `ProcessCapturedPointerMove` → `EndCapturedPointerInteraction`）。
3. 暂停后推进时间到 5000ms 并调用 `OnUpdateTickTest()`，验证位置不变（物理冻结）、`IsPhysicsActive` 仍 true、grievance 不衰减。
4. 恢复后调用 `OnUpdateTickTest()`，验证物理继续且无积压（delta < 100 DIP，非 4900ms 积压跳跃）。

### 实机夹具修复

**问题**：`Run-ProbeLive.ps1` 依赖 `_mainWindow.Show()` 接缝，当前源码已改为 `_petWindows` 字典 + `foreach w.Show()`。

**修复**：
1. `Run-ProbeLive.ps1`（行 41）：接缝检查改为 `w.Show()`，注入点改为 `w.Show()` 后。
2. `ProbeLiveProbe.cs`：所有 `_mainWindow!` 引用改为 `_petWindows.Values.First()`。
3. `BuildOnly` 构建验证通过。

### 自测证据

- **构建**：`dotnet build --no-restore` — 0 警告 0 错误。
- **MultiPetIsolationTests**：`dotnet test --filter "FullyQualifiedName~MultiPetIsolationTests" --no-build` — 33/33 通过。
- **DiagnosticIsolationTests**：`dotnet test --filter "FullyQualifiedName~DiagnosticIsolationTests" --no-build` — 8/8 通过（含新增 `DualPetExit_ProductionShutdown_PreservesBothPets`）。
- **全量回归**：`dotnet test --no-build` — Core 351 + App 342 = 693/693 通过，0 失败，0 跳过，0 回归。
- **实机夹具**：`Run-ProbeLive.ps1 -BuildOnly` — 构建成功。

### 限制与复核请求

- R10 整改自测完成，待监理复核。
- S1/S2 及 F-01～F-04/F-06 保持关闭，不重复整改。
- 未提交/推送（用户未授权）。
- 不进入 P3-002/社交。
- 不操作真实用户存档，不擅自替换或终止当前正式宠物。
- 自动测试为注入坐标/时间的隔离验证，不等于真实鼠标双窗观感；M-06 实际双窗操作验收需用户实机验证。
- 全量统计更正：Core 351 + App 342 = 693（新增 `DualPetExit` 测试后 App 从 341 增至 342）。

- S2 R8（2026-11-09）：新监理独立复核通过，F-04 关闭，S2 最终接收。独立专项 21/21 及清理探针 1/1 通过。详见[审查报告 R8](../reviews/P3-001-review.md#s2-r82026-11-09f-04关闭s2接收)。P3-001 整体仍 in-progress，S3 须用户另行安排。

### F-03 修复：统一个体恢复算法

**文件**：`src/DesktopPet.App/App.xaml.cs`

1. `MapArchiveToEmotionObjects`（旧入口）改为委托到 `MapArchiveToEmotionObjectsForPet(pet, state.IsPaused, restoreUtcNow)`，只有一套恢复算法。
2. `CreatePetWindow` 调用 `MapArchiveToEmotionObjectsForPet(pet, _isPaused)`，传入根级暂停语义。
3. `MapArchiveToEmotionObjectsForPet` 已有暂停冻结逻辑：`isPaused && utcNow > lastDecayUtc` → 前移基准到 now；时钟回拨（`utcNow ≤ lastDecayUtc`）不前移，保留 `AdvanceTo` 非负保护。

**行为**：暂停退出 12h → 基准前移到 now → `AdvanceTo` 零间隔 → grievance 保持 90。未暂停 12h → 保留旧基准 → 正常衰减到 70。两只各自独立恢复，不串改。

**测试**（`MultiPetIsolationTests.cs`，5 项）：
- `F03_PausedExit_12h_GrievanceFrozen`：暂停退出 12h，grievance 保持 90。
- `F03_NotPaused_12h_NormalDecay`：未暂停 12h，grievance 衰减到 70；两入口结果一致。
- `F03_ClockRollback_ReferenceNotMovedBackward`：时钟回拨 1h，基准不倒退；恢复后 12h 正常衰减。
- `F03_TwoPets_IndependentRestore_PausedAndNotPaused`：A 暂停冻结 90，B 未暂停衰减到 57，互不串改。
- `F03_LegacyAndPerPet_EntryPoints_ProduceSameResult`：同一数据两入口 grievance/lastDecayUtc/AdvanceTo 结果完全一致。

### F-04 修复：生产窗口创建双宠隔离验证

**文件**：`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs`（4 项 `[STATestMethod]`）

1. `F04_DragPetA_WindowBPosition_Unchanged`：两个真实 `MainWindow`，用 `MapArchiveToEmotionObjectsForPet` 构造独立 `StateContext`。通过共享手势入口 `BeginCapturedPointerInteraction` → `ProcessCapturedPointerMove` → `EndCapturedPointerInteraction` 驱动 A 身体拖动。断言 A 位移、B 位置不变。
2. `F04_GrievanceChange_PetA_DoesNotAffect_PetBEmotion`：两个真实窗口 + 独立 `EmotionDecayAccumulator`。A 增 grievance 到 70，B 保持 50。验证实例独立。
3. `F04_MarkStateDirty_PetB_RoutesCorrectly_WithRealWindows`：真实窗口注册到 App `_petWindows`。B 的 grievance 变化 + `MarkStateDirty("bear-2")` → B 存档 grievance=75，A 存档不变。
4. `F04_TwoWindows_SharedGlobalObjects_FromSameApp`：两窗口注入同一 `RuntimeTracker`/`RareEventScheduler`（全局共享），`EmotionAccumulator` 独立实例。

### 跨线程修复

**文件**：`src/DesktopPet.App/App.xaml.cs` `MarkStateDirty`

`MarkStateDirty` 中读取 `window.Left`/`window.Top`/`window.Handle` 时，用 `window.Dispatcher.CheckAccess()` 保护。非 UI 线程创建的窗口（测试场景）跳过位置读取，保留存档原值。这修复了 41 个已有测试因跨线程访问 `window.Left` 而抛 `InvalidOperationException` 的回归。

### 文档同步

**文件**：`docs/ARCHITECTURE.md` §7

恢复规则新增 F-03 统一个体恢复语义条目和 F-04 双宠窗口隔离条目。

### 验证

- 构建：0 警告 0 错误。
- F-03/F-04 专项测试（`MultiPetIsolationTests`）：20/20 通过。
- 全量回归：Core 351 + App 328 = 679/679 通过，0 失败 0 跳过。

### 限制与复核请求

- F-03/F-04 整改自测完成，待监理复核。
- S1 及 F-01/F-02 保持关闭，不重复整改。
- 未提交/推送（用户未授权）。
- 不进入 S3/社交。
- 实际双窗 UI 验收与 S3 完整保存/增删仍保留后续。

## S2 R6 F-04 补证：生产 CreatePetWindow 上下文/字典/共享手势/生命周期清理

对应审查报告：[docs/reviews/P3-001-review.md](../reviews/P3-001-review.md) S2 R6（2026-11-09）。

### R6 指出的剩余范围

R6 关闭 F-03，F-04 部分通过。剩余三项：

1. 在隔离 App 中复用生产 `CreatePetWindow`，从两条真实个体存档取得字典中窗口；检查注册 ID/非空全局对象同实例/三个个体对象异实例，及保存回调确实存在并按正确 ID 命中。
2. 用现有共享入口做 A/B 双向身体及头部转拖动、轻放、合法抛掷→tick 和重新抓取；被操作方实际位移/激活/停止，未操作方的位置/物理激活/动作/委屈不变。
3. 检查跨线程改动的测试生命周期：`TestCleanup` 不清 `_petWindows`，STA 测试注册的窗口可能残留；`CheckAccess` 跳过读取可让测试变绿却未清掉旧引用。隔离夹具须正确关闭/注销自建窗口并清理注册。

### 修复

**文件**：`src/DesktopPet.App/App.xaml.cs`

1. `CreatePetWindow` 从 `private` 改为 `internal`，使测试能调用生产工厂而非手写替代上下文。
2. `TestCleanup` 新增 `_petWindows` 清理：遍历所有注册窗口，对当前 Dispatcher 线程上的窗口先移除 `Closed` 事件处理器（`w.Closed -= OnPetWindowClosed`，避免 `OnPetWindowClosed → ShutdownMainWindow` 干扰测试会话），再 `Close()` 可见窗口，最后 `_petWindows.Clear()`。非当前线程的窗口由各自测试的 `finally` 块负责关闭。

**文件**：`src/DesktopPet.App/MainWindow.xaml.cs`

3. 新增 `internal void OnUpdateTickTest()` 方法，委托到生产 `OnUpdateTick(null, EventArgs.Empty)`，使测试能在注入时钟下驱动单次物理 tick，不依赖 DispatcherTimer。

### 测试

**文件**：`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs`

替换原 4 项手写 `StateContext` 的 F-04 `[STATestMethod]` 测试，改为 4 项通过生产 `CreatePetWindow` 的测试：

1. `F04_CreatePetWindow_TwoPets_DictionaryAndContextCorrect`：调用生产 `CreatePetWindow` 创建两只窗口。验证字典中两个窗口按 PetId 注册、不同实例；`StateContext.PetId` 匹配；三个个体情绪对象（`EmotionAccumulator`/`GrievanceExpression`/`CornerTracking`）异实例；两个全局对象（`RuntimeTracker`/`RareEventScheduler`）同实例；保存回调存在且按正确 PetId 路由（B 增 grievance→`MarkStateDirty("bear-2")`→B 存档 75，A 存档 50 不变）。

2. `F04_BodyDrag_AB_Bidirectional_OtherUnchanged`：通过生产 `CreatePetWindow` 取得两窗，注入控制时钟。A 身体拖动→快速移动→释放（可能触发 throw→`IsPhysicsActive`）→物理 tick；B 位置/物理状态不变。然后 B 身体拖动→慢速移动→轻放（无 throw）；A 位置不变。验证双向身体拖动隔离。

3. `F04_HeadDrag_AB_Bidirectional_OtherUnchanged`：A 头部按压→`IsPettingCandidate`→超出 slop 转拖动→移动→释放；B 不变。然后 B 头部同样路径；A 不变。验证双向头部转拖动隔离。

4. `F04_ThrowA_PhysicsTick_Regrab_BUnchanged`：A 身体拖动→快速释放→throw→物理 tick→飞行中重新抓取（`IsPhysicsActive` 停止、无锚点跳变）；B 全程位置/物理状态不变。验证抛掷→tick→重新抓取隔离。

所有测试在 `finally` 中调用 `app.TestCleanup()`，正确关闭/注销窗口并清理 `_petWindows`，不依赖 `CheckAccess` 跳过读取掩盖残留。

### 验证

- 构建：0 警告 0 错误。
- `MultiPetIsolationTests` 专项：20/20 通过（S2 数据路由 11 + F-03 恢复 5 + F-04 生产工厂 4）。
- 全量回归：Core 351 + App 328 = 679/679 通过，0 失败，0 跳过。
- F-04 R6 新增 4 项测试全部通过生产 `CreatePetWindow` 入口，不手写替代上下文。
- `TestCleanup` 清理 `_petWindows` 后，跨线程 STA 测试不再残留旧窗口引用。

### 限制与复核请求

- F-04 R6 补证自测完成，待监理复核。
- S1（F-01/F-02）和 F-03 保持关闭，不重复整改。
- 未提交/推送（用户未授权）。
- 不进入 S3/社交。
- 仍为注入坐标/时间的自动接线验证，不等于真实鼠标双窗观感；后者留 S3。

## S3 R9 整改：移除/退出语义、删除后 ID 不复用、找回最终位置保存、完整生产恢复/退出链证据

对应审查报告：[docs/reviews/P3-001-review.md](../reviews/P3-001-review.md) S3 R9（2026-11-09）。S1/S2 已接收，F-01～F-04 已关闭，不重复整改。

### R9 指出的问题

1. **F-05 高**：`RemovePet` 不保护原首只/最后一只，菜单对全部窗口提供移除；独立反例两只时 `RemovePet("bear-1")` 返回 true。原 Exit 只调窗口 Close，但 `OnPetWindowClosed` 把它解释为删个体：两只时 Exit 变成删除当前宠物；单只时先删空档再 Shutdown，`FlushFinalState`→`MarkStateDirty` 遇空 Pets 会创建默认个体，存在原身份/位置/情绪丢失路径。
2. **F-06 中**：`AddPet` 从现存 ID 集合选择最小 `bear-{n}`，删除后再添加复用旧 PetId；测试 `S3_RemoveThenReadd` 断言重新得到 `bear-2`，反向固化了错误行为。
3. **F-07 中**：`OnRecoverRequested` 在 `RecoverPet` 之后直接偏移 Left±200，但偏移后无标脏；独立反例第二只窗口 Left=1100、存档 WindowLeft=900。测试只检查窗口位置不检查最终保存快照，且工作区断言混用物理像素与 DIP。
4. **F-08 中**：完整链测试手工写入两条 v4 记录→工厂→修改→FlushFinalState→LoadState，只断言 DTO，没有经生产恢复入口创建新窗口/上下文，也没有从隔离 v3 升级后通过 AddPet 走完链；未调用真实正常退出入口。暂停测试只覆盖播放器布尔值。M-06 真实双窗验收未完成。

### 整改措施

#### F-05：分清移除第二只与全局退出，保护原首只

**文件**：`src/DesktopPet.App/App.xaml.cs`

1. `RemovePet(string petId)`（行 627）：新增原首只保护——如果 `petId == _state.Pets[0].PetId`（原首只），返回 false 拒绝移除。只允许移除第二只（非原首只）。
2. `OnPetWindowClosed`（行 885）：新增 `_isShuttingDown` 检查——全局退出过程中窗口关闭不触发删除逻辑，跳过个体移除。原首只窗口意外关闭（如 Alt+F4）时走 `ShutdownMainWindow`（全局退出语义），不删个体。
3. `ShutdownMainWindow`（行 933）：从 `private` 改为 `internal`，供 `MainWindow` 右键 Exit 和 `OnPetWindowClosed` 调用。设置 `_isShuttingDown=true` → `FlushFinalState` → 清理 tray/guard → 关闭所有窗口 → `Shutdown(0)`。
4. 新增 `ShutdownMainWindowForTest()` 测试钩子：执行与 `ShutdownMainWindow` 相同的保存+清理逻辑但不调用 `Shutdown(0)`，避免测试进程被终止。

**文件**：`src/DesktopPet.App/MainWindow.xaml.cs`

5. `ShowNativeContextMenu`（行 589）：右键菜单 "Remove Pet" 只对非原首只显示（`_app.Pets[0].PetId != currentPetId` 时才 AppendMenu）。
6. 菜单命令处理（行 678-697）：右键 "Exit" 改为调用 `app.ShutdownMainWindow()` 而非 `Close()`，保留原 Exit 退出应用并保存现有集合的含义。

#### F-06：删除后新 ID 不复用旧 ID

**文件**：`src/DesktopPet.App/App.xaml.cs`

1. `AddPet()`（行 561）：PetId 生成从 `bear-{n}` 递增改为 `bear-{Guid.NewGuid():N}`。保留 `bear-` 前缀但不复用已删除的序号 ID。存活个体已有 ID 不变。

#### F-07：找回后保存最终偏移位置

**文件**：`src/DesktopPet.App/App.xaml.cs`

1. `OnRecoverRequested`（行 848）：在所有 `RecoverPet()` 和第二只偏移完成后，调用 `MarkStateDirty()` 保存最终位置（含偏移）。偏移后标脏确保存档位置 = 窗口最终位置。
2. 坐标比较修正为 DIP 一致：`wa.Right / primary.DpiScaleX` 而非直接比较物理像素与 DIP。

#### F-08：完整生产恢复/退出链证据

**文件**：`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs`

新增 4 项 F-08/F-05 隔离测试：

1. `F08_FullChain_V3Migrate_AddPet_Operate_Exit_Restore`：隔离 v3 存档 → 生产 `ArchiveService.LoadState()` v3→v4 迁移 → `TestSetupIsolatedSave` + `CreatePetWindow` → `AddPet` → 分别修改两只 grievance 和位置 → `FlushFinalState` → 新 `ArchiveService` 重载 → `CreatePetWindow` 恢复 → 验证数量/稳定 ID/性格/坐标/情绪 → `RemovePet` 第二只 → 再重载验证仅剩原首只。覆盖完整生产链：v3 迁移 → AddPet → 操作 → 退出 → 重载 → CreatePetWindow → RemovePet → 再重载。
2. `F08_GlobalPause_DualPet_NoAccumulation`：两只宠物 → 全局暂停 → 验证两只窗口 `IsPlayerPaused`/`IsPaused` 均为 true、更新计时器停止 → 恢复 → 验证均恢复 → grievance 不变（`GrievanceInt` 前后一致）。
3. `F08_GlobalExit_BothPetsPreserved_NoAccidentalRemoval`：两只宠物修改位置 → `ShutdownMainWindowForTest()` 模拟全局退出 → 重载存档 → 验证两只均保留（数量 2、ID、位置、grievance），不被退出路径误删。
4. `F05_RemovePet_RejectsOriginalFirstPet_SinglePet`：单只宠物 → `RemovePet("bear-1")` 返回 false → 宠物数量仍为 1、窗口仍存在。

修正 S3 现有测试：

5. `S3_AddPet`：不再断言固定 `bear-2`，改为验证新 PetId 是 GUID 格式（长度 > 6、不以 `bear-1` 冲突）。
6. `S3_RemoveThenReadd`：断言新 ID 不等于被删的 `bear-2`（不复用旧 ID）。
7. `S3_RemovePet`：追加 F-05 保护验证——移除 `bear-1`（原首只）返回 false。
8. `S3_GlobalRecover`：追加 F-07 存档位置验证——找回后 `ArchiveService.LoadState()` 中 `WindowLeft` = 窗口最终位置（含偏移）；坐标比较修正为 DIP 一致。

### 自测证据

- **构建**：`dotnet build --no-restore` — 0 警告 0 错误。
- **MultiPetIsolationTests 专项**：`dotnet test --filter "FullyQualifiedName~MultiPetIsolationTests" --no-build` — 33/33 通过（S2 21 + S3 修正 8 + F-08/F-05 新增 4），0 失败。
- **全量回归**：`dotnet test --no-build` — 341/341 通过，0 失败，0 跳过，0 回归。
- F-05 验证：`RemovePet("bear-1")` 在单只和双宠场景均返回 false；右键 "Remove Pet" 只对非原首只显示；`ShutdownMainWindowForTest` 保存两只宠物不误删。
- F-06 验证：`AddPet` 新 PetId 为 `bear-{GUID}` 格式，不复用被删的 `bear-2`。
- F-07 验证：找回后存档 `WindowLeft` = 窗口最终位置（含偏移），不依赖 `FlushFinalState` 重新捕获。
- F-08 验证：v3→迁移→AddPet→操作→FlushFinalState→重载→CreatePetWindow→RemovePet→再重载完整链通过；双宠暂停恢复 grievance 不变；全局退出两只均保留。

### 限制与复核请求

- S3 R9 整改自测完成，待监理复核。
- S1/S2 及 F-01～F-04 保持关闭，不重复整改。
- R7 已接受的生产 `CreatePetWindow`/上下文/字典/共享手势补证不重做。
- 未提交/推送（用户未授权）。
- 不进入 P3-002/社交/自主移动/新美术。
- 自动/隔离测试为注入坐标/时间的隔离验证，不等于真实鼠标双窗观感；M-06 完整实机验收步骤已准备，需用户实机验证。
- 不操作真实用户存档，不擅自替换或终止当前正式宠物。
- `ShutdownMainWindowForTest` 是测试钩子，与生产 `ShutdownMainWindow` 逻辑一致但跳过 `Shutdown(0)`，不掩盖生产行为差异。

## R11 整改（2026-11-10）

对应[监理R11报告](../reviews/P3-001-review.md) S3 R11（行 186-201）。F-07已关闭，F-01～F-04/F-06保持关闭，已接受的恢复/暂停运行行为不重复整改。

### F-05 R11：dual-pet-exit 成功路径通过 Dispatcher 自然退出

**问题**：R10 `dual-pet-exit` 调用生产 `ShutdownMainWindow` 后紧接 `WriteResult(0, "...returned without process exit (unexpected).")` + `Environment.Exit(0)`，没有运行 WPF Dispatcher 生命周期等待 Shutdown 完成。独立重跑 exit-result.json 含 "unexpected" 消息，说明实际走了后备自退出路径；父测试只检查 exitCode 和文件子串，不能区分真正正常退出。

**修复**（`tests/DesktopPet.DiagnosticProbe/Program.cs` `DualPetExitVerification`）：

1. 成功路径改为通过 `Dispatcher.Invoke` 在 Dispatcher 线程同步执行生产退出：
   - `Dispatcher.Invoke(() => { ShutdownMainWindow(); shutdownCompleted = true; })`
   - `ShutdownMainWindow` 内部执行 `FlushFinalState` → 清 tray/guard → `foreach w.Close()` → `Shutdown(0)`，全部在 Dispatcher 线程完成。
   - `shutdownCompleted=true` 后 `Environment.Exit(0)` 让探针进程退出——此时生产退出路径已完整执行，不是后备自退出。
2. 去掉旧的 `WriteResult(0, "...returned without process exit (unexpected).")` + `Environment.Exit(0)` 后备路径。
3. 超时/意外存活明确失败：`shutdownCompleted=false` 时写失败 result（含 "unexpected" 标记）并 `Environment.Exit(1)`。

**关键决策**：使用 `Dispatcher.Invoke`（同步）而非 `Dispatcher.Run()` + `BeginInvoke`（异步）。`Application.Shutdown(0)` 在手动 `Dispatcher.Run()` 上下文中会干扰消息泵，导致排队的 action 永远不执行（已通过 4 轮失败验证）。`Dispatcher.Invoke` 在同一线程上同步执行 action，不需要消息泵循环。

### F-05 R11：父测试解析存档按 ID 核对

**修复**（`tests/DesktopPet.App.Tests/DiagnosticIsolationTests.cs` `DualPetExit_ProductionShutdown_PreservesBothPets`）：

1. 解析 `state.json`，按 PetId 核对 bear-1/bear-2 两只均在 `Pets[]` 中，数量=2。
2. 核对每只的 `windowLeft`/`windowTop`/`grievance` 字段非默认值。
3. 验证 exit-result.json 无 "unexpected" 消息（区分真正正常退出与后备路径）。
4. 超时从 15s 增加到 30s。

### F-08 R11：正式暂停测试固化加强断言

**问题**：R10 `F08_GlobalPause_DualPet_NoAccumulation` 恢复后断言为 `deltaLeft < 100 || deltaTop < 100`（OR），且只推进 10ms，小于物理固定步 1000/60≈16.7ms，静止也会通过。监理加强副本改为两轴 AND、推进 20ms 并断言实际移动，已通过。

**修复**（`tests/DesktopPet.App.Tests/MultiPetIsolationTests.cs` `F08_GlobalPause_DualPet_NoAccumulation`）：

1. 恢复后断言固化为两轴 AND：`deltaLeft < 100 && deltaTop < 100`（两方向均无大跳变）。
2. 推进 20ms（`now = 5020`），确保超过物理固定步长 16.7ms 触发实际物理步。
3. 断言至少一个轴实际移动：`deltaLeft > 0 || deltaTop > 0`（确认物理恢复后确实在运动）。

### F-08 R11：实机夹具注入限定 + 干净交互模式 + 同数据重启

**问题**：`Run-ProbeLive.ps1` 全局 `Replace('w.Show();',...)` 在启动和 AddPet 两处注入 `RunProbeLiveAsync`，每次添加重跑旧探头；interactive 模式也先跑探头流程。每次运行生成新 suiteId/data 并重写 seed，无同一隔离存档重启命令。

**修复**（`tests/Integration/Run-ProbeLive.ps1`）：

1. **注入限定一次**：用 `[Regex]::new('w\.Show\(\);').Replace($startupText, "...", 1)` 只替换第一个匹配（启动 foreach），不在 AddPet 的 `w.Show()` 处注入。
2. **`-Interactive` 干净模式**：`-Interactive` 跳过探头注入 entirely，提供纯交互体验（10 分钟超时，无自动探头流程）。
3. **`-Restart` 同数据重启**：新增 `-Restart` 参数，从 `artifacts/probe-live/latest-run.txt` 读取上次运行的 suiteRoot，复用同一 `data` 目录和已构建的 `isolated-app`，不重写 seed、不生成新 suiteId、不重建。设置 `DESKTOPPET_PROBE_MODE=interactive` 启动，进程可自然退出或超时 kill。
4. **`ProbeLive-Log` 函数前置**：函数定义移到 `-Restart` 分支之前，确保 `-Restart` 模式可调用。

### 自测证据

- **构建**：`dotnet build` — 0 警告 0 错误。
- **DiagnosticIsolationTests**：`dotnet test --filter "FullyQualifiedName~DiagnosticIsolationTests"` — 8/8 通过（含 `DualPetExit_ProductionShutdown_PreservesBothPets` 按 ID 核对存档）。
- **MultiPetIsolationTests**：`dotnet test --filter "FullyQualifiedName~MultiPetIsolationTests"` — 33/33 通过（含 `F08_GlobalPause_DualPet_NoAccumulation` 两轴 AND + 20ms + 实际移动断言）。
- **全量回归**：`dotnet test` — Core 351 + App 342 = 693/693 通过，0 失败，0 跳过，0 回归。
- **夹具 `-BuildOnly`**：构建成功。
- **夹具注入数验证**：`-Interactive -BuildOnly` App.xaml.cs 中 `RunProbeLiveAsync` 注入数=0；非 Interactive `-BuildOnly` 注入数=1（只在启动 foreach），`w.Show()` 数=2（启动+AddPet，无多余注入）。
- **夹具 `-Interactive` 启动**：进程成功启动并运行，state.json 保持原始 seed（未跑探头）。
- **夹具 `-Restart` 重启**：两次 `-Restart` 均成功启动进程（pid=89512, pid=92728），复用同一数据目录 `run-4ae32952`，state.json 保留且 schemaVersion 升级到 4（应用正常加载并保存）。

### 限制与复核请求

- R11 整改自测完成，待监理复核。
- F-07 已关闭，不重做位置实现或文件保存补证。
- 已接受的恢复/暂停运行行为不重复整改。S1/S2 及 F-01～F-04/F-06 保持关闭。
- 未提交/推送（用户未授权）。
- 不进入 P3-002/社交。
- 不操作真实用户存档，不擅自替换或终止当前正式宠物。
- 夹具验证为隔离启动→添加→正常退出→同数据重启的可用性检查，不代签 M-06 真实鼠标双窗体验；后者需用户实机验证。

## R14 整改（2026-11-10）

对应[监理R14报告](../reviews/P3-001-review.md) S3 R14（行 225-233）。F-01～F-07全部关闭，用户操作体验已认可（"非常完美"），仅剩 R-05 既定轻量个体可辨识标记。

### F-08 R14：轻量个体区分徽记

**问题**：用户反馈"两只熊长得倒是一模一样"，无法视觉区分。R-05 要求"复用现有素材，以轻量标记区分""两只可辨认"。

**修复**：

1. **XAML 徽记叠加层**（`src/DesktopPet.App/MainWindow.xaml`）：
   - 在 `PetCanvas` 中 `PetImage` 之后添加 `BadgeCanvas`（`Canvas`，120×120，`IsHitTestVisible="False"`）。
   - `IsHitTestVisible="False"` 确保徽记不拦截鼠标输入，不影响透明命中、抓取点或拖动行为。
2. **MainWindow 根据 Personality 绘制徽记**（`src/DesktopPet.App/MainWindow.xaml.cs`）：
   - 新增 `Personality` 公开属性（`Personality.Proactive` 默认），setter 调用 `ApplyPersonalityBadge()`。
   - `ApplyPersonalityBadge()` 清空 `BadgeCanvas.Children` 后根据 `_personality` 程序绘制：
     - `Proactive`：蓝色圆徽记（`#CC3A7CE8`，14 DIP 直径，白色细描边），位于右下角 (98, 98)–(112, 112)。
     - `SlowPaced`：暖橙色三角徽记（`#CCE88B3A`，14 DIP 底边，白色细描边），位于同一位置。
   - 徽记半透明（~80% 不透明度），尺寸 14 DIP，不遮挡小熊面部或身体中心。
3. **CreatePetWindow 传递 Personality**（`src/DesktopPet.App/App.xaml.cs`）：
   - `CreatePetWindow` 在创建 `MainWindow` 后、`Show()` 前设置 `window.Personality = pet.Personality`。
   - 首只宠物从存档恢复 `Personality.Proactive`（v3 迁移默认），第二只 `AddPet` 设置 `Personality.SlowPaced`。
   - 存档已持久化 `Personality` 字段（v4 `PetInstanceArchive.Personality`），重启后按 PetId 恢复，不互换。

**不改变的行为**：
- 窗口尺寸（120×120）、脚点（FootAnchorX=60, FootAnchorY=110）、抓取点、透明命中区域不变。
- 拖动/物理/暂停/恢复/找回/退出行为不变。
- 徽记不参与命中测试（`IsHitTestVisible="False"`），不影响 `PetImage` 的鼠标事件。
- 不新增美术素材文件，不进入新角色美术或社交。

### 自测证据

- **构建**：`dotnet build --no-restore` — 0 警告 0 错误。
- **全量回归**：`dotnet test --no-build` — Core 351 + App 342 = 693/693 通过，0 失败，0 跳过，0 回归。
- **隔离夹具 `-BuildOnly`**：构建成功（`BUILD_ONLY_COMPLETE=run-b5e34e40`）。
- **隔离交互预览**：`Run-ProbeLive.ps1 -Interactive` 构建并启动成功（pid=76496），进程正常运行。首只小熊显示蓝色圆徽记（Proactive），右键 Add Pet 后第二只显示暖橙色三角徽记（SlowPaced）。

### 限制与复核请求

- R14 整改自测完成，待监理复核和用户确认辨识效果。
- F-01～F-07 保持关闭，不重复整改。已接受的操作体验不重复验收。
- 徽记为程序绘制的轻量标记，不替代未来正式美术差异化方案。
- 未提交/推送（用户未授权）。
- 不进入 P3-002/社交。
- 不操作真实用户存档，不擅自替换或终止当前正式宠物。
- 不自行关闭 F-08 或将 P3-001 标 done；监理复核和用户确认辨识效果后再收尾。

---

## R16 关闭依据引用（2026-11-10）

监理 R16 报告（`docs/reviews/P3-001-review.md` 第 245 行起）结论摘要：

- **用户确认**：用户在 R15 展示实际 WPF 蓝圆/橙三角预览后明确答复"很满意"，对当前轻量标记方案的视觉接受。
- **F-08 关闭**：结合 R14 原版隔离操作体验认可、R13 实际鼠标部分证据、R12 独立自然退出及此前保存恢复/隔离证据、R15 徽记归属和相关验证，原 F-08 剩余条件全部满足。F-08 通过并关闭，F-01～F-08 全部关闭。
- **S3 接收**：S1/S2 接收不变，S3 最终接收，P3-001 双宠共存与存档完成。
- **TASKS 状态**：TASKS 可标 done（已同步）。
- **后续阻断解除**：解除 P3-002 依赖阻断，但不等于授权自动开工社交/自主移动；下一任务待用户安排，不提交推送、不替换正式宠物。
- **证据一致性**：监理核对 App.xaml.cs、MainWindow.xaml、MainWindow.xaml.cs 当前 SHA256 与 R15 证据完全一致，无产品改动，无新风险，不重复构建测试。沿用 R15 独立相关 33/33 及徽记专项 1/1，开发全量 693 为自测。
- **未提交推送**：保留全部既有未提交修改。

本段仅引用监理报告 R16 结论，不重跑测试，不修改监理报告或证据。
