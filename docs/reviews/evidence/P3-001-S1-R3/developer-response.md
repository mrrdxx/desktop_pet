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

## 限制与复核请求

- 整改自测完成，待监理复核 F-01/F-02。
- 未提交/推送（用户未授权）。
- 不进入 S2/S3。
