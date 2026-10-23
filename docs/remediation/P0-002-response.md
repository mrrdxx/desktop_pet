# P0-002 整改记录

维护者：开发 AI。此文件由监理建立空白起点；开发 AI 已填写整改内容和自测证据，当前状态见各问题条目和后续记录。
对应报告：[P0-002 监理审查](../reviews/P0-002-review.md)。
工作规则：[审查协作规则](../REVIEW_PROCESS.md)，详细字段见 [模板](../templates/REMEDIATION_TEMPLATE.md)。

- 建立日期：2026-10-21（Asia/Shanghai）。
- 更新时间/时区：2026-10-21（Asia/Shanghai）。
- 代码版本：无 Git；变更文件 `src/DesktopPet.App/MainWindow.xaml`（25 行，SHA256 `F1CF5CDFAC060CD8651B00776C4C9DDFAEE23420857F95DBE39DE50C66F511A7`）、`src/DesktopPet.App/MainWindow.xaml.cs`（415 行，SHA256 `2BF4DC04D9ED7A07F2440B2E3798CFF5CE855087137DBD8D2BBCFF6FBA92A1BA`）。`dotnet build DesktopPet.slnx -c Debug` 成功无错误无警告（2026-10-21）。
- 本轮范围：处理报告中的三个阻断项 F-01/F-02/F-03；本文件不改变 TASKS 状态。
- 当前边界：监理 R2 已通过，P0-002 已标记 done；详见 [审查报告](../reviews/P0-002-review.md) R2 节。

## docs/reviews/P0-002-review.md#F-01

- 开发处置：已整改，待复核。
- 修改说明与文件位置：
  - `MainWindow.xaml`：添加 `SourceInitialized="MainWindow_SourceInitialized"` 事件绑定。
  - `MainWindow.xaml.cs`：
    - 添加 Win32 P/Invoke 声明 `GetWindowLong`/`SetWindowLong`，常量 `WS_EX_NOACTIVATE`、`WS_EX_LAYERED`、`WM_MOUSEACTIVATE`、`MA_NOACTIVATE`。
    - `MainWindow_SourceInitialized`：窗口句柄创建后追加 `WS_EX_NOACTIVATE` 扩展样式，确保点击不激活窗口。
    - `OnSourceInitialized` + `WndProc`：挂钩 `WM_MOUSEACTIVATE` 消息返回 `MA_NOACTIVATE`，作为第二层防护。
    - 保留原有 `Focusable=False` 和 `ShowActivated=False`。
- 自测环境：Windows 11 (10.0.26200)，.NET 10 SDK 10.0.402。`Graphics.FromHwnd(0).DpiX=96` 来自探测进程的 DPI 感知结果，仅说明探测进程看到的 DPI，不足以独立证明系统显示缩放为 100%；实测时应从显示设置或宠物窗口自身的 DPI 感知确认。
- 实际命令、退出码及关键结果：
  - `dotnet build DesktopPet.slnx -c Debug`：成功，无错误无警告。
  - `dotnet run --no-build`：窗口启动成功，可见窗口 1 个，标题 "DesktopPet"。
  - `PostMessage(WM_CLOSE)`：进程自然退出。
- 人工步骤、观察者和实际现象：**未执行**。报告要求的验证（编辑器输入→拖动宠物→释放→继续输入→文字仍进入原编辑器）需人工实机操作。
- 未验证项及原因：非激活效果在 100% 缩放下通过静态代码审查和构建验证确认实现到位，但"拖动不抢焦点"的端到端人工验证未执行。
- 证据路径：无独立证据文件；代码变更在上述文件中。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：F-01 的实现方案和静态正确性；人工焦点验证待用户/监理执行。

## docs/reviews/P0-002-review.md#F-02

- 开发处置：已整改，待复核。
- 修改说明与文件位置：
  - `MainWindow.xaml.cs` 的 `PetCanvas_MouseLeftButtonDown` 和 `PetCanvas_MouseMove`：
    - `PointToScreen` 返回物理像素，`Window.Left/Top` 使用 DIP（逻辑单位）。
    - 通过 `PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice` 将屏幕像素转换为 DIP 后再计算偏移和赋值。
    - 100% 缩放下 `TransformFromDevice` 为单位矩阵，行为与之前一致；非 100% 缩放下修正了像素与 DIP 混用问题。
- 自测环境：Windows 11 (10.0.26200)，.NET 10 SDK 10.0.402。`Graphics.FromHwnd(0).DpiX=96` 来自探测进程的 DPI 感知结果，仅说明探测进程看到的 DPI，不足以独立证明系统显示缩放为 100%；实测时应从显示设置或宠物窗口自身的 DPI 感知确认。
- 实际命令、退出码及关键结果：
  - `dotnet build`：成功，无错误无警告。
  - `dotnet run`：窗口启动成功，可见。
  - 100% 缩放下 `TransformFromDevice` 为单位矩阵，代码推断拖动行为与修复前一致；此为单位矩阵下的代码推断，不是真实拖动观察结果。
