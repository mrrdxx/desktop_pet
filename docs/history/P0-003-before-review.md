> 归档说明：正文是当时的状态快照，不是当前任务状态。2026-10-22 全面核查仅修正因归档目录变化而失效的相对链接，历史内容和结论保留。

# 当前状态

更新时间：2026-10-21 17:30（Asia/Shanghai）。任务状态唯一出处为 [TASKS](../TASKS.md)。

## 当前接手任务

P0-003（坐标、显示器与找回）必需项已全部完成，用户确认 100% 实机验证通过。
混合 DPI 双屏和显示器断开恢复因无第二屏标记未验证（TASKS 明确为可选）。
监理 R2 已解除 P0-002 阻断；见 [审查报告](../reviews/P0-002-review.md) R2 节。
KL-001（Alt+Tab 拖动跟随）保留为已知限制，不本轮修复。

## P0-003 实现进度

- Core 坐标模型：`MonitorRectPx`/`WorkAreaPx`/`MonitorInfo`/`CoordinateTransform`/`MonitorLocator`，物理像素浮点坐标，脚底中央锚点。
- Core 测试：17/17 通过（CoordinateTransformTests 6 项 + MonitorLocatorTests 11 项），覆盖 100%/150% DPI、负坐标、显示器间空洞、工作区约束、找回映射。
- App Platform：`MonitorInterop.cs`（EnumDisplayMonitors + GetMonitorInfoW + Shcore.GetDpiForMonitor）；帮手修复了错误导入。
- App 找回集成：右键菜单增加 Recover 项（MenuIdRecover=2），`RecoverPet()` 调用 MonitorInterop + MonitorLocator + CoordinateTransform。
- App PMv2 DPI 感知：添加 `app.manifest` 声明 PerMonitorV2（ARCHITECTURE §4 要求）。
- App 代码编译 0 CS 错误；完整构建成功（0 警告 0 错误）。

### 验证结果

- 150% 实机（帮手自测）：启动点、连续 3 次 Recover、部分屏外、完全屏外均恢复到工作区内；拖动/松手、焦点保持、菜单关闭/Esc/Exit 均通过。详见 [修复交接](../handoffs/P0-003-recover-fix.md)。
- 100% 实机（用户确认 2026-10-21）：Recover 恢复到主屏可见位置，拖动抓取点不跳变，PMv2 声明未引入回归。用户反馈"非常完美"。
- Core 测试 17/17 通过（100%/150% DPI 转换、负坐标、显示器间空洞、工作区约束、找回映射）。
- 混合 DPI 双屏、显示器断开恢复：无第二屏，标记未验证（TASKS 明确为可选，不阻塞）。

### Recover 修复历史

- 根因是 `GetDpiForMonitor` 错误导入为 `user32.dll`（应为 `shcore.dll`），触发 `EntryPointNotFoundException` 崩溃。
- 帮手修复：改为 `shcore.dll` 导入，DPI 查询移到回调返回后，增加防御检查。详见 [修复交接](../handoffs/P0-003-recover-fix.md)。
- 主开发 AI 接手后核对帮手改动并保留，添加 PMv2 DPI 感知声明，完成 100% 实机验证。

## P0-002 完成依据

- 用户在本监理对话直接确认七项验收中六项通过：透明区穿透、焦点保持、100%/150% 抓取点、正常拖放、菜单打断后再拖。
- Alt+Tab 拖动切换仍跟随这一项失败，用户接受现状；按报告 R2 的 KL-001 解除本任务阻断，不能写成七项全通过。
- F-01/F-02 的原整改有 R1 静态核查与本轮用户实测；F-03 的已测正常路径接受，Alt+Tab 部分以用户豁免处理。
- [菜单修复交接](../handoffs/P0-002-menu-fix.md) 的 SHA256 只标识当时版本；P0-003 已增加 Recover，当前版本见本轮修复交接，不能再声称主窗口代码未变化。
- 既有构建及菜单回归日志有效，来源为修复作者，不冒充本轮独立测试；开发侧过程见 [整改记录](../remediation/P0-002-response.md)。

## 已知限制

- KL-001：拖动中 Alt+Tab 切走后桌宠仍跟随。现阶段接受，不要求本轮修复；保留原测试预期及失败结果。
- 豁免仅覆盖这一场景，不包括正常松手仍跟随、其他捕获异常或后续新问题；来源与完整范围以 R2 为准。
- 已有 P0-002 单屏 100%/150% 用户拖动结果已由 P0-003 的 Recover 和 PMv2 声明补充覆盖；100%/150% Recover 和拖动均实测通过。
- 混合 DPI 双屏、显示器断开恢复：无第二屏，标记未验证（TASKS 明确为可选）。
- PMv2 已通过 app.manifest 声明；但交接记录指出微软建议 PMv2 下用 `GetDpiForWindow` 而非 `GetDpiForMonitor`，混合 DPI 多屏场景在后续有设备时重新评估。
- 当前无正式美术、安装包。核心测试工程已建立（DesktopPet.Core.Tests，17 项通过）。

## 接续动作

1. P0-003 必需项已全部完成：Core 坐标模型与 17 项测试通过，Recover 功能 100%/150% 实测通过，PMv2 DPI 感知已声明。混合 DPI 双屏和显示器断开恢复因无设备标记未验证。
2. 下一步是 P0-004（生命周期与可靠存档），不提前进入。
3. 阶段 0 的其他任务和最终整体验收继续按原计划执行。

## 主要入口

- 产品与技术：[PRODUCT](../PRODUCT.md)、[ARCHITECTURE](../ARCHITECTURE.md)。
- 验证和审查：[TESTING](../TESTING.md)、[REVIEW_PROCESS](../REVIEW_PROCESS.md)。
- 验收例外：[DECISIONS](../DECISIONS.md) D-007；正式原文与处置见 R2。
- 换新对话：[接续指南](../CONTEXT_HANDOFF.md)。
