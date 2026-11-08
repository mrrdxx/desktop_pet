# PREP-002 S2 R2 整改记录

## F-01 中：多个依赖共用首对象判空，违反逐对象三级优先

**报告**：[PREP-002-review.md](../reviews/PREP-002-review.md) S2 R2 F-01

**问题**：OnUpdateTick 中仅在 `runtimeTracker` 为 null 时才读取 `StateContext.RareEventScheduler`；若只注入 `TestRuntimeTracker`，`rareScheduler` 仍 null，上下文中的 scheduler 永远不被取用。同类模式见情绪组三对象（只在 `emotionAcc` 为空时补 `corner`/`grievance`）和 `InterruptGrievanceExpression`（只在 `grievanceExpr` 为空时补 `emotionAcc`）。

### 整改内容

1. **提取最小解析 helper**（`MainWindow.xaml.cs`）：
   ```csharp
   private static T? Resolve<T>(T? test, T? context, Func<App, T?> getApp) where T : class
   {
       if (test is not null) return test;
       if (context is not null) return context;
       if (System.Windows.Application.Current is App app)
           return getApp(app);
       return null;
   }
   ```

2. **每个状态对象独立解析**，不再用首对象判空控制其他对象回退：
   - `OnUpdateTick` 情绪组：`emotionAcc`、`cornerTrack`、`grievanceExpr` 各自独立 `Resolve`。
   - `OnUpdateTick` 稀有事件组：`runtimeTracker`、`rareScheduler` 各自独立 `Resolve`。
   - `InterruptGrievanceExpression`：`grievanceExpr`、`emotionAcc` 各自独立 `Resolve`。
   - `EndDrag`：`cornerTrack` 独立 `Resolve`。
   - `PauseAnimation`：`rtPause` 独立 `Resolve`。
   - `ResumeAnimation`：`emotionAcc`、`cornerTrack`、`rtResume` 各自独立 `Resolve`。

3. **更正"Application.Current 搜索结果为零"描述**：`Application.Current is App` 仍保留在 `NotifyStateDirty()` 回退和 `Resolve` helper 中，正确描述是"生产显式上下文优先，保留 App.Current 回退"。

### 修复位置

- `src/DesktopPet.App/MainWindow.xaml.cs`：`Resolve` helper（第 279-288 行）、`OnUpdateTick` 情绪组、`OnUpdateTick` 稀有事件组、`InterruptGrievanceExpression`、`EndDrag`、`PauseAnimation`、`ResumeAnimation`。

### 新增回归测试

- `tests/DesktopPet.App.Tests/PetStateContextTests.cs`（7 个测试）：
  - `F01_NonNullContext_TickAdvancesTracker_AndFiresSaveCallback`：非空上下文 tick 推进 tracker 累计 1 分钟，创建计划，保存回调命中。
  - `F01_OverrideTestRuntimeTracker_SchedulerStillFromContext`：仅覆盖 TestRuntimeTracker，scheduler 仍来自上下文。
  - `F01_OverrideTestEmotion_CornerAndGrievanceStillFromContext`：仅覆盖 TestEmotionAccumulator，corner/grievance 仍来自上下文。
  - `F01_InterruptGrievance_OverrideTestGrievance_EmotionStillFromContext`：打断路径仅覆盖 TestGrievanceExpression，emotion 仍来自上下文，保存回调命中。
  - `F01_PauseWithContext_TrackerEndActivePeriodCalled`：暂停路径上下文 tracker EndActivePeriod 被调用。
  - `F01_ResumeWithContext_TrackerStartActivePeriodCalled`：恢复路径上下文 tracker StartActivePeriod 被调用。
  - `F01_NullContext_TestFieldsStillWork`：空上下文 Test* 字段仍正常工作。

### 自测证据

- 构建 0 警告 0 错误。
- 全量回归 636/636 通过（Core 351 + App 285），0 失败 0 跳过。
- 新增 7 个测试全部通过（285 - 278 = 7）。
- 行为、时间注入、存档格式和物理参数不变。
- 现有测试无需修改（Test* 为 null 且 StateContext 为 null 时走 App.Current 回退，语义不变）。

### 限制

- 未执行系统鼠标实机烟测。
- `Resolve` helper 是最小解析函数，不扩展为容器框架。

### 复核请求

请监理复核 F-01 整改是否满足"每个状态独立按 Test* → StateContext 对应成员 → App 对应成员解析"要求，以及新增回归测试是否覆盖非空上下文、单项覆盖、保存回调及打断路径。

## R5 最终依据追加（2026-11-08）

**报告**：[PREP-002-review.md](../reviews/PREP-002-review.md) 最终复核 R5

监理 R5 接受 PREP-002 有限整理范围，标 done。开发方在原整改记录追加 R5 依据，不重复申请关闭或扩大重构。

### R5 关键结论

- 烟测原始日志 run-bb4af09a 覆盖 2026-11-08 02:27～02:34 Asia/Shanghai，DPI144（150%），单屏 2560×1600，interactive 模式。探头 curious 启动→2.89s 恢复 idle/原位置，consumed=true，checksFailures=0。
- 用户反馈启动/悬停、身体/头部拖动、轻放/抛掷、暂停恢复、找回、正常退出全部正常；交互模式非 auto 全自动 PASS，不将探针首段检查替代后续用户体验。
- REAL_ARCHIVE_BEFORE/AFTER 哈希一致，生产存档未改变。隔离档 schemaVersion=3，位置 1426/898.67，isPaused=false，累计 156.62，计划已消费。
- 当前 MainWindow/MonotonicClock/PetStateContext/RareEventUpdateService/DiagnosticLogger 与隔离副本逐字节一致；夹具补 Diagnostics 属正常适配。
- 合并 S1 R1 独立 52/52、S2 R3 独立 54/54 + F-01 关闭、S3 R4 独立 81/81，同版本全量 636/636（不重跑）+ 必要烟测，PREP-002 验收通过。

### 限制保留

单屏单 DPI144，不证明混合 DPI/热切换；Shell 通知区域真实点击未覆盖；半身可见为人工判断；ART 历史限制不代签。不宣称性能改善。
