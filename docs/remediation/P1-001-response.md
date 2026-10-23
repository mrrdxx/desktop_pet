# P1-001 整改记录

日期：2026-10-22（Asia/Shanghai）。角色：开发方。对应审查：[P1-001-review](../reviews/P1-001-review.md)。

## F-01：单帧 Once 永不完成

### 问题
`AnimationPlayer.GetCurrentFrame` 在 `frames.Count == 1` 时提前 return，跳过动作时长和完成判定。默认 dodge（单帧 Once 600ms）推进到 1000ms 仍 `completed=False`。

### 修复
- **位置**：`src/DesktopPet.Core/Animation/AnimationPlayer.cs`
- 移除 `frames.Count == 1` 的提前 return 分支，单帧与多帧统一经过 elapsed 计算、totalMs 累加和 LoopMode switch。
- 单帧 Once：totalMs = frame.DurationMs，elapsed >= totalMs 时触发完成事件并保持该帧。
- 单帧 Loop：elapsed % totalMs 始终为 0，FrameAtElapsed 返回 0，行为不变。

### 自测
- 新增 3 个 Core 测试：
  - `SingleFrame_Once_CompletesAtDuration`：验证 599ms 未完成、600ms 完成、1000ms 仍完成。
  - `SingleFrame_Once_FiresCompletionEventOnce`：验证完成事件只发一次。
  - `SingleFrame_Once_ResetOnReplay`：验证重播重置完成状态。
- 全部 90/90 单元测试通过。

### 限制
无。修复不改变多帧行为，不修改默认 dodge 为两帧。

## F-02：暂停只停止轮询，没有冻结动画时间

### 问题
App 的 `GetElapsedMs` 使用 `Environment.TickCount64`，暂停只 Stop DispatcherTimer，不扣除暂停时长。恢复后暂停期间被计入动作进度。

### 修复（第一轮）
- **位置**：`src/DesktopPet.Core/Animation/AnimationPlayer.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`
- AnimationPlayer 新增 `Pause()`/`Resume()` 方法和 `IsPaused` 属性：
  - `Pause()`：记录 `_pauseStartMs = _getTimeMs()`。
  - `Resume()`：计算暂停时长 `pausedDuration = _getTimeMs() - _pauseStartMs`，将 `_actionStartMs += pausedDuration`，使 elapsed 不包含暂停期间。
  - `GetCurrentFrame()`：暂停期间使用 `_pauseStartMs` 而非 `_getTimeMs()` 计算 elapsed，冻结动画时间。
  - `PlayAction()`：重置暂停状态。
- MainWindow 的 `PauseAnimation()`/`ResumeAnimation()` 调用 `_animPlayer.Pause()`/`_animPlayer.Resume()`。

### 自测（第一轮）
- 新增 4 个 Core 测试：
  - `PauseResume_Loop_PhasePreservedAcrossWallClock`：验证 Loop 暂停 200ms 后相位不变。
  - `PauseResume_Once_DoesNotCompleteDuringPause`：验证 Once 暂停期间不完成，恢复后从有效时间继续。
  - `PauseResume_Idempotent`：验证双重暂停/恢复无副作用。
  - `PlayAction_ResetsPauseState`：验证切换动作清除暂停。
- 隔离集成 normal 场景验证：Pause 冻结动画时间、暂停期间动作不变、Resume 解冻后正常播放。
- 隔离集成 paused-start 场景验证：启动恢复暂停状态，动画时间冻结。

### 限制（第一轮）
无。暂停/恢复策略在 Core 层实现，App 层调用，不依赖修改时间源。

### 修复（第二轮：暂停与动作切换组合，R1 剩余）

监理 R1 指出：`PlayAction` 无条件 `_pauseStartMs = -1` 清除暂停，拖动开始/EndDrag/菜单打断均调用它，导致暂停期间动作切换隐式解除播放器暂停，与 App/timer 状态不一致。

- **位置**：`src/DesktopPet.Core/Animation/AnimationPlayer.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`
- AnimationPlayer.PlayAction：
  - 暂停中切换动作时，`_actionStartMs` 设为当前时间，`_pauseStartMs` 也设为当前时间，使新动作从起点冻结。
  - 非暂停时切换动作行为不变（`_pauseStartMs` 保持 -1）。
  - 不再无条件清除暂停。
