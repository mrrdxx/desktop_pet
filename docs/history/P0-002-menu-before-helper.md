# 当前状态

更新时间：2026-10-21（Asia/Shanghai）。这是接手摘要，不是完整工作日志。

## 实际交付

- 已建立文档入口、产品初稿、技术方案、任务、决策及验证规则。
- **P0-001 已完成**：最小工程可构建、可运行，正常退出验证通过。
  - .NET 10 SDK 10.0.402，运行时 10.0.13，Windows 11 (10.0.26200)。
  - `DesktopPet.slnx`、`global.json`、`Directory.Build.props`、Core + App 工程。
- **P0-002 静态修复已复核，仍需实机验证**：
  - 透明无边框置顶窗口、两帧占位角色、身体拖动、非激活防护、DPI 转换、捕获清理。
  - F-01/F-02/F-03 静态修复经监理 R1 部分通过，均未关闭；详见 [审查报告](../reviews/P0-002-review.md)。
  - 整改记录见 [P0-002-response](../remediation/P0-002-response.md)。
- 没有正式动画素材或安装包。测试工程尚未创建（P0-003 起加入）。

## 当前接手任务

P0-002 人工验收进行中，右键菜单问题尚未解决，已暂停代码修改等待帮手处理。
TASKS 保持 `awaiting-verification`，不进入 P0-003。

### 右键菜单问题——当前未解决

**核心矛盾**：`WS_EX_NOACTIVATE`（F-01 非激活防护）与右键菜单的弹出/关闭机制冲突。

**已尝试的 3 种方案及结果**：

1. **WPF ContextMenu + WPF 事件路由**（原始方案）
   - `WS_EX_NOACTIVATE` 阻止 WPF `MouseRightButtonUp` 事件路由，右键完全无反应。

2. **WndProc 拦截 WM_RBUTTONUP + WPF ContextMenu + 动态切换 WS_EX_NOACTIVATE**
   - 打开菜单前移除 `WS_EX_NOACTIVATE`，菜单关闭后恢复。
   - 异步恢复（`Dispatcher.BeginInvoke`）：点击外部关闭不稳定，"一次行一次不行"（时序竞争）。
   - 同步移除 + 异步恢复：仍不稳定。
   - 同步移除 + 同步恢复：菜单能弹出、Exit 能关闭，但点击空白处不消失（`WS_EX_NOACTIVATE` 移除后 WPF 菜单仍不跟踪外部点击）。

3. **WndProc 拦截 WM_RBUTTONUP + Win32 TrackPopupMenuEx**（当前代码状态）
   - 同步移除 `WS_EX_NOACTIVATE` → `TrackPopupMenuEx`（同步阻塞）→ 同步恢复。
   - 用 `ClientToScreen` 转换坐标。
   - 用户反馈：菜单出现在屏幕左上角（坐标问题），点击空白处无反应。
   - **此方案有未修复 bug，代码已保留在 MainWindow.xaml.cs 中。**

**当前代码状态**（`src/DesktopPet.App/MainWindow.xaml.cs`）：
- 第 37–71 行：Win32 P/Invoke 声明（`TrackPopupMenuEx`/`CreatePopupMenu`/`AppendMenu`/`DestroyMenu`/`ClientToScreen`/`POINT`）。
- 第 114–166 行：`WndProc` 处理 `WM_RBUTTONUP`，用 `TrackPopupMenuEx` 弹出原生菜单。
- 第 348–358 行：旧的 `PetCanvas_MouseRightButtonUp`（WPF ContextMenu）仍存在但不会被调用（WndProc 拦截了右键消息）。
- XAML 中仍保留 `ContextMenu` 和 `MouseRightButtonUp` 绑定（不影响功能，可清理）。
- 构建通过，无编译错误。

**待帮手解决**：
- `TrackPopupMenuEx` 坐标错误（菜单出现在左上角而非右键位置）。
- `TrackPopupMenuEx` 点击外部不关闭（可能需要 `TPM_LEFTALIGN` 或窗口激活配合）。
- 或采用其他方案实现右键菜单，不限于 `TrackPopupMenuEx`。

### 已完成的 R1 文档更正

监理 R1 提出的 5 项文档更正已全部处理（详见整改记录"R1 复核后文档更正"节）：
1. 整改记录开头矛盾措辞已修正。
2. "100% 拖动行为一致"改为代码推断。
3. DPI 96 来源限制已补充。
4. TESTING.md 过期描述已去掉。
5. `CaptureMouse()` 返回值更正已注明。

### 人工验收已收集的反馈

- 右键菜单：能弹出、Exit 能关闭（方案 2），但点击空白处不消失（不稳定/无反应）。
- 其余验收项（M-01 透明穿透、F-01 焦点保持、F-02 拖动抓取点、F-03 正常拖放/菜单打断）尚未执行。

## 已观察到的环境事实

- 项目目录：`C:\Users\mrrdxx\Desktop\pet`。
- Windows 11 (10.0.26200)，x64，.NET 10 SDK 10.0.402。
- 未初始化 Git。
- 单屏，Graphics 探测 DPI=96；系统缩放需明确来源后再确认。

## 验证与证据

- `dotnet build` 成功无错误无警告（2026-10-21）。
- 窗口启动可见、WM_CLOSE 正常退出。
- 右键菜单：用户实机反馈如上，未通过。
- M-01/M-02/150% 缩放拖动：未执行。

## 阻塞与未决事项

- 右键菜单问题未解决，等待帮手处理。
- F-01/F-02/F-03 仍缺实机证据，均未关闭。
- 帮手修改后需重新验收全部项目。

## 下一步

1. **帮手接手后**：先检查 `MainWindow.xaml.cs` 和 `MainWindow.xaml` 的实际变更，不覆盖或回退帮手的改动。
2. 帮手解决右键菜单后，重新构建并启动，引导用户完成全部人工验收。
3. 人工验收通过后，将用户反馈记录到整改记录，请求监理复核。
4. 复核通过后标记 P0-002 `done`，推进 P0-003。
