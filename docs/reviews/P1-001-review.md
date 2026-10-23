# P1-001 动画资源与行为协调审查

日期：2026-10-22（Asia/Shanghai）。角色：项目监理，本轮未参与该实现。
结论：**部分实现成立，F-01～F-05 阻断 P1-001 完成及 P1-002 开始。** 构建和 78 项单元测试通过不能替代下列反例与 App 接线验收。
开发方按 [REVIEW_PROCESS](../REVIEW_PROCESS.md) 在 `docs/remediation/P1-001-response.md` 逐项整改和请求复核，不修改本报告。本轮没有修改应用、角色资源或开发测试源码。

## 审查范围及版本

- 对照 [TASKS](../TASKS.md) 的 P1-001、ARCHITECTURE §5/8，阅读动画模型/加载器/播放器、新 MainWindow、角色 JSON、两份动画测试及阶段 0 隔离入口。
- 无 Git，相关源码、资源定义和探针摘要保存在 [版本清单](evidence/P1-001-versions.txt)。MainWindow.xaml.cs 为 `4F88F8C57F0820EC09390FE44BBFFFFE56570A8B1A4D0720E1F1F3F096A8B48D`，AnimationPlayer.cs 为 `6ACA1565343A0540DEF2582AE215ACFC9EEC003EA11B0BB02930855AC125541B`。
- 本次真实运行：生产构建退出码 0、零警告/错误；正式 VSTest Core 45 + App 33 = **78/78** 通过；[构建输出](evidence/P1-001-build.txt)、[测试输出](evidence/P1-001-tests.txt)。测试在沙箱外执行，未改测试或删除断言。
- 使用当前真实 Core DLL、可控时钟执行定点反例：[输出](evidence/P1-001-targeted-probe.txt)、[探针源码](evidence/P1-001-Probe.ps1)。
- 另建仅位于 artifacts 的 WPF 诊断程序，引用当前 App，仅构造未显示的 MainWindow，用专属输出目录的 JSON/图片；未实例化生产 App、未读取真实存档。验证缺图、坏图及偏移渲染：[结果](evidence/P1-001-ui-probe.txt)、[源码](evidence/P1-001-UiProbe.cs.txt)、[项目配置留存](evidence/P1-001-UiProbe.csproj.txt)。源码留存用于追溯，不是可直接脱离原目录运行的项目。辅助程序初次编译的字符串语法错误修正后构建通过，不计为产品缺陷。
- 实际运行现有 UTF8 隔离入口的 normal 场景，驱动退出码 1、场景退出码 2，原存档快照不变；[完整输出](evidence/P1-001-existing-integration.txt)。没有重跑其他注定受同一失效字段影响的场景。
- 未亲自执行当前 PNG 版本透明区点击、焦点/拖动、托盘真实点击及人眼脚点观察；不把无显示窗口的诊断说成视觉验收。

## 已成立的部分

- Core 动画代码与 App 分离，支持注入时间源；普通多帧 Loop/Once 和现有 PingPong 测试通过。
- 有五张占位 PNG 和 idle/curious/dodge/drag JSON，App 预加载、缓存并按图像路径改变 Image.Source；不是每帧重新读盘。
- 拖起/释放已经接入 PlayAction(drag/idle)，原 Win32 非激活、菜单和位置适配主要链路保留。
- 本任务不要求正式美术，也不要求提前实现鼠标躲避触发、情绪、物理或社交。

## F-01：单帧 Once 永不完成（P1，阻断）

- 位置：`AnimationPlayer.GetCurrentFrame` 约 102 行，`frames.Count == 1` 提前 return。
- 默认角色的 dodge 正是单帧 Once、600ms。真实 Core 反例推进到 1000ms，仍 `completed=False; completionEvents=0`。因此 App 声称的 Once 完成回 idle 对该实际样例不会触发。
- 现有 Once 测试使用两帧；单帧测试仅使用 Loop，未覆盖这条交叉路径。
- 整改目标：单帧与多帧统一经过动作时长和完成判定；Once 到期后完成事件只发一次，切换动作可重置，不能靠把默认 dodge 改成两帧绕开。
- 验收：固定时钟验证单帧 Once 在阈值前/等于阈值/阈值后及重复查询的完成状态；App 播放当前 dodge 能按约定回 idle。无需提前实现鼠标触发躲避。