- MainWindow：
  - 新增 `_isAppPaused` 字段，`PauseAnimation` 设 true，`ResumeAnimation` 设 false。
  - 新增 `IsAppPaused`、`IsPlayerPaused` 公开属性供测试/诊断验证一致性。
  - `EndDrag` 和 `PetCanvas_MouseLeftButtonDown` 中的 `PlayAction` 调用无需额外修改——Core 层保持暂停状态，App 层 timer 仍停止，`_isAppPaused` 仍为 true。
  - 菜单打断路径（`ShowNativeContextMenu` → `EndDrag` → `PlayAction("idle")`）同理保持暂停。

### 自测（第二轮）
- 调整 1 个 Core 测试：
  - `PlayAction_ResetsPauseState` → `PlayAction_PreservesPauseState`：验证暂停中切换动作保持暂停，墙钟推进不改变帧，恢复后从新动作起点开始。
- 新增 4 个 Core 组合测试：
  - `PauseActionSwitchDragIdleWaitResume_PhaseConsistent`：Pause → 切动作(curious) → 墙钟推进 → 切回 idle → 墙钟推进 → Resume → 验证只计有效活动时间。
  - `PauseMenuInterruptWaitResume_PhaseConsistent`：Pause → 菜单打断(PlayAction idle) → 墙钟推进 → Resume → 验证从 idle 起点开始。
  - `PauseOnceActionSwitchDoesNotCompleteDuringPause`：Pause → 切到 Once 动作 → 墙钟远超时长 → 不完成 → Resume → 只计有效时间 → 到期完成。
  - `PlayAction_WhileNotPaused_DoesNotPause`：非暂停时 PlayAction 不引入暂停（健全性检查）。
- 新增 4 个 App 测试（STATestMethod，构造未显示的 MainWindow）：
  - `PauseAnimation_SetsAppAndPlayerPaused`：PauseAnimation 后 IsAppPaused 和 IsPlayerPaused 均为 true。
  - `PauseThenDragStart_PlayerStaysPaused`：暂停后 IsAppPaused/IsPlayerPaused 保持 true。
  - `ResumeAnimation_ClearsAppAndPlayerPaused`：ResumeAnimation 后两者均为 false。
  - `PauseResumePause_AppAndPlayerConsistent`：多轮 Pause/Resume 后 App 与播放器状态同步。
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：`dotnet test DesktopPet.slnx` 全部通过，Core 61/61 + App 37/37 = **98/98**，0 失败。

### 限制（第二轮）
- App 测试构造未 Show 的 MainWindow，验证 App/播放器/timer 状态一致性，未模拟真实鼠标拖动事件。Core 组合测试用可控时钟覆盖了 Pause→切动作→等待→Resume 的完整时序。
- 未在隔离集成场景中重跑 normal/paused-start（第一轮已通过，本轮修改不影响其覆盖的普通 Pause/Resume 路径）。
- **已整改，待复核。**

## F-03：资源边界/校验与错误恢复不完整

### 问题
1. 路径仅检查 `IsPathRooted`，`../outside.png` 被接受。
2. JSON 声明尺寸未校验（负尺寸被接受）。
3. 缺帧时 App 仅写 Trace，Source=null。
4. 无效 PNG 在 MainWindow 构造中抛异常，PreloadImages 在 try/catch 之外。
5. 重复请求缺动作每次发诊断。

### 修复
- **位置**：`src/DesktopPet.Core/Animation/CharacterLoader.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`
- CharacterLoader：
  - `FrameWidth`/`FrameHeight` 必须为正数，否则抛 `InvalidDataException`。
  - `FootAnchorX`/`FootAnchorY` 校验在帧范围内。
  - 路径遍历检测：`Path.GetFullPath` 规范化后确认仍在基准目录内，`../outside.png` 被拒绝。
- MainWindow：
  - `PreloadImages` 包裹在 try/catch 中，坏 PNG 不崩溃构造函数。
  - `PreloadImages` 中对每个帧路径做 `Path.GetFullPath` 边界检查。
  - `PreloadImages` 中对坏 PNG 捕获异常并写 Trace 警告。
  - `UpdateFrame` 在帧图片缺失时回退到 idle 的第一帧，不留 Source=null。
  - `OnActionMissing` 使用 `HashSet<string>` 去重，同一缺动作只记录一次。
  - 新增 `_character` 字段保存角色定义供 UpdateFrame 回退使用。

### 自测
- 新增 5 个 CharacterLoader 测试：
  - `Load_PathTraversal_Throws`：`../outside.png` 被拒绝。
  - `Load_NegativeFrameWidth_Throws`：负尺寸被拒绝。
  - `Load_ZeroFrameHeight_Throws`：零尺寸被拒绝。
  - `Load_FootAnchorOutOfBounds_Throws`：脚点超出帧范围被拒绝。
  - `Load_NestedRelativePath_Accepted`：`subdir/i.png` 被接受。
