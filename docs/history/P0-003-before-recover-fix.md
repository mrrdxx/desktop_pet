> 归档说明：正文是当时的状态快照，不是当前任务状态。2026-10-22 全面核查仅修正因归档目录变化而失效的相对链接，历史内容和结论保留。

# 当前状态

更新时间：2026-10-21（Asia/Shanghai）。任务状态唯一出处为 [TASKS](../TASKS.md)。

## 当前接手任务

P0-003（坐标、显示器与找回）代码已实现，Core 测试 17/17 通过，App 代码编译通过。
**Recover 功能实机验证失败**：右键 Recover 后宠物消失（窗口移出可见区域）。
用户已请帮手处理此问题，主开发 AI 暂停代码修改，等待接手通知。
监理 R2 已解除 P0-002 阻断；见 [审查报告](../reviews/P0-002-review.md) R2 节。
KL-001（Alt+Tab 拖动跟随）保留为已知限制，不本轮修复。

## P0-003 实现进度

- Core 坐标模型：`MonitorRectPx`/`WorkAreaPx`/`MonitorInfo`/`CoordinateTransform`/`MonitorLocator`，物理像素浮点坐标，脚底中央锚点。
- Core 测试：17/17 通过（CoordinateTransformTests 6 项 + MonitorLocatorTests 11 项），覆盖 100%/150% DPI、负坐标、显示器间空洞、工作区约束、找回映射。
- App Platform：`MonitorInterop.cs`（EnumDisplayMonitors + GetMonitorInfoW + GetDpiForMonitor）。
- App 找回集成：右键菜单增加 Recover 项（MenuIdRecover=2），`RecoverPet()` 调用 MonitorInterop + MonitorLocator + CoordinateTransform。
- App 代码编译 0 CS 错误；完整构建成功（0 警告 0 错误）。

### Recover 功能 bug 与已尝试方法

- **现象**：右键 → Recover 后宠物消失（窗口移出可见区域）。
- **初版 `RecoverPet()`**：从当前 `Left/Top`（DIP）经 `TransformFromDevice.M11` 反推物理像素，计算脚底中央坐标，再 `ClampToWorkArea` 后转回 DIP 赋值 `Left/Top`。实机结果：宠物消失。
- **第一轮修复**：改为直接定位到主显示器（origin 0,0）工作区中心，不依赖当前位置转换。构建通过并启动，**尚未验证结果**（用户在此轮请帮手接手）。
- **当前代码状态**：`MainWindow.xaml.cs` 中 `RecoverPet()` 是第一轮修复版本（直接定位主屏中心），尚未实机验证。
- **可能原因猜测**（未证实）：DPI 转换边界问题、`MonitorInterop.EnumerateMonitors` 返回数据异常、`FootCenterToWindowTopLeft` 在实际 DPI 下计算偏差。
- **未完成改动**：Recover 功能修复未完成；100%/150% 拖动无回归验证未执行。

### 帮手接手说明

- 用户已请帮手处理 Recover 功能 bug。
- 主开发 AI 暂停所有代码修改，保留现有修改不回退。
- 接手后先检查帮手的实际变更和交接记录，不覆盖或回退帮手改动。

## P0-002 完成依据

- 用户在本监理对话直接确认七项验收中六项通过：透明区穿透、焦点保持、100%/150% 抓取点、正常拖放、菜单打断后再拖。
- Alt+Tab 拖动切换仍跟随这一项失败，用户接受现状；按报告 R2 的 KL-001 解除本任务阻断，不能写成七项全通过。
- F-01/F-02 的原整改有 R1 静态核查与本轮用户实测；F-03 的已测正常路径接受，Alt+Tab 部分以用户豁免处理。
- 两份源码 SHA256 与 [菜单修复交接](../handoffs/P0-002-menu-fix.md) 一致，没有新代码变更。
- 既有构建及菜单回归日志有效，来源为修复作者，不冒充本轮独立测试；开发侧过程见 [整改记录](../remediation/P0-002-response.md)。

## 已知限制

- KL-001：拖动中 Alt+Tab 切走后桌宠仍跟随。现阶段接受，不要求本轮修复；保留原测试预期及失败结果。
- 豁免仅覆盖这一场景，不包括正常松手仍跟随、其他捕获异常或后续新问题；来源与完整范围以 R2 为准。
- 已有单屏 100%/150% 用户操作结果不等于跨屏 DPI、负坐标或显示器断开已完成，这些仍由 P0-003 验证。
- 当前无正式美术、安装包。核心测试工程已建立（DesktopPet.Core.Tests，17 项通过）。

## 接续动作

1. 两处非阻断措辞更正已完成：Alt+Tab 说明改为"验收失败、用户接受的限制"；菜单自动回归覆盖范围收窄。整改记录顶部"当前边界"已更新为引用 R2。
2. P0-003 代码已实现，Core 测试 17 项全部通过；Recover 功能实机验证失败（宠物消失），用户已请帮手处理，主开发 AI 暂停等待接手。
3. 接手后先检查帮手变更和交接记录，不覆盖或回退帮手改动；再继续 Recover 修复和 100%/150% 拖动无回归验证。
4. 阶段 0 的其他任务和最终整体验收继续按原计划执行，不因 P0-002 放行跳过。

## 主要入口

- 产品与技术：[PRODUCT](../PRODUCT.md)、[ARCHITECTURE](../ARCHITECTURE.md)。
- 验证和审查：[TESTING](../TESTING.md)、[REVIEW_PROCESS](../REVIEW_PROCESS.md)。
- 验收例外：[DECISIONS](../DECISIONS.md) D-007；正式原文与处置见 R2。
- 换新对话：[接续指南](../CONTEXT_HANDOFF.md)。