- 人工步骤、观察者和实际现象：**未执行**。报告要求的 150% 缩放拖动验证需人工切换缩放设置后操作。
- 未验证项及原因：当前环境为 100% 缩放，无法自动验证 150% 缩放下抓取点不漂移。DPI 转换逻辑通过代码审查确认正确，但缺少非 100% 缩放的实机证据。
- 证据路径：无独立证据文件。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：F-02 的 DPI 转换实现方案；150% 缩放拖动验证待人工执行。

## docs/reviews/P0-002-review.md#F-03

- 开发处置：已整改，待复核。
- 修改说明与文件位置：
  - `MainWindow.xaml`：添加 `LostMouseCapture="PetCanvas_LostMouseCapture"` 事件绑定。
  - `MainWindow.xaml.cs`：
    - `PetCanvas_MouseLeftButtonDown`：先调用 `CaptureMouse()`，检查 `IsMouseCaptured` 确认捕获成功后才进入拖动状态。不再先设 `_isDragging` 再捕获。注：`CaptureMouse()` 官方签名返回 bool（[CaptureMouse](https://learn.microsoft.com/dotnet/api/system.windows.uielement.capturemouse)）；当前代码通过检查 `IsMouseCaptured` 判断捕获状态，逻辑有效，无需为此重写。
    - 新增 `EndDrag()` 统一清理入口：重置 `_isDragging`，仅在 `IsMouseCaptured` 为 true 时调用 `ReleaseMouseCapture()`。
    - `PetCanvas_MouseLeftButtonUp`：调用 `EndDrag()`。
    - `PetCanvas_LostMouseCapture`：捕获丢失时调用 `EndDrag()`，防止捕获被抢走后保留错误拖动状态。
    - `PetCanvas_MouseMove`：检测到左键已松开但仍在拖动状态时，通过 `EndDrag()` 清理。
    - `OnClosed`：窗口关闭时调用 `EndDrag()` 清理。
- 自测环境：Windows 11 (10.0.26200)，.NET 10 SDK 10.0.402。`Graphics.FromHwnd(0).DpiX=96` 来自探测进程的 DPI 感知结果，仅说明探测进程看到的 DPI，不足以独立证明系统显示缩放为 100%；实测时应从显示设置或宠物窗口自身的 DPI 感知确认。
- 实际命令、退出码及关键结果：
  - `dotnet build`：成功，无错误无警告。
  - `dotnet run`：窗口启动成功，正常退出。
- 人工步骤、观察者和实际现象：**未执行**。报告要求的验证（正常拖放、捕获失败/丢失、菜单打断后均可再次拖动）需人工实机操作。
- 未验证项及原因：捕获丢失/菜单打断场景需人工触发并观察行为。统一清理入口通过代码审查确认覆盖所有路径，但缺少实机干扰测试证据。
- 证据路径：无独立证据文件。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：F-03 的统一清理入口实现方案；捕获丢失/菜单打断的人工验证待执行。

## 后续记录

开发 AI 按规则补齐实际修改、验证环境、命令/观察和版本。监理复核结论只链接原报告，不在此代签。

### 2026-10-21 首轮整改

- 三个阻断项均已实现修复，构建通过，窗口启动和正常退出验证通过。
- 人工验证项（M-01 透明穿透、M-02 焦点保持/拖动抓取点/捕获丢失、150% 缩放拖动）均未执行，需有桌面控制能力的操作者完成。
- 环境限制：单屏 100% 缩放，无法自动切换缩放或模拟跨进程点击。

### 2026-10-21 重新核实

- 重新阅读 AGENTS.md（含审查与整改纪律）和 REVIEW_PROCESS.md，确认流程要求。
- 对照代码逐项核实 F-01/F-02/F-03 整改实现，与整改记录描述一致：
  - F-01：`MainWindow_SourceInitialized` 设置 `WS_EX_NOACTIVATE`；`OnSourceInitialized` 挂钩 `WndProc` 拦截 `WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`（MainWindow.xaml.cs 第 69–96 行）。
  - F-02：`PetCanvas_MouseLeftButtonDown`/`PetCanvas_MouseMove` 使用 `TransformFromDevice` 将 `PointToScreen` 物理像素转 DIP 后再赋值 `Left/Top`（第 214–244 行）。
  - F-03：`CaptureMouse()` 先调用再检查 `IsMouseCaptured`；`EndDrag()` 统一清理入口；`LostMouseCapture`/`MouseLeftButtonUp`/`MouseMove` 左键松开检测/`OnClosed` 覆盖所有取消路径（第 202–302 行）。
- `dotnet build DesktopPet.slnx -c Debug`：成功，无错误无警告。
- 开发处置不变：已整改，待复核。人工验证项仍需操作者执行。

### 2026-10-21 R1 复核后文档更正

监理 R1 复核结论：部分通过，均未关闭（详见 [审查报告](../reviews/P0-002-review.md) R1 节）。无新增确定性代码阻断项；完成门槛仍是原三项和 P0-002 自身验收。按 R1"文档与陈述更正请求"逐项修正：

1. 整改记录开头"尚无开发整改或自测结果"与后文冲突 → 已改为历史建立说明。
2. "100% 缩放下拖动行为与修复前一致"列为结果但无真实拖动观察 → 已改为"单位矩阵下的代码推断，不是真实拖动观察结果"。
3. `Graphics.FromHwnd(0).DpiX=96` 不足以独立证明系统缩放为 100% → 三个问题条目的自测环境均已补充来源限制说明。
4. TESTING.md 开头"当前尚无应用"过期 → 已去掉过期项目状态，保留为操作说明。
5. 对话中称 `CaptureMouse()` 返回 void 是错误的 → 已在 F-03 条目更正：官方签名返回 bool，当前检查 `IsMouseCaptured` 的代码逻辑有效，无需重写。

以上修订不新建 F 编号、不改变原阻断范围。代码未变更，无新增构建或运行验证。

### 2026-10-21 帮手右键菜单修复接手

用户授权监理助手临时介入右键菜单修复，主开发 AI 已核对实际代码并接手，保留修复，不恢复旧 WPF ContextMenu 逻辑。修复详情见 [修复交接](../handoffs/P0-002-menu-fix.md)。

**代码变更**（已核对，与交接记录一致）：

- `MainWindow.xaml`（25 行）：删除了 WPF `ContextMenu` 绑定和 `MouseRightButtonUp` 事件绑定。
- `MainWindow.xaml.cs`（415 行）：
  - 新增 Win32 P/Invoke：`TrackPopupMenuEx`、`CreatePopupMenu`、`AppendMenuW`、`DestroyMenu`、`GetCursorPos`、`GetForegroundWindow`、`SetForegroundWindow`、`IsWindow`、`PostMessageW`。
  - `WndProc` 拦截 `WM_RBUTTONUP`（0x0205），用 `GetCursorPos` 获取屏幕坐标后调用 `ShowNativeContextMenu`。
  - `ShowNativeContextMenu`：创建弹出菜单 → 临时移除 `WS_EX_NOACTIVATE` 并调用 `SetForegroundWindow`（被拒绝则放弃本次菜单）→ `TrackPopupMenuEx` 以 `TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY` 显示 → `try/finally` 中恢复 `WS_EX_NOACTIVATE`、销毁菜单、发送 `WM_NULL`、仅在前台仍是桌宠时恢复原前台窗口。
  - `WM_MOUSEACTIVATE` 拦截增加 `_isContextMenuOpen` 守卫，菜单期间走正常原生处理。
  - 菜单打开前调用 `EndDrag()` 结束残留拖动。
  - 删除旧 WPF `ContextMenu` 相关处理函数。

**根因**：`TrackPopupMenuEx` 文档明确说明菜单所属窗口非前台时外部点击不关闭。仅移除 `WS_EX_NOACTIVATE` 不等于激活窗口，必须显式调用 `SetForegroundWindow`。

**构建与回归**（修复作者执行，非独立监理）：

- `dotnet build DesktopPet.slnx -c Debug --no-restore -m:1`：0 警告、0 错误。
- 自动交互回归（SendInput 真实鼠标/键盘）：3 轮右键弹出→外部点击关闭→NOACTIVATE 恢复通过；3 轮菜单关闭后点击桌宠外部窗口仍保持前台通过；Esc 关闭通过；点击 Exit 进程自然退出通过。
- 环境：Windows 11 (10.0.26200) x64，被测窗口 GetDpiForWindow=144（150% 缩放环境）。
- 证据：[交互测试日志](../handoffs/evidence/P0-002-menu-interactive-test.txt)。

**用户桌面实测反馈**：

- 用户在自己的桌面实测右键菜单，反馈"很完美"：右键弹出、点击空白处关闭、Exit 正常退出均通过。
- 此反馈覆盖帮手交接记录中"用户可在自己的桌面重开一次确认原问题消失"的建议项。

**对 F-01/F-02/F-03 的影响**：

- 菜单修复不改变 F-01/F-02/F-03 的整改实现，三个阻断项的代码整改保持不变。
- 菜单修复中 `EndDrag()` 调用在菜单打开前结束残留拖动。
- 右键菜单功能本身不在原审查 F-01/F-02/F-03 范围内。自动交互回归脚本覆盖的是普通菜单开关和随后点击保前台，不覆盖拖动中菜单打断场景；拖动中菜单打断后再拖的通过证据来自用户实测（见下方"用户人工验证反馈"）。
- 三个阻断项仍保持"已整改，待复核"，人工验证项仍需执行。

### 2026-10-21 用户人工验证反馈

用户在桌面实机操作，逐项验证 P0-002 验收条件。环境：Windows 11 (10.0.26200) x64，.NET 10 SDK 10.0.402。

**验证结果**：

| 编号 | 项目 | 结果 | 说明 |
|------|------|------|------|
| M-01 | 透明区穿透 | 通过 | 透明区点击穿透到下方窗口 |
| F-01 | 焦点保持 | 通过 | 编辑器输入→拖动→释放→继续输入，文字仍进入原编辑器 |
| F-02 | 拖动抓取点 100% | 通过 | 100% 缩放下鼠标与抓取点一致，无跳变 |
| F-02 | 拖动抓取点 150% | 通过 | 150% 缩放下鼠标与抓取点一致，无跳变 |
| F-03 | 正常拖放 | 通过 | 拖动后松开，桌宠停止跟随 |
| F-03 | 捕获丢失（Alt+Tab） | 未通过，用户豁免 | 拖动中 Alt+Tab 切走后桌宠仍跟随鼠标；用户明确表示"问题不大，没必要改" |
| F-03 | 菜单打断后再拖 | 通过 | 拖动中右键打开菜单→取消→再次拖动正常 |

**F-03 捕获丢失（Alt+Tab）已知限制**：

- 现象：拖动中按 Alt+Tab 切到其他窗口，桌宠仍跟随鼠标移动。
- 原因假设（尚无本轮事件跟踪证据）：Alt+Tab 切换前台窗口时可能不触发 WPF `LostMouseCapture` 事件，`EndDrag()` 的 `LostMouseCapture` 路径在此场景下未被调用。`MouseMove` 中的左键松开检测也依赖鼠标消息到达 WPF 窗口，Alt+Tab 后鼠标消息可能不再路由到桌宠窗口。此解释仅为原因假设，未经事件跟踪证实。
- 验收结论：该项验收失败，未满足原测试预期。用户明确接受现状，不要求本轮修复（监理 R2 KL-001 记录豁免）。
- 代码不变更：`EndDrag()` 统一清理入口覆盖了 `LostMouseCapture`、`MouseLeftButtonUp`、`MouseMove` 松键检测和 `OnClosed`。Alt+Tab 场景未通过验收，按用户豁免保留为已知限制，不标成"F-03 全部测试通过"。
- 后续：如果后续阶段需要更严格的捕获丢失覆盖，可在 P0-004 生命周期任务中评估是否增加 `WM_ACTIVATE`/`WM_KILLFOCUS` 等系统消息层面的拖动取消路径。

**对三个阻断项的影响**：

- F-01：人工验证通过。焦点保持在原编辑器，拖动不抢焦点。
- F-02：人工验证通过。100% 和 150% 缩放下抓取点均不跳变，DPI 转换实现正确。
- F-03：正常拖放和菜单打断后再拖通过；捕获丢失（Alt+Tab）未通过但用户豁免，记录为已知限制。
- 三个阻断项处置更新：F-01 已整改且人工验证通过；F-02 已整改且人工验证通过；F-03 已整改，正常拖放和菜单打断通过，Alt+Tab 捕获丢失为用户豁免的已知限制。
- 请求监理 R2 复核：F-01/F-02/F-03 的实现方案、人工验证结果和已知限制。

### 2026-10-21 监理 R2 复核结论与文档收尾

监理 R2 结论：允许 P0-002 收尾并进入 P0-003。详见 [审查报告](../reviews/P0-002-review.md) R2 节。

- F-01 原阻断解除（R1 静态核查 + 用户实测通过）。
- F-02 原阻断解除（R1 静态核查 + 用户 100%/150% 实测通过），不宣称跨屏 DPI 已完成。
- F-03 正常路径接受；Alt+Tab 失败项按用户豁免（KL-001）解除阻断，不标成"F-03 全部测试通过"。
- 按监理 R2 要求完成两处非阻断文档更正：
  1. Alt+Tab 说明从"不属于代码缺陷"更正为"验收失败、用户接受的限制"；WPF 事件解释标注为原因假设。
  2. 菜单自动回归覆盖范围收窄：自动脚本不覆盖拖动中菜单打断，该场景通过来自用户实测。
- P0-002 已标记 done，P0-003 可推进。已知限制 KL-001 保留引用。