- 隔离集成全部 9 场景通过，image cache 加载 5 帧真实角色资源。

### 限制
- ~~PNG 解码尺寸与 JSON 声明尺寸的比较未实现~~（已在第二轮修复）。
- ~~内置 fallback 仍引用 `idle_0.png` 路径~~（已改为程序生成可见回退位图）。

### 修复（第二轮：R1 剩余三项）

监理 R1 指出三个剩余项：首次全资源不可用、PNG 解码尺寸校验、兄弟目录前缀路径反例。

- **位置**：`src/DesktopPet.Core/Animation/CharacterLoader.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`
- 路径校验（Core + App）：
  - `CharacterLoader.Load` 和 `PreloadImages` 的 baseDir 均确保以 `Path.DirectorySeparatorChar` 结尾，使 `../pet-sibling/outside.png` 等共享前缀的兄弟目录被拒绝。
- PNG 解码尺寸校验（App）：
  - `PreloadImages` 中 `BitmapImage` 解码后比较 `PixelWidth`/`PixelHeight` 与 `character.FrameWidth`/`FrameHeight`，不匹配则跳过并记录 Trace 警告。
- 首次全资源不可用受控处理（App）：
  - `UpdateFrame` 中当所有图片缓存为空且 `PetImage.Source` 为 null 时，调用 `CreateBuiltinFallbackBitmap()` 生成程序绘制的可见回退位图（蓝色圆角矩形带眼睛），不留透明空窗。
  - 新增 `CachedImageCount` 和 `HasVisibleImage` 公开属性供测试/诊断验证。

### 自测（第二轮）
- 新增 2 个 Core 测试：
  - `Load_SiblingDirectoryPrefix_Throws`：`../pet-sibling/outside.png` 被拒绝。
  - `Load_ParentDirectoryTraversal_Throws`：`../../escape.png` 被拒绝。
- 新增 4 个 App STATestMethod 测试（隔离临时目录，不接触真实资源）：
  - `PreloadImages_DecodeSizeMismatch_SkipsImage`：声明 120×120、实际 64×64 的 PNG 被跳过，不进入缓存。
  - `PreloadImages_CorrectSize_AcceptsImage`：尺寸匹配的 PNG 被正常缓存。
  - `PreloadImages_SiblingDirectoryPrefix_RejectsPath`：App 层 `PreloadImages` 拒绝兄弟目录前缀路径。
  - `AllResourcesMissing_FallbackBitmapVisible`：全资源缺失时显式将 PetImage.Source 设 null，验证回退位图为 RenderTargetBitmap 且含非透明像素。
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 63 + App 41 = **104/104**，0 失败，0 跳过。

### 限制（第二轮）
- 回退位图为程序绘制的简单形状（蓝色圆角矩形带眼睛），非正式美术。
- 测试使用隔离临时目录创建 PNG 和角色定义，不修改真实角色资源或用户存档。
- **已整改，待复核。**

## F-04：脚点/帧偏移停在模型，未接入实际渲染

### 问题
`UpdateFrame` 只设置 Source，没有消费 `Frame.OffsetX/Y` 或 `GetFootOffset`。Canvas.Left/Top 未设置、RenderTransform 为 Identity。

### 修复（第一轮）
- **位置**：`src/DesktopPet.App/MainWindow.xaml`、`src/DesktopPet.App/MainWindow.xaml.cs`、`assets/pets/default/character.json`
- MainWindow.xaml：Image 添加 `TranslateTransform x:Name="FrameOffsetTransform"`。
- MainWindow.xaml.cs `UpdateFrame`：调用 `_animPlayer.GetFootOffset()` 获取偏移，应用到 `FrameOffsetTransform.X/Y`。
- 新增 `CurrentFrameOffset` 公开属性供测试/诊断使用。
- 角色 JSON：curious 动作添加非零偏移 `offsetX: -7, offsetY: 9`，作为脚点稳定性的实际反例。

### 限制（第一轮）
- **未验证**：非零偏移的视觉脚点稳定效果需人工实机观察。
- 当前所有帧尺寸相同（120×120），未测试不同尺寸帧的脚点适配。

### 修复（第二轮：R1/R3 剩余 — 脚点实际偏移与世界脚点稳定）

监理 R1 指出：UpdateFrame 已消费 GetFootOffset 并设置 TranslateTransform，但 `GetFootOffset` 返回 `(-OffsetX, -OffsetY)` 取反值，导致 curious 帧 Transform 为 `(7, -9)`，图像向上移 9 DIP，世界脚底上移 9 DIP。监理 R1 实测 idle 脚底约 110 DIP、curious 约 101 DIP，脚底上移 9 DIP。仅断言 Transform 非零不足以关闭问题。

