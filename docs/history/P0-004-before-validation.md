> 归档说明：正文是当时的状态快照，不是当前任务状态。2026-10-22 全面核查仅修正因归档目录变化而失效的相对链接，历史内容和结论保留。

# 当前状态

审查更新：2026-10-22（Asia/Shanghai）。任务状态唯一出处为 [TASKS](../TASKS.md)。

## 当前接手任务

P0-004（生命周期与可靠存档）代码实现与自动测试已完成，状态 `awaiting-verification`。
P0-003 监理 R4 已通过（F-01～F-04 全部关闭），不重复其验收。
KL-001（Alt+Tab 拖动跟随）保留为已知限制，不本轮修复。

## P0-004 实现进度

- 存档模型 v1：`PetStateArchive`（state.json）和 `PetSettingsArchive`（settings.json），字段结构已补入 ARCHITECTURE §7。
- ArchiveService：安全写入（tmp→bak→replace）、损坏恢复（保留 .corrupted + .bak 回退）、未知高版本只读保护。
- SaveCoordinator：5 秒防抖保存 + Flush 即时落盘，拖动结束 MarkStateDirty 触发。
- SingleInstanceGuard：命名互斥锁 `Global\DesktopPet.App.SingleInstance`，重复启动弹提示并退出。
- TrayService：WinForms NotifyIcon 托盘菜单（Pause/Resume、Recover、Exit），事件回调集成到 App。
- App.xaml.cs：启动链加载存档→恢复窗口位置→创建托盘；退出链保存位置→Flush→释放托盘和互斥锁。
- MainWindow：PauseAnimation/ResumeAnimation 控制动画定时器；EndDrag 后 MarkStateDirty。

### 验证结果

- 构建验证：`dotnet build` 成功，0 警告 0 错误（4 项目：Core, App, Core.Tests, App.Tests）。
- 测试验证：`dotnet test` Core 22/22 + App 25/25 = **47/47 全部通过**。
- M-05 存档恢复自动测试（10 项）：保存/恢复、损坏→备份回退、损坏→默认回退、未知版本不覆盖、Flush 即时保存、防抖不逐帧写盘、备份保留、缺失文件默认值。
- M-04 生命周期：单实例、托盘退出/暂停/找回、防抖保存的代码路径已实现；人工实机验证（M-04 四项）待用户执行。
- 修复了 `SaveState_UnknownHigherVersion_DoesNotOverwrite` 测试断言字符串匹配问题（原始 JSON 有空格）。

## P0-003 完成依据

- 监理 R4（2026-10-22）已关闭 F-02，F-01～F-04 全部关闭；位移断言正反例独立检查通过。
- 证据见 [P0-003 审查报告](../reviews/P0-003-review.md) R4 和 [整改记录](../remediation/P0-003-response.md)。

## 已知限制

- KL-001：拖动中 Alt+Tab 切走后桌宠仍跟随。现阶段接受，不要求本轮修复。
- 混合 DPI 双屏、显示器断开恢复：无第二屏，标记未验证（TASKS 明确为可选）。
- 当前无正式美术、安装包。测试工程已建立（Core.Tests 22 项 + App.Tests 25 项 = 47 项通过）。
- M-04 生命周期四项人工验证待执行（单实例重复启动、暂停行为停止、托盘退出无进程、重启位置恢复）。

## 接续动作

1. 用户执行 M-04 人工验证（4 项必需）和 M-05 人工验证（4 项必需），确认后标记 P0-004 done。
2. 混合 DPI 双屏验证为可选项，有条件时补充。
3. P0-004 完成后进入 P0-005 阶段 0 整体验收。

## 主要入口

- 产品与技术：[PRODUCT](../PRODUCT.md)、[ARCHITECTURE](../ARCHITECTURE.md)。
- 验证和审查：[TESTING](../TESTING.md)、[REVIEW_PROCESS](../REVIEW_PROCESS.md)。
- 验收例外：[DECISIONS](../DECISIONS.md) D-007；正式原文与处置见 R2。
- 换新对话：[接续指南](../CONTEXT_HANDOFF.md)。
