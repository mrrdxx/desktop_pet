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
- 阶段 1 鼠标位置由一个共享采样器读取 `GetCursorPos`，建议 30 Hz；实体点击/拖动通过窗口消息与鼠标捕获处理。
- 鼠标速度/距离采样器已实现于 `DesktopPet.Core.Input.MouseSampler`：滑动窗口（默认 10 样本）、过滤采样中断（间隔超限清窗）和瞬移（等效位移超限清窗）、输出两种速度信号——净位移速度（移动趋势，方向反转会抵消）和近期分段速度（最近 N 段最大单段速率，不抵消、不被旧静止样本稀释，供甩动检测使用）。跨屏 DPI 等效换算。纯 Core 逻辑，参数可配置（`MouseSamplerConfig`），DPI 缩放运行时可调。已接入 App 层：`MainWindow.OnUpdateTick` 在 ~30Hz 调用 `GetCursorPos` 喂给采样器，拖动/菜单/暂停时 Clear。DPI 已接入窗口实际显示器（`MonitorInterop.GetWindowDpiScale`），DPI 变化时清空采样器。
- 悬停检测器已实现于 `DesktopPet.Core.Input.HoverDetector`：纯 Core 逻辑，鼠标在实体矩形内驻留 1.2 秒触发一次（R-01），离开 0.5 秒后重新允许，连续悬停只触发一次。参数可配置（`HoverDetectorConfig`：DwellMs/LeaveResetMs/HoverMarginPx）。已接入 App 层 `OnUpdateTick`，触发时 `PlayAction("curious")`，拖动/菜单/暂停时 Reset。
- 甩动检测器已实现于 `DesktopPet.Core.Input.SwipeDetector`：消费近期最大分段速度及其 PeakSegmentEnd 终点的边缘距离，冷却参数为 3 秒，已接入 App `OnUpdateTick` 和 dodge 动作。参数由 SwipeDetectorConfig 配置，拖动/菜单/暂停时 Reset；不将最新慢段位置错误匹配给旧峰值。
- 悬停离开时间使用 `_leaveStartMs` 仅记录离开段首个样本，连续 outside 不覆盖。[复核 R1](reviews/P1-002-review.md) 已关闭原悬停重置与峰值位置组合的 F-02/F-03；完整 App 交互和真实手感尚未验收。
- 头部抚摸与身体拖动的命中区由资源提供；在手势识别完成前不能同时启动两种操作。
- 不为悬停/速度检测安装全局键盘钩子。托盘及菜单可暂时正常激活，宠物本体交互不得打断用户键入。
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

行为选择初始优先级：生命周期/暂停 → 用户抓取 → 可见区域恢复/支撑失效 → 抛掷与落地 → 鼠标躲避 → 休息提醒 → 社交 → 情绪表达/悬停 → 彩蛋 → 普通游走/休息。
高优先级可中断低优先级。被打断的彩蛋/社交先取消，不自动从中间恢复；稳定落地后重新选择行为。
夜间困倦是普通行为的表现偏好，不阻止抓取、找回或退出。

## 6. 隔离外部依赖

仅在需要时引入小接口，不提前建立完整服务框架：

- 时间源：单调运行时间与 UTC 墙钟分离；测试可推进时间。
- 随机源：测试能固定种子；生产随机结果必要时持久化，重启不重新抽奖。
- 桌面快照：鼠标、显示器、空闲、后期的窗口信息；Core 不直接调用 Win32。
- 存档仓库：App 负责 IO；Core 仅产出可保存状态。

每只宠物有稳定 `PetId`；社交会话有独立 ID、参与者、阶段、截止时间和取消原因。
关系按无序宠物 ID 对存储；个体情绪与双方关系分开，不能共享一个全局好感变量。
移除宠物要取消所有相关会话；超时和被拖动的退出路径必须与正常结束一样可测试。

## 7. 时间和存档

- 默认目录：`%LOCALAPPDATA%\DesktopPet\`，不向源码目录或安装目录写用户状态。
- `settings.json` 保存设置；`state.json` 保存宠物、情绪、事件与位置；未来习惯数据单独存储。
- 每份文件带整数 `schemaVersion`。阶段 0 为 v1，实际结构如下：

  **state.json v1**（`PetStateArchive`，`src/DesktopPet.Core/Persistence/PetStateArchive.cs`）：
  - `schemaVersion` (int, 默认 1)：模式版本号。
  - `windowLeft` (double, 默认 100)：窗口左上角 X，WPF DIP 坐标。
  - `windowTop` (double, 默认 100)：窗口左上角 Y，WPF DIP 坐标。
  - `savedDeviceName` (string|null)：上次保存时宠物所在显示器设备名，用于恢复时检测显示器是否仍存在。
  - `lastSavedUtc` (string|null)：上次保存的 UTC 时间戳（ISO 8601 round-trip 格式）。
  - `isPaused` (bool, 默认 false)：是否处于暂停状态（主动行为停止）。

  **settings.json v1**（`PetSettingsArchive`，同文件）：
  - `schemaVersion` (int, 默认 1)：模式版本号。
  - `animationSpeed` (double, 默认 1.0)：动画速度倍率字段。阶段 0 可序列化/读取，但尚未接到实际动画速度，也没有设置界面；不能把数据往返测试当作调速功能通过。
  - `autoStart` (bool, 默认 false)：开机自启（P0-004 预留，未实现）。

  **恢复规则**：
  - 文件不存在时回退默认值，不报错。
  - JSON 解析失败视为损坏：原文件重命名为 `*.corrupted` 保留，优先尝试 `*.bak` 备份恢复，无备份则回退默认值。
  - `schemaVersion` 高于当前版本时建立该文件独立的只读保护；兼容字段尽力读取，类型不兼容则以默认运行数据继续，原未来版本文件不能归类为损坏。state/settings 的保护互不影响；正常自动保存会检查标记、DTO 和磁盘版本，避免替换对象或新建服务后降级。启动时发出托盘只读提示。
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

阶段 0 不要求资源加载器；程序绘制形状必须有明显身体/头部，便于命中测试。
阶段 1 加入角色 JSON：角色 ID、帧尺寸、脚点、交互区域、动作 ID、帧路径、每帧时长和循环方式。
坐标均为未缩放素材像素；用脚点固定切换动作时的位置，禁止动作变化导致角色脚底跳动。
最小动作集：idle、curious、dodge、drag；后续按任务增加。缺失动作回退 idle 并记录一次诊断，不崩溃。
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

## 9. 后期 Windows 能力边界

窗口栖息先验证枚举、可见外框、最小化/关闭与 Z 序，随后评估 `SetWinEventHook` 配合低频校验。
只关心外部顶层窗口的相关事件，过滤自身与子对象噪声；保留回调引用，退出时解除钩子。
HWND 可能失效或复用，不能把一个数值句柄当成永久窗口身份。
初版不获取 UAC 安全桌面信息、不要求管理员权限、不改第三方窗口父子关系。
来源：[SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)。

习惯统计先用本地频次模型，读取失败或样本不足时无预测。图标定位不属于窗口位置 API 自动提供的能力，另做验证任务。