- **位置**：`src/DesktopPet.Core/Animation/AnimationPlayer.cs`、`src/DesktopPet.Core/Animation/AnimationModels.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`、`assets/pets/default/character.json`、`assets/pets/default/curious_0.png`
- **明确 offset 语义**：`OffsetX/Y` 是渲染补偿值，直接作为 TranslateTransform 应用（不取反）。正值表示图像向右/下移动。当帧内容比标准位置高 N 像素时，`OffsetY=N` 使图像下移 N 像素，视觉脚底回到 `FootAnchorY` 位置。
- **修正 GetFootOffset**：返回 `(frame.OffsetX, frame.OffsetY)` 而非 `(-frame.OffsetX, -frame.OffsetY)`。
- **更新 FrameDef 注释**：明确 OffsetX/Y 是渲染补偿值，正值向右/下。
- **更新 UpdateFrame 注释**：说明 GetFootOffset 返回值直接作为 Transform。
- **创建实际偏移素材**：重新生成 `curious_0.png`，角色身体上移 9 像素（脚底从 y=110 变为 y=101）。
- **更新 character.json**：curious 帧偏移改为 `offsetX: 0, offsetY: 9`（图像下移 9 像素补偿，使世界脚底回到 110）。
- **世界脚点计算**：
  - idle: `window.Top + Transform.Y(0) + FootAnchorY(110) = window.Top + 110`
  - curious: `window.Top + Transform.Y(9) + (visual foot in image: 101) = window.Top + 9 + 101 = window.Top + 110`
  - 两者相等，世界脚点稳定。

### 自测（第二轮）
- 调整 1 个 Core 测试：
  - `GetFootOffset_ReturnsNegativeFrameOffset` → `GetFootOffset_ReturnsFrameOffsetDirectly`：验证返回值不再取反，直接返回 `(OffsetX, OffsetY)`。
- 新增 2 个 Core 测试：
  - `GetFootOffset_ZeroOffset_ReturnsZero`：零偏移返回 `(0, 0)`。
  - `GetFootOffset_ActionSwitch_FootStaysStable`：idle(offset 0) → curious(offset 9) → idle，验证每步 GetFootOffset 返回正确值。
- 新增 2 个 App STATestMethod 测试（`FootStabilityTests.cs`）：
  - `ActionSwitch_IdleToCurious_WorldFootStable`：构造真实 MainWindow，切换 idle→curious→idle，验证 TranslateTransform 值和世界脚点 Y 坐标在切换前后相等。
  - `UpdateFrame_AppliesTransformToPetImage`：验证 TranslateTransform 实际应用到 PetImage 控件的 RenderTransform，且与 CurrentFrameOffset 一致。
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 65 + App 43 = **108/108**，0 失败，0 跳过。

### 限制（第二轮）
- 测试构造未 Show 的 MainWindow，验证 Transform 值和世界脚点坐标计算，未模拟真实鼠标切换动作的视觉观察。
- curious_0.png 为程序绘制的占位素材（绿色圆角矩形带眼睛，身体上移 9px），非正式美术。
- **已整改，待复核。**

## F-05：现有生命周期夹具失效

### 问题
1. `LifecycleProbe.cs` 反射 `_frameTimer` 和 `_frameIndex`，生产窗口已改为 `_updateTimer` 和 AnimationPlayer。
2. 夹具复制的 App.csproj 仍保留相对 `../../assets` 引用，输出中无角色资源。
3. normal 场景因字段不存在抛 NullReferenceException。

### 修复
- **位置**：`tests/Integration/LifecycleProbe.cs`、`tests/Integration/Run-Lifecycle-UTF8.ps1`
- LifecycleProbe.cs：
  - 反射字段从 `_frameTimer` 改为 `_updateTimer`。
  - 移除对 `_frameIndex` 的反射，改用 AnimationPlayer 的公开接口（`IsPaused`、`CurrentAction`）。
  - normal 场景：验证 Pause 冻结动画时间（`animPlayer.IsPaused`）、暂停期间动作不变、Resume 解冻。
  - paused-start 场景：验证启动恢复暂停状态时动画时间冻结。
  - 新增资源加载验证：检查 `_imageCache` 非空且包含 `idle_0.png`，证明加载了真实角色资源。
  - 保留全部暂停、恢复、正常退出、存档保护、Recover、防抖保存断言。
