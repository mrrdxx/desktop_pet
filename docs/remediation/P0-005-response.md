# P0-005 整改记录

对应报告：`docs/reviews/P0-005-review.md`
整改方：开发 AI。日期：2026-10-22（Asia/Shanghai）。

源码版本（与监理报告记录一致）：
- App.xaml.cs SHA256：`0AE58BB7D682CB484BD08986792B942909786EB3FD26C3F7EE71FEBB373EA440`
- MainWindow.xaml.cs SHA256：`7EEC77CAC03D3621A98EB7C68D774153A71A60C1BFFF36823518512920D4E8BC`

本轮未修改应用代码；仅运行验证、采集证据和更正文档。

---

## F-01：把未验证的实际托盘入口列为不阻断

### 问题

阶段验收写 Shell 真实点击未验证，却得出所有必需项通过。自动化未触发菜单的原因未确定，不能确认是产品故障也不能证明是自动化限制。

### 整改措施

1. **文档修正**：已更正 `docs/history/P0-validation.md` 中 Shell 托盘部分：
   - "原因分析"改为"推测原因（待核实）"，不再写确定性结论。
   - "用户手动右键点击托盘图标应能正常弹出菜单"改为"此结论待用户实际操作确认"。
   - NV-02 状态从"未验证"改为"待用户手动确认"，不自行降为不阻断。
   - 验收结论中不再写"所有必需项通过"，改为逐项注明状态。

2. **用户手动确认步骤**（需用户实际操作）：

   先启动桌宠（如未运行）：
   ```
   c:\Users\mrrdxx\Desktop\pet\src\DesktopPet.App\bin\Debug\net10.0-windows\DesktopPet.exe
   ```

   然后执行以下步骤：
   - **步骤 1**：右键点击通知区域（系统托盘）中的桌宠图标，确认菜单弹出（应显示 Pause、Recover、Exit 三项）。
   - **步骤 2**：点击 Pause，观察宠物动画是否停止（占位形状不再切帧）。
   - **步骤 3**：再次右键托盘图标，点击 Recover，确认宠物窗口移动到屏幕可见区域中央。
   - **步骤 4**：再次右键托盘图标，点击 Resume（或 Pause 切换），确认动画恢复。
   - **步骤 5**：在桌面空白处点击或按 Esc，确认菜单关闭后仍可拖动宠物。
   - **步骤 6**：右键托盘图标，点击 Exit，确认宠物进程退出（任务管理器中不再有 DesktopPet.exe）。

   每步记录通过/失败，反馈给开发方写入本记录。

### 处置状态

已修改文档，待用户手动确认。未补证前 NV-02 不自行降为不阻断。

---

## F-02：旧 DPI 日志被用于当前新增启动链的完整结论

### 问题

所引用的 R4 日志属 P0-003 版本。P0-004 之后更改了 App 启动时的暂停应用、SourceInitialized 位置恢复及保存链。100% 下当前版本启动恢复未验证。

### 整改措施

1. **文档修正**：已更正 `docs/history/P0-validation.md` M-03 部分：
   - 逐条注明沿用旧证据（P0-003 R4）或本轮新增。
   - 100% 启动链标注为"本轮新增（F-02 补证）"。
   - 150% 注明沿用旧证据，未重跑启动链（代码路径相同，100% 已补）。

2. **本轮新增验证**：在 100% DPI（DPI=96）下运行当前版本隔离测试。

   - 脚本：`tests/Integration/Run-Lifecycle-UTF8.ps1 -Scenarios offscreen-start,paused-start,normal`
   - 环境：Windows 10.0.26200.0，DPI=96，Screen=2560×1600，Debug 构建
   - 源码版本：App.xaml.cs SHA256 同上
   - 隔离目录，不影响用户真实数据

   **结果**（证据：`artifacts/p0-005-fix/startup-recovery-100pct.txt`）：

   | 场景 | 验证内容 | 结果 |
   |------|---------|------|
   | offscreen-start | 屏外位置(100000,100000)+MISSING_DISPLAY 启动恢复 | PASS，窗口恢复到工作区内 (2380,1348)-(2560,1528) |
   | paused-start | 暂停状态启动恢复 | PASS，paused=True, timerEnabled=False，帧不变化 |
   | normal | 正常启动 Pause/Recover/Resume | PASS，Pause 停止动画，Recover 可用，Resume 恢复无积压 |

   所有场景退出码 0，真实存档目录快照不变。

