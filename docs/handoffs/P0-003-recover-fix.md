# P0-003 Recover 崩溃修复交接

时间：2026-10-21 16:56（Asia/Shanghai）。用户明确授权监理助手介入修复，主开发 AI 已暂停。
本文件是修复作者的实现和自测记录，不是独立审查报告，不代表 P0-003 整体验收完成。

## 已确认的原因

点击 Recover 会调用 `MonitorInterop.EnumerateMonitors()`，其中把 `GetDpiForMonitor` 导入为 `user32.dll`。
实机捕获到 `System.EntryPointNotFoundException`，异常发生在原生显示器枚举回调内；这次“消失”是未处理异常导致崩溃，不能继续按窗口移出屏幕诊断。
正式 DLL 为 `Shcore.dll`，返回值为 HRESULT，见 [微软文档](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)。

原始证据：[修复前复现日志](evidence/P0-003-recover-before-test.txt)。日志记录到未处理异常，但 20 秒等待内进程尚未退出，因此没有捕获到准确崩溃退出码；测试清理后已无该实例，不把此项冒充正常退出通过。
接手时的开发方猜测和未验证改动保留在 [原 STATUS 快照](../history/P0-003-before-recover-fix.md)，以本文件的实际诊断更正。

## 实际改动

- `src/DesktopPet.App/Platform/MonitorInterop.cs`：从 `shcore.dll` 导入 `GetDpiForMonitor`，使用 `int` 接收 HRESULT，明确函数入口。
- 原生枚举回调只收集显示器信息，DPI 查询移到回调返回后，避免此类互操作异常跨原生回调栈传播。
- 检查枚举失败、DPI 查询失败、零 DPI 和无效工作区；不把无效缩放送入位置换算。无有效显示器时现有 Recover 保持原位置。
- 删除未使用的重复 `MONITORINFO` 声明。
- 保留主开发 AI 在接手前已写好的“主屏工作区中央脚点” Recover 算法；没有再改 `MainWindow.xaml.cs`，没有重写菜单、拖动或 Core 规则。

## 验证与范围

- `dotnet build DesktopPet.slnx -c Debug --no-restore -m:1`：退出码 0，0 警告、0 错误。
- `dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj -c Debug --no-build --no-restore -m:1`：17/17 通过，0 跳过。首次沙箱运行因 testhost 连接超时中止，获授权在沙箱外重跑通过；未修改测试或依赖。
- 实机环境：Windows 10.0.26200.0，单屏工作区 `(0,0)-(2560,1528)`；桌宠 `GetDpiForWindow=144`（150%），窗口 180×180 物理像素。
- 使用真实原生菜单选择 Recover：启动位置 1 次、重复 3 次、部分移出屏幕后 1 次，均恢复到窗口矩形 `(1190,584)-(1370,764)`，完整位于工作区内。
- 完全屏外场景：测试程序把自己的桌宠移到屏外，再向该 HWND 投递右键消息打开原生菜单并实际点击 Recover；恢复成功。这是命令集成测试，不代表当前产品已有从托盘唤回的入口（P0-004 未实现）。
- Recover 后真实拖动：鼠标移动 `(100,60)` 物理像素，窗口同量移动；松手停止；外部测试窗口仍保持前台；NOACTIVATE 恢复。
- 外部测试窗口空白区点击关闭菜单、Esc 取消、实际点击 Exit 后进程自然退出（退出码 0），均通过。
- 原始通过日志：[修复后回归](evidence/P0-003-recover-after-test.txt)。上述结果均为修复作者自测。
- 首轮拖动自动检查曾失败，随后带位移日志复跑通过；最终脚本改为等待原生鼠标捕获/释放状态后再测量，全流程通过。未因此改动拖动实现，不把一次固定延时失败包装为应用缺陷。

复现脚本：`artifacts/recover-fix/Test-Recover.ps1`；修复前加 `-ExpectCrash`，仅针对旧二进制，不能拿修复后程序重现旧错误。
该目录被 Git 忽略；迁移项目若需重跑测试，应携带脚本。长期原始结果已复制至本目录的 evidence。
脚本只操作其自行启动的桌宠和外部测试窗口，最后关闭它们并恢复鼠标；没有留下测试桌宠，也没有用强杀代替 Exit 验证。

## 版本

无 Git。SHA256：

- `MonitorInterop.cs`：`1F07FE81D2392676607C5FF37270BA81E8157E55307611124635DEA5C12954D3`。
- `MainWindow.xaml.cs`：`6B71FCDBA0BC918EF96697EBDD6F743560F52F5606BD486B5EDA1E6FBCA0CF44`（接手前后相同）。
- `MainWindow.xaml`：`F1CF5CDFAC060CD8651B00776C4C9DDFAEE23420857F95DBE39DE50C66F511A7`（接手前后相同）。

修复前源码副本在 `artifacts/recover-fix/before/`，只供对比，不应覆盖当前代码。

## 开发 AI 接续

1. 先核对上述源码和证据，保留修复；用户再点 Recover 确认可见，并反馈即可，无需重做全部既往验收。
2. P0-003 仍以 TASKS 为准，不能因为这次修复就推进 P0-004。本轮没有补齐 100% 的 Recover 实测、混合 DPI 多屏、布局变化恢复或完整世界坐标接入。
3. 实测窗口的 PMv2 检查为 false。ARCHITECTURE 的 Per-Monitor V2 是待落实目标；启用后应重新评估 DPI 查询边界。微软明确提示每显示器感知线程不要直接使用 `GetDpiForMonitor`，窗口 DPI 应采用 [GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)。不能只修正 DLL 就宣称混合 DPI 已支持。
4. KL-001 的 Alt+Tab 例外保持原范围；未修改 P0-002 报告和整改记录，也未签发本修复的独立审查结论。