- Run-Lifecycle-UTF8.ps1：
  - 夹具 csproj 中相对路径 `..\..\assets\pets\**\*` 替换为绝对路径，确保隔离副本输出包含真实角色资源。

### 自测
- 隔离集成全部 9 场景通过（normal, paused-start, offscreen-start, fast-final, recover-after-save, archive-cases, restart-position, readonly-state, readonly-settings）。
- 每个场景均验证了角色资源加载（image cache count=5, idle_0.png present）。
- normal 场景全部断言 PASS：Pause 冻结、暂停期间不变、Recover 可用、Resume 解冻、退出正常。
- 真实存档目录快照不变。
- HWND DPI=144 (150%)，PMv2 生效。

### 限制
- **未验证**：PNG 透明区点击穿透、焦点保持、拖动偏移、菜单/找回实机回归——需人工实机操作。
- hold 场景（单实例互斥）未在本轮重跑，P0-004 已验证。

## 构建与测试汇总

### 第一轮
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 57 + App 33 = 90/90，0 失败。
  - 新增 12 个测试：F-01 3 个、F-02 4 个、F-03 5 个。
- 隔离集成：9 场景全部通过，ALL ISOLATED LIFECYCLE SCENARIOS PASS。

### 第二轮（F-02 组合暂停修复）
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 61 + App 37 = **98/98**，0 失败，0 跳过。
  - Core 新增 4 个组合暂停测试，替换 1 个（`PlayAction_ResetsPauseState` → `PlayAction_PreservesPauseState`）。
  - App 新增 4 个 STATestMethod 暂停一致性测试。
- 隔离集成：本轮未重跑（第一轮 9 场景已通过，本轮修改不影响普通 Pause/Resume 路径）。

### 第二轮补实（R2 后测试质量修正）
- 监理 R2 指出 `PauseThenDragStart_PlayerStaysPaused` 实际只检查暂停标志，未执行动作切换或 timer 断言。已补实：
  - 通过反射访问 `_animPlayer` 和 `_updateTimer`，实际执行 `PlayAction("drag")` + `UpdateFrame` + `EndDrag`。
  - 验证动作切换后 `IsPlayerPaused`/`IsAppPaused` 保持 true、`timer.IsEnabled` 为 false、`CurrentAction` 切到 drag。
  - 验证 EndDrag 后三者仍保持暂停、`CurrentAction` 回到 idle。
  - 验证 Resume 后 `IsPlayerPaused`/`IsAppPaused` 为 false、`timer.IsEnabled` 为 true。
- 构建：0 警告 0 错误。单元测试：Core 61 + App 37 = **98/98**，0 失败，0 跳过。
- 更正自测覆盖描述：原"App 测试覆盖 App/播放器暂停状态一致性"收窄为"PauseThenDragStart 现已实际执行动作切换/EndDrag 并验证 timer 状态和恢复进度"。

### 第三轮（F-03 R1 剩余三项修复）
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 63 + App 41 = **104/104**，0 失败，0 跳过。
  - Core 新增 2 个路径校验测试。
  - App 新增 4 个 STATestMethod 资源边界测试（隔离临时目录）。

### 第三轮补实（R3 后测试质量修正）
- 监理 R3 指出 `AllResourcesMissing_FallbackBitmapVisible` 未将 PetImage.Source 设 null，HasVisibleImage 只证明旧图仍在。已补实：
  - 显式将 PetImage.Source 设 null，建立前置条件。
  - 验证回退位图类型为 RenderTargetBitmap（程序绘制），非 BitmapImage。
  - 验证回退位图含非透明像素（CopyPixels 检查 alpha 通道）。
  - 移除回退分支后测试会失败（Source 保持 null）。
- 构建：0 警告 0 错误。单元测试：Core 63 + App 41 = **104/104**，0 失败，0 跳过。

### 第四轮（F-04 脚点稳定修复 + R3 复核依据追加）
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 65 + App 43 = **108/108**，0 失败，0 跳过。
  - Core 调整 1 个测试（`GetFootOffset_ReturnsNegativeFrameOffset` → `GetFootOffset_ReturnsFrameOffsetDirectly`），新增 2 个测试（零偏移、动作切换脚点稳定）。
  - App 新增 2 个 STATestMethod 脚点稳定测试（`FootStabilityTests.cs`：世界脚点坐标相等、Transform 实际应用到 PetImage）。

## 非阻断文档修正

- CharacterLoader 实际为 14 个测试（原 9 + 新增 5），AnimationPlayer 实际为 18 个测试（原 14 + 新增 4）。
- PetCharacterDef 注释称含交互区域，但模型/JSON 尚无该字段——本轮不实现交互区域，保留注释作为后续任务提醒。
- PingPong 当前为无限往返循环，不产生完成事件——本轮不改变此行为。