## F-02：暂停只停止轮询，没有冻结动画时间（P2，阻断）

- 位置：MainWindow 的 GetElapsedMs、PauseAnimation/ResumeAnimation（约 127、579、587 行）。播放器使用 Environment.TickCount64，暂停只 Stop DispatcherTimer，不扣除暂停时长。
- 真实 Core 的相同时钟策略反例：idle 进行到 750ms 为 idle_0；停止查询并让时钟走到 950ms，再查询已为 idle_1。App 的暂停路径没有提供任何时钟修正，因此恢复会把暂停期间计入动作进度；长暂停还会使多帧 Once 在恢复首 tick 立即完成。
- 此处是可控时钟反例加 App 接线核对，不冒充已进行真实鼠标计时操作。架构要求暂停/恢复不累计行为时间或补演积压，不能用“暂停时画面没变”替代恢复进度正确。
- 整改目标：让播放器消费有效活动时间或明确的暂停时间模型，暂停/恢复和暂停期间用户拖动/动作中断的处理一致；不要只在 Resume 时随意重新开始 idle 掩盖问题。
- 验收：假时钟下验证 Loop/Once 暂停任意墙钟时间后相位不变，恢复后从有效时间继续；App 暂停/恢复使用该策略，启动为暂停时也适用。

## F-03：资源边界/校验与错误恢复不完整（P1，阻断）

- 位置：CharacterLoader.Load（尺寸、脚点、路径和时长）及 MainWindow.PreloadImages/LoadCharacter/CreateBuiltinFallbackCharacter。
- 路径仅检查 IsPathRooted，`../outside.png` 被实际加载器接受，之后 Path.Combine 可以走到角色包之外，不符合“仅包内相对路径”。应在最终资源解析边界规范化并确认仍位于角色目录，Core 元数据校验与 App IO 各负其责。
- JSON 声明尺寸未与解码 PNG 比较。定点输入显式负尺寸并缺省脚点时，结果尺寸被改为 120×120，脚点却为 `(-20,-90)`，说明默认计算仍使用未规范化 DTO；非法定义被接受。
- 缺少实际帧文件时，App 仅写 Trace，第一次渲染 Source=null；诊断构造成功却没有任何可见图像。所谓内置 fallback 仍依赖外部 idle_0.png，不能覆盖整个资源目录缺失的情况。
- 无效 PNG 的真实诊断在 MainWindow 构造中抛出 `System.NotSupportedException`（未找到图像处理组件），PreloadImages 位于加载 JSON 的 try/catch 之外，没有有控制的失败策略。不要把它描述成坏资源已被加载器拒绝。
- 整改目标：明确缺省与非法元数据策略，校验尺寸/时长/脚点；验证文件、解码尺寸及路径边界。加载失败有可诊断且可控的行为：例如可见的独立回退资源，或清楚提示后正常退出，不能静默留下全透明宠物或未处理异常。方案按现有职责实现，不新增通用资源框架。
- 验收：合法包、包外路径、缺图、坏 PNG、尺寸不匹配、非法尺寸/时长及缺省脚点等具有区分度用例；隔离副本运行，不能通过修改真实工作目录资源测试。缺失动作应回 idle；重复请求同一缺动作当前会每次发诊断，按 §8“记录一次”去重或明确定义限制。

## F-04：脚点/帧偏移停在模型，未接入实际渲染（P2，阻断）

