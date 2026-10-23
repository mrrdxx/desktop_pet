> 归档说明：正文是当时的状态快照，不是当前任务状态。2026-10-22 全面核查仅修正因归档目录变化而失效的相对链接，历史内容和结论保留。

# 当前状态

审查更新：2026-10-22（Asia/Shanghai）。任务状态唯一出处为 [TASKS](../TASKS.md)。

## 当前接手任务

**P0-004 暂停交接**：监理验证发现 F-01～F-04 四项阻断，退回整改（TASKS=`in-progress`）。
开发方暂停代码修改，等待监理助手接手修复；不进入 P0-005。
依据见 [P0-004 验证报告](../reviews/P0-004-review.md)，整改记录待建立 `docs/remediation/P0-004-response.md`。
P0-003 监理 R4 已通过（F-01～F-04 全部关闭），不重复其验收。
KL-001（Alt+Tab 拖动跟随）保留为已知限制，不本轮修复。

## 实际代码状态（2026-10-22 暂停前验证）

- 构建验证：`dotnet build` 成功，0 警告 0 错误（4 项目：Core, App, Core.Tests, App.Tests）。
- 测试验证：`dotnet test` Core 22/22 + App 25/25 = **47/47 全部通过**。
- 生产代码未回退，保留现有修复和验证证据。
- 无运行中的桌宠进程；有 3 个 MSBuild 节点进程（`/nodeReuse:true`，IDE 后台节点，会自行回收，无需手动终止）。

## P0-004 已实现代码（保留不回退）

- 存档模型 v1：`src/DesktopPet.Core/Persistence/PetStateArchive.cs`（PetStateArchive + PetSettingsArchive）。
- ArchiveService：`src/DesktopPet.App/Persistence/ArchiveService.cs`（安全写入+备份+损坏恢复+版本检查）。
- SaveCoordinator：`src/DesktopPet.App/Persistence/SaveCoordinator.cs`（5秒防抖+Flush）。
- SingleInstanceGuard：`src/DesktopPet.App/Platform/SingleInstanceGuard.cs`（命名互斥锁）。
- TrayService：`src/DesktopPet.App/Platform/TrayService.cs`（WinForms NotifyIcon 暂停/找回/退出）。
- App.xaml.cs：启动链（单实例→加载存档→恢复位置→创建托盘）、退出链（保存位置→Flush→释放资源）。
- MainWindow.xaml.cs：PauseAnimation/ResumeAnimation、EndDrag 后 MarkStateDirty。
  - P0-003 R2 重构已完成：`ClampWindowToWorkArea`/`OnDisplayLayoutChanged`/`RecoverPet` 均已改为调用 `WindowPositionAdapter` 静态方法。
  - `WindowPositionAdapter.cs` 已完整创建（3 个公开方法：ClampToWorkArea/RecoverPosition/RecoverToPrimary）。
  - `WindowPositionAdapterTests.cs` 已创建（16 项 App 适配测试，含跨 DPI、布局缩减、边界场景）。
- ARCHITECTURE §7 已补入 v1 字段结构和恢复规则。

## 监理发现的四项阻断（待修复）

- **F-01**（P1）：不兼容字段的高版本存档被误判损坏并降级。ArchiveService.DeserializeFile 先读版本号但仍按 v1 DTO 反序列化，JsonException 导致 futurePayload 丢失。需：未知高版本进入独立只读保护，不依赖 v1 类型反序列化成功。
- **F-02**（P1）：防抖保存完成后再改变位置，退出不保证最后位置落盘。Recover 改变位置后 ShutdownMainWindow 更新 _state 但未重新标脏，Flush 看到 dirty=false 不写盘。需：退出必须将最终状态纳入保存协调器。
- **F-03**（P2）：重启仅恢复暂停标记，没有恢复暂停行为。OnStartup 设置 _isPaused=True 但 MainWindow 构造时直接启动 _frameTimer。需：启动时应用存档的暂停行为，使状态/菜单/定时器一致。
- **F-04**（P1）：启动直接赋值旧坐标，没有执行可见位置恢复。OnStartup 直接 `_mainWindow.Left = _state.WindowLeft` 而不经过显示器恢复适配器。需：加载存档后坐标进入工作区/显示器恢复流程再显示。

## 未完成修改

- 上述 F-01～F-04 均未开始修复，代码保持监理验证时的状态。
- `docs/remediation/P0-004-response.md` 尚未建立。
- 隔离验证副本在 `artifacts/p0-004-validation/`（已加入 .gitignore），监理助手可参考其探针和驱动脚本。

## 已知限制

- KL-001：拖动中 Alt+Tab 切走后桌宠仍跟随。现阶段接受，不要求本轮修复。
- 混合 DPI 双屏、显示器断开恢复：无第二屏，标记未验证（TASKS 明确为可选）。
- Shell 托盘真实点击、真实休眠/锁屏等未执行，不写成已通过。

## 接续动作

1. 监理助手接手后先核对当前代码与报告 F-01～F-04 描述一致，再开始修复。
2. 修复按报告中的"整改目标"和"验收"执行，补充对应应用/服务回归测试。
3. 整改过程写入 `docs/remediation/P0-004-response.md`，不修改监理报告。
4. 四项阻断关闭且验收完成后才进入 P0-005。

## 主要入口

- 产品与技术：[PRODUCT](../PRODUCT.md)、[ARCHITECTURE](../ARCHITECTURE.md)。
- 验证和审查：[TESTING](../TESTING.md)、[REVIEW_PROCESS](../REVIEW_PROCESS.md)。
- 验收例外：[DECISIONS](../DECISIONS.md) D-007；正式原文与处置见 R2。
- 换新对话：[接续指南](../CONTEXT_HANDOFF.md)。