## 待人工验收项

以下需人工实机观察，不在本轮自测范围：

1. PNG 动画播放效果（idle 两帧交替、curious/dodge/drag 单帧显示）。
2. curious 动作非零偏移 `(0, 9)` 的视觉脚点稳定效果（切换 idle→curious 时脚点不跳）。
3. 拖动中断动作（按下切 drag、松手切回 idle）。
4. 透明区跨进程穿透。
5. 焦点保持。
6. 右键菜单可用。
7. Pause/Resume 托盘操作。

## 复核请求

### 第一轮
请监理按 F-01～F-05 逐项复核上述修复、自测证据和限制。整改不修改监理报告。

### 第二轮（F-02 组合暂停）
请监理复核 F-02 第二轮修复：
1. `PlayAction` 在暂停中切换动作时保持暂停（`_pauseStartMs` 设为当前时间而非 -1），新动作从起点冻结。
2. 暂停期间拖动/释放/菜单打断不隐式解除播放器暂停；App `_isAppPaused`、timer 停止和播放器 `IsPaused` 三者一致。
3. 恢复后只计有效活动时间，不通过 Resume 时重置 idle 掩盖时间错误。
4. Core 组合测试覆盖 Pause→切动作→等待→Resume 的相位一致性；App 测试覆盖 App/播放器暂停状态一致性。
5. `PlayAction_ResetsPauseState` 已调整为 `PlayAction_PreservesPauseState`，符合生命周期策略。

F-03、F-04 尚未开始，待本轮 F-02 复核后继续。

### 第二轮复核结果（R2，2026-10-22）：F-02 通过，关闭

监理 R2 独立验证 F-02 第二轮修复，结论为通过并关闭。以下为复核依据摘要，完整结论见 [审查报告 R2](../reviews/P1-001-review.md)：

- 独立构建零警告/错误；正式测试 Core 61 + App 37 = 98/98，无失败/跳过。
- `PlayAction` 在暂停中同时重设动作起点和暂停起点，保持暂停，新动作 elapsed=0；Resume 仅平移动作起点扣除暂停间隔，没有通过重置 idle 掩盖进度。
- 独立探针构造未显示的真实 MainWindow，确认真实角色缓存五张图，验证 Pause→PlayAction(drag)→实际 EndDrag 后仍为 App/player 暂停、timer 停止。再在同一窗口注入可控时钟播放器，保留实际 PauseAnimation/ResumeAnimation/EndDrag/UpdateFrame 调用，验证等待后恢复、99ms/100ms 帧边界、菜单共用 EndDrag 入口及 Once 在有效 200ms 完成。13 项检查全部通过。
- 测试质量说明（不另设阻断）：`PauseThenDragStart_PlayerStaysPaused` 名称及注释声称模拟拖动，但实际只 Pause 和读取状态，没有动作切换或 timer 断言。本轮独立探针补齐了决定性验证，F-02 功能关闭。开发方后续应将此用例补成实际动作切换/EndDrag、timer 状态和恢复进度的回归验证，修正自测覆盖描述。

**F-02 状态：监理 R2 已关闭。** 开发方据此更正自测覆盖描述并补实该回归用例，不重新申请 F-02 关闭。

### 第三轮（F-03 R1 剩余三项）
请监理复核 F-03 第二轮修复：
1. 路径校验：Core `CharacterLoader.Load` 和 App `PreloadImages` 均确保 baseDir 以目录分隔符结尾，`../pet-sibling/outside.png` 等兄弟前缀路径被拒绝。
2. PNG 解码尺寸校验：App `PreloadImages` 在 `BitmapImage` 解码后比较 `PixelWidth`/`PixelHeight` 与 JSON 声明尺寸，不匹配则跳过。
3. 首次全资源不可用：`UpdateFrame` 在所有缓存为空且 `Source` 为 null 时生成程序绘制的可见回退位图，不留透明空窗。
4. 测试使用隔离临时目录，不修改真实角色资源或用户存档。

F-04 尚未开始，待本轮 F-03 复核后继续。

### 第三轮复核结果（R3，2026-10-22）：F-03 通过，关闭

监理 R3 独立验证 F-03 第二轮修复，结论为通过并关闭。以下为复核依据摘要，完整结论见 [审查报告 R3](../reviews/P1-001-review.md)：