- 位置：AnimationPlayer.GetFootOffset、MainWindow.UpdateFrame（约 403 行）、MainWindow.xaml 的固定 120×120 Image。
- UpdateFrame 只设置 Source，没有消费 Frame.OffsetX/Y、FootAnchorX/Y 或 GetFootOffset。实际 WPF 诊断中模型要求补偿 `(-7,9)`，但 Canvas.Left/Top 均未设置、RenderTransform 为 Identity、Margin 为零；无位置适配。
- 现有脚点测试只证明 GetFootOffset 返回负偏移，不证明 App 使用了结果；默认素材全零偏移且相同尺寸，人工看“不跳”不足以验证资源格式所承诺的脚点稳定。
- 整改目标：定义素材坐标、脚点、局部帧偏移与窗口的对应关系，并把该关系接入实际渲染。动画局部表现不能直接变成世界坐标写入，不能靠忽略资源字段声称支持。若只支持固定帧尺寸，明确并校验，不静默接受其他尺寸。
- 验收：至少一个具有实际非零偏移/不同视觉脚点的资源反例，证明动作切换时世界脚点稳定且局部布局符合定义；App 层有接线断言或可重复观察步骤，不能只测 Core 的返回值。

## F-05：现有生命周期夹具失效，当前版本缺少必要回归证据（验证缺口，阻断）

- `tests/Integration/LifecycleProbe.cs` 仍反射 `_frameTimer` 和 `_frameIndex`，生产窗口已改为 `_updateTimer` 和 AnimationPlayer。实际 normal 场景在第 56 行因字段不存在抛 NullReferenceException，场景退出码 2；不是应用 Pause 本身失败的证明。
- 夹具复制的 App.csproj 仍保留相对 `../../assets` 引用，基于 artifacts 中的副本目录解析后，输出中无 `assets/pets/default/character.json`（本次目录 run-06048555）。若只修复字段反射，仍可能在无实际 PNG 资源的副本上测试。
- 旧 55 项单元测试保持通过是真的，但它们不验证 PNG 透明输入或新动画生命周期。因此 STATUS 的“阶段 0 代码逻辑未修改/未受影响”应收窄为实际覆盖结论。
- 整改目标：让夹具使用当前资源复制规则，并校验实际加载的是预期角色/帧；更新观察手段以测真实播放器/画面状态。不得删除暂停、恢复、正常退出或存档保护断言，也不能改回旧窗口实现。
- 验收：当前版本完整隔离场景通过，证据注明实际 HWND DPI/源码/资源版本；再对 PNG 实体拖动、透明区跨进程穿透、焦点保持、菜单/找回及 Pause/Resume 做必要的实机回归。只补受渲染/时钟改变影响的范围，不要求重做物理或可选多屏。
- 历史 P0-005 结论适用于旧版本，本轮不改写历史或自动重开所有 P0 任务；新版本回归作为 P1-001 的完成条件。

## 非阻断文档修正

- 按实际源码统计为 CharacterLoader 9 个测试、AnimationPlayer 14 个，合计仍为 23；不是报告中的 8+15，修正文档即可。
- PetCharacterDef 注释称含交互区域，但模型/JSON 尚无该字段。按 §8 补最小区域定义或明确尚未实现并在相关任务前落实，不能宣称已支持头部/身体资源命中；本轮不要求实现抚摸业务。
- PingPong 当前为无限往返循环，不产生完成事件；文档里“Once/PingPong 完成”与实现不符，应统一约定，不因注释额外扩展播放模式。

## 接续

P1-001 保持 awaiting-verification；开发方按问题分小步整改、自测并同步权威文档，再提交复核。先不用用户重复当前五步人工验收，修复后集中验证实际可见行为。
缺少条件时明确记未验证；不提前进入 P1-002，不把已有 78/78 写成上述新边界均通过。

## 复核 R1（2026-10-22）：部分通过

### 用户人工反馈及本轮方法

