# P4-003 监理 R1 整改记录

整改时间：2026-11-16（Asia/Shanghai）。对应 [审查报告](../reviews/P4-003-review.md) R1。

## F-01：有效头部交互打断睡眠动作

**问题**：夜间按住小熊实体头部时，`BeginCapturedPointerInteraction` 已接受手势进入抚摸候选，但画面仍播放 `sleep`，未释放夜间动作。

**修复位置**：
- `src/DesktopPet.App/MainWindow.xaml.cs` `BeginCapturedPointerInteraction`：入口守卫之后、`SkipRestReminder()` 之前，统一新增「有效实体按压打断睡眠」——
  `if (string.Equals(_animPlayer.CurrentActionId, "sleep", StringComparison.Ordinal)) _animPlayer.PlayAction("idle");`
- 头部按压（进入 `_isPettingCandidate`）与身体按压（`StartDragging`）两路共用该入口，一次覆盖；未加新状态标志。

**松手后按本地时间重判**：`EndPetting`/`EndDrag` 均回到 idle，夜间接线在下一 tick 按当时 `TestGetLocalNow`/`DateTime.Now` 重判——夜间松手回睡、06:00 后松手保持 idle，无需额外代码。

**与 F-10 探头契约不冲突**：探头期间 `CurrentActionId=="curious"`，永不等于 `"sleep"`，头部短按不打断探头的行为不变。

**新增测试**（`tests/DesktopPet.App.Tests/NightSleepyIntegrationTests.cs`，3 项，走真实 `BeginCapturedPointerInteraction`/`EndCapturedPointerInteraction` 路径）：
- `R1F01_HeadPressDuringSleep_InterruptsSleepAnimation_ReentersSleepAfterRelease`：夜间 sleep 中实体头部按压 → 立即离开 sleep（idle）；松手后下一 tick 回睡。
- `R1F01_BodyPressDuringSleep_InterruptsSleepAnimation`：身体按压直接拖动 → 立即离开 sleep；松手后回睡。
- `R1F01_HeadPressAtNight_ReleaseAfter0600_StaysAwake`：按压起于夜间、松手时已过 06:00 → 保持 idle，不回睡（松手按当时本地时间决定）。

**自测证据**：`NightSleepy` 专项 13/13 通过（原 10 + 新增 3）。全量 1005/1005（Core 494 + App 511），0 回归。

## F-02：预览判定改用真实拖动证据 + 无输入对照

**问题**：`NightSleepyVerification` 用「动作变化」推断拖动打断与回睡：首次 idle→sleep 计作重入、清晨 sleep→idle 计作拖动，完全无输入时两标志也误报 true。

**修复位置**（`tests/DesktopPet.DiagnosticProbe/Program.cs`）：
- `NightSleepyVerification` 判定重写：
  - 新增 `realDragSeen`：每个观测点采样 `MainWindow.IsDragging`（public 属性），只有真实拖动才置位。
  - `dragInterruptSeen`：仅当 `lastAction=="sleep"` → 非 sleep 的转换 **且** `realDragSeen==true` **且** 发生在 05:59 保持窗口内（排除清晨唤醒）才置位。
  - `sleepReentrySeen`：仅当 `dragInterruptSeen` 已确认后再次进入 sleep 才置位（无拖动时清晨唤醒后的任何转换不再误报）。
  - 观测采样加密到 1 秒（原 2 秒），并新增 `isDragging` 字段写入 jsonl，供逐条核对。
- 新增场景 `night-sleepy-noinput`（对照轮）：同一虚拟钟序列，操作员全程不输入；退出码要求 `realDragSeen/dragInterruptSeen/sleepReentrySeen` 均为 false。

**自测证据**（两轮探针实跑，产物 `artifacts/p4-003-preview-r1*/`）：
- 带拖动轮（`night-sleepy`，`SendInput` 注入真实身体按压+拖动+松手）：exit=0，`sleepSeen=true`、`wakeSeen=true`、`realDragSeen=true`、`dragInterruptSeen=true`、`sleepReentrySeen=true`——真实「拖动打断 → 松手回睡」序列被正确判定。
- 无输入对照轮（`night-sleepy-noinput`，全程零输入）：exit=0，`realDragSeen=false`、`dragInterruptSeen=false`、`sleepReentrySeen=false`——修复前误报的两标志不再为 true。

**运行命令**：
```powershell
dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj
tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe <baseDir> night-sleepy <resultFile>
tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe <baseDir> night-sleepy-noinput <resultFile>
```

## R3 F-03：sleep_light/sleep_deep 素材修正（去睁眼瞳孔与高光）

整改时间：2026-11-16。对应监理 R3 的 F-03（[R3 双宠验证记录](../reviews/evidence/P4-003-R3/README.md)）。

**问题**：旧 sleep 素材在 `idle_0` 睁眼基础上仅叠一条眼下弧线，瞳孔与高光未擦除，120×120 下浅/深睡难辨认。

**修复内容**（仅重绘两张 PNG，生成脚本 `fix_sleep_assets.py` v2，可重跑）：
- `assets/pets/bear/sleep_light.png`：基底 `idle_0`，眼部区域（x38-81, y40-55）整体复制 `blink_closed.png` 闭眼区域（瞳孔/高光全部消除）；眼睑线加厚 1px（左右各 8px，睡眠重于眨眼）。
- `assets/pets/bear/sleep_deep.png`：同上基底，另附三枚 Z 标（48px，x88-105, y6-30，与角色头部右上设计一致）。
- 脚点行 y103-110 与 `idle_0` 逐像素一致（脚点不动）；`character.json` sleep 时序（light 2500 / deep 4000 / light 2500）未改；`blink_closed.png`、`idle_0.png` 未改。

**像素自测证据**（`verify_sleep.py`，断言全部通过）：
- light vs deep 差异恰为 48px（仅三枚 Z）；light vs idle 差 418px、deep vs idle 差 466px。
- 眼区无残留瞳孔（除鼻部固有深色）；ASCII 像素图确认闭眼弧清晰、Z 三枚完整。

**整改后双宠观感复验**（复用 R3 隔离探针副本，`artifacts/p4-003-r3/harness`，双宠 `AddPet` + 共享注入本地时钟，`night-sleepy` 场景）：
- 隔离构建 0 警告 0 错误。
- 全场景实跑 exit=0：`sleepSeen=true`、`wakeSeen=true`；逐秒观测 1184 条，首宠 855 帧、次宠 891 帧 sleep，虚拟 23:30 入睡、06:00 唤醒回 idle，脚点坐标 (100,100) 不变。
- 双宠窗口实屏抓取（A=(100,100)-(220,220)，B=(300,100)-(420,220)）模板比对：两只渲染像素与新 `sleep_deep` 素材不透明区 0/6098 失配——渲染即新闭眼素材，眼区为闭眼弧（y49-51）、无瞳孔/高光，Z 三枚完整；浅睡帧与深睡帧仅差 Z 标（同一基底与渲染路径）。

**运行命令**：
```powershell
dotnet build artifacts/p4-003-r3/harness/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-003-r3/build
# 构建所得 exe：<baseDir> night-sleepy <resultFile>（双宠隔离预览，约 130 秒）
```

## 全量验证

- 构建 0 警告 0 错误（探针与 App 均已重编译验证）。
- 全量 1005/1005 通过（Core 494 + App 511），0 回归。
- R3 F-03 仅重绘 PNG 素材，不改代码与测试，未重跑全量回归；上述像素断言与双宠隔离预览实跑通过。
- P4-003 保持 `awaiting-verification`，未提交/推送，待监理复核后进行 M-13 实机验收。