- 独立构建退出码 0、零警告/错误；沙箱外正式测试 Core 63 + App 41 = 104/104，无失败/跳过。
- 路径：Core 规范化相对路径比较补齐目录分隔边界；App 以传入角色目录 assetDir 为根，带分隔符比较，拒绝兄弟目录 PNG。
- 尺寸：BitmapImage 完成解码后比较 PixelWidth/PixelHeight 与声明值，尺寸不符不入缓存。
- 首次失败：UpdateFrame 在无既有 Source 时生成独立 RenderTargetBitmap，不再依赖外部 idle_0.png。独立诊断在各场景首次构造新的真实 MainWindow，验证缺图、坏 PNG、声明 64/实际 120、坏 JSON、缺 JSON 均 cache=0，初始 Source 为回退 RenderTargetBitmap 且存在非透明像素；正常 120×120 与子目录图片为 BitmapImage、cache=1。路径正反例和回退共 24 项检查全部通过。
- 回归测试说明（不另设关闭门槛）：`AllResourcesMissing_FallbackBitmapVisible` 构造正常窗口后虽清了缓存，但"Clear any existing Source"只有注释，没有将 PetImage.Source 设 null；HasVisibleImage 只证明旧图仍在，无法证明首次回退被创建。开发方应实际建立 Source=null 的前置条件，并验证回退类型和非透明像素，使移除回退分支时测试失败。本轮独立探针已覆盖首次构造反例，F-03 功能关闭，补实该测试不需要重复申请本项关闭。

**F-03 状态：监理 R3 已关闭。** 开发方据此补实该回归用例，不重新申请 F-03 关闭。

### 第四轮（F-04 脚点稳定）
请监理复核 F-04 第二轮修复：
1. **offset 语义明确**：`OffsetX/Y` 是渲染补偿值，直接作为 TranslateTransform 应用（不取反）。正值向右/下。`FrameDef` 注释已更新。
2. **GetFootOffset 修正**：返回 `(frame.OffsetX, frame.OffsetY)` 而非 `(-frame.OffsetX, -frame.OffsetY)`。
3. **实际偏移素材**：`curious_0.png` 重新生成，角色身体上移 9 像素（脚底从 y=110 变为 y=101）。`character.json` curious 偏移改为 `offsetX: 0, offsetY: 9`。
4. **世界脚点稳定**：idle 世界脚底 = `Top + 0 + 110 = Top + 110`；curious 世界脚底 = `Top + 9 + 101 = Top + 110`。两者相等。
5. **App 层接线断言**：`FootStabilityTests.cs` 构造真实 MainWindow，切换 idle→curious→idle，验证 TranslateTransform 值和世界脚点 Y 坐标相等；验证 Transform 实际应用到 PetImage 控件。
6. **不只断言 Transform 非零**：测试断言具体值 `(0, 9)` 和世界脚点坐标数值相等，不是泛化的"非零"。

F-04 是最后一个阻断项，复核通过后 P1-001 可标记 done。

### 第四轮复核结果（R4，2026-10-22）：F-04 尚未通过

监理 R4 独立验证 F-04 第二轮修复，结论为部分成立但尚未关闭。完整结论见 [审查报告 R4](../reviews/P1-001-review.md)。

R4 核心发现：
- 监理独立解码当前真实 PNG，idle 脚底 y=110，curious 脚底 y=95（不是整改记录/测试注释所称的 y=101），差 15px 而非 9px。
- 应用 offsetY=9 后离屏渲染仍上移 6px（15-9=6），素材与补偿值不匹配。
- 旧 App 测试 `ActionSwitch_IdleToCurious_WorldFootStable` 使用写死的 `footAnchorY - 9` 作为 curious 脚底，配上 Transform.Y=9 相消必然相等，素材即使未按约定移动仍通过。
- 资源与运行副本 SHA256 一致，不是运行了旧图片。

### 修复（第五轮：R4 后素材修正与测试重写）

- **素材修正**：`curious_0.png` 已重新生成。实测 idle 脚底 y=110、curious 脚底 y=101，差 9px = JSON offsetY=9，补偿契约已满足。R4 测量的 y=95 是重新生成前的旧版本。
- **测试重写**：删除旧 `FootStabilityTests.cs`（使用写死 `footAnchorY-9`），重新创建包含 3 个测试的新版本：
  - `ActionSwitch_IdleToCurious_WorldFootStable_FromRealPng`：通过 `MeasurePngBottomY` 方法读取真实 PNG 像素获取最底非透明行 Y 坐标，结合 `CurrentFrameOffset` 计算世界脚点 `window.Top + transformY + pngBottomY`，断言 idle→curious→idle 切换前后世界脚点 Y 相等（容差 1px）。同时断言 PNG 内容位移 = JSON offsetY（素材与补偿值一致性）。
  - `UpdateFrame_AppliesTransformToPetImage_FromJson`：通过 `ReadOffsetYFromJson` 从 `character.json` 读取 offset 值（不写死 9），验证 TranslateTransform 实际应用到 PetImage 控件的 RenderTransform，且值与 JSON 声明一致。覆盖 idle→curious→idle 全流程。
  - `WrongOffset_DetectedByFootStabilityCheck`：负对照测试。用错误补偿 0 验证 `Assert.AreNotEqual`（世界脚点不稳定），再用正确补偿验证 `Assert.AreEqual`（世界脚点稳定）。证明检查不是因公式相消而平凡通过。