- 用户对开发方提供的七项清单明确反馈：**“我验证了这七项均通过”**。记录 PNG 切帧、非零偏移视觉、拖动/释放、透明区跨进程、焦点、原生菜单与托盘 Pause/Resume 为用户实测反馈，不冒充监理亲自操作，也不作为资源异常输入或暂停组合场景的豁免。
- 本轮读取整改记录和真实修改，重跑生产构建及正式测试：0 警告/错误，**90/90**（Core 57、App 33）通过；[构建](evidence/P1-001-R1-build.txt)、[测试](evidence/P1-001-R1-tests.txt)。
- 当前版本见 [R1 摘要](evidence/P1-001-R1-versions.txt)，包括默认 JSON 和用于脚底对照的两张 PNG。本轮未改生产或测试源码。
- [Core 定点反例](evidence/P1-001-R1-core-probe.txt) 使用真实 DLL/可控时钟；[未显示窗口诊断](evidence/P1-001-R1-ui-probe.txt) 引用当前 App，用专属资源副本，未创建生产 App 或接触真实存档。探针源码：[Core](evidence/P1-001-R1-Probe.ps1)、[WPF](evidence/P1-001-R1-UiProbe.cs.txt)、[项目](evidence/P1-001-R1-UiProbe.csproj.txt)。

### F-01：通过，关闭

已移除单帧提前 return，统一经过时长和完成判断；新增阈值/事件一次/重播测试通过。默认单帧 dodge 推进到 1000ms，独立探针得到 completed=True、completionEvents=1。App 原有完成回 idle 接线保留，不要求重做多帧既有测试。

### F-02：普通暂停通过，暂停与动作切换组合未通过，尚未关闭

- 普通 Pause/Resume 已正确冻结时间，Core 750ms 暂停、墙钟走到 950ms 再恢复仍为同一帧，用户普通托盘操作也通过。
- 但 PlayAction 无条件 `_pauseStartMs=-1`，会清除生命周期暂停。MainWindow 拖动开始、EndDrag 均调用它；打开本体菜单也会调用 EndDrag。App 的暂停状态/停止的 timer 未随之解除，播放器却已取消暂停。
- 实际 App 诊断：PauseAnimation 后调用与拖动开始相同的 PlayAction(drag)/UpdateFrame，`playerPausedBefore=True; playerPausedAfter=False; timerEnabled=False`。恢复时 Resume 因播放器不处于暂停而不扣除墙钟间隔，新动作仍会跳进度。
- 原 F-02 已明确要求暂停期间用户拖动/中断处理一致；这不是新增需求。现有 `PlayAction_ResetsPauseState` 测试将该缺陷当成预期，需调整为符合生命周期策略的断言，不是为了清除失败删测试。
- 剩余：动作切换不隐式解除用户暂停，定义暂停中切动作的起点；覆盖 Pause→拖动并释放/菜单打断→等待→Resume 的相位及 App/播放器一致性。普通 Pause/Resume 已通过部分保留。

### F-03：部分修复，尚未关闭

- 显式非法尺寸/脚点部分已拒绝，普通 `../outside.png` 被拒绝，坏 PNG 构造不再直接抛出，App 缺动作日志有去重。
- 初次缺图、坏 idle 或所有图片不可用时仍是 `Source=null`。原“Source 保持上一帧而非 null”只适用于此前已经显示过有效帧，不能用于启动；WPF 诊断再次确认缺图/坏 PNG 均没有图像。需要独立可见回退或明确提示并正常退出，不能只吞异常。
- 声明 64×64、实际 PNG 120×120 的包仍被 App 接受。开发方已注明未实现，理由“Core 不能使用 WPF”不阻止在 App 解码后比较 PixelWidth/PixelHeight；这是原 F-03 的验收目标，不作隐式延期。
- 路径使用 `StartsWith(baseDir)` 未校验目录分隔边界。真实 Core 探针的 `../pet-sibling/outside.png` 仍被接受，因为兄弟路径也以 pet 字符串开头；App 也使用相同前缀判断。应基于实际资源根目录和带分隔符的规范路径判断，不把当前工作目录当作角色根目录。
- 剩余：补首次启动全资源不可用的受控策略、解码尺寸校验、兄弟前缀路径反例。使用隔离副本；已通过的 JSON 基本结构测试不重复扩大。

### F-04：已接线，但脚点仍移动，尚未关闭