### 处置状态

已整改，待复核。

---

## F-03：性能记录缺观察项，且采样环境/时长表述不准确

### 问题

1. 帧率/停顿未测量，以 CPU 低间接推断。
2. 环境表述不准确：脚本进程 DPI 不等于系统缩放设置。
3. 稳定采样跨度约 286 秒，不足完整 5 分钟。
4. 脚本强杀所有 DesktopPet 进程。

### 整改措施

1. **文档修正**：已更正 `docs/history/P0-validation.md` 性能基线和环境部分。

2. **重采脚本**：`artifacts/p0-005-fix/idle-perf-resample.ps1`
   - 22 个采样点 × 15 秒间隔，稳定时段 ≥300 秒。
   - 环境记录区分：物理分辨率（WMI）、采样进程 DPI、逻辑屏幕边界、宠物窗口 DPI。
   - CPU 使用 Stopwatch 实际经过时间作分母。
   - 不强杀其他 DesktopPet 实例，仅管理自己创建的实例。
   - 退出方式：WM_CLOSE，非 Stop-Process。

3. **重采结果**（证据：`artifacts/p0-005-fix/idle-perf-resample.txt`）：

   | 指标 | 值 |
   |------|-----|
   | 采样点数 | 22 |
   | 稳定时段 | 300.4 秒（21 个间隔，≥300 秒 ✓） |
   | CPU 平均 | 40.9ms/间隔 |
   | CPU 占比 | 0.29% 单核（859ms CPU / 300437ms 实际经过时间） |
   | CPU 范围 | min=0ms, max=187.5ms |
   | 工作集 | avg=122.6MB, min=117.5MB, max=128.9MB |
   | 私有内存 | avg=68.3MB, max=74.4MB |
   | 句柄（最终） | 633 |
   | 线程（最终） | 15 |
   | 内存趋势 | 11.4MB 增长 |
   | 动画指标 | 21 个间隔中 18 个有非零 CPU 活动 |
   | 退出方式 | WM_CLOSE 正常退出，退出码 0 |

   环境记录：
   - 物理分辨率：2560×1600（WMI Win32_VideoController）
   - 采样进程 DPI：96（~100%），注明此为采样进程自身 DPI 感知返回值，未独立验证系统缩放设置
   - 逻辑屏幕边界（采样进程视角）：1707×1067
   - 宠物窗口 DPI：由 app.manifest PerMonitorV2 声明，100% 下为 96

4. **动画观察**：
   - CPU 模式：21 个稳定间隔中 18 个有非零 CPU 活动，与 ~500ms 定时器驱动帧切换一致。
   - 视觉帧观察：待用户确认（见 F-01 手动步骤中可附带观察宠物占位形状是否在眨眼切帧）。

### 处置状态

已整改，待复核。视觉帧观察待用户确认。

---

## R2 轮次（2026-10-22，监理 R1 后补 F-02）

监理 R1 复核结论（见 [P0-005 审查报告](../reviews/P0-005-review.md) 文末 R1）：
- F-01、F-03 已关闭。
- F-02 部分通过：探针没有记录实际 HWND DPI，日志矩形 180×180 与标题 DPI=96 矛盾。
- 唯一剩余验证：当前启动探针记录真实 HWND DPI=96、窗口矩形/工作区及三场景，保留完整输出及版本。若仍为 DPI=144，先更正标签。

### F-02 R2：探针增加真实 HWND DPI 读取 + 更正环境标签

- 开发处置：已整改待复核
- 修改说明与文件位置：
  1. `tests/Integration/LifecycleProbe.cs`：新增 `GetDpiForWindow` 和 `GetDpiForSystem` P/Invoke 声明；新增 `RecordHwndDpi()` 方法，在每个场景开始时记录真实 HWND DPI、系统 DPI 和缩放百分比；`WindowVisibleInWorkArea()` 增加窗口物理像素尺寸和工作区/边界矩形记录。
  2. `tests/Integration/Run-Lifecycle-UTF8.ps1`：`Start-TestPet` 改用 `System.Diagnostics.Process` 替代 `Start-Process`，解决 `RedirectStandardError` 模式下 `ExitCode` 返回 null 的问题；stderr 改为通过 `StandardError.ReadToEnd()` 读取。
