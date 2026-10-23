# P0-002 右键菜单修复交接

日期：2026-10-21（Asia/Shanghai）。
角色：用户明确要求监理助手临时介入代码，主开发 AI 已在 STATUS 记录暂停等待帮手。
本文件是修复作者的实现与自测记录，不是独立监理复核，不关闭原审查问题。

## 范围与原状态

- 用户本轮委托：右键菜单出现后，左键点击桌宠外部空白区域不能关闭，必须点桌宠本体。
- 接手代码已改用 Win32 原生菜单，同时遗留 WPF ContextMenu 绑定；开发交接还提到菜单坐标异常。
- 接手前完整状态已保存到 [历史交接](../history/P0-002-menu-before-helper.md)。其中各方案的原因分析属于当时开发方判断，不是本次已确认根因。
- 未修改透明区命中、拖动物理、动画、存档或后续阶段；未安装依赖、未提交或推送。

## 原因与修改

菜单所属窗口一直未被设置为前台。只移除 WS_EX_NOACTIVATE 不等于激活窗口，无法保证原生弹出菜单收到正常的外部关闭处理。
微软在 [TrackPopupMenuEx 备注](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenuex) 中说明了菜单所属窗口非前台时外部点击不关闭的问题。

实际修改：

- `src/DesktopPet.App/MainWindow.xaml.cs`：单一原生菜单入口；用 GetCursorPos 获取屏幕坐标，避免客户端坐标/DIP 混算。
- 只在打开右键菜单期间移除 NOACTIVATE，并明确调用 SetForegroundWindow；激活被系统拒绝时放弃本次菜单，避免留下不能正常关闭的弹出框。
- WM_MOUSEACTIVATE 的非激活拦截仅用于普通桌宠交互；菜单期间走正常原生菜单处理。
- 用 try/finally 销毁菜单、恢复原 NOACTIVATE 位并发送 WM_NULL，覆盖选择、取消和提前失败路径。
- 若关闭后前台仍是桌宠，才恢复原应用；如果用户已点到其他应用，不把焦点抢回。
- 菜单打开前结束残留拖动；保留既有拖动与 DPI 处理。
- `src/DesktopPet.App/MainWindow.xaml`：删除不再使用的 WPF ContextMenu 与右键处理绑定，对应旧处理函数也删除。
- 完善菜单互操作的 Unicode/指针宽度及关键失败检查。

## 构建与实际回归

- 命令：`dotnet build DesktopPet.slnx -c Debug --no-restore -m:1`。
- 结果：退出码 0，0 警告、0 错误。
- 回归脚本：[Test-ContextMenu.ps1](../../artifacts/menu-fix/Test-ContextMenu.ps1)。该临时脚本位于忽略的 artifacts，转移项目需要重跑时应另行带上。
- 原始结果：[交互测试日志](evidence/P0-002-menu-interactive-test.txt)。
- 环境：Windows 10.0.26200.0，x64；被测桌宠窗口 GetDpiForWindow=144。不是早先 Graphics 探测进程的 DPI 推断。
- 测试启动独立桌宠实例和一个外部 WinForms 测试窗口，通过 SendInput 真实发送鼠标/键盘事件；点击限制为这两个测试进程，结束后恢复鼠标位置并关闭测试窗口。
- 连续 3 轮：右键在点击位置附近弹出菜单 → 左键点击外部测试窗口空白区 → 菜单消失 → NOACTIVATE 恢复，全部通过。
- 连续 3 轮：菜单关闭后再左键点击桌宠，外部测试窗口仍保持前台，全部通过。
- Esc 取消菜单通过；再次打开并实际点击 Exit 后，被测 PID 自然退出，通过。没有强杀代替退出验证。

## 版本

无 Git，本次修复源码 SHA256：

- MainWindow.xaml：`F1CF5CDFAC060CD8651B00776C4C9DDFAEE23420857F95DBE39DE50C66F511A7`。
- MainWindow.xaml.cs：`2BF4DC04D9ED7A07F2440B2E3798CFF5CE855087137DBD8D2BBCFF6FBA92A1BA`。
- 接手前源码备份在 `artifacts/menu-fix/before/`，仅供对比，不应覆盖回当前代码。

## 边界与接续

- 已实际验证的是点击外部测试窗口的空白区，不宣称测试过所有 Shell 桌面、全屏或多显示器场景；用户可在自己的桌面重开一次确认原问题消失。
- 本轮未重测透明区穿透、拖动轨迹/抓取点、拖动中捕获被抢走；不能把 DPI=144 的菜单测试写成完整 150% 拖动验收。
- 主开发 AI 接手时先检查这两个源文件的实际变更，保留修复，再把后续真实反馈写入其整改记录；不要恢复被删除的旧 WPF 菜单入口。
- 项目任务保持 awaiting-verification。原审查结论适用旧版本，本文件不冒充独立监理通过；后续按用户实测与协作规则完成任务验收。