- UpdateFrame 现在消费 GetFootOffset 并设置 TranslateTransform，这部分成立。用户报告视觉认为通过，予以保留；但仅加平移并给 JSON 填非零数值，不自动证明世界脚点固定。
- 本轮读取实际 idle_0/curious_0 PNG，并在未显示的真实 MainWindow 切换 idle→curious，按两张图的不透明最底行（当前素材的脚底）及图像 Transform 计算渲染位置：idle 约 110.015 DIP，curious 约 101.015 DIP，**脚底上移 9 DIP**，窗口 Top 始终为 100。两张原始 PNG 的脚底原本同一行；新增 `(7,-9)` 平移直接移动了脚。
- 这是可重复的像素/布局反例，不是要求用户再凭眼睛确认细小偏移。GetFootOffset 返回负 offset 和 Transform 数值不为零仍不足以关闭问题。
- 剩余：明确 offset 描述的是素材实际偏移还是表现位移，令素材、脚点元数据和局部布局一致；提供具有实际偏移的素材反例，断言切换后的世界脚点坐标相等。不要只改验收说法、删除非零反例，或移动世界窗口掩盖局部布局问题。

### F-05：通过，关闭

- 夹具已改到 `_updateTimer`/AnimationPlayer，并修正资产绝对来源；独立执行新版完整 **十场景**（含 hold）全部通过，驱动退出码 0，真实存档摘要前后相同。每场景实际缓存五张角色图；[完整输出](evidence/P1-001-R1-lifecycle.txt)，目录 run-55af38e3，当前 HWND DPI=144。
- 结合本轮用户七项普通桌面反馈，原夹具失效与 PNG 普通交互回归缺口关闭。其通过不覆盖 F-02 的组合场景、F-03 的坏资源或 F-04 的精确脚点反例。

### 当前结论与最小接续

**F-01、F-05 已关闭；F-02、F-03、F-04 仍阻断 P1-001 完成和 P1-002 开始。** TASKS 保持 awaiting-verification。
用户已完成的普通七项反馈不要求全量重复。开发方只处理剩余组合/异常/几何问题，在整改记录追加修复与针对性证据；必要视觉验证待实际改动后按影响范围进行。P0 历史通过结论不改写。

## 复核 R2（2026-10-22）：F-02 通过，关闭

### 对象、方法和结果

- 本轮仅复核 F-02 第二轮修复，不重审已关闭的 F-01/F-05，不改生产代码、资源或开发测试。当前摘要见 [版本清单](evidence/P1-001-R2-F02-versions.txt)。
- 独立执行 `dotnet build DesktopPet.slnx -c Debug --no-restore -m:1 --nologo -v minimal`，退出码 0，零警告/错误；正式测试在沙箱外运行，Core 61 + App 37 = **98/98**，无失败/跳过，[完整输出](evidence/P1-001-R2-F02-tests.txt)。
- `PlayAction` 在暂停中同时重设动作起点和暂停起点，保持暂停，新动作 elapsed=0；Resume 仅平移动作起点以扣除暂停间隔，没有通过重置 idle 掩盖进度。MainWindow 的 Pause/Resume 同步播放器及 timer，拖动和 EndDrag 的现有调用未另行取消暂停。
- 独立探针构造未显示的真实 MainWindow，确认真实角色缓存五张图，并验证真实播放器 Pause→PlayAction(drag)→实际 EndDrag 后仍为 App/player 暂停、timer 停止。再在同一窗口注入可控时钟播放器，保留实际 PauseAnimation/ResumeAnimation/EndDrag/UpdateFrame 调用，验证等待后恢复、99ms/100ms 帧边界、菜单共用 EndDrag 入口及 Once 在有效 200ms 完成。**13 项检查全部通过，退出码 0**；[输出](evidence/P1-001-R2-F02-probe.txt)、[源码](evidence/P1-001-R2-F02-Probe.cs.txt)、[项目留存](evidence/P1-001-R2-F02-Probe.csproj.txt)。项目留存需恢复至 `artifacts/p1-001-r2-f02/` 才能按原相对引用运行。
- 探针不显示窗口、不实际捕获鼠标或打开原生菜单，菜单部分验证其共用中断入口并核对生产调用；没有启动生产 App 或访问真实存档。本轮未重跑生命周期十场景，也未要求用户重复普通七项实测。结论由当前定点运行、源码调用核对和既有适用证据共同支持，不扩展为新的桌面实测。

