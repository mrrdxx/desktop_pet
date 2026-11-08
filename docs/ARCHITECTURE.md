# 技术架构基线 v0.1

性质：已采用的技术基线与后续架构约定并存，不代表全部模块已有实现。实施状态见 [TASKS](TASKS.md)，决策理由见 [DECISIONS](DECISIONS.md)。
本文件约束模块边界和关键数据含义；具体方法签名随当前任务落地，不提前生成大量空接口。

## 1. 技术栈

- 平台：Windows 11 x64，优先当前仍受支持的版本；具体实测版本记入验证结果。
- 语言与运行时：C#、.NET 10 LTS。SDK 建好环境后用 `global.json` 锁定；不依赖预览特性。
- UI：WPF，App 工程目标 `net10.0-windows`、`UseWPF=true`，需要托盘时使用系统自带 WinForms `NotifyIcon` 并设置 `UseWindowsForms=true`；避免 WPF/WinForms 类型名称冲突。
- Windows 集成：集中封装 Win32 P/Invoke。以小范围显式声明起步，检查返回值、指针位数、回调寿命与资源释放。
- 行为核心：普通 C# 库，目标 `net10.0`，不依赖 WPF、HWND 或真实系统时钟。
- 存档：`System.Text.Json`，不引入数据库；未来数据规模有证据后再考虑 SQLite。
- 测试：MSTest；Core.Tests 目标 `net10.0`，App.Tests 因引用 WPF App 而目标 `net10.0-windows`。统一通过 `dotnet test` 运行。
- 美术：阶段 0 使用程序绘制占位形状；阶段 1 起采用透明 PNG 帧序列与 JSON 动作描述。
- 发布：后续提供 `win-x64` 自包含目录包；先不做安装器、自动更新和单文件裁剪。

当前选型仅服务 Windows。暂不引入 Electron、浏览器渲染层、游戏引擎、Live2D、3D 或 LLM SDK。
如果原型证明 WPF 的透明输入或性能不满足要求，记录可复现证据后调整局部实现，不直接重写全部架构。