- 自测环境：Windows 11 (10.0.26200) x64，.NET 10 SDK 10.0.402。
- **关键发现：当前系统实际 DPI=144（150%），非 96（100%）**。
  - 注册表 `HKCU:\Control Panel\Desktop\WindowMetrics` AppliedDPI=144 确认系统缩放为 150%。
  - 之前日志标注 "DPI=96" 是错误的：非 PMv2 的 PowerShell 进程在 150% 缩放下被 DPI 虚拟化后 `GetDpiForSystem()` 返回 96，但桌宠应用在 PMv2 下 `GetDpiForWindow` 返回真实物理 DPI=144。
  - 窗口矩形 180×180 物理像素 = 120×120 DIP × 1.5（150% 缩放），与 DPI=144 一致，与之前标注的 "DPI=96" 矛盾。现更正为 DPI=144（150%）。
- 实际命令、退出码及关键结果：
  - `dotnet build`（隔离 fixture）：成功，0 警告 0 错误。
  - 三场景测试（`-Scenarios offscreen-start,paused-start,normal`）：**ALL PASS**，退出码 0。
  - 探针记录的真实 HWND DPI：
    - offscreen-start: `HWND=0xA1198; GetDpiForWindow=144; GetDpiForSystem=144; scale=150%`
    - paused-start: `HWND=0xB1198; GetDpiForWindow=144; GetDpiForSystem=144; scale=150%`
    - normal: `HWND=0xC3122C; GetDpiForWindow=144; GetDpiForSystem=144; scale=150%`
  - 窗口矩形：offscreen-start `(2380,1348,2560,1528) size=180x180`；normal Recover `(1190,584,1370,764) size=180x180`。180×180 物理像素 = 120×120 DIP × 1.5，与 DPI=144 一致。
  - 工作区：`(0,0,2560,1528)`，边界：`(0,0,2560,1600)`。
  - 所有场景退出码 0，真实存档目录快照不变。
- 证据路径：`artifacts/p0-005-fix/startup-recovery-hwnd-dpi-150pct-final.txt`。
- 未验证项及原因：
  - **100% DPI=96 的当前版本启动恢复证据尚未采集**：当前系统缩放为 150%，无法直接获得 DPI=96 的证据。需要用户将系统缩放切换到 100% 后重新运行 `tests/Integration/Run-Lifecycle-UTF8.ps1 -Scenarios offscreen-start,paused-start,normal`。
  - 之前标注 "DPI=96" 的日志 `artifacts/p0-005-fix/startup-recovery-100pct.txt` 实际是在 150% 环境下运行的，环境标签错误，现更正。该日志的三场景功能结果（offscreen 恢复、paused 启动、normal Pause/Recover/Resume）仍然有效，但 DPI 标签不正确。
  - 混合 DPI 双屏、显示器断开恢复：无第二屏，按 TASKS 为可选，不阻塞。
- 异议/需求疑问及反证：
  - 之前日志中 "DPI=96" 标签的来源：非 PMv2 的 PowerShell 进程在 150% 缩放下被 DPI 虚拟化后返回 96，不代表系统实际缩放。桌宠应用在 PMv2 下通过 `GetDpiForWindow` 读取的真实 HWND DPI=144 才是准确值。注册表 AppliedDPI=144 进一步确认。
  - 窗口矩形 180×180 物理像素与 DPI=144（150%）完全一致：120×120 DIP × 1.5 = 180×180。之前标注 "DPI=96, 窗口 120×120 物理像素" 是错误的——DPI=96 下 120×120 DIP 窗口应为 120×120 物理像素，但实际测量为 180×180，证明 DPI 不是 96。
- 请求监理复核的范围：F-02 R2 探针 HWND DPI 读取、环境标签更正、150% 三场景完整日志。100% DPI=96 证据待用户切换缩放后补采。

---

## 保留有效证据

- P0-004 独立复核 R1、55 项单元测试、十场景隔离集成：继续有效，不重做。
- P0-002 透明穿透/焦点反馈及 KL-001 例外：继续保留。
- P0-003 R4 两种缩放的 PMv2、拖动和 Recover 结果：按旧版本覆盖范围沿用，不删除或覆盖旧日志。
- 旧性能日志 `artifacts/idle-perf-baseline.txt`：保留不覆盖，作为已观察时段的基线。
- 之前 `artifacts/p0-005-fix/startup-recovery-100pct.txt`：保留不删除，但环境标签 "DPI=96" 更正为实际 DPI=144（150%）。功能结果有效，DPI 标签无效。