### 测试质量说明（不另设阻断）

`PauseThenDragStart_PlayerStaysPaused` 名称及注释声称模拟拖动，但实际只 Pause 和读取状态，`beforeAction` 未使用，没有动作切换或 timer 断言；不能将其通过称为 App 组合路径已自动覆盖。本轮独立探针补齐了决定性验证，因此 F-02 功能关闭，不要求因该测试再走一轮审批。开发方后续应将此用例补成实际动作切换/EndDrag、timer 状态和恢复进度的回归验证，修正自测覆盖描述；构造异常统一转 Inconclusive 也应避免掩盖产品回归。此项不阻止开始 F-03。

### 当前结论与接续

**F-01、F-02、F-05 已关闭；F-03/F-04 仍阻断 P1-001 完成及 P1-002 开始。** 可继续 F-03，之后 F-04，不重开 P0。
本轮自测已提交且无正在修改的任务，TASKS 恢复 awaiting-verification；实际开始 F-03 时再按规则改为 in-progress，不把等待复核与正在执行混写。

## 复核 R3（2026-10-22）：F-03 通过，关闭

### 对象与验证

- 仅复核 F-03 的 R1 剩余三项及相关开发测试，未修改生产代码、角色资源或开发测试。无 Git，当前源码及探针摘要见 [版本清单](evidence/P1-001-R3-F03-versions.txt)。
- 独立构建退出码 0、零警告/错误，[输出](evidence/P1-001-R3-F03-build.txt)；沙箱外正式测试 **Core 63 + App 41 = 104/104**，无失败/跳过，[输出](evidence/P1-001-R3-F03-tests.txt)。
- 路径：Core 的规范化相对路径比较补齐目录分隔边界；实际文件读取的 App 以传入角色目录 assetDir 为根，带分隔符比较，拒绝已有且有效的兄弟目录 PNG。Core 仍用工作目录进行相对路径结构校验，最终角色根目录边界由 App IO 检查，本次不将 Core 本身描述为已接收角色目录参数。
- 尺寸：BitmapImage 完成解码后比较 PixelWidth/PixelHeight 与声明值，尺寸不符不入缓存，正常及子目录图片可加载。
- 首次失败：UpdateFrame 在无既有 Source 时生成独立 RenderTargetBitmap，不再依赖外部 idle_0.png。独立诊断在各场景首次构造新的真实 MainWindow，验证缺图、坏 PNG、声明 64/实际 120、坏 JSON、缺 JSON 均 cache=0，初始 Source 为回退 RenderTargetBitmap 且存在非透明像素；正常 120×120 与子目录图片为 BitmapImage、cache=1。路径正反例和回退共 **24 项检查全部通过**，异常同时留下 Trace 诊断。
- [定点输出](evidence/P1-001-R3-F03-probe.txt)、[探针源码](evidence/P1-001-R3-F03-Probe.cs.txt)、[项目配置](evidence/P1-001-R3-F03-Probe.csproj.txt)。恢复至 `artifacts/p1-001-r3-f03/` 可按原相对引用运行；专用深层输出避免缺 JSON 时退回仓库真实资源。仅修改探针输出副本、构造未显示窗口，没有生产 App 启动、真实存档访问或人工桌面观察。诊断最初漏写 System.IO 引用，已修正；[初次编译输出](evidence/P1-001-R3-F03-probe-initial-build.txt) 属于审查辅助程序错误，不是产品问题。

### 回归测试说明（不另设关闭门槛）

`AllResourcesMissing_FallbackBitmapVisible` 构造正常窗口后虽清了缓存，但“Clear any existing Source”只有注释，没有将 PetImage.Source 设 null；HasVisibleImage 只证明旧图仍在，无法证明首次回退被创建。开发方应实际建立 Source=null 的前置条件，并验证回退类型和非透明像素，使移除回退分支时测试失败。本轮独立探针已经覆盖首次构造反例，因此 F-03 功能关闭，可继续 F-04，补实该测试不需要重复申请本项关闭。
F-02 的 App 用例已实际加入 PlayAction/EndDrag 及 timer 断言；末尾仍只断言动作 ID，不能将它单独表述为帧相位验证。F-02 既有独立可控时钟证据仍有效，本轮不重开。