依据：微软将 .NET 10 列为 LTS；WPF 提供透明无边框窗口能力，但这不构成本项目鼠标穿透或性能已验证的证据。
来源：[.NET 生命周期](https://dotnet.microsoft.com/en-us/platform/support/policy)、[WPF 窗口](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)。
Windows 10 不作为默认支持承诺，兼容性另测，并参考 [.NET 10 OS 支持列表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)。

## 2. 最小工程布局

阶段 0 已创建两个生产项目及两个测试项目；App 内 Platform/Persistence 已有实现，Views 和正式资源目录仍按需要建立。

```text
DesktopPet.slnx            # slnx 格式，.NET 10 原生支持
global.json                # 锁定 SDK 10.0.402
Directory.Build.props      # 统一构建属性（Nullable, TreatWarningsAsErrors 等）
.gitignore
src/
  DesktopPet.Core/          # 状态、行为选择、物理、社交、可测试数据规则
  DesktopPet.App/           # WPF 展示、输入、Win32、存档、程序生命周期
    Platform/              # Windows API、坐标适配、单实例与托盘
    Persistence/           # 本地读写、版本、恢复和防抖保存
    Views/                 # 宠物窗口、托盘/设置入口（后续任务创建）
assets/pets/               # 角色资源，有实际资源时再创建
tests/
  DesktopPet.Core.Tests/   # 核心规则测试，P0-003 起创建
  DesktopPet.App.Tests/    # App 适配与存档测试
  Integration/            # 从当前源码建立隔离副本的生命周期验证
docs/
artifacts/                 # 构建/验证产物，已加入 .gitignore
```

只先创建两个生产项目。Platform 和 Persistence 初版是 App 内目录，不因规划而再拆多个程序集。
设置界面可用简单 WPF 绑定；不要为了“完整 MVVM”把宠物每一帧状态映射成大量 ViewModel 属性。

## 3. 进程、窗口与输入

- 单进程承载所有宠物；每只宠物一个紧凑透明顶层窗口，不用覆盖整个虚拟桌面的大透明窗口。
- 默认不显示任务栏按钮；非激活、置顶、托盘及退出策略在 P0-002/P0-004 验证。
- 透明区穿透与实体可拖动必须分别实测。仅设置透明背景不等于完成了输入命中设计。
- 不把整个窗口永久设为穿透，也不假定 `HTTRANSPARENT` 能解决所有跨进程情况。
- 第一原型优先验证 WPF/layered window 的透明像素行为；如需原生命中或分区，先验证跨进程效果再采用。
- 阶段1在共享 `MainWindow.OnUpdateTick` 中约30Hz通过 `TryGetMousePosition` 读取当前 `GetCursorPos`，直接驱动悬停；实体点击/拖动通过窗口消息与鼠标捕获处理。无速度历史窗口，也无需速度归一化/DPI缓存。
- 按 [D-009](DECISIONS.md#d-009取消快速移鼠受惊功能) 已删除MouseSampler/SwipeDetector、甩动阈值/冷却及自动dodge接线；窗口DPI感知、坐标适配和阶段2拖动抛掷需求保留，不以此恢复鼠标速度触发表情。
- 悬停检测器已实现于 `DesktopPet.Core.Input.HoverDetector`：驻留 1.2 秒触发一次，离开 0.5 秒重新允许。App 通过可选 isOverEntity 提供当前帧 Alpha 命中，完全透明点不计驻留；纯 Core 未传该输入时保留矩形/边距逻辑。App 的 PetImageGeometry 按 Stretch=None 自然尺寸缓存 Alpha，含帧 Transform，通过 Image.PointFromScreen/PointToScreen 映射实际坐标；距离检测使用不透明区域外接矩形，不用透明窗口全框。触发 curious，拖动/菜单/暂停时 Reset。实现方实机记录见 [P1-002 验证](history/P1-002-live-validation.md)。
- 悬停离开时间使用 `_leaveStartMs` 仅记录离开段首个样本，连续 outside 不覆盖。连续离开0.5秒由检测器发出一次 `HoverTrigger.Left` 并结束旧驻留；这是重触发门槛。低频采样下返回时若已超过期限，仍结束旧驻留并从返回时重新计时。
- D-011将好奇显示生命周期独立为Core `CuriosityReaction`：主动保持2000ms、提前离开300ms结束；App仅在当前动作curious且未被中断时消费结束请求并回idle。到时退出不Reset悬停检测器，因此持续驻留不会重播。两类时间各有职责，不用重触发的500ms代替表情退出的300ms。
- 拖动、抚摸、菜单、暂停和委屈表达期间清理悬停；恢复后需重新完整驻留。CuriosityReaction在暂停中冻结剩余寿命、恢复重算离开时段；其他中断取消旧生命周期，残留curious在恢复后重新接管退出，不覆盖其他动作。仍为curious时新驻留事件不重启其寿命。见 [ART-002交接](handoffs/ART-002-natural-expressions.md)；[INPUT-003](handoffs/INPUT-003-curiosity-exit-fix.md)保留原版本依据。
- 点击起点统一要求命中当前帧Alpha；抚摸与拖动共享已捕获的输入，不能同时启动。按D-010，头部不是拖动排除区；具体手势契约见§8。
- 不为悬停检测安装全局键盘钩子。托盘及菜单可暂时正常激活，宠物本体交互不得打断用户键入。
- 当前临时退出入口统一使用 Win32 原生弹出菜单：打开时明确设置所属窗口为前台，关闭时恢复原非激活位；不得只删除 NOACTIVATE 样式就假定外部点击会关闭。实现与回归见 [菜单修复交接](handoffs/P0-002-menu-fix.md)。
- 拖动捕获丢失、窗口销毁、暂停、会话锁定都必须释放输入状态。
- 单实例属于阶段 0 生命周期工作，重复启动应恢复既有实例或给明确反馈，不启动两套存档写入者。

## 4. 坐标与显示器约定

- 系统适配层统一产出虚拟桌面物理像素坐标，允许负坐标。
- Core 的世界位置使用物理像素浮点数；宠物位置锚点为脚底中央。窗口左上角由锚点、素材脚点和缩放推导。
- WPF 的 DIP 与原生物理像素只在 Platform 边界转换；类型/变量名明确 `Px`、`Dip`，不混存。
- 宠物显示大小以 DIP 表达；鼠标触发的等效像素阈值按宠物所在显示器 DPI 转为物理距离。
- 启用并验证 Per-Monitor V2 DPI 感知。跨屏时同时处理尺寸、脚点和鼠标抓取偏移；不能只用主屏比例换算全部窗口。
- 显示器列表保留每块的实际矩形与工作区；可行区域是各矩形的并集，不是整个桌面的外接矩形。
- 目标存档模型应包含显示器标识、相对工作区位置和上次缩放信息；当前 v1 实际只保存显示器标识与 WPF DIP 左上角（见 §7），尚未实现相对位置/旧 DPI 持久化，不能据此承诺混合 DPI 精确恢复。标识失效时映射到当前主屏可见区域。
- 当前 P0-003 Recover 入口将脚点放到主屏工作区中央，并约束完整可见；无有效显示器信息时保持位置。`GetDpiForMonitor` 来自 `Shcore.dll`，须检查 HRESULT 和非零 DPI；窗口实际 DPI 查询使用 `GetDpiForWindow`（User32）。本项目 WPF 启动链通过 `ApplicationManifest` 属性接入 app.manifest 的 DPI 声明启用 PMv2，不依赖 WinForms 的 `ApplicationHighDpiMode`；WFO0003 仅因该混合项目的启动机制而有依据地屏蔽。模式生效以实际 HWND 检查为准，真实混合 DPI 跨屏仍未验证。历史问题见 [Recover 修复交接](handoffs/P0-003-recover-fix.md) 和 [审查最新复核](reviews/P0-003-review.md)。
- `GetWindowRect` 有 DPI 虚拟化且可包含不可见边框；后期读取可见外框时优先评估 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`，统一坐标后再用。

来源：[GetWindowRect 的边界与 DPI 说明](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect)。
阶段 0 必须用实际窗口验证坐标转换，不能只凭公式声称支持混合 DPI。

## 5. 更新循环与单一写入

本节为阶段 1 起行为系统的架构约定。阶段 0 仍由 MainWindow 的 800ms DispatcherTimer 播放两帧占位动画，拖动/找回直接更新窗口；尚无完整共享行为循环或 Core 运动协调器，后续实现须按本节收口，不能把规划当作现状。

采用一个共享更新循环，初始逻辑步长 1/60 秒；渲染可按 30 FPS 起步，根据测量调整。
WPF 可用一个调度入口累计经过时间；不得为每个行为创建独立无限循环或定时器。

每步顺序：采集/消费输入快照 → 处理高优先级中断 → 更新情绪与意图 → 选择行为 → 生成运动请求 → 物理积分/约束 → 发布渲染快照。

- Core 的运动协调器是宠物世界位置与速度的唯一写入者。
- 拖动、追逐、寻找、窗口跟随只提交运动请求，不直接改 WPF `Left/Top` 或 Core 坐标。
- 动画系统只消费动作 ID、方向和时间，可加局部表现偏移，不能偷偷更改世界位置。
- App 根据最终快照移动窗口。Windows 回调把观察结果入队，不在回调中推进整套行为。
- 用单调时钟计算运行间隔。卡顿最多补 5 个固定步，丢弃剩余积压；睡眠/暂停恢复时清空积压和拖动历史。
- 即使渲染帧率变化，状态机的冷却和物理结果仍基于时间，而非“经过多少帧”。

行为选择初始优先级：生命周期/暂停 → 用户抓取 → 可见区域恢复/支撑失效 → 抛掷与落地 → 休息提醒 → 社交 → 情绪表达/悬停 → 彩蛋 → 普通游走/休息。
高优先级可中断低优先级。被打断的彩蛋/社交先取消，不自动从中间恢复；稳定落地后重新选择行为。
夜间困倦是普通行为的表现偏好，不阻止抓取、找回或退出。

2026-11-06 P2-001 释放测速（`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs`）：
- 纯 Core 逻辑，不依赖 Win32/WPF/真实时钟。调用方在拖动期间通过 `RecordSample(x, y, timeMs)` 喂入物理像素坐标和单调毫秒时间戳；释放时调 `EvaluateRelease(releaseTimeMs)` 得到 `ReleaseResult(ShouldThrow, VelocityX, VelocityY, RawSpeed)`。
- 近期运动段速度估算：只保留最近 `SampleWindowMs`（默认 100ms）内的样本，从末尾向前找连续同方向运动样本对（`FindRecentMotionSegment`），用该段位移除以时间差估算速度。不取整个窗口首尾净位移——方向反转时按释放前实际运动方向决定。不取瞬时峰值。
- 停止判定：末样本对速度低于 `StopSpeedPxPerMs`（默认 0.02）时视为停止，返回轻放——不沿用旧峰值。
- 轻放/甩出判定：估算速度低于 `ThrowThresholdPxPerMs`（默认 0.15）或样本不足时 `ShouldThrow=false`，宠物留在释放位置。
- 速度钳位：超过 `MaxSpeedPxPerMs`（默认 2.0）时按比例缩放，保留方向。
- DPI/坐标上下文：采样器不凭坐标自动识别 DPI 跳变。调用方须在上下文变化时调 `ClearSampleContext()` 清空旧基准；清空后需重新积累有效轨迹才能估算速度，坐标跳变本身不产生抛掷。
- 安全处理：零时间差或倒序样本被拒绝且不污染有效历史（`RecordSample` 与最新样本 `_samples[^1]` 比较时间）；x/y NaN/Infinity 坐标被拒绝；NaN/Infinity 释放时间或早于最新样本的释放时间返回轻放；所有返回速度有限。采样中断只是窗口内样本变少。
- `Reset()` 清空样本供下次拖动使用。不拥有宠物位置，不启动物理，不调用 Win32。
- 参数初值见 PRODUCT R-04 P2-001 参数表，标为调试初值待手感校准。

2026-11-06 P2-002 有界物理轨迹与碰撞停止（`src/DesktopPet.Core/Physics/PhysicsSimulator.cs`）：
- 纯 Core 逻辑，不依赖 Win32/WPF/真实时钟/渲染。调用方在甩出释放时调 `Launch(x, y, vx, vy)` 设置初始位置和速度；之后每帧调 `Update(elapsedMs, workArea)` 推进物理，读取 `State.X/Y` 作为宠物世界位置。
- 固定步长 semi-implicit Euler 积分：`v += g*dt; x += v*dt`，dt=`FixedStepMs`（默认 1000/60≈16.67ms）。`elapsedMs` 换算为步数后最多执行 `MaxStepsPerUpdate`（默认 5）步，超出时间丢弃不积累。
- **时间余量累积（carry）**：`Update` 内部维护 `_timeCarryMs` 累积器，跨调用保留不足一步的余量。高频小 dt（如 6×10ms）与等价大 dt（1×60ms）产生相同步数和结果。仅超出 `MaxStepsPerUpdate × FixedStepMs` 的积压才丢弃。`Launch`/`Reset`/`ForceStop` 清零 carry；`ClearTimeAccumulation()` 供暂停恢复清时间不清运动状态。
- 工作区碰撞：`WorkArea(Left, Top, Right, Bottom)` 矩形约束脚底中央锚点。越界时位置钳回边界，对应轴速度反射并乘以 `BounceRestitution`（默认 0.4）。地板反弹额外衰减水平速度（地板摩擦）。`ResolveCollisions` 返回 `(bounced, hitFloor)` 区分地板支撑与侧墙/天花板。
- **自然停止只在地板**：只有地板碰撞（`hitFloor`）后速度低于或等于 `StopSpeedPxPerMs`（默认 0.02）时才设 `IsResting=true`，后续 `Update` 不再步进。侧墙/天花板碰撞不触发自然停止——宠物继续受重力下落直到地板。`MaxBounces`（默认 8）达上限后零水平速度但保留重力——宠物必须落地板后自然停止，不会冻结在半空。零阈值（`StopSpeedPxPerMs=0`）合法：地板反弹后 `Speed=0 <= 0` 成立→静止；恰等于正阈值也静止。
- **ForceStop 与自然停止区别**：`ForceStop()` 在任意位置立即停止（用户重新抓取或暂停时调用），不要求宠物在地板。自然停止要求地板支撑 + 低速。
- 轻放路径：`ReleaseSpeedSampler.ShouldThrow=false` 时调用方不调 `Launch`，模拟器不执行步进，宠物保持释放位置。未 `Launch` 时 `Update` 返回 0 步。
- 暂停恢复：模拟器不在调用间积累时间。调用方在恢复后调 `ClearTimeAccumulation()` 清空 carry，然后传入恢复后的新 elapsed，不传暂停期间的总间隔。`ClearTimeAccumulation` 只清时间不清运动状态（位置/速度/弹跳计数）。
- **输入校验**：`Launch` 拒绝 NaN/Infinity 坐标/速度，返回 `false` 且不改已有状态。`Update` 拒绝非法 dt（NaN/Infinity/负值）、非法 `WorkArea`（颠倒/NaN/Infinity 边界）、非法 `PhysicsConfig`，返回 `Rejected=true` 且不推进模拟、不污染已有状态。`PhysicsConfig.IsValid`（FixedStepMs>0、MaxStepsPerUpdate≥1、GravityPxPerMs2≥0、BounceRestitution∈[0,1]、StopSpeedPxPerMs≥0、MaxBounces≥1）、`WorkArea.IsValid`（四边有限且 Left≤Right、Top≤Bottom）、`PhysicsState.IsFinite`（四分量均有限）提供运行时校验。
- `Reset()` 清空状态供复用。`IsLaunched` 标志表示已 Launch 且未 Reset。
- 不拥有宠物位置（调用方读取 `State.X/Y`），不调用 Win32，不渲染。参数初值见 PRODUCT R-04 P2-002 参数表，标为调试初值待手感校准。

2026-11-06 P2-003 Step 1 拖动采样与释放判定接入（`src/DesktopPet.App/MainWindow.xaml.cs`）：
- `MainWindow` 持有 `ReleaseSpeedSampler` 实例（默认 `ReleaseSpeedConfig`）。`BeginCapturedPointerInteraction` 记录按下时间到 `_pointerPressTimeMs`。`StartDragging` 调 `Reset` 清空旧样本后，立即用 `RecordFootSample(_pointerPressTimeMs)` 记录按下位置的 foot-center 物理像素 + 按下时间作为基准样本——使单次快移 + 基准 = 2 样本即可甩出，不要求两次 Move 事件。对于头部转拖动路径，`ConvertPettingToDrag` 在首个 Move 内调 `StartDragging`，基准样本使用按下时间（早于转换 Move 时间），避免与首次 Move 同时间被 Core 的相同时间校验拒绝。`ProcessCapturedPointerMove` 每帧更新 `Left/Top` 后，用 `CoordinateTransform.WindowTopLeftToFootCenter(Left, Top, Width, Height, dpiX, dpiY)` + `MonitorInterop.GetWindowDpiScale(_hwnd)` 将窗口位置转为 foot-center 物理像素，连同单调 ms 喂入 `RecordSample`。`RecordFootSample` 是 `StartDragging` 基准采样和 `EndDrag` 静止探测的共享方法，生产与测试共用同一路径。
- `EndDrag` 在 `EvaluateRelease` 前做静止探测：如果释放时间距最后样本超过 `SampleWindowMs / 2`（默认 50ms），用 `RecordFootSample` 补一个当前位置样本，使真实无 MouseMove 的静止也能判停（最后样本对变为同位置→`FindRecentMotionSegment` 返回空→轻放）。如果释放紧跟最后 Move（短间隔，<50ms），不补探测，运动样本仍在窗口内→正常甩出。这避免把正常快移后短间隔释放判成轻放，也不靠测试虚构同位置 Move 掩盖静止缺口。
- `OnDisplayLayoutChanged` 调 `ClearSampleContext` 清空 DPI 上下文，防止跨显示器坐标跳变产生误抛。`PauseAnimation` 调 `ClearSampleContext`+`Reset`，暂停后恢复不沿用旧采样。F-02 整改新增 WPF `DpiChanged` 事件接线（`MainWindow_DpiChanged`→`NotifyDpiChanged`→`ClearSampleContext`），覆盖移动到不同 DPI 显示器但不触发 `WM_DISPLAYCHANGE` 的场景。`NotifyDpiChanged` 为 `internal` 方法，生产与测试共用同一路径。
- `ReleaseSpeedSampler` 新增 `NewestSampleTimeMs` 只读属性（返回最新样本时间或 0），供 `EndDrag` 判断静止探测时机。不改变采样器行为，只暴露已有数据。
- 坐标语义：`CoordinateTransform` 的 foot-center = 窗口底边中央（`topPx + windowHeightPx`），与 `character.json` 的 `footAnchorY=110`（图片内脚底位置）不同——后者只影响帧内渲染偏移，不影响物理坐标。物理模拟的 `WorkArea.Bottom` 对应窗口底边。
- 公开属性：`LastReleaseResult`（`ReleaseResult`）、`DragSampleCount`（`int`）供测试验证。`NotifyDpiChanged`（`internal void`）供测试触发 DPI 变化清理。

2026-11-06 P2-003 Step 2 窗口物理接入（`src/DesktopPet.App/MainWindow.xaml.cs`）：
- `MainWindow` 持有 `PhysicsSimulator` 实例（默认 `PhysicsConfig`）、`_isPhysicsActive`（bool）和 `_lastPhysicsUpdateMs`（double）字段。`StartDragging` 中 `_isPhysicsActive = false; _physicsSimulator.Reset()` 立即停止物理——重新抓取时窗口不再被物理驱动，抓取点不跳。S3 细化 ForceStop 语义，但基本停止逻辑在 S2 实现。
- `EndDrag(bool isCancellation = false)` 拆分正常释放与取消路径：
  - **正常释放**（`isCancellation=false`）：`EvaluateRelease` 后若 `ShouldThrow=true`，用当前 foot-center 物理像素和释放速度调 `_physicsSimulator.Launch(x, y, vx, vy)`，设 `_isPhysicsActive=true`，记录 `_lastPhysicsUpdateMs`。轻放（`ShouldThrow=false`）不 Launch，窗口留在释放位置。
  - **取消路径**（`isCancellation=true`）：菜单打开、丢失鼠标捕获、窗口关闭等场景。只清理采样器状态（`Reset`），不调 `EvaluateRelease`，不 Launch。取消不触发抛掷。F-03 整改：`ShowNativeContextMenu` 和 `OnClosed` 均显式传 `isCancellation: true`，不再依赖默认参数。
- `EndCapturedPointerInteraction(bool isCancellation = false)` 传递 `isCancellation` 到 `EndDrag`。`PetCanvas_LostMouseCapture` 传 `isCancellation: true`。`ShowNativeContextMenu` 传 `isCancellation: true`。`OnClosed` 传 `isCancellation: true`。`PetCanvas_MouseLeftButtonUp` 传 `isCancellation: false`（正常松手）。
- `OnUpdateTick` 中 `_isPhysicsActive && !_isDragging` 时调 `UpdatePhysics(nowMs)`：计算 elapsed = nowMs - _lastPhysicsUpdateMs，调 `_physicsSimulator.Update(elapsed, BuildWorkArea())`。若 `IsResting` 或 `Rejected`，设 `_isPhysicsActive=false` 并 `Reset` 模拟器。否则将 `State.X/Y`（foot-center 物理像素）通过 `CoordinateTransform.FootCenterToWindowTopLeft` 转回 DIP 并设置 `Left/Top`。F-04 整改：`Rejected`（非法工作区/非法 dt）时设 `_isPhysicsActive=false`、`_physicsSimulator.Reset()` 并 return，不写入无效坐标，不无限保持物理活动。
- `BuildWorkArea()`：用 `MonitorLocator.FindMonitor`/`FindNearestMonitor` 找目标显示器，取其 `WorkArea`（物理像素），构建 `WorkArea(Left, Top, Right, Bottom)`。foot-center 坐标系：`minFootY = wa.Top + winH`（foot 是底边中央，窗口顶部到达工作区顶部时 foot 在 `wa.Top + winH`），`maxFootY = wa.Bottom`。
- `ClampWindowToWorkArea` 在 `_isPhysicsActive` 时跳过——物理模拟的 `ResolveCollisions` 处理边界碰撞，`ClampWindowToWorkArea` 重复钳位会干扰物理轨迹。
- 公开属性：`IsPhysicsActive`（bool，物理是否正在驱动窗口）、`IsPhysicsLaunched`（bool，模拟器是否已 Launch 且未 Reset）供测试验证。

2026-11-07 P2-003 Step 3 R5 打断/恢复协调整改（`src/DesktopPet.App/MainWindow.xaml.cs`）：
- **重新抓取不跳**：`StartDragging` 中 `_isPhysicsActive = false; _physicsSimulator.Reset()` 立即停止物理。`_dragOffset` 在 `BeginCapturedPointerInteraction` 中用 `screenDip.X - Left` 计算——物理运动后 `Left` 已被物理更新，offset 基于当前窗口位置，重新抓取时 `Left = screenDip.X - _dragOffset.X` 正确保持窗口在鼠标下，不跳。
- **头部抓取停物理**（R5 F-07）：`BeginCapturedPointerInteraction` 中头部路径（`isHeadPress`）在 `_isPettingCandidate = true` 之前，如果 `_isPhysicsActive` 则 `_isPhysicsActive = false; _physicsSimulator.ForceStop()`。`OnUpdateTick` 物理驱动条件加 `!_isPettingCandidate`，使头部按下时物理不推进窗口从鼠标下移走。头部容差/原始抓取偏移/捕获/短按规则不变。
- **暂停冻结非终止**（R5 F-05）：`PauseAnimation` 不调 `ForceStop`、不清 `_isPhysicsActive`，只调 `ClearTimeAccumulation()`。Timer 已停，`OnUpdateTick` 不调用，物理不推进——这是冻结不是终止。`_isPhysicsActive` 保持原值，速度和 `IsResting` 不变。`ResumeAnimation` 重设 `_lastPhysicsUpdateMs = nowMs` + `ClearTimeAccumulation()`，不改变 `_isPhysicsActive`。恢复后第一个 tick 的 `elapsedMs` 是正常 delta（~33ms），不是 5 秒积压。如果暂停前物理在活动，恢复后继续原轨迹。
- **物理期间延迟 Evaluate**（R5 F-06）：`OnUpdateTick` 中 `Evaluate` 条件加 `!_isPhysicsActive`，与 `!_isExpressingGrievance` 同级。`Evaluate` 有 `MarkConsumed` 副作用——物理期间跳过 Evaluate，计划保留原始 date/roll/delay/baseline 和未消费状态。物理结束后下一 tick 正常 Evaluate + 消费。
- **工作区变化停物理**：`OnDisplayLayoutChanged` 中 `_isPhysicsActive` 为 `true` 时调 `ForceStop()` + `_isPhysicsActive = false`，然后 `ClampWindowToWorkArea` 恢复到新合法区域。显示器变化后物理轨迹的边界假设已过时，必须停止。
- **退出保存合法位置**：`RestoreProbePositionForExit` 中 `_isPhysicsActive` 为 `true` 时调 `ForceStop()` + `_isPhysicsActive = false` + `ClampWindowToWorkArea()`，确保保存的位置是合法静止位置而非物理中间坐标。`OnClosed` 中 `_isPhysicsActive` 为 `true` 时调 `ForceStop()` + `_isPhysicsActive = false`，覆盖 alt-F4 等非正常退出路径。
- **无可用显示器安全处理**：`BuildWorkArea` 在 `monitors.Count == 0` 时返回 `new WorkArea(1, 0, 0, 0)`——`Left(1) > Right(0)` 使 `IsValid` 返回 `false`，`Update` 返回 `Rejected`，`UpdatePhysics` 的 Rejected 路径停止物理并 Reset，不写入无效坐标。`BuildWorkArea` 支持 `TestMonitorList` 注入点（`TestMonitorList ?? MonitorInterop.EnumerateMonitors()`），测试可注入真实空列表而非 Width hack。不修改 `WorkArea.IsValid` 的语义（保持 `<=`）。

2026-11-07 P2-003 Step 3 R6 整改（`src/DesktopPet.App/MainWindow.xaml.cs`、`src/DesktopPet.App/App.xaml.cs`）：
- **暂停 tick 守卫**（R6 F-05）：`OnUpdateTick` 物理驱动条件从 `_isPhysicsActive && !_isDragging && !_isPettingCandidate` 改为 `_isPhysicsActive && !_isDragging && !_isPettingCandidate && !_isAppPaused`。停 DispatcherTimer 不等于 tick 方法本身在暂停时不会推进——若 tick 被直接调用仍会推进物理写位置。加 `!_isAppPaused` 后，即使 tick 进入也不推进物理。不改为 ForceStop（保持冻结语义）。
- **生产退出恢复链**（R6 F-08）：`App.RestoreWindowPosition` 从 `private` 改为 `internal`，新增 `App.TestSetMainWindow(MainWindow)` 和 `App.TestState` internal 访问点。测试复用生产 `TestSetupIsolatedSave` → `TestSetMainWindow` → `FlushFinalState`（生产退出路径：`RestoreProbePositionForExit` + `MarkStateDirty` + `SaveCoordinator.Flush`）→ `ArchiveService.LoadState`（生产加载入口）→ `RestoreWindowPosition`（生产恢复入口：`WindowPositionAdapter.RecoverPosition`），断言整个窗口位于合法工作区内、无速度持久化、首 tick 静止。不另写测试专用保存/恢复算法。

## 6. 隔离外部依赖

仅在需要时引入小接口，不提前建立完整服务框架：

- 时间源：单调运行时间与 UTC 墙钟分离；测试可推进时间。
- 随机源：测试能固定种子；生产随机结果必要时持久化，重启不重新抽奖。
- 桌面快照：鼠标、显示器、空闲、后期的窗口信息；Core 不直接调用 Win32。
- 存档仓库：App 负责 IO；Core 仅产出可保存状态。

### 阶段3拟采用边界（D-017，尚未实现）

- 同一App/单实例进程管理最多两个Pet实例，每个实例持有自己的窗口、输入、动画、物理和个体委屈状态；PetId贯穿运行期与存档，不以数组下标/窗口句柄作为永久身份。当前仍为单宠实现，不把现有全局字段当成已隔离。
- App负责实例增删、全局托盘操作与保存协调；只抽取当前双宠所需职责，不建立插件系统/通用实体框架。托盘Pause/Resume/Recover/Exit作用于全部现存宠物；单宠右键原有含义保留，避免删除错误目标。
- 个体状态与全局暂停/设置/稀有每日计划分开；后者沿用全应用共享额度，不能复制一份计划给每只宠物以增加彩蛋次数。存档集合布局/版本号在P3-001-S1核实当前v3后写回本节，现有格式尚不修改。
- 兼容迁移须保留单宠旧字段到第一只稳定实体，不复制旧状态给新增实体；旧无性格存档的首只默认稍主动，第二只慢半拍，角色与ID绑定。移除第二只须取消相关活动并释放窗口/引用，下一次新增不复用旧ID。
- 后续社交协调器管理唯一共享会话，持会话ID/参与者/阶段/截止/取消原因；角色只提交位置意图，沿用单一位置所有权规则。用户实体抓取（含头部候选）立即优先，任一参与者中断须释放双方；首步不提前生成会话框架。
- 主动移动先用已有工作区/窗口合法边界和物理静止状态判断底边支撑，同显示器同活动面才可达；悬空轻放不会因社交启动被下拉。具体容差/速度在相应任务规定，不复用图像透明底边测量来暗改坐标模型。
- 性格是稳定行为配置，影响社交发起/回应，不阻塞Dispatcher线程或用户输入；不预写关系数值/对用户亲近度。规则唯一出处为[PRODUCT R-05](PRODUCT.md)。

## 7. 时间和存档

- 默认目录：`%LOCALAPPDATA%\DesktopPet\`，不向源码目录或安装目录写用户状态。
- `settings.json` 保存设置；`state.json` 保存宠物、情绪、事件与位置；未来习惯数据单独存储。
- 每份文件带整数 `schemaVersion`。阶段 0 为 v1，实际结构如下：

  **state.json v3**（`PetStateArchive`，`src/DesktopPet.Core/Persistence/PetStateArchive.cs`）：
  - `schemaVersion` (int, 默认 3)：模式版本号。v1 文件读取时情绪字段回退默认值；v1/v2 文件读取时稀有事件字段回退默认值。
  - `windowLeft` (double, 默认 100)：窗口左上角 X，WPF DIP 坐标。
  - `windowTop` (double, 默认 100)：窗口左上角 Y，WPF DIP 坐标。
  - `savedDeviceName` (string|null)：上次保存时宠物所在显示器设备名，用于恢复时检测显示器是否仍存在。
  - `lastSavedUtc` (string|null)：上次保存的 UTC 时间戳（ISO 8601 round-trip 格式）。
  - `isPaused` (bool, 默认 false)：是否处于暂停状态（主动行为停止）。
  - `grievance` (int, 默认 50)：委屈值（0-100，中性 50）。v1 回退 50。
  - `lastDecayUtc` (string|null)：上次衰减计算基准时间（UTC ISO 8601）。v1 回退为 `lastSavedUtc`。
  - `pendingGrievanceExpression` (bool, 默认 false)：是否有待执行的委屈表达。v1 回退 false。
  - `grievanceExpressionDate` (string|null)：标记待表达的本地日期（yyyy-MM-dd）。v1 回退 null。
  - `accumulatedRuntimeMinutes` (double, 默认 0)：有效运行时间累计（会话级，单调时间源）。v1/v2 回退 0。
  - `rareEventPlan` (object|null)：每日稀有事件计划快照（`RareEventPlanSnapshot`）。v1/v2 回退 null。子字段：`localDate` (string|null)、`triggerDelayMinutes` (int)、`triggerRoll` (double)、`consumed` (bool)、`accumulatedBaselineMinutes` (double)。

  **settings.json v1**（`PetSettingsArchive`，同文件）：
  - `schemaVersion` (int, 默认 1)：模式版本号。
  - `animationSpeed` (double, 默认 1.0)：动画速度倍率字段。阶段 0 可序列化/读取，但尚未接到实际动画速度，也没有设置界面；不能把数据往返测试当作调速功能通过。
  - `autoStart` (bool, 默认 false)：开机自启（P0-004 预留，未实现）。

  **恢复规则**：
  - 文件不存在时回退默认值，不报错。
  - JSON 解析失败视为损坏：原文件重命名为 `*.corrupted` 保留，优先尝试 `*.bak` 备份恢复，无备份则回退默认值。
  - `schemaVersion` 高于当前版本时建立该文件独立的只读保护；兼容字段尽力读取，类型不兼容则以默认运行数据继续，原未来版本文件不能归类为损坏。state/settings 的保护互不影响；正常自动保存会检查标记、DTO 和磁盘版本，避免替换对象或新建服务后降级。启动时发出托盘只读提示。
  - v1 → v2 升级：情绪字段（`grievance`、`lastDecayUtc`、`pendingGrievanceExpression`、`grievanceExpressionDate`）回退默认值；`lastDecayUtc` 回退为 `lastSavedUtc`。
  - v1/v2 → v3 升级：稀有事件字段（`accumulatedRuntimeMinutes`、`rareEventPlan`）回退默认值（0/null），不影响已有情绪字段。
  - F-06 稀有事件恢复约定：恢复时 `EffectiveRuntimeTracker.Restore` 仅恢复累计总量，不恢复活跃引用；调用方须调 `StartActivePeriod` 建立新单调基准，离线间隔不计有效运行时间。`RareEventScheduler.RestorePlan` 仅接受 `IsValid` 通过的快照，非法计划被拒绝且不影响已有计划。基准大于累计等不一致数据不崩溃，Evaluate 用差值评估自然得到 DelayNotReached。
  - 安全写入：先写 `*.tmp` → 旧文件移为 `*.bak` → `*.tmp` 移为正式文件。不先删正式文件。
- 先写同目录临时文件，完成序列化与关闭后再替换正式文件，保留上次有效备份；不能先删正式文件再写。
- 单一写入队列，脏状态按 5 秒防抖保存并在正常退出时落盘，不逐帧写磁盘。
- 正常退出重新捕获最终状态并标脏后 Flush，不依赖前一次防抖仍未完成。手动找回和布局恢复的位置变化同样标脏，关闭路径防重入。
- 启动显示前应用 isPaused 的行为；获得窗口句柄后复用位置适配器校验旧屏/工作区位置，不能直接把未验证旧坐标赋给窗口。保存时记录实际所在显示器名称；v1 没有保存旧屏 DPI，真实混合 DPI 恢复仍须按验证边界处理。
- 损坏文件保留原件并尝试备份恢复，再回退默认值；未知更高版本不能直接覆盖为低版本默认存档。
- 保存 UTC 时间戳；“第二天”和每日额度按用户当前本地日期判断。日期换算与 UTC 间隔不可混用。
- 衰减间隔取非负值，初始最大按 30 天计算；时钟回拨不增益。记录最近处理日期，避免回拨重复触发每日内容。
- 离线只推进允许衰减的数值，不补演离线社交、物理运动或历史提醒。
- 锁屏/睡眠/恢复事件由 App 提供；不读取具体键盘内容。`GetLastInputInfo` 仅用于当前会话输入空闲判断，不能证明连续打字。

来源：[GetLastInputInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo)。

## 8. 资源起步约定

2026-10-24 小熊接入：默认运行目录为 assets/pets/bear/，角色ID bear-v1，旧 assets/pets/default/ 保留为原型回退。原P1-001占位图及其测量记录为历史基线，不是当前美术。九张静态PNG为120×120、96DPI，帧脚点(60,110)；curious PNG底部101加offsetY9，其余底部110。表情母版/导出约束见 assets/art/bear-v1/README.md，运行映射见 assets/pets/bear/README.md。

2026-10-25 ART-002：额外两张blink_half/closed保持原idle底图RGBA（仅两处眼部RGB变化，Alpha完全不变）。JSON增加blink Once序列，idle仅保留中性睁眼图。Core `IdleBlinkScheduler`消费该序列并调度；App只在idle且无输入/情绪占用时以返回帧覆盖渲染，不调用PlayAction或改变CurrentAction/世界位置。暂停冻结帧与剩余间隔，恢复移除暂停时段；未更新超过500ms则弃掉旧计划、重排安静间隔。退出其他表情后也重新等待；无idle以外的眨眼、跨图片透明叠加或新资源字段。原default未注册blink时不启用调度，原型加载/回退保留。

ART-003：drag映射drag_gentle，新release Once序列供Core `DragReleaseFeedback`消费，仅作idle渲染覆盖。实际EndDrag先停止抓取/移动并恢复idle身份，再且仅在确有拖动结束时启动面部过渡；菜单未拖动时不能制造过渡。渲染优先当前高优先级动作→合法idle的release覆盖→idle blink→原idle，所有帧偏移来自最终选中帧，不写世界坐标。BeginCapturedPointerInteraction/StartDragging取消旧release；暂停/恢复补偿计时，在暂停中松手的新过渡从自身起点冻结；其他动作/菜单清理后不复活旧过渡。无release注册的default仍按原路径立即回idle。
MainWindow既有委屈生命周期改为播放独立 grievance 静态脸；时长/冷却/优先级不变。其他新增表情仅注册供后续使用，不新增自动状态。身体动作暂缓，原基本切帧继续保留。不能把图像候选板当作运行图集，也不能声称已实现背对动作。

阶段 0 不要求资源加载器；程序绘制形状必须有明显身体/头部，便于命中测试。
阶段 1 加入角色 JSON：角色 ID、帧尺寸、脚点、交互区域、动作 ID、帧路径、每帧时长和循环方式。
坐标均为未缩放素材像素；用脚点固定切换动作时的位置，禁止动作变化导致角色脚底跳动。
当前基础动作集：idle、curious、drag；情绪接入grievance，静态储备表情按bear资源说明注册。dodge已按D-009从bear和default的JSON注册移除。缺失动作回退idle并记录一次诊断，不崩溃；通用Once播放器能力保留。
路径仅允许包内相对路径，加载时校验帧尺寸与时长，PNG 解码后缓存，不每帧重新读盘。
实际 JSON 样例与字段结构在对应任务建立，未建立前不宣称已有可用资源格式。

P1-001 已实现：
- 角色 JSON 格式：`assets/pets/default/character.json`，字段含 id/frameWidth/frameHeight/footAnchorX/footAnchorY/actions。
- 动作定义：每个动作含 loop（once/loop/pingpong）和 frames 列表，每帧含 imagePath/durationMs/offsetX/offsetY。
- Core 模型：`AnimationModels.cs`（PetCharacterDef/ActionDef/FrameDef/LoopMode）、`CharacterLoader.cs`（JSON 解析+校验，含路径遍历拒绝、尺寸/脚点校验）、`AnimationPlayer.cs`（时间驱动播放、循环模式、缺失回退、脚点偏移、完成事件、Pause/Resume 时间冻结）。
- 占位 PNG：程序导出的 5 帧（idle_0/1、curious_0、dodge_0、drag_0），120×120 透明 PNG。curious 声明 offsetY=9；实测 idle 脚底 y=110、curious 脚底 y=101，差 9px 与 offsetY=9 一致，补偿契约已满足。R4 曾发现旧版 curious 脚底为 y=95（差 15px），已重新生成素材修正。
- App 渲染：MainWindow 用 Image 控件显示 PNG 帧，30FPS DispatcherTimer 统一更新，PNG 预加载缓存。UpdateFrame 通过 TranslateTransform 应用 GetFootOffset 的帧偏移到渲染。OffsetX/Y 是渲染补偿值（正值向右/下），直接作为 Transform 应用，不取反。当帧内容比标准位置高 N 像素时，OffsetY=N 使图像下移 N 像素，视觉脚底回到 FootAnchorY 位置，世界脚点稳定。
- 拖动中断：鼠标按下切 drag 动作，松手切回 idle；Once 动作完成后自动回 idle。
- 暂停/恢复：AnimationPlayer.Pause/Resume 冻结动画时间，暂停期间 elapsed 不推进，恢复后从有效时间继续。PlayAction 在暂停中切换动作时保持暂停状态，新动作从起点冻结，不隐式解除暂停。App PauseAnimation/ResumeAnimation 调用播放器 Pause/Resume 并维护 _isAppPaused 标志，确保 App 暂停状态、timer 和播放器三者一致。
- 资源校验：CharacterLoader 拒绝路径遍历（../）、负/零尺寸、脚点越界；路径比较确保目录分隔边界，兄弟目录前缀路径被拒绝。PreloadImages 包裹 try/catch，坏 PNG 不崩溃；PNG 解码后比较 PixelWidth/PixelHeight 与 JSON 声明尺寸，不匹配则跳过；缺帧回退 idle 首帧；缺动作诊断去重。全资源不可用时生成程序绘制的可见回退位图，不留透明空窗。

2026-11-04 P1-004 稀有事件 Core 调度：`src/DesktopPet.Core/Events/` 下三文件。
- `RareEventPlan`：每日计划快照 DTO。字段：`LocalDate`(yyyy-MM-dd)/`TriggerDelayMinutes`(20–120)/`TriggerRoll`(0–1 预滚动)/`Consumed`/`AccumulatedBaselineMinutes`(F-05: 计划创建时的会话累计基准，当日有效时间 = 当前累计 - 基准)。`CreateNew` 用可控 Random 同时抽取延迟和滚动值，接受 `accumulatedBaselineMinutes` 参数；`ShouldTrigger(0.5)` 判断 `TriggerRoll < probability`；`AsConsumed` 返回已消耗副本（保留基准）。`IsValid` 校验日期可解析、延迟在 [0, MaxTriggerDelayMinutes]、Roll 为有限 [0,1)、Baseline 为有限非负。全字段可序列化用于快照保存/恢复。
- `RareEventScheduler`：调度器。`EnsurePlanForDate(localDate, random, minDelay, maxDelay, currentAccumulatedMinutes)` 按本地日期前进创建新计划（同日不重抽，回拨不创建新计划），F-05 将 `currentAccumulatedMinutes` 写入计划基准；`Evaluate(effectiveRuntimeMinutes, currentLocalDate, isPaused, isDragging, isSocialActive, isRestReminderActive)` 返回 `TriggerResult(ShouldFire, DenyReason, UpdatedPlan)`——F-01 日期一致性检查：`currentLocalDate` 必须与计划日期匹配，回拨返回 `DateRollback`、前进返回 `DateAdvanced`，均不消耗计划；F-03 拒绝 NaN/Infinity/负运行时间和无效计划；F-05 当日有效时间 = `effectiveRuntimeMinutes - plan.AccumulatedBaselineMinutes`，与 `TriggerDelayMinutes` 比较决定延迟是否到达；不合适时拒绝且不消耗计划，延迟到达时消耗计划（无论触发与否，每日仅一次尝试）；`MarkConsumed` 手动标记跳过；`RestorePlan` 仅接受 `IsValid` 通过的快照，拒绝无效快照且保留现有有效计划；`GetSnapshot` 支持重启恢复。共享每日额度通过 `Consumed` 标志实现。
- `EffectiveRuntimeTracker`：有效运行时间累计器。F-02 单调时间契约：输入为单调时间源（如 `Stopwatch.GetTimestamp()` 转换的 double 分钟值），不是 UTC 墙钟；UTC 仅用于日期判定，不用于活跃计时。`StartActivePeriod(double)`/`EndActivePeriod(double)` 管理活跃区间，暂停期间不累计；F-03 Start/End 拒绝 NaN/Infinity/负值，不修改已有有效状态；`UpdateTo(double)` 仅正 elapsed 累加，单调时钟不回退，拒绝 NaN/Infinity；`Restore(double)` 仅恢复累计总量，不恢复活跃引用——G-01 离线契约：调用方必须在 `Restore` 后调用 `StartActivePeriod` 建立新活跃基准，离线时间不计有效运行时间。累计分钟为会话级，跨日不重设；F-05 每日计划用差值（当前累计 - 基准）评估当日延迟。

2026-11-05 P1-004 App 运行时接线（`App.xaml.cs` + `MainWindow.xaml.cs` OnUpdateTick）：
- App 持有 `RuntimeTracker`/`RareEventScheduler` 字段，`OnStartup` 经 `MapArchiveToRareEventObjects` 从存档恢复并 `StartActivePeriod`（未暂停时）；`MarkStateDirty` 经 `MapRareEventObjectsToArchive` 同步到存档；`FlushFinalState` 退出时经既有 `SaveCoordinator` 落盘。
- OnUpdateTick 每 tick（未暂停时）调用 `UpdateTo` → `EnsurePlanForDate` → `Evaluate`。
- F-08 保存通知：计划创建、日期更新或 Consumed 状态变化时统一通知既有保存队列（`MarkStateDirty`），不只在 `ShouldFire` 时通知。通过比较上次计划日期 + Consumed 状态避免每 tick 标脏，保证 5 秒防抖能正常完成。RollFailed 也会消耗计划并触发保存。

2026-11-05 P1-004 探头视觉表现（`MainWindow.xaml.cs` OnUpdateTick）：
- ShouldFire 返回 true 时启动探头状态：记录当前 `Left`/`Top` 为原始位置，计算最近工作区边缘的探头位置，设置窗口位置到探头位置，播放 `curious` 动作。
- 探头持续约 3000ms，OnUpdateTick 中用 `_probeStartMs` + `_probeDurationMs` 跟踪剩余时间。到时后恢复原始 `Left`/`Top`，通知保存队列，回 idle 动作。
- 位置单一写入：探头通过 OnUpdateTick 设置 `Left`/`Top`，与拖动共享同一写入路径。F-10：仅真正取得拖动所有权时（`StartDragging`，含身体立即拖动和头部转拖动）才调用 `CancelProbeForDrag`（不恢复原始位置），之后由拖动接管位置。头部短按/微移未转拖动及透明/无效按下不取消探头。拖动优先，探头不与拖动并发。
- 暂停冻结：`PauseAnimation` 时若探头活跃，冻结剩余时间（不推进 `nowMs` 判定）；`ResumeAnimation` 后继续剩余时长。
- 退出恢复：`FlushFinalState` 前检查探头活跃状态，若活跃则先恢复原始位置再标脏和 Flush，确保存档记录原始位置。
- F-09 探头动作所有权：探头活跃期间（`_isProbing`），OnUpdateTick 的普通 hover/curiosity 分支被跳过（条件含 `!_isProbing`），低优先级分支不得接管或覆盖探头 curious 表情。else 分支中也不取消 curiosity（`!_isProbing` 守卫）。高优先级委屈表达与探头双向协调：委屈表达已开始时（`_isExpressingGrievance`），`Evaluate` 调用被延迟（`result = null`，不评估不消费），保留原计划日期、roll、delay、baseline 和未消费状态，委屈结束后下一 tick 正常评估同一计划（符合 R-03"不合适时延迟至空闲行为期间"）；`StartProbe` 调用条件含 `!_isExpressingGrievance` 作为二级守卫；探头进行中委屈需要启动时，委屈分支先设 `_isExpressingGrievance = true`，再检查 `_isProbing` 调 `EndProbe(restoreOriginal: true, nowMs)` 安全结束探头（恢复原始位置），然后播放 grievance；`EndProbe` 回 idle 条件含 `!_isExpressingGrievance`，委屈进行中不回 idle。已经实际开始过的探头被委屈打断，维持已消费且不重播（`Evaluate` 在委屈前已消费计划，延迟只影响未评估的计划）。探头正常结束或被拖动打断时统一释放。

P1-003 交互区域约定（D-013 已取消好感度/抚摸计分，头部手势不计分，保留拖动分流规则）：
- 头部区域（手势命中区）：图像矩形内上 1/3，即 X ∈ [0, frameWidth] 且 Y ∈ [0, frameHeight/3]。坐标超出图像边界（负值或超出宽高）不算头部。
- 头部区域还需命中当前帧的不透明像素（Alpha > 0）：头部矩形内的透明空白不算头部，防止在空白处误判手势。
- 拖动起点：当前帧所有不透明像素，包含头顶/耳朵/眼睛上方。身体按下立即拖动；头部按下先进入手势候选，不能把该候选误当成不能拖动。
- `BeginCapturedPointerInteraction`在鼠标捕获成功后保存最初的screen DIP位置与抓取偏移；`ProcessCapturedPointerMove`用相对按下点的累计距离区分手势。超过8DIP、单次位移超过8DIP或移出头部区时先转拖动，不先调用会释放捕获的EndPetting，也不以转换时位置重算抓取点；同一事件立即应用完整位移。
- 头部转拖动清空手势候选状态，保留原捕获；抬起经`EndCapturedPointerInteraction`统一结束，捕获丢失/窗口关闭沿用原清理路径。真实窗口事件和测试使用同一坐标处理入口，DIP换算仍在WPF边界完成。
- 当前区域比例硬编码为上 1/3（`HeadZoneRatio`），来源为 frameHeight；角色 JSON 中的 per-character 交互区域字段待后续阶段加入。

## 9. 后期 Windows 能力边界

窗口栖息先验证枚举、可见外框、最小化/关闭与 Z 序，随后评估 `SetWinEventHook` 配合低频校验。
只关心外部顶层窗口的相关事件，过滤自身与子对象噪声；保留回调引用，退出时解除钩子。
HWND 可能失效或复用，不能把一个数值句柄当成永久窗口身份。
初版不获取 UAC 安全桌面信息、不要求管理员权限、不改第三方窗口父子关系。
来源：[SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)。

习惯统计先用本地频次模型，读取失败或样本不足时无预测。图标定位不属于窗口位置 API 自动提供的能力，另做验证任务。

## 10. 双宠前工程准备（D-018）

### PREP-001 异常诊断边界（已实现，待监理复核）

- App在生产启动早期（OnStartup 首行，单实例检查之前）注册诊断通知，区分主UI Dispatcher、AppDomain终止通知和Task未观察异常；它们不覆盖所有线程/致命故障。已知可恢复业务错误仍在局部边界处理。实现位置：`src/DesktopPet.App/Diagnostics/DiagnosticLogger.cs`（日志组件）、`App.xaml.cs` `RegisterDiagnosticHandlers()`（接线）。
- 未知UI异常默认记录后保留框架异常处理策略，不一律Handled=true；AppDomain只尽力记录，不许诺恢复。Task通知不等于立即或必然终止，不替代正常await/观察异常。不自动重启、不在未知损坏状态强制保存；既有正常退出保存链（FlushFinalState/SaveCoordinator）不受破坏，异常退出与正常退出接线已通过隔离子进程验证。
- 日志组件不依赖Dispatcher排队/复杂锁在终止时运行；写锁采用有界策略（`Monitor.TryEnter(0)`，获取失败即舍弃日志），保证 `Record`/`Dispose` 不因其他线程持锁无限等待。记录失败不抛第二异常、不递归提示或死锁，不能为了日志吞掉原异常。异常去重/关联不改变原退出策略。
- 本地目录采用 `%LOCALAPPDATA%/DesktopPet/logs/`，与state/settings隔离，支持测试专用目录。初值：单文件上限256KiB（编码字节），最多保留5份（含当前，跨日期全局清理），限制异常正文（2048字符）/堆栈长度（8192字符）；写前按实际UTF-8字节计算，超长单条按字符边界截断。不引入日志框架。实现若需调整初值写回这里，不让日志失败影响原有异常处理。
- 记录UTC时间、应用版本/构建标识、事件来源、终止标记、异常类型/消息/堆栈及必要线程信息；不采集键盘内容、屏幕或存档全文，不自动上传。
- 隔离子进程验证真实注册/事件/退出（`tests/DesktopPet.DiagnosticProbe/`），探针复用生产 `App.RegisterDiagnosticHandlers()` 入口（非自建handler副本），父进程采集OS真实退出码；不用直接调用handler冒充接线测试。组件测试验证容量、跨日保留、精确字节、锁竞争有界与记录失败（`DiagnosticLoggerTests` 16项）。不可恢复故障、进程强杀/断电等不承诺必留日志或自动恢复。Task通知的GC依赖在隔离测试中标注为Inconclusive。
- 技术依据：[WPF异常通知](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-10.0)、[AppDomain异常通知](https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception?view=net-10.0)、[Task异常通知](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskscheduler.unobservedtaskexception)。

### PREP-002 有限职责整理

当前S1已实现：App与MainWindow共享 `Platform/MonotonicClock.Instance`，保留double毫秒/懒起点/暂停补偿和测试注入；App不再经MainWindow静态取时钟，MainWindow公开取时钟包装仅作兼容。见[监理S1 R1](reviews/PREP-002-review.md)。MainWindow仍集中输入/动画/物理/情绪/稀有事件更新，不是已完成组件化；后续分小步收拢单宠状态访问/保存通知和更新/位置职责，保留WPF命中/坐标适配边界。

当前S2已实现（F-01整改后，R3接受）：创建 `Platform/PetStateContext`（持有 `EmotionAccumulator`/`CornerTracking`/`GrievanceExpression`/`RuntimeTracker`/`RareEventScheduler` 五个状态对象 + `Action? MarkStateDirty` 回调，`init` 属性），App在 `OnStartup` 中 `new MainWindow()` 后显式赋值 `MainWindow.StateContext`。MainWindow每个状态对象独立按 `Test*` → `StateContext` 对应成员 → `App.Current` 对应成员解析（`Resolve` helper），不用首对象判空控制其他对象回退；所有 `app.MarkStateDirty()` 散落调用改为 `NotifyStateDirty()` 集中方法（`StateContext.MarkStateDirty` → `App.Current` 回退）。生产显式上下文优先，保留 `App.Current` 回退，不宣称已完成按PetId实例隔离。行为、时间注入、存档格式和物理参数不变；现有测试无需修改。构建0警告0错误，全量636/636通过（Core 351 + App 285，含7个非空上下文回归）。见[监理S2 R3](reviews/PREP-002-review.md)、[整改记录](remediation/PREP-002-response.md)。

当前S3已实现：抽取稀有事件计划评估和有效运行时间推进的更新职责到 `Platform/RareEventUpdateService`（静态服务，无实例状态）。边界如下：
- **依赖**：`EffectiveRuntimeTracker`、`RareEventScheduler`（由调用方通过参数传入，不自行解析三级优先——解析仍由 `MainWindow.Resolve` 完成后传入）；单调时钟毫秒（`double nowMs`，由 `MainWindow` 的 `TestGetElapsedMs/GetElapsedMs` 提供）；当前本地日期字符串（`string today`，由调用方传入）；拖动/委屈/物理暂停状态（`bool isDragging`、`bool isExpressingGrievance`、`bool isPhysicsActive`，由调用方传入）。
- **返回结果**：`RareEventUpdateResult` 记录——`bool ShouldFire`（是否应启动探头）、`bool StateChanged`（计划状态是否变更需保存）、`string? PlanDate`/`bool? PlanConsumed`（当前计划快照，供调用方更新 `_lastRareEventPlanDate`/`_lastRareEventPlanConsumed`）。
- **保存通知**：`RareEventUpdateService` 不直接调用 `NotifyStateDirty()`——返回 `StateChanged` 后由 `MainWindow.OnUpdateTick` 调用 `NotifyStateDirty()`，保持保存通知在 MainWindow 统一入口。
- **位置所有权**：`ShouldFire=true` 时的 `StartProbe` 调用和探头超时检查 `EndProbe` 保留在 `MainWindow.OnUpdateTick`——探头涉及窗口位移，不抽取到更新服务。
- **不抽取**：探头启动/超时、物理推进、悬停检测、情绪衰减、帧更新——这些保留原统一入口。
- **保持语义**：暂停时不推进 tracker/不评估 Evaluate；委屈/物理期间跳过 Evaluate（延迟评估）；每日共享额度（EnsurePlanForDate）和消费（MarkConsumed）/保存（stateChanged→NotifyStateDirty）语义不变；`new Random()` 每次创建的随机源行为不变。
- **测试注入**：`MainWindow` 的 `TestRuntimeTracker`/`TestRareEventScheduler` 仍由 `Resolve` 解析后传入服务方法；服务本身不持有 `Test*` 字段或 `StateContext` 引用。

不改变用户行为、单宠存档v3、物理参数或美术，不在此准备阶段引入PetId/双窗/社交会话。不能以partial拆文件冒称隔离完成，不建立泛化实体框架，不在重构和P3迁移阶段各写一套存档映射；阶段3按§6落实PetId所有权。S3已实施：`RareEventUpdateService.EvaluateTick` 接收 tracker/scheduler/nowMs/today/isDragging/isExpressingGrievance/isPhysicsActive，返回 `RareEventUpdateResult`（ShouldFire/StateChanged/PlanDate/PlanConsumed）；`OnUpdateTick` 调用服务后负责 `StartProbe`/探头超时 `EndProbe`/`NotifyStateDirty` 和 `_lastRareEventPlanDate`/`_lastRareEventPlanConsumed` 缓存更新。构建0警告0错误，全量636/636通过（Core 351 + App 285）。实机烟测通过（隔离实例，用户确认启动/悬停/身体头部拖动/轻放/抛掷/暂停恢复/找回/正常退出全部正常，生产存档未触碰），已提交最终复核。