## R2 最终补证（2026-10-22，监理 R2 通过后追加）

监理 R2 复核结论（见 [P0-005 审查报告](../reviews/P0-005-review.md) §复核 R2）：
- F-02 通过，关闭。F-01～F-03 全部关闭，P0-005 阶段 0 整体验收通过。
- 100% 三场景实际记录 GetDpiForWindow=96、GetDpiForSystem=96、scale=100%，原生窗口 120×120 物理像素。
- 150% 对照实测 HWND DPI=144、窗口 180×180，三场景同样通过。
- 探针 LifecycleProbe.cs SHA256：`73FDF6C337487F1275BF040F3BFA9D8325781A13C260EFD69136346A5C035172`；Run-Lifecycle-UTF8.ps1：`41A8E11F96B34A2627A86001CE4FAB26BEDA260D36E6E96014BB83F7FCE05454`。

### 100% DPI=96 最终补证

用户将系统缩放切换到 100% 后，使用当前版本夹具运行三场景隔离测试。

- 命令：`tests/Integration/Run-Lifecycle-UTF8.ps1 -Scenarios offscreen-start,paused-start,normal`
- 环境：Windows 10.0.26200.0，DPI=96（GetDpiForWindow=96, GetDpiForSystem=96, scale=100%），Screen=2560×1600，work=(0,0,2560,1552)，Debug 构建
- 源码版本：App.xaml.cs SHA256 `0AE58BB7D682CB484BD08986792B942909786EB3FD26C3F7EE71FEBB373EA440`，MainWindow.xaml.cs SHA256 `7EEC77CAC03D3621A98EB7C68D774153A71A60C1BFFF36823518512920D4E8BC`（与监理报告一致，未修改应用代码）
- 隔离目录，不影响用户真实数据

**结果**（证据：`docs/reviews/evidence/P0-005-startup-hwnd-dpi-100pct.txt`）：

| 场景 | HWND DPI | 窗口尺寸 | 验证内容 | 结果 |
|------|---------|---------|---------|------|
| offscreen-start | 96 | 120×120 px | 屏外位置(100000,100000)+MISSING_DISPLAY 启动恢复 | PASS，恢复矩形 (2440,1432)-(2560,1552)，位于工作区内 |
| paused-start | 96 | — | 暂停状态启动恢复 | PASS，paused=True, timerEnabled=False，帧不变化 |
| normal | 96 | 120×120 px | 正常启动 Pause/Recover/Resume | PASS，Pause 停止动画，Recover 矩形 (1220,656)-(1340,776) 在工作区内，Resume 无积压 |

- 所有场景 exited=True、exitCode=0
- 真实存档目录快照不变
- 末尾：ALL ISOLATED LIFECYCLE SCENARIOS PASS

### 150% DPI=144 对照（沿用 R2 轮次）

- 证据：`docs/reviews/evidence/P0-005-startup-hwnd-dpi-150pct.txt`
- 三场景均 GetDpiForWindow=144、scale=150%，窗口 180×180 物理像素，全部 PASS、退出码 0

### 早前错误标签修订轨迹

- `artifacts/p0-005-fix/startup-recovery-100pct.txt`：文件名标注 "100%"，实际在 150% 环境运行（DPI=144），环境标签错误。功能结果有效，DPI 标签无效。保留不删除，作为历史修订轨迹。
- `artifacts/p0-005-fix/startup-recovery-hwnd-dpi-150pct-final.txt`：R2 轮次 150% 补证，HWND DPI=144，有效。
- 最终 100% 证据以 `docs/reviews/evidence/P0-005-startup-hwnd-dpi-100pct.txt` 为准。

### 处置状态

F-01～F-03 全部关闭。P0-005 阶段 0 整体验收通过，TASKS 已标 done。
本轮仅文档收尾，未修改应用代码，未开始阶段 1 实现。

---

## 复核请求

请监理复核 R2 轮次 F-02 补证。F-01/F-03 已在 R1 关闭，不重复。~~100% DPI=96 的启动恢复证据待用户切换系统缩放后补采。~~

**更新（R2 最终补证后）**：100% DPI=96 证据已补采完毕，监理 R2 已通过并关闭 F-02。F-01～F-03 全部关闭，P0-005 阶段 0 整体验收通过。