### 当前结论与接续

**F-01、F-02、F-03、F-05 已关闭；仅 F-04 阻断 P1-001 完成及 P1-002 开始。** 可开始 F-04，P0 历史结论保留。P1-001 已提交当前小步复核，TASKS 设 awaiting-verification；实际继续修改时改 in-progress。
后续修改渲染布局时同时确认回退位图仍处于可见局部区域；本次未验收 F-04 的偏移/脚点正确性，也未重跑完整生命周期或重复用户普通七项。

## 复核 R4（2026-10-22）：F-04 尚未通过

### 本轮对象与已成立部分

- 阅读 F-04 第二轮整改、实际模型/播放器/渲染、JSON/PNG 及新增测试；未修改生产代码、角色资源或开发测试。当前版本见 [摘要](evidence/P1-001-R4-F04-versions.txt)。
- 独立构建零警告/错误、退出码 0，[输出](evidence/P1-001-R4-F04-build.txt)；正式测试 **Core 65 + App 43 = 108/108**，无失败/跳过，[输出](evidence/P1-001-R4-F04-tests.txt)。
- 将 Offset 定义成直接渲染补偿、由 GetFootOffset 返回原值的方案本身可成立；模型和架构已说明正值向右/下，实际 Image 上 Transform=(0,9)，无须强制恢复旧取反策略。
- R3 要求的回退测试已加入 Source=null 前置条件、RenderTargetBitmap 类型及非透明像素检查，本次正式运行通过，F-03 保持关闭。

### F-04 剩余反例：素材实际移动 15px，而非声明的 9px

- 独立解码当前真实 PNG，按最底非透明行读取：idle 为 **y=110**，curious 为 **y=95**，不是整改记录/测试注释所称 y=101。素材与诊断输出副本 SHA256 一致，[副本比对](evidence/P1-001-R4-F04-resource-copies.txt)，不是运行了旧图片。
- 使用真实未显示 MainWindow，冻结播放器，切换 idle→curious→drag→dodge→idle→curious→idle；对实际 Canvas 布局后进行 WPF 离屏栅格渲染，测读取成图的脚底。idle 最底行为 111，curious 应用 (0,9) 后为 105，**仍上移 6 个 96-DPI 栅格像素**；Top/Left 均未改变。这里的行号比源图多 1 与图像 DPI/边缘栅格化有关，不改变 6px 差值。测试允许 1px 边缘误差，当前明显超出。
- 共 25 项定点断言，**3 项失败、退出码 1**：两次切入 curious 的脚底 Y 均不稳定，以及“源图正好上移 9px”断言失败。去掉补偿的负对照也能检出不稳定；正常动作和 X 方向检查通过。[完整结果](evidence/P1-001-R4-F04-probe.txt)、[首次遇错停止结果](evidence/P1-001-R4-F04-probe-first.txt)、[源码](evidence/P1-001-R4-F04-Probe.cs.txt)、[项目留存](evidence/P1-001-R4-F04-Probe.csproj.txt)。源码需恢复至 `artifacts/p1-001-r4-f04/` 按原相对项目引用运行。
- 资源快照：[idle](evidence/P1-001-R4-F04-idle.png)、[curious](evidence/P1-001-R4-F04-curious.png)、[JSON](evidence/P1-001-R4-F04-character.json.txt)。没有启动生产 App、访问用户存档或真实桌面输入；这是实际资源及 WPF 离屏成图的反例，不冒充实机人眼观察。

### 测试为何仍通过与最小整改