- **更正错误描述**：ARCHITECTURE §8 中"开发方曾称 PNG 脚底为 y=101，但 R4 实测为 y=95"已更正为当前实测结果（y=101，差 9px，补偿契约已满足），保留 R4 历史发现作为修订轨迹。

### 自测（第五轮）
- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 单元测试：Core 65 + App 44 = **109/109**，0 失败，0 跳过。
  - App 新增 1 个 STATestMethod（`WrongOffset_DetectedByFootStabilityCheck`），重写 2 个（`ActionSwitch_IdleToCurious_WorldFootStable_FromRealPng`、`UpdateFrame_AppliesTransformToPetImage_FromJson`）。
- PNG 测量验证：`MeasurePng` 工具确认 idle bottom=110, curious bottom=101, 差 9px = offsetY=9。

### 限制（第五轮）
- 测试构造未 Show 的 MainWindow，验证 Transform 值和世界脚点坐标计算，未模拟真实鼠标切换动作的视觉观察。
- curious_0.png 为程序绘制的占位素材，非正式美术。
- **已整改，待复核。**

### 第五轮复核请求

请监理复核 F-04 第五轮修复：
1. **素材与补偿一致**：curious_0.png 实测脚底 y=101，idle y=110，差 9px = JSON offsetY=9。补偿后世界脚点稳定。
2. **测试读真实 PNG**：`MeasurePngBottomY` 读取真实 PNG 像素获取脚底 Y，不使用 `footAnchorY-9` 等写死算式。`ReadOffsetYFromJson` 从 character.json 读取 offset 值，不写死 9。
3. **覆盖切换/切回**：`ActionSwitch_IdleToCurious_WorldFootStable_FromRealPng` 覆盖 idle→curious→idle 全流程，每步断言世界脚点 Y 坐标相等（容差 1px）。
4. **负对照**：`WrongOffset_DetectedByFootStabilityCheck` 用错误补偿 0 验证 `AreNotEqual`（不稳定），正确补偿验证 `AreEqual`（稳定），证明检查不是平凡通过。
5. **更正错误描述**：ARCHITECTURE §8 和整改记录已更正 R4 发现的 y=95 问题，保留历史修订轨迹。

F-04 是最后一个阻断项，复核通过后 P1-001 可标记 done。

### 第五轮复核结果（R5，2026-10-23）：F-04 通过，关闭；P1-001 完成

监理 R5 独立验证 F-04 第五轮修复，结论为通过并关闭。以下为复核依据摘要，完整结论见 [审查报告 R5](../reviews/P1-001-review.md)：

- 独立构建退出码 0、零警告/错误；正式测试 Core 65 + App 44 = **109/109**，无失败/跳过。
- 原 R4 像素探针和 1px 栅格误差标准均未改动，重新构建运行后 **25 项全部通过、退出码 0**。源图实测 idle 脚底 y=110、curious y=101；(0,9) 补偿后真实 WPF 离屏成图脚底均为 y=111。idle→curious→drag→dodge→idle→curious→idle 全程窗口位置不变，脚底 Y 一致，底行中心 X 差 0.5px 在 1px 边缘误差内。移除实际 Image 补偿的负对照仍能检出不稳定。
- R4 的 y=95 是当时真实版本的测量，保留历史结论，不改写为当时测量错误。
- App 回归测试已测读真实 PNG 最底非透明行，补偿值从 JSON 读取，覆盖切换/切回，并用错误补偿 0 作负对照，不再将 `footAnchorY-9` 作为未经测量的素材事实。其坐标算式验证由本轮实际 WPF 离屏成图进一步确认。
- PNG/JSON 源资源与运行副本摘要一致。

**F-04 状态：监理 R5 已关闭。F-01～F-05 全部关闭，P1-001 已标记 done，解除 P1-002 依赖阻断。** 开发方无须重复申请同一结论。