- `ActionSwitch_IdleToCurious_WorldFootStable` 没有读取 PNG 像素或测量实际成图，直接使用 `footAnchorY - 9` 作为 curious 素材脚底，配上 Transform.Y=9，相消必然相等。素材即使未按约定移动，它仍通过。Core 的同名稳定性测试也只检查偏移值，不能代替 App 素材验证。
- 保留已确定的直接补偿语义，让实际 PNG 内容位移和元数据补偿相符；可以正确生成上移 9px 的素材，也可根据实际素材记录正确补偿，但必须测读取资源，不能只改注释/公式。具体素材制作选择由开发方决定，不扩大为通用素材框架。
- App 回归用例应读取实际源图脚底并结合真实渲染，或比较离屏渲染得到的脚底世界坐标；覆盖切回及非零补偿。保留一个错误补偿/错位素材的负对照，证明检查会失败，不能依赖写死的 101 或在公式中抵消常数。
- 更正架构、STATUS 和整改记录中关于“当前 PNG 脚底 y=101、已证明稳定”的实际实现描述；历史过程保留修订轨迹。无须用户再次重复普通七项或重做 P0。

### 当前结论

**F-04 部分成立但尚未关闭，继续阻断 P1-001 完成和 P1-002 开始。F-01/F-02/F-03/F-05 保持关闭。** 当前已交付小步的 TASKS 设 awaiting-verification；开始下一步修复时再设 in-progress。

## 复核 R5（2026-10-23）：F-04 通过，P1-001 完成

### 对象与独立结果

- 读取第五轮整改和重写的三项 App 测试，核对真实资源与源码；未修改生产代码、图片或开发测试。当前版本见 [摘要](evidence/P1-001-R5-F04-versions.txt)。相比 R4 清单，生产 C#/XAML、JSON 和 idle PNG 摘要未变，curious PNG 和脚点测试已更新；R4 的 y=95 是当时真实版本的测量，保留历史结论，不改写为当时测量错误。
- 独立构建退出码 0、零警告/错误，[输出](evidence/P1-001-R5-F04-build.txt)；正式测试 **Core 65 + App 44 = 109/109**，无失败/跳过，[输出](evidence/P1-001-R5-F04-tests.txt)。
- 原 R4 像素探针和 1px 栅格误差标准均未改动，重新构建运行后 **25 项全部通过、退出码 0**，[输出](evidence/P1-001-R5-F04-probe.txt)。源图实测 idle 脚底 y=110、curious y=101；(0,9) 补偿后真实 WPF 离屏成图脚底均为 y=111。idle→curious→drag→dodge→idle→curious→idle 全程窗口位置不变，脚底 Y 一致，底行中心 X 差 0.5px，在 1px 边缘误差内。移除实际 Image 补偿的负对照仍能检出不稳定。
- PNG/JSON 源资源与运行副本摘要一致，[比对](evidence/P1-001-R5-F04-resource-copies.txt)；新 [curious 快照](evidence/P1-001-R5-F04-curious.png)。探针 [源码](evidence/P1-001-R5-F04-Probe.cs.txt)、[项目留存](evidence/P1-001-R5-F04-Probe.csproj.txt) 与 R4 相同，原运行目录为 `artifacts/p1-001-r4-f04/`。
- App 回归测试已测读真实 PNG 最底非透明行，补偿值从 JSON 读取，覆盖切换/切回，并用错误补偿 0 作负对照，不再将 `footAnchorY-9` 作为未经测量的素材事实。其坐标算式验证由本轮实际 WPF 离屏成图进一步确认，满足原 F-04 目标。

### 覆盖与完成结论

本轮为未显示真实 MainWindow 的资源和渲染验证，没有真实鼠标操作、生产 App 启动或用户存档访问；不冒充新的 100%/150% 桌面体验。用户已通过的普通七项反馈、R1 生命周期十场景及 R2/R3 针对性结果按各自范围保留。当前窄改动为资源和测试，不无理由重跑全部生命周期或要求用户重复七项。

**F-04 通过，关闭。F-01～F-05 全部关闭，P1-001 可标记 done，解除 P1-002 的依赖阻断。** 已有阶段 0 限制、占位美术及完整行为循环尚未接入的后续范围继续保留，不扩展为全部产品完成。开发方在整改记录追加 R5 关闭依据即可，无须重复申请同一结论；本轮监理不开始 P1-002 应用实现。
