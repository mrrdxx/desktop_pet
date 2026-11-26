# 任务清单

这是任务状态的唯一出处。按顺序实施；同一时刻最多一个 `in-progress`。

状态：`todo` / `in-progress` / `awaiting-verification` / `done` / `blocked`。
`blocked` 必须说明具体缺失条件；`done` 必须在 STATUS 或其链接证据中可找到验证结果。
大阶段不是一次任务；后期阶段开始前再拆分，不提前写完整实现细节。
有关联审查时，同时遵守 [审查协作规则](REVIEW_PROCESS.md) 的阻断与复核门槛；自测完成不等于监理通过。

## 当前插入任务：小熊美术接入

### ART-003 温和拖动表情与松手恢复

- 状态：`awaiting-verification`。依据D-012；小熊基线4b25681已普通推送并核对远端，本项实现/自动验证已完成，实机指针偏离导致输入链尚未完成，用户新表情观感也待确认。
- 范围：同底图的轻微意外表情，松手后约250ms局部脸部过渡回待机；不改变位置、抓取点或松手即停，不接入抛掷/抚摸成功新表情。
- 验收：头/耳/身体拖动及时响应；松手立即结束捕获与移动、表情自行恢复；新抓取/菜单/其他表情可打断，暂停冻结不积压；身体/Alpha/世界脚点稳定，相关自动及真实输入检查通过。
- 已有依据：构建0警告/错误，Core197+App167=364/364通过，默认眨眼导出与原文件SHA256一致；原生探针三次未完整通过，不借ART-002的旧37项结果替代。当前启动入口已更新但正式旧进程未替换。见 [ART-003交接](handoffs/ART-003-drag-feedback.md)。

### ART-002 安静自然的眨眼与短暂好奇

- 状态：`done`。关联R-01与D-011；350项自动/37项实机检查通过后，用户2026-10-25反馈“我觉得现在很不错，按你说的做”，认可当前观感并要求收尾。属于用户观感接受，不代签ART-001独立监理结论。
- 范围：同底图眼部半闭/闭合帧、3～7秒克制随机眨眼（约220ms，低概率双眨）、好奇约2秒自动结束及提前离开0.3秒恢复。重新触发仍要求离开0.5秒与驻留1.2秒；保留D-009/010。
- 不含：走路/呼吸/歪头、眼球追踪、拖动新表情/松手缓冲、抚摸成功笑脸及委屈新素材，不进入P1-004。
- 验收：眨眼眼部区域外像素完全相同、嘴形不变；实机不再整身抖动/每秒眨眼；暂停无补演；持续悬停好奇只播放一次且到时回待机，移开提前恢复；相关输入与情绪回归保持通过。实机主观观感与自动状态证据分别记录。
- 已有依据：构建0警告/错误，Core191+App159=350/350通过；150%真实窗口/鼠标37项全部通过，包含26秒眨眼Source与时序观察。固定底图像素检查通过，当前正式版已启动。详见 [ART-002交接](handoffs/ART-002-natural-expressions.md)，不代签独立ART-001验收。

### INPUT-003 修复好奇表情移开不恢复

- 状态：`done`。用户2026-10-25反馈鼠标移开后仍好奇、需点击才恢复；关联R-01。
- 范围：补离开事件与好奇→idle，不增加新的美术/身体动作，不把尚在讨论的随机眨眼或好奇定时结束方案当成已批准规格。
- 验收：好奇后连续离开0.5秒自行恢复待机；透明点按离开处理；短暂离开不闪烁/重播；重入完整驻留；暂停/其他动作保持正确；保留INPUT-001/002行为。结果见 [交接](handoffs/INPUT-003-curiosity-exit-fix.md)。
- 完成依据：原反例两项先失败，修复后Core178+App154=332/332通过；150%真实鼠标31项全部通过，含不点击移开、暂停后恢复及头顶/双耳拖动。已正常重启修复版并同步桌面入口。作者自测不代签ART-001独立验收，不开始其他功能。
- 后续：该轮证据按历史版本保留；D-011/ART-002已将退出改为0.3秒并添加自然结束，运行路径以STATUS为准，不能按旧阈值回退当前实现。

### INPUT-002 修复头顶和双耳拖动

- 状态：`done`。用户2026-10-25报告眼睛上方不能点击拖动并授权修正；关联R-00/R-02、D-010。
- 范围：可见头部按下、抚摸/拖动分流、累计位移、原抓取点和捕获保持；保留透明穿透及D-009功能删除，不开始其他阶段。
- 验收：头顶/左右耳朵在idle及curious偏移下都能转拖动；慢移不被永远吞为抚摸；首段完整位移、移出头部/窗口、松手停止；抚摸仍+5且转拖动不误计分；正常捕获保持/释放。证据入口见 [修复交接](handoffs/INPUT-002-head-drag-fix.md)。
- 完成依据：构建零警告/错误，318/318自动测试（新增16）；150%真实鼠标26项全部通过；正常重启修复版本并更新桌面入口。此为作者实现自测，不代签ART-001独立验收，不进入P1-004。

### INPUT-001 移除快速移鼠受惊

- 状态：`done`。用户2026-10-25明确要求完全删除，优先于ART-001剩余检查；关联R-01、D-009。
- 范围：移除检测/速度历史/冷却/自动dodge和专用测试，保留悬停、抚摸、拖动与委屈；同步当前规格、启动入口和交接，历史报告不改写。
- 验收：高速往返不改变表情；驻留触发及中断后重新计时保持正确；构建和完整回归通过；桌面脚本指向当前版本。结果见 [交接](handoffs/INPUT-001-remove-mouse-fright.md)。
- 完成依据：构建零警告/错误，Core174+App128=302项通过；新版本正常启动，可见窗口1个/DPI144，桌面脚本路径检查通过。此为实现方自测交付，不代签ART-001独立验收，不继续P1-004。

### ART-001 小熊形象与静态表情

- 状态：`awaiting-verification`。用户2026-10-24已认可v1设计并授权接入；当前优先本项，动作暂缓，暂停功能阶段推进。
- 交付：独立透明母版、9张120×120运行PNG、bear角色JSON、默认角色加载与独立委屈表情映射；设计/资源说明及 [自测记录](history/ART-001-bear-integration.md)。
- 验证：接入时完整重建336项通过；后续 [动画与鼠标补测](history/ART-001-animation-review.md) 在150%下补真实悬停/短拖动/暂停菜单及隔离表情生命周期，相关17项回归通过。旧快划触发待测已按D-009取消，不再阻塞ART-001；旧失败证据按版本保留。
- 范围边界：本助手为实现者，以上是自测，不代签独立监理结论；原型default保留，未实现表情自动触发和身体动作不能写成已完成。眨眼与后续覆盖建议见补测记录；好奇移开不退出已由INPUT-003修复，美术任务仍保持awaiting-verification。
- 既有P1-004撤回至todo的未提交修改在本轮开始已存在，本轮原样保留，未恢复或继续阶段功能。

## 阶段 0：能稳定运行的单宠基础

### P0-001 环境与最小工程

- 状态：`done`。依赖：无。关联：R-00。
- 阅读：README、ARCHITECTURE §1–2、STATUS。
- 输入：空代码仓库及本文档包；已安装 .NET 10 SDK 10.0.402。
- 范围：确认 Windows/架构与 SDK；如需安装遵循权限；创建约定解决方案、Core 与 WPF App、SDK 锁定文件和必要忽略规则。
- 测试工程在首次存在可测逻辑时加入，不写仅断言常量或默认模板的空测试。
- 输出：最小工程可构建，App 能打开并正常关闭一个原型窗口；更新真实命令、SDK 和依赖版本。
- 验收：restore/build 成功；启动/退出实测，进程确实退出。没有桌面运行条件时保持 awaiting-verification。
- 边界：不实现情绪、物理、社交，不安装额外引擎；不自动初始化或推送远端仓库。

### P0-002 透明窗口与输入验证

- 状态：`done`。依赖：P0-001。关联：R-00。
- 阅读：PRODUCT R-00、ARCHITECTURE §3–4、TESTING M-01/M-02。
- 范围：紧凑透明窗口、两帧占位动作、身体拖动、不抢输入焦点、退出临时入口。
- 先做透明输入原型；证据不满足要求时记录失败场景，再调整实现，不直接重写技术栈。
- 验收：实体可拖；完全透明区能操作下方另一进程的软件；键入焦点保持；抓取点无明显跳变；捕获丢失能恢复。
- 输出：实现及真实人工步骤结果，明确测试过的 Windows/缩放环境。
- 当前审查：[监理报告](reviews/P0-002-review.md)；开发整改写入 [整改记录](remediation/P0-002-response.md)。完成前需关闭报告明确的阻断项并通过任务验收，不得自行代签监理结论。
- 用户授权助手介入的右键菜单修复、实现方回归与接续说明见 [修复交接](handoffs/P0-002-menu-fix.md)，不等于独立复核或任务整体验收通过。
- 完成依据：监理报告 R2 已核对用户六项通过结果，并记录 Alt+Tab 未通过但用户接受的 KL-001；原阻断解除，可进入 P0-003。不得改写为七项测试全部通过。

### P0-003 坐标、显示器与找回

- 状态：`done`。依赖：P0-002。关联：R-00。
- 阅读：ARCHITECTURE §4、TESTING M-03。
- 范围：统一坐标与脚点；工作区约束；找回动作；显示器布局改变后的恢复；建立实际坐标规则测试。
- 验收：单屏 100% 和 150% 下抓取不跳变；负坐标/显示器间空洞用逻辑测试覆盖；有条件时实测混合 DPI 双屏。
- 无第二屏时不得宣称支持已验证；记录限制，不阻塞已明确为可选的实机双屏覆盖。
- 输出：坐标契约与自动测试；至少单屏缩放实测。
- 当前审查：[监理报告](reviews/P0-003-review.md)。开发方曾提交 done，本轮核查发现必需实现和证据缺口，退回整改；关闭报告阻断项后才可完成本任务并开始 P0-004。整改由开发方写入 `docs/remediation/P0-003-response.md`。
- 完成依据：监理报告 R4（2026-10-22）已关闭 F-02，F-01～F-04 全部关闭；位移断言正反例独立检查通过，修正后脚本的 100%/150% 原始日志均记录位移一致性与交互通过，沿用既有 38 项测试及构建证据。解除 P0-004 依赖阻断，多屏实测缺失仍保留。
- Recover 崩溃的帮手修复、150% 自测和接续边界见 [修复交接](handoffs/P0-003-recover-fix.md)；局部修复不代表本任务全部完成。

### P0-004 生命周期与可靠存档

- 状态：`done`。依赖：P0-003。关联：R-00。
- 阅读：ARCHITECTURE §7、TESTING M-04/M-05。
- 范围：单实例、托盘退出/暂停/找回、配置与位置存档、防抖保存、损坏/未知版本处理。
- 补充实际 v1 字段结构、默认值及恢复规则到 ARCHITECTURE §7；测试使用临时目录。
- 验收：重启恢复可见位置；损坏文件有恢复路径；未知高版本不被覆盖；退出后无进程；暂停停止主动行为，菜单仍可操作。
- 输出：验证证据，README 中实际使用入口，基础 IO 与恢复逻辑测试。
- 当前审查：[P0-004 验证报告](reviews/P0-004-review.md)。2026-10-22 独立验证发现 F-01～F-04，退回整改；修复后复核 R1 已全部通过关闭。整改记录见 [整改记录](remediation/P0-004-response.md)，修复交接见 [交接](handoffs/P0-004-lifecycle-fix.md)。
- 完成依据：复核 R1（2026-10-22）逐项核对源码、版本和证据，F-01～F-04 全部通过。正式 VSTest 55/55、十场景隔离集成通过。Shell 托盘真实点击、100% 启动链、混合 DPI 双屏等未验证项不阻断当前任务，按既定范围在后续阶段补齐。

### P0-005 阶段 0 整体验收

- 状态：`done`。依赖：P0-004。
- 范围：执行 TESTING 阶段 0 集合；记录实际环境、性能基线、已知限制及复现方式。
- 验收：所有必需项通过；可选多屏实测缺失单列。没有功能时“未实现”不能被写成“不适用”来跳过。
- 输出：`docs/history/P0-validation.md`（仅在真实验证时创建），STATUS 链接结果。
- 完成依据：[审查 R2](reviews/P0-005-review.md) 已关闭 F-02，F-01～F-03 全部关闭。实际 HWND DPI=96/144 的当前启动三场景通过、用户托盘/动画六步通过、完整性能基线及校正已留存；其他既有结果沿用，多屏等限制保留。开发记录见 [整改记录](remediation/P0-005-response.md)。解除 P1-001 依赖，不代表已开始阶段 1 实现。
- 完成后才开始阶段 1。若用户体验反馈要求调整，先创建小修复任务并验证。

## 阶段 1：单宠开始有性格

### P1-001 动画资源与行为协调

- 状态：`done`。依赖：P0-005。关联：R-01，ARCHITECTURE §5/8。
- 范围：最小 PNG/JSON 资源格式与样例、动作回退、共享时间循环和行为中断；原型绘制资产也可由程序导出，不依赖正式美术。
- 验收：动作切换脚点稳定；拖动中断动作；缺帧/错误资源可诊断；固定时钟下行为可复现。
- 完成依据：[P1-001 审查 R5](reviews/P1-001-review.md)（2026-10-23）已关闭 F-04，F-01～F-05 全部关闭。独立构建零警告/错误、109/109 测试及 25 项真实 PNG/WPF 离屏检查通过；用户普通七项、R1 生命周期及 R2/R3 定点证据按覆盖范围保留。开发方在 `docs/remediation/P1-001-response.md` 追加关闭依据。解除 P1-002 依赖，不代表已开始该任务。

### P1-002 鼠标互动

- 状态：`done`。依赖：P1-001。关联：R-01。
- 当前范围：悬停一次触发与移开重置、拖动/抚摸/菜单/暂停禁用；原速度/距离采样及快划冷却已按D-009取消，由INPUT-001移除。
- 当前验收：PRODUCT R-01；历史完成依据仍按当时范围保留，不重新恢复已取消功能。
- 完成依据：监理 [复核 R4](reviews/P1-002-review.md) 已关闭 F-01～F-04。帮手实机修复透明角落误触后 18/18 真实输入两轮通过、163/163 自动测试通过、十场景生命周期通过；接手者核对 diff 和重验构建/测试。用户人工体验后明确接受 P1-002 交互功能（"暂未发现阻碍继续开发的问题"），并明确区分接受的是交互功能而非最终美术/表情/动作观感。用户要求收尾标 done，构成对真实鼠标手感验收项的明确豁免（REVIEW_PROCESS §5）。帮手修复的独立核对由接手者完成，不代签监理结论。整改记录见 [P1-002-response](remediation/P1-002-response.md)，修复交接见 [交接](handoffs/P1-002-live-input-fix.md)，实机验证见 [验证](history/P1-002-live-validation.md)。解除 P1-003 依赖，不代表已开始该任务。

### P1-003 情绪记忆

- 状态：`done`。依赖：P1-002。关联：R-02。
- 范围：先确定并记录 R-02 尚缺的增量/阈值；实现抚摸、委屈判定、衰减与次日表达；演进存档并保留旧版读取路径。
- 范围调整：用户于 2026-10-24 明确将高亲近主动靠近延后到阶段 2，本轮先验收情绪记忆，见 [D-008](DECISIONS.md#d-008高亲近主动靠近延后到阶段-2)。不等于人工体验已通过。
- 验收：PRODUCT R-02；普通拖动不误判；离线、回拨与跨天测试通过；情绪动画不妨碍操作。
- 最终完成依据：2026-10-24 [监理 R9](reviews/P1-003-review.md) 验收通过。用户授权监理鼠标验证后，生产 App/Core 隔离副本真实输入18项全部通过，实际抚摸50→55并防抖落盘、身体拖动与暂停恢复通过，真实存档未变；详见 [实机记录](history/P1-003-live-validation.md)。结合既有 R8/自动时间与存档证据，按 D-008 调整范围收尾；占位美术及未验证显示环境保留。不代表已开始 P1-004。
- 收尾：F-01～F-06 已关闭，G-01/G-02/G-03 接入验证接受；原待验收的三项客观操作由用户授权监理实机执行并通过，不再等待用户重复。原有 Core 208/App 126 结果按版本沿用，本轮未重跑。占位 curious 委屈动作与非最终美术限制保留，原D-008主动靠近安排已被D-013取消，不进入阶段2。开发方在 [整改记录](remediation/P1-003-response.md) 追加 R9 依据，不改写监理报告。

### P1-004 第一个稀有事件

- 状态：`done`。依赖：P1-003。关联：R-03。
- 最新验收（监理R16，2026-11-06）：F-01～F-11关闭，463项与auto已接受；单屏150%实机身体/头部拖动、头部轻点、菜单暂停恢复/退出及同目录重启通过，真实存档不变。见[审查R16](reviews/P1-004-review.md)与[实机记录](history/P1-004-live-validation.md)。夹具定位/冻结限制及其他DPI/多屏/Explorer入口未验证明确保留，不扩大为ART验收。不自动推进阶段2/提交/推送；下方旧轮次内容为历史。
- 范围：探头动作、每日计划与共享额度、持久化和时钟异常；测试入口与正式概率分开。
- 验收：PRODUCT R-03；固定种子可复现；重启不重抽；不合适时延迟或跳过。
- 进度：
  - Core 稀有事件调度逻辑完成：`RareEventPlan`（每日计划快照，含 F-05 `AccumulatedBaselineMinutes` 当日基准）、`RareEventScheduler`（每日计划/触发概率/共享额度/跨日/时钟回拨/暂停与不合适时拒绝/计划快照恢复/F-05 当日有效时间差值评估）、`EffectiveRuntimeTracker`（有效运行时间累计/暂停排除/单调时间契约/F-03 Start/End 有限值校验）。
  - 监理审查 `docs/reviews/P1-004-review.md` R1 关闭 F-01/F-02；F-03/F-04 部分通过，新增 F-05。R1 整改：F-03 `StartActivePeriod`/`EndActivePeriod` 拒绝 NaN/Infinity/负值，不污染已有有效状态；F-04 STATUS/TASKS 文档修正为实际状态并磁盘重读验证；F-05 `RareEventPlan` 新增 `AccumulatedBaselineMinutes`，`EnsurePlanForDate` 新增 `currentAccumulatedMinutes` 参数，`Evaluate` 用当日差值评估，补组合测试含快照恢复。
  - 监理 R2 关闭 F-01～F-05 全部 Core 问题，解除对后续存档/App 接入步骤的阻断。
  - 存档 v3 扩展完成：`PetStateArchive.CurrentSchemaVersion=3`，新增 `AccumulatedRuntimeMinutes` 和 `RareEventPlanSnapshot` DTO；`ArchiveService.LoadState` v1/v2 → v3 兼容回退（稀有事件字段默认 0/null）。补 7 项存档测试：v3 计划保存/加载、消耗计划持久化、null 计划、v1/v2 回退、重启恢复链（计划功能验证）、消耗计划不重复触发、null 计划新建。
  - 修复 D-013 遗留编译错误：`PauseSemanticsTests.cs`/`ArchiveServiceTests.cs`/`IsolatedArchiveChainTests.cs` 移除已删除的 `Affinity` 字段和 3 参数构造函数引用；移除已取消的抚摸 +5 affinity 测试（F05_R4）。
  - 监理 R3 提出 F-06/F-07，F-01～F-05 保持关闭。R3 整改：F-06 `App.xaml.cs` 新增 `MapArchiveToRareEventObjects`/`MapRareEventObjectsToArchive` 生产映射入口，测试通过真实 scheduler/tracker 运行→隔离文件保存→新对象恢复验证同日不重抽/剩余等待不变/离线不计/已消耗不重触发（6 项 `RareEventPersistenceTests`）；F-07 ARCHITECTURE §7/PRODUCT R-02/PetStateArchive 注释同步 v3 并清除 affinity 字段，磁盘重读确认。
  - 监理 R4 关闭 F-06，F-07 部分通过。R4 整改：清理 PRODUCT R-02 主体段落和细化参数段落中 D-013 已取消的亲近度计分/衰减/主动靠近规格，消除与"affinity 已按 D-013 取消"的矛盾。
  - 监理 R5 关闭 F-07，F-01～F-07 全部关闭，Core 与可靠存档小步接受。PRODUCT 当前将好感度/计分/衰减/主动靠近明确取消，委屈与非计分手势保留，存档说明 v3。
  - 测试通过：Core 253 + App 184 = 437/437，0 失败/跳过。构建 0 警告/错误。整改记录见 `docs/remediation/P1-004-response.md`。
  - App 生命周期接入与隔离存档生产链验证已完成：OnStartup 恢复稀有事件对象并 StartActivePeriod；OnUpdateTick 推进 tracker + EnsurePlanForDate + Evaluate + ShouldFire 时 MarkConsumed；PauseAnimation/ResumeAnimation 管理 active period；MarkStateDirty 同步稀有事件到存档；FlushFinalState 退出时保存。16 项生产链测试覆盖保存/重载/离线不计/消耗不重触发/暂停恢复/拖动拒绝/跨天新计划。
  - 未完成：探头视觉表现已实现（ShouldFire→边缘探头→curious→3秒恢复原位置），含 F-09 探头动作所有权排他和 F-10 仅真正拖动取消探头。17 项专项测试通过（含 9 项 F-09/F-10 测试），全量 459/459 通过。待监理 R8 复核与端到端验收。不修改监理报告。不标 P1-004 done。
  - 监理 R8/R9/R10 逐步关闭 F-10 和 F-09 各部分：R8 关闭 F-10，F-09 低优先级排他通过；R9 接受视觉优先级但指出提前消费；R10 要求延迟 Evaluate。R10 整改：委屈进行时延迟 Evaluate（`result=null`），保留原计划未消费；委屈结束后再评估同一计划。已展示后被委屈打断的探头维持已消费不重播。纠正 PRODUCT/ARCHITECTURE 中"委屈中仍消费"的错误解释。修改 2 项 + 新增 2 项连续 tick 测试。全量 463/463 通过。
  - 监理 R11（`docs/reviews/P1-004-review.md` R11 段落）关闭 F-09：有消费副作用的 Evaluate 在委屈活跃时不调用；连续 tick 输出委屈中 `Consumed=false`，表达结束后 `consumed=True; probing=True; action=curious`，同一原计划延迟而非跳过。**F-01～F-10 全部关闭，P1-004 代码与自动验证部分接受，进入实机验收**。状态改 `awaiting-verification`，不标 done。剩余为真实窗口显示最近边缘/半身可见、3秒归位、身体/头部拖动打断且抓取点稳定、头部轻点仍归位、暂停剩余时间/退出保存恢复。实机夹具见 `tests/Integration/Run-ProbeLive.ps1` + `ProbeLiveProbe.cs`（隔离存档 + 预置可触发计划，不改正式概率，不污染真实存档，不要求等20分钟）。ART-001/003 既有未验证项保留。开发方追加关闭依据，不重复已关闭整改。不提交/推送。
  - 监理 R12 审查实机夹具发现 F-11（探针在探头开始后改写窗口位置导致基线/边缘断言失真、未记录实际 HWND DPI/显示器矩形/起止时间、未区分退出模式、超时路径未执行 finally 存档比较）。R12 整改：探针全文重写——基线在首 tick 前捕获、之后只观察不改 Left/Top、记录 DPI/工作区/窗口矩形/起止时间、区分 auto/interactive/timeout 三种退出模式、finally 始终执行存档快照比较、半身可见记为 INFO 不自动标 PASS。驱动修改——超时分支改为记录+kill 不直接 throw、finally 始终执行存档比较、新增 `-Interactive` 参数。
  - 监理 R13 确认 F-11 位置/时序修复通过，但发现最终判定漏洞（超时/非零退出/tray exit 失败可被吞掉仍 PASS）。R13 整改：`ProbeTrayExit` catch 增加 `_probeLiveFailures++` + FAIL 关键词；驱动 auto 最终 PASS 须同时满足 5 条件（未超时 + exit 0 + 零 FAIL/ERROR + 6 标记 + `RESULT failures=0`）；新增 `Run-NegativeControls.ps1` 负对照测试（5 负例 + 1 正例全部正确）。
  - 监理 R14（`docs/reviews/P1-004-review.md` R14 段落）独立执行 auto 运行 exit 0、RESULT failures=0、真实存档不变；另提取驱动实际判定代码对 5 负例 + 1 正例独立执行全部正确。**F-11 通过并关闭，F-01～F-11 全部关闭，代码/自动验证及隔离自动场景接受**。P1-004 继续 `awaiting-verification`，只剩真实观感/输入交互：半身可见、身体/头部真正拖动打断和抓取点、头部轻点归位、暂停剩余时间与退出恢复。使用既有隔离 Interactive 入口逐项实际验证，保持真实存档不受污染、正式概率不变。不新增产品代码、不重做旧整改或重跑无关全套；不提交/推送。

## 阶段 2：物理手感

依据[D-016](DECISIONS.md#d-016阶段2轻放悬停与玩偶式物理手感)及PRODUCT R-04：方向已确认，以下均未实现/未开工。按顺序小步验收，D-009/D-013取消项不恢复。

### P2-001 释放测速与轻放/甩出判定

- 状态：`done`。依赖：P1-004。关联：R-04。
- 范围：先Core纯逻辑采样/释放判定及测试，沿用物理像素/DPI边界与可控单调时钟；定义可调采样窗/轻放阈值/上限等初值并写回PRODUCT，不接App位置、不先做全套物理或新增图片。
- 验收：静止/轻放不启动物理，快拖释放产生有界方向速度，快速往返合理处理，停住后松手不沿用旧峰值；零时间/采样中断/恢复/DPI切换不产生异常速度，相关测试有正反例。此为释放测速，不能恢复D-009快划受惊。
- 完成依据：Core `ReleaseSpeedSampler`（`src/DesktopPet.Core/Physics/ReleaseSpeedSampler.cs`）实现释放测速纯逻辑：近期运动段速度估算（`FindRecentMotionSegment` 从末尾向前找连续同方向运动 pair）、停止判定（`StopSpeedPxPerMs`）、轻放/甩出阈值判定、方向保持的速度钳位、DPI上下文重置（`ClearSampleContext`）、坐标/释放时间有限值校验。44 项专项测试覆盖方向反转/连续序列停止/DPI上下文重置/坐标校验/释放时间校验/往返拖动/零时间差/采样中断/边界阈值/NaN-Infinity 拒绝/Reset 重用。全量 Core 297/297 通过，0 回归。PRODUCT R-04 参数初值已写入（SampleWindowMs=100, ThrowThreshold=0.15px/ms, MaxSpeed=2.0px/ms, MinSamples=2, MinTimeSpan=5ms, StopSpeedPxPerMs=0.02px/ms），标为调试初值待手感校准。未接 App、未实现重力/碰撞、未新增图片。不自行签通过，提交监理审查。

- 当前审查：[P2-001复核R1](reviews/P2-001-review.md)已关闭F-01～F-03，独立Core297/297及8项原反例/恢复检查通过，纯Core验收完成。App实际DPI清空接线留P2-003，手感初值留P2-004；不宣称物理已接桌宠。开发方原整改记录追加关闭依据，不改报告。

### P2-002 有界物理轨迹与碰撞停止

- 状态：`done`。依赖：P2-001。关联：R-04。
- 范围：Core固定步长重力、当前工作区边界、衰减反弹和最终停止；先验证极端dt/长卡顿/暂停恢复，再接UI。
- 验收：无力松手路径保持原地；抛掷轨迹可复现且有界，底部反弹衰减后确实静止，不能无限抖动、越界或落入屏幕空洞；暂停无积压。
- 完成依据：Core `PhysicsSimulator`（`src/DesktopPet.Core/Physics/PhysicsSimulator.cs`）实现固定步长 semi-implicit Euler 重力积分、`WorkArea` 矩形边界碰撞（反射+钳位+地板摩擦）、衰减反弹与停止判定。`PhysicsConfig` 可调参数：FixedStepMs=1000/60, MaxStepsPerUpdate=5, GravityPxPerMs2=0.002, BounceRestitution=0.4, StopSpeedPxPerMs=0.02, MaxBounces=8。28 项专项测试覆盖重力轨迹/确定性/地板反弹/墙壁反弹/天花板反弹/不越界/MaxBounces上限/无无限微抖/极端dt/零dt/负dt/NaN dt/Infinity dt/暂停恢复/多次Update累积/轻放不进物理/ForceStop/Reset/弹跳计数/地板摩擦/有限值校验/与ReleaseSpeedSampler集成。全量 Core 325/325 通过，0 回归。PRODUCT R-04 追加 P2-002 参数表，ARCHITECTURE §5 追加物理模拟器描述。未接 App、未新增图片、未进入 P2-003。不自行签通过，提交监理审查。

- 监理当前结论：[P2-002首轮审查](reviews/P2-002-review.md)未通过。独立Core325/325，但F-01固定步长余量、F-02自然停止支撑、F-03输入/配置边界反例已复现，阻断本小步完成和P2-003。开发方原有交付保留，下一步仅限这些问题整改与复核，不自行关闭或推进。

- 整改进度（2026-11-06）：F-01～F-03 代码与测试整改已完成，整改记录见 [P2-002-response](remediation/P2-002-response.md)。F-01 新增 `_timeCarryMs` 累积器（跨调用等价）；F-02 `ResolveCollisions` 返回 `hitFloor`，自然停止只在地板，MaxBounces 达上限重力继续，`<=` 比较保证零/等于阈值收敛；F-03 `Launch` 返回 `bool`、`Update` 返回 `Rejected`、新增 `IsValid`/`IsFinite` 校验。专项测试 54/54 通过（28 旧适配 + 22 新反例 + 4 零/等于阈值补充），全量 Core 351/351 通过，0 回归。PRODUCT/ARCHITECTURE 已同步。状态保持 `awaiting-verification`，待监理复核，不自行关闭。

- 最新监理复核：[R1](reviews/P2-002-review.md)：Core347/347及7项独立原反例通过；F-01/F-03关闭，F-02主要修复接受，仅剩合法零/等于停止阈值的地板收敛。保持awaiting-verification，只补此边界，不进入App/P2-003；旧轮次记录保留历史含义。

- 最终复核：[监理R2](reviews/P2-002-review.md)已关闭F-02，F-01～F-03全部关闭，独立Core351/351及原反例复验通过；纯Core验收完成。App/实际手感未实现或验证，P2-003须用户明确开工，不自动提交/推送；上列旧轮次结论按历史保留。

### P2-003 App接入与行为打断

- 状态：`done`。依赖：P2-002。关联：R-04、R-03。
- 范围：实际拖动释放→Core→统一位置写入，抓取打断、暂停/退出恢复、工作区变化和探头延迟协调；复用现有熊图，确需缺失反馈再短暂文字占位。
- 验收：实际App接线及隔离保存链可验证，运动中抓取无锚点跳变，物理/探头不抢位置，重启静止恢复合法位置；普通悬停/头部非计分手势保留，不抢焦点、不扩大透明命中。
- 第一小步进度（2026-11-06）：拖动采样和释放判定已接入。`MainWindow` 新增 `ReleaseSpeedSampler` 字段，`StartDragging` 清空采样器后记录按下基准样本，`ProcessCapturedPointerMove` 每帧将 foot-center 物理像素+单调 ms 喂入 `RecordSample`，`EndDrag` 调 `EvaluateRelease` 记录结果并 `Reset`。`OnDisplayLayoutChanged` 调 `ClearSampleContext`，`PauseAnimation` 调 `ClearSampleContext`+`Reset`。暂不驱动窗口按物理轨迹运动。8 项 App 接线测试通过（轻放/甩出/二次拖动重置/停住不误抛/暂停清采样/显示器变化清采样/默认状态），全量 App 218/218 + Core 351/351 通过，0 回归。下一步：接入 PhysicsSimulator 驱动窗口运动。

- 当前监理结论：[S1首轮](reviews/P2-003-review.md)部分接受。独立App专项8/8通过，但F-01初始/静止采样及F-02真实DPI上下文清理待修，阻断S2窗口运动；保持in-progress。全量569是开发自测，旧进度不能当放行。开发方在docs/remediation/P2-003-response.md独立整改。

- F-01/F-02 整改进度（2026-11-06）：代码与测试整改已完成，整改记录见 [P2-003-response](remediation/P2-003-response.md)。F-01 `StartDragging` 记录按下基准样本（`RecordFootSample`），`EndDrag` 静止探测（释放距最后样本>50ms 补当前位置样本）；F-02 接入 WPF `DpiChanged` 事件→`NotifyDpiChanged`→`ClearSampleContext`，`NotifyDpiChanged` 为 internal 供测试共用。`ReleaseSpeedSampler` 新增 `NewestSampleTimeMs` 只读属性。13 项 App 接线测试通过（新增 6 项 F-01/F-02 关键场景，去掉 1 项重复，修正 `StopBeforeRelease` 去掉虚构同位置 Move），全量 App 223/223 + Core 351/351 = 574/574 通过，0 回归。ARCHITECTURE §5 已同步。状态保持 `in-progress`（awaiting-verification），待监理复核，不自行关闭。

- 最新监理 S1 R1：[报告](reviews/P2-003-review.md)关闭 F-02；F-01 身体初始基准和真实静止修复接受，仅剩头部单次快移转拖动的同时间采样丢失。独立专项 13/13 及真实手势探针已执行；全量 574 为开发自测。P2-003 保持 `in-progress`，只补该分支及相关测试，S2/S3 尚未放行。

- F-01 R1 头部整改进度（2026-11-06）：代码与测试整改已完成，整改记录追加见 [P2-003-response](remediation/P2-003-response.md)。`BeginCapturedPointerInteraction` 记录按下时间到 `_pointerPressTimeMs`；`StartDragging` 用 `_pointerPressTimeMs` 作为基准样本时间，使头部转换时基准@0、首次 Move@10 不同时间戳，Core 相同时间拒绝不丢弃首次位移。新增 `HeadPress_SingleFastMove_ConvertsAndThrows`（头部单次快移转拖动可甩）和 `HeadPress_LightTapWithinSlop_NoThrow`（容差内微移不误抛）2 项实际手势链测试。专项 15/15 通过，全量 App 225/225 + Core 351/351 = 576/576 通过，0 回归。ARCHITECTURE §5 已同步。状态保持 `in-progress`（awaiting-verification），待监理复核，不自行关闭。

- 最新监理 S1 R2：[报告](reviews/P2-003-review.md)关闭 F-01，F-01/F-02 全部关闭，S1 接线接受。独立专项 15/15 及原头部/身体/真实静止手势探针通过；全量 576 为开发自测。整体保持 `in-progress`，下一小步可进入 S2 窗口物理接入，不代表 S2/S3 已实现或实机手感通过。

- S2 窗口物理接入进度（2026-11-06）：代码与测试实现完成，整改记录追加见 [P2-003-response](remediation/P2-003-response.md)。`MainWindow` 新增 `PhysicsSimulator`/`_isPhysicsActive`/`_lastPhysicsUpdateMs` 字段；`EndDrag(bool isCancellation)` 拆分正常释放（`ShouldThrow=true` 时 `Launch` + `_isPhysicsActive=true`）与取消路径（只清理不 Launch）；`OnUpdateTick` 中 `_isPhysicsActive && !_isDragging` 时 `UpdatePhysics` 驱动窗口按物理轨迹运动并转回 DIP 设置 `Left/Top`；`BuildWorkArea` 构建 foot-center 坐标系合法边界；`ClampWindowToWorkArea` 物理活动时跳过；`StartDragging` 停止物理。`EndCapturedPointerInteraction(bool)` 传参，`LostMouseCapture` 传 `isCancellation: true`。新增 5 项 S2 测试（快移甩出/轻放/丢失捕获/物理到静止/重新抓取停止），修复 4 处测试回归（3 处反射 `EndDrag` 传 `false` + `SharedGestureEntry` 改极慢移动避免触发 throw）。专项 20/20 通过，全量 App 230/230 + Core 351/351 = 581/581 通过，0 回归。ARCHITECTURE §5 已同步。状态保持 `in-progress`（awaiting-verification），待监理复核，不自行关闭。不进入 S3，不提交/推送。

- 最新监理 S2 R3：[报告](reviews/P2-003-review.md)部分接受普通运动与边界换算；独立专项 20/20 和实际 tick 普通运动通过，但 F-03 菜单/关闭默认正常释放、F-04 Rejected 后不清理活动状态待补。仅整改该两项，S1 F-01/F-02 不重开；整体保持 `in-progress`，不进入 S3，不提交/推送。全量 581 为开发自测。

- S2 R3 整改进度（2026-11-06）：F-03/F-04 代码与测试整改完成，整改记录追加见 [P2-003-response](remediation/P2-003-response.md)。F-03 `ShowNativeContextMenu` 和 `OnClosed` 中 `EndDrag()` 改为 `EndDrag(isCancellation: true)`；F-04 `UpdatePhysics` 中 `Rejected` 时设 `_isPhysicsActive=false`、`_physicsSimulator.Reset()` 并 return，不写入无效坐标。新增 4 项 S2 R3 测试（关闭取消不 Launch/菜单取消不 Launch/正常释放仍 Launch/Rejected 停止清理），通过真实生产入口（`OnClosed`、`ShowNativeContextMenu`）验证，不手工传 `true` 替代。专项 24/24 通过，全量 App 234/234 + Core 351/351 = 585/585 通过，0 回归。ARCHITECTURE §5 已同步。状态保持 `in-progress`（awaiting-verification），待监理复核，不自行关闭。不进入 S3，不提交/推送。

- 最新监理 S2 R4：[报告](reviews/P2-003-review.md)关闭 F-03/F-04，S2 接受，F-01～F-04 全部关闭。独立专项 24/24、实际关闭/Rejected 原反例及普通 tick 运动链通过；全量 585 为开发自测。整体仍 `in-progress`，可进入 S3；工作区协调应覆盖无可用显示器/区域，未实测手感仍留 P2-004。不提交/推送，不提前标 done。

- S3 打断/恢复协调进度（2026-11-06）：代码与测试实现完成，整改记录追加见 [P2-003-response](remediation/P2-003-response.md)。S3-1 重新抓取不跳（`StartDragging` 已停物理+Reset，`_dragOffset` 基于物理更新后当前位置，无需改动，补测试验证）；S3-2 暂停恢复无积压（`PauseAnimation` 中 `ForceStop`+`_isPhysicsActive=false`，`ResumeAnimation` 中 `ClearTimeAccumulation`，物理不跨暂停）；S3-3 物理期间探头延迟（`OnUpdateTick` 中 `StartProbe` 条件加 `!_isPhysicsActive`）；S3-4 工作区变化/退出保存/重启恢复（`OnDisplayLayoutChanged` 停物理+恢复位置，`RestoreProbePositionForExit` 停物理+钳位，`OnClosed` 停物理）；S3-5 无可用显示器（`BuildWorkArea` 返回 `WorkArea(1,0,0,0)` 使 `IsValid=false`→Rejected→安全停止，不修改 `WorkArea.IsValid` 语义）。新增 7 项 S3 测试（重新抓取不跳/暂停冻结无积压/探头延迟/工作区变化停物理/退出保存合法位置/OnClosed 停物理/无显示器安全处理），驱动实际 `OnUpdateTick` 并检查位移/边界/物理状态。专项 31/31 通过，全量 App 241/241 + Core 351/351 = 592/592 通过，0 回归。ARCHITECTURE §5 已同步。状态保持 `in-progress`（awaiting-verification），待监理复核，不自行关闭。不提交/推送。

- 最新监理 S3 R5（2026-11-07）：[报告](reviews/P2-003-review.md)未通过，F-05～F-08 分别为暂停恢复语义、计划提前消费、头部抓取继续运动、无显示器/隔离退出重载证据缺口。独立专项 31/31 但实际入口反例已复现，全量 592 为开发自测。整体保持 `in-progress`，仅整改并补证 S3 范围，F-01～F-04 不重开；不进入 P2-004，不提交/推送。

- S3 R5 整改进度（2026-11-07）：F-05～F-08 一次整改完成，整改记录追加见 [P2-003-response](remediation/P2-003-response.md)。F-05 `PauseAnimation` 改为冻结非终止（不调 `ForceStop`/不清 `_isPhysicsActive`，只 `ClearTimeAccumulation`），`ResumeAnimation` 重设 `_lastPhysicsUpdateMs` + `ClearTimeAccumulation`，恢复后继续原轨迹无 5 秒积压；F-06 `OnUpdateTick` Evaluate 条件加 `!_isPhysicsActive`，物理期间跳过 Evaluate 保留未消费计划；F-07 `BeginCapturedPointerInteraction` 头部路径在 `_isPettingCandidate=true` 前停物理（`ForceStop`+`_isPhysicsActive=false`），`OnUpdateTick` 物理驱动条件加 `!_isPettingCandidate`；F-08 新增 `TestMonitorList` 注入点，`BuildWorkArea` 使用 `TestMonitorList ?? EnumerateMonitors()`，无显示器测试改为真实空列表注入，新增隔离保存→重载→新窗口静止恢复测试（temp 目录 `ArchiveService`，验证完整窗口边界及速度不持久化）。替换 2 项测试、修改 1 项、新增 1 项。构建 0 警告 0 错误，全量 App 242/242 + Core 351/351 = 593/593 通过，0 回归。ARCHITECTURE §5 已同步 R5 整改。状态保持 `in-progress`（awaiting-verification），待监理复核，不自行关闭。不进入 P2-004，不提交/推送。

- 最新监理 S3 R6：[报告](reviews/P2-003-review.md)关闭 F-06/F-07，空显示器拒绝部分接受；仅剩 F-05 暂停 tick 守卫/活跃时间轨迹对照、F-08 隔离生产退出恢复链及边界断言。独立专项 32/32，原计划消费/头部抓取反例通过；全量 593 为开发自测。整体保持 `in-progress`，不进入 P2-004、不提交/推送，已关闭项不重做。

- S3 R6 整改进度（2026-11-07）：F-05/F-08 代码与测试整改完成，整改记录追加见 [P2-003-response](remediation/P2-003-response.md)。F-05 `OnUpdateTick` 物理分支条件加 `!_isAppPaused` 守卫，暂停时即使 tick 被直接调用也不推进物理；测试重写为对照组（无暂停 5 tick）vs 暂停组（3 tick→暂停→暂停中调 `OnUpdateTick` 验证 delta=0→恢复→2 tick），断言最终位置与对照组一致（误差 1.0 DIP）。F-08 `RestoreWindowPosition` 改 internal，新增 `TestSetMainWindow`/`TestState` internal 访问点；测试重写为复用生产链 `TestSetupIsolatedSave`→`TestSetMainWindow`→`FlushFinalState`→`ArchiveService.LoadState`→`RestoreWindowPosition`，断言完整窗口边界位于合法工作区内、无速度、首 tick 静止，使用隔离存档不接触真实存档。构建 0 警告 0 错误，全量 App 242/242 + Core 351/351 = 593/593 通过，0 回归。ARCHITECTURE §5 已同步 R6 整改。状态保持 `in-progress`（awaiting-verification），待监理复核，不自行关闭。不进入 P2-004，不提交/推送。

- 最终监理 S3 R7（2026-11-07）：[报告](reviews/P2-003-review.md)关闭 F-05/F-08，F-01～F-08 全部关闭，S1/S2/S3 接入与隔离验证接受。独立专项 32/32、暂停实际 tick 与独立生产退出保存/恢复探针通过；全量 593 为开发自测。P2-003 标 `done`，实机手感/参数校准留 P2-004；不自动提交/推送，历史轮次为当时状态。

### P2-004 实机手感验收

- 状态：`done`。依赖：P2-003。关联：R-04。
- 范围：验证轻放、方向/速度、反弹收敛、重新抓取与暂停退出；按反馈调整初值，只做必要相关回归。记录环境及未验证项，不自动补大量新动作图或任务栏滑落。
- 验收：轻放停留、甩出可控、落地站稳、重复操作可恢复；实际鼠标体验与日志分开记录，未实测不标done。

- 2026-11-07 监理按用户授权执行首轮真实鼠标验收：[实机记录](history/P2-004-live-validation.md)。100%单屏静止轻放、左右/斜向甩出、采样内边界/收敛、空中暂停恢复接受；飞行中身体/头部追抓及完整慢拖/停住松手手感待补，短自动拖拽触发不一致现象保留待核实。不标 done、不进入阶段3，不改参数、不提交/推送。
- 2026-11-07 诊断补充：`ReleaseSpeedSampler` 新增 `ReleaseDiagInfo` 快照（采样列表、运动段、判定原因、配置阈值），`EvaluateRelease` 各路径标记 reason，`MainWindow` 暴露 `LastReleaseDiag`，观察宿主 timeline 追加 `releaseDiag` 字段。判定逻辑/阈值/参数完全未改，全量 593/593 通过。待监理使用诊断宿主补测慢拖、停住释放及飞行中身体/头部重新抓取。不标 done、不进入阶段3、不提交/推送。

- 2026-11-07第二轮监理复测：独立专项32/32，真实短拖已出现throw和insufficient_samples两种结果，失败时窗口移动40px但仅1个被接受样本。见[实机记录第二轮](history/P2-004-live-validation.md)，需补Move入口/被拒样本诊断确认根因，暂不调阈值；状态维持awaiting-verification，剩余手感项目保留。

- 2026-11-07入口诊断核对：生产EndDrag后的Reset清空AttemptLog，LastSampleAttempts尚非释放快照；独立可控反例日志2→0已保留，未做第三轮真实鼠标。先补松手后独立快照及生产手势验证，保持awaiting-verification，既有采样判定/阈值不变。详见实机记录第三轮。

- 2026-11-07第四轮：[P2-004审查R1](reviews/P2-004-review.md)确认F-01：真实40px移动的baseline/move同为12375ms，move被同时间戳拒绝。释放快照修复和独立38/38接受；全量599为开发自测。维持awaiting-verification，先修App真实时间/采样边界并复核，再补剩余手感。不改阈值、不提交推送、不进入阶段3。

- 2026-11-07第五轮：[监理R2](reviews/P2-004-review.md)关闭F-01，独立43/43，两次真实短拖Move时间戳不同且全部accepted，无采样不足。本次末段静止/不足5ms仍按现规则轻放，不记甩出成功。P2-004保持awaiting-verification，剩余完整手感待补；不无限补诊断、不改阈值迎合脚本，不进入阶段3、不提交推送。

- 最终监理R3（2026-11-07）：用户明确反馈“五项均完美测试通过”，补齐慢拖、停住松手、正常甩出、身体/头部飞行追抓和锚点稳定；合并既有暂停/恢复及生产隔离退出证据，[报告R3](reviews/P2-004-review.md)接受当前范围，P2-004标done。未测试缩放/多屏及ART旧限制保留，不改历史失败；桌面启动入口更新待后续交付收尾。未改参数、未提交推送，阶段3等待用户安排。

## 阶段3前工程准备（D-018，已规划未实现）

来源与取舍见[决策D-018](DECISIONS.md)；技术边界见[ARCHITECTURE §10](ARCHITECTURE.md)；验证入口见[TESTING M-07/M-08](TESTING.md)。不改变阶段2完成状态，不以外部评分新增无限整改。一次一个任务，首个仅PREP-001，开发AI接到用户转达的开工提示后实施；后续任务不能打包提前实现。

### PREP-001 异常记录与明确退出策略

- 状态：`done`。依赖：P2-004。关联：D-018、R-00。
- 范围：App启动尽早接入UI未处理异常、AppDomain未处理异常和Task未观察异常的诊断通知，提取最小本地日志组件及明确的继续/退出策略；不接双宠，不搬动整个MainWindow，不新增云日志或自动重启。
- 默认策略：未知UI异常记录后保持框架默认异常处理，不全局吞错；AppDomain只尽力记录，不许诺恢复。Task通知记录真实来源，不把通知当立即/必然崩溃；正常await/局部捕获仍负责已知异步错误。日志路径、容量限制与保留规则按架构§10落地。
- 验收：隔离子进程通过实际异常事件验证UI/工作线程/Task通知各自日志和真实退出行为；注入日志目录不可写/日志组件失败时不再抛第二异常、不递归提示/死锁。日志包含时间/版本/来源/异常类型与堆栈，容量受限；实际路径不接触真实用户存档，不在未知状态强制Flush存档。
- 限制：不要求模拟可控范围外StackOverflow/进程强杀/本机断电，也不承诺其必留日志。不能只直接调用handler就宣称事件接线通过；Task通知的延迟/GC依赖须在隔离测试中说明。
- 完成条件：相关构建/异常隔离验证及必要原功能回归，更新实现位置和验证记录；提交监理复核，不自行签通过，不自动进入PREP-002或P3-001。
- 实现依据（2026-11-07）：
  - 日志组件 `src/DesktopPet.App/Diagnostics/DiagnosticLogger.cs`：最小本地有界日志，不依赖Dispatcher/复杂锁，记录失败不抛第二异常。单文件上限256KiB，最多保留5份，异常正文/堆栈截断（2048/8192字符）。日志路径 `%LOCALAPPDATA%/DesktopPet/logs/`，与存档隔离。
  - App接线 `App.xaml.cs` `RegisterDiagnosticHandlers()`：在 `OnStartup` 最早期（`base.OnStartup` 之后、单实例检查之前）注册三个通知——DispatcherUnhandledException（记录不吞）、AppDomain.UnhandledException（尽力记录）、TaskScheduler.UnobservedTaskException（记录不SetObserved）。不自动重启、不强制保存、不全局Handled=true。
  - 隔离子进程验证 `tests/DesktopPet.DiagnosticProbe/`（独立可执行项目）：通过真实子进程触发UI异常（Dispatcher BeginInvoke抛出）、Worker异常（后台线程抛出→AppDomain通知）、Task未观察异常（GC触发）。`DiagnosticIsolationTests` 4项场景：UI异常日志通过、Worker异常日志通过（含terminating=True）、Task通知GC依赖标注Inconclusive（架构§10已知限制）、正常退出无误报。
  - 组件测试 `DiagnosticLoggerTests` 10项：日志内容（时间/版本/来源/类型/堆栈/线程）、null异常标记、普通消息、不可写目录不抛、Dispose后不抛、文件滚动、保留上限、长异常截断、并发写入安全、重复失败不递归/死锁。
  - 构建0警告0错误。全量回归 Core 351/351 + App 266通过+1跳过 = 617+1，0 回归。
  - 未改存档格式、物理参数或MainWindow行为；不进入PREP-002/003或P3-001。不提交/推送。

- 最新监理R1：[报告](reviews/PREP-001-review.md)部分接受，独立13通过+1跳过，但F-01容量/跨日保留、F-02真实生产接线/退出证据、F-03锁竞争有界返回未满足。仅整改本范围并在独立整改记录回应，保持awaiting-verification，不进入PREP-002或双宠。全量617+1为开发自测。

- R1整改（2026-11-07，[整改记录](remediation/PREP-001-response.md)）：
  - F-01：`WriteEntry` 写前按 UTF-8 字节计算，`fileSize + entryBytes > cap` 时先滚动；单条超 cap 经 `TruncateUtf8Bytes` 按字符边界截断；新增 `PruneOldDateFiles` 写后全局清理（含跨日期），总文件数含当前≤maxRetainedFiles。修正测试断言为严格 cap/maxRetainedFiles，新增跨日保留/单条>cap/边界=cap/精确字节 4 项。
  - F-02：探针删除自建 handler，改为创建 `WpfApp` 实例 → `SetDiagnosticLogger` 注入隔离 logger → 调用生产 `RegisterDiagnosticHandlers()`；UI/worker 异常不 catch 吞掉，父进程采集 OS 真实退出码（非零）；Task 修正 `type:` 格式匹配；异步读 stdout/stderr 防管道死锁。新增 `SetDiagnosticLogger` internal 方法 + `InternalsVisibleTo` 探针项目。
  - F-03：`WriteEntry`/`Dispose` 改 `Monitor.TryEnter(0)` 有界策略，获取失败即舍弃/返回；新增 `TryEnterWriteLock`/`ExitWriteLock` 测试辅助。新增锁竞争有界 Record/Dispose 2 项测试，修正并发写测试适应有界丢弃。
  - 构建0警告0错误。全量回归 Core 351/351 + App 272通过+1跳过 = 623+1，0 回归。新增6项+修正4项测试。
  - 未修改 `OnExit`/`FlushFinalState`（理由：异常终止不经 `OnExit`，`FlushFinalState` 已有 null guard；存档完整性验证属 P0-004 域）。
  - 未改存档格式/物理参数/MainWindow；不进入PREP-002/003/P3-001；不提交推送。待监理复核。

- 最新监理R2：[报告](reviews/PREP-001-review.md)关闭F-03；F-01仅剩滚动失败后仍追加越界（独立256→512字节），F-02因UI实际STA失败及生产启动未隔离/保存证据缺失仍未通过。独立18通过+1失败+1跳过，全量623+1为开发自测。先修隔离、勿再运行旧UI探针；保持awaiting-verification，不进入PREP-002，不提交推送。

- R2整改（2026-11-07，[整改记录](remediation/PREP-001-response.md)）：
  - F-01：`RollFiles` 返回 `bool`（当前文件成功移走= true）。`WriteEntry` 滚动失败时检查当前文件剩余容量，不够则 `return` 安全放弃（不继续追加越界）；容量检查 `catch` 分支也防御性验证。新增 3 项反例测试：持有文件不可改名/删除时验证不越界且 entry 未写入、UTF-8 多字节字符截断不产生 U+FFFD、滚动失败但有剩余容量仍写入。
  - F-02：探针 `Main` 标 `[STAThread]` 修复 UI 专项；新增 `App.ProbeSetupIsolated(dir, mutexName)` 隔离存档/锁名/日志（不执行生产 `OnStartup`，不创建 MainWindow/TrayService）；`SingleInstanceGuard` 新增 `internal` 构造函数支持自定义 mutex 名（探针用 `Global\DesktopPet.DiagnosticProbe.{GUID}` 唯一锁）；Task 提取 `[NoInlining]` helper 避免 TCS 局部变量存活。新增 `normal-save`/`abnormal-save` 场景验证正常保存链不被破坏、异常退出 `FlushFinalState` null guard 不强制保存未知状态。
  - 构建0警告0错误。全量回归 Core 351/351 + App 277/277 = 628/628 通过，0 失败，0 跳过。DiagnosticLoggerTests 18/18，DiagnosticIsolationTests 6/6（含 R2 新增 5 项）。
  - 未修改监理报告、`OnExit`/`FlushFinalState` 逻辑（只通过隔离测试验证 guard）；未改存档格式/物理参数/MainWindow；不进入PREP-002/003/P3-001；不提交推送。待监理复核。

- 最新监理R3：[报告](reviews/PREP-001-review.md)关闭F-01，F-03保持关闭；F-02 STA/注册/异常通知通过，仅剩真实正常/异常保存生命周期及隔离配置不被OnStartup覆盖的证明。独立24/24、原容量反例通过，全量628为开发自测。保持awaiting-verification，只补剩余F-02，不进入PREP-002，不提交推送。

- 最终监理R4：[报告](reviews/PREP-001-review.md)关闭F-02，F-01～F-03全部关闭，PREP-001标done。独立隔离专项7/7及真实Shutdown→OnExit保存探针通过，全量629为开发自测。异常/正常保存、启动隔离证据闭环；文件I/O与致命故障限制保留，未提交推送。PREP-002仅待用户指令开始共享时钟小步，不自动打包后续。

### PREP-002 面向双宠的有限App职责整理

- 状态：`done`。依赖：PREP-001。关联：D-018、R-04、R-05。
- 第一小步：提取App可共享的高分辨率单调时钟，移除App向MainWindow静态取时间的依赖，保留double毫秒/统一原点/既有注入与暂停约定；先独立验证，不混入状态迁移。
- 后续小步：按当前单宠需要收拢状态访问/保存通知，划分输入、行为更新和位置控制职责，使未来每只宠物能持有自己的状态。只抽取真正内聚部分，不机械partial拆文件、不预造通用框架，不以“文件少于多少行”作为验收。
- 验收：生产与测试走同一时钟/行为路径，App无反向引用MainWindow时钟；物理、悬停、头部手势、暂停、探头/委屈优先级和保存语义保持；相关专项与一个当前环境的必要实机烟测通过。重构任务结束执行现有全量回归一次，只有后续变化/失败才扩大重跑。
- 不改范围：单宠数量、存档v3及其兼容、阈值参数和资源不变；PetId/集合/迁移属于后续P3-001-S1。不重做已完成美术或阶段2验收。

- S1监理R1（2026-11-07）：[报告](reviews/PREP-002-review.md)接受共享单调时钟提取，独立相关52/52通过，App反向取时钟依赖已消除；全量629为开发自测。整体未完成，下一小步限定单宠状态访问/保存通知收拢，保留后续更新职责整理与最终回归/实机烟测。不进入PREP-003/P3-001，不提交推送。

- S2状态访问收拢（2026-11-07）：创建 `Platform/PetStateContext` 持有5个状态对象 + `MarkStateDirty` 回调，App显式注入 `MainWindow.StateContext`；MainWindow每个状态对象独立按 Test*→StateContext→App.Current 解析（`Resolve` helper），所有 `app.MarkStateDirty()` 改为 `NotifyStateDirty()` 集中方法。行为/时间注入/存档/物理参数不变，现有测试无需修改。构建0警告0错误，全量636/636通过（Core 351 + App 285，含7个非空上下文回归）。S2 R2 F-01整改后待监理复核，见[整改记录](remediation/PREP-002-response.md)，未提交推送。

- S2监理R2：[报告](reviews/PREP-002-review.md)部分接受，独立47/47，完整上下文实际tick可用；F-01单个Test覆盖导致其他上下文依赖丢失待修，补逐对象独立解析与非空上下文回归。全量629为开发自测；整体in-progress，不继续后续抽取/PREP-003/双宠，不提交推送。S1/PREP-001保持接受。

- S2监理R3（2026-11-08）：[报告](reviews/PREP-002-review.md)关闭F-01并接受S2；独立54/54与原上下文混用反例通过，全量636为开发自测。下一小步仅抽取稀有事件计划评估/有效运行时间推进，先写清架构边界，探头位移/输入/物理统一入口不搬动；保持延迟评估、每日额度及保存语义。整体in-progress，任务末全量/实机烟测仍待完成，不进入PREP-003/双宠、不提交推送。

- S3稀有事件更新服务抽取（2026-11-08）：创建 `Platform/RareEventUpdateService`（静态服务，`EvaluateTick` 方法接收 tracker/scheduler/nowMs/today/isDragging/isExpressingGrievance/isPhysicsActive，返回 `RareEventUpdateResult` 记录——ShouldFire/StateChanged/PlanDate/PlanConsumed）。`OnUpdateTick` 稀有事件段改为调用服务方法，保留 `StartProbe`/探头超时 `EndProbe`/`NotifyStateDirty` 和 `_lastRareEventPlanDate`/`_lastRareEventPlanConsumed` 缓存更新在 MainWindow。行为/时间注入/存档/物理参数不变，`new Random()` 每次创建的随机源行为不变。构建0警告0错误，全量636/636通过（Core 351 + App 285）。待监理复核，未提交推送。

- S3监理R4（2026-11-08）：[报告](reviews/PREP-002-review.md)接受有限稀有事件更新抽取，独立生产相关81/81，调用顺序/延迟/消费/保存缓存保持；不再扩大重构。整体awaiting-verification，仅剩当前构建必要实机烟测与证据汇总。本轮同版本636全量自测可沿用，无变化无需因复核通过重跑；烟测修代码后按需相关回归。不提前done/PREP-003/双宠，不提交推送。

- 实机烟测与最终复核提交（2026-11-08）：用 `Run-ProbeLive.ps1 -Interactive` 隔离实例执行当前构建烟测（Debug/.NET 10 SDK 10.0.402/Win11 10.0.26200/DPI=144/单屏 2560×1600）。用户反馈全部正常：启动/悬停、身体拖动、头部拖动、轻放、抛掷、暂停恢复、找回、正常退出。探针自动验证探头启动/恢复/消费通过；隔离存档记录拖动后位置/恢复后退出/schemaVersion=3；生产存档哈希前后一致。夹具修复：复制列表补 `Diagnostics` 目录。S3后无代码变化，沿用636/636全量结果。已提交最终复核，待监理最终接收；不提前标done，不进入PREP-003/P3-001，不提交推送。

- 最终监理R5（2026-11-08）：[报告](reviews/PREP-002-review.md)接受S1～S3+现构建150%单屏必要烟测，PREP-002标done。烟测关键源码与当前一致、探头自动检查无失败、正式存档快照未变，用户操作正常为执行方转述，实际边界详报告；稳定证据已入reviews/evidence。全量636与已有独立专项沿用，无变化不重复。单DPI/Shell/ART限制保留，不宣称性能提升；下一步PREP-003待用户安排，不提交推送。

### PREP-003 当前单宠Release性能基线

- 状态：`done`。依赖：PREP-002。关联：D-018、TESTING性能记录。
- 范围：固定当前Release构建、同机环境采集空闲和真实拖动/抛掷负载；不发布安装包、不为性能测量改产品参数。
- 验收：按TESTING M-08记录版本/环境、实际经过时间/CPU分子同区间、工作集/私有内存/句柄、原始数据及退出结果；空闲至少5分钟，动态至少2分钟并标注输入方式及次数。至少两轮同条件测量，说明差异；复用同一构建，不为每轮重建。
- 限制：旧P0 Debug基线仅供历史背景，不直接把变化归因性能退化；不从短时增长推断泄漏，不在数据前指定任意CPU/内存承诺。若无可靠动态输入能力，保留未验证并安排用户操作，不把纯Core循环冒称实际窗口动态负载。
- 完成后：形成阶段3双宠可对照的单宠基线，计划交付后再按用户指令开P3-001-S1；不自动开工、提交或推送。

- PREP-003监理R1（2026-11-08）：[报告](reviews/PREP-003-review.md)接受四份CSV数值复算，但F-01显式目标进程归属待补；先找原始终端采样PID/启动DLL/退出材料，再决定仅补采无法确认轮次。脚本需显式PID/单调计时/元数据，报告修正CPU证明输入/噪声归因等过强措辞。保持awaiting-verification，不整批无条件重采，不进入双宠、不提交推送。

- PREP-003监理R2（2026-11-08）：[报告](reviews/PREP-003-review.md)部分接受显式PID/元数据/Stopwatch及措辞修正，F-01未关闭：CPU累计递增和隔离存档时长不足以绑定目标进程，原终端材料未落库。独立短时验证发现终点不足、提前退出仍exit0，详稳定证据；先完善入口并用短时正反例验证，再补原始材料或仅补采无法绑定轮次。保持awaiting-verification，不进入双宠、不提交推送。

- PREP-003监理R3（2026-11-08）：[报告](reviews/PREP-003-review.md)接受正常终点采集/身份负例及报告收窄，F-01部分通过；补齐终点失败误报成功、错误CSV列位和dynamic-r1原始材料。限定短测通过后直接补采缺证轮次，无需再等开采批准；收齐后一并最终复核。保持awaiting-verification，不进入双宠、不提交推送。

- PREP-003最终监理R4（2026-11-08）：[报告](reviews/PREP-003-review.md)关闭F-01并接收四轮新Release基线，PID/完整DLL身份/终点/时长/退出状态一致，CPU独立复算通过，终点读取失败反例非零退出。稳定证据已入reviews/evidence；旧四轮仅作历史参考，操作次数/退出来源/单屏DPI等限制保留。PREP-001～003完成，P3-001-S1须用户安排，不提交推送。

## 阶段 3：双宠社交（D-017，已规划未开工）

需求权威出处：[PRODUCT R-05](PRODUCT.md)；拟采用架构：[ARCHITECTURE §6](ARCHITECTURE.md)；验证步骤：[TESTING M-06](TESTING.md)。用户已确认性格与底边范围，本轮仅文档，应用实现须用户明确开工且先完成D-018工程准备；按顺序一次一个小步。

### P3-001 双宠共存与存档

- 状态：`done`（监理 [S3 R16 最终接收](reviews/P3-001-review.md)）。依赖：PREP-003（P2-004已完成）。关联：R-05、D-017、D-018。
- 范围：最多两只、稳定ID/性格配置、独立输入/动画/物理/委屈状态、添加/移除第二只、全局操作及兼容存档。不实现社交/自主移动/关系值或新美术。
- S1：核对单宠全局字段与存档v3，明确实例/全局数据归属；实现最小身份/集合及旧档迁移，写回架构/存档契约并验证。不开第二窗口前先交付可核对迁移证据。
- S2：将运行时状态按PetId隔离，创建第二窗口；通过真实共享输入入口证明拖动/抛掷A不改B；保留单宠回归和位置单写入。
- S3：接入添加/移除、全局暂停/恢复/找回/退出和实际保存/重载链；隔离文件验证旧档→双宠→移除→重启，最后实际双窗操作验收。
- 验收：按R-05共存目标与M-06执行，稳定ID/数量/角色恢复、独立状态、最多两只、移除后不复活、找回不完全重叠；新格式未知高版本/损坏保护不退化。自动/隔离/实际UI证据分开记录。无实测保持awaiting-verification，不自行签监理。

- S1 实现（2026-11-08）：存档升级到 v4（`PetStateArchive` 全局字段 + `Pets[]` 个体数组，`PetInstanceArchive` 9 字段含 `PetId`/`Personality`），`ArchiveService.DeserializeFile` 中 v3→v4 迁移用 `JsonNode` 从原始 JSON 提取旧扁平字段到 `pets[0]`，全局字段由 `JsonSerializer` 正常反序列化。App 4 个 Map 方法 + `RestoreWindowPosition` + `MarkStateDirty` 适配 `Pets[0]`。新增 `TestReloadStateFromArchive` 方法修复 probe 模式下 `Pets` 为空导致的位置值丢失。ARCHITECTURE §7 已写回 v4 存档契约。构建 0 警告 0 错误，全量 642/642 通过（Core 351 + App 291，含 6 个 v3→v4 迁移专项测试）。未提交推送，待监理复核。
- S1监理R1（2026-11-09）：[报告](reviews/P3-001-review.md)部分接受正常v3→v4迁移，独立相关57/57通过；F-01旧字段类型错误逃逸恢复、F-02非法个体条目导致启动映射崩溃待修，反例已落库。整体in-progress，整改后统一复核S1，不进入S2/S3、不提交推送。
- S1监理R2（2026-11-09）：[报告](reviews/P3-001-review.md)未通过；关键代码/测试/架构与R1哈希一致、所称整改记录不存在，两个原反例重构建后仍失败。先定位工作目录/保存版本，再实际整改F-01/F-02；不以642正常测试替代阻断验证，不进入S2/S3、不提交推送。
- S1监理R3（2026-11-09）：[报告](reviews/P3-001-review.md)独立相关67/67通过，F-01关闭；F-02防崩溃接受但备份恢复/原件保留及身份集合规则未完成，需补有区分度断言。仅修F-02，不进入S2/S3、不提交推送。
- S1监理R4（2026-11-09）：[报告](reviews/P3-001-review.md)关闭F-02，F-01保持关闭；独立相关74/74及原反例重跑通过，接收S1。整体P3-001仍in-progress，S2按PetId运行时隔离/第二窗口待用户安排；S3/双窗实机尚未完成，不提前done、不提交推送。
- S2监理R5（2026-11-09）：[报告](reviews/P3-001-review.md)独立相关37/37通过，方向部分接受；F-03个体恢复漏暂停退出冻结、F-04生产双窗共享输入链缺证待修。代码已存在按PetId创建窗口，不能再写S2未开工。S1不重开，不进入S3、不提交推送。
- S2监理R6（2026-11-09）：[报告](reviews/P3-001-review.md)独立相关46/46及暂停原反例通过，F-03关闭；F-04部分通过，仍须生产CreatePetWindow上下文/字典证据、A/B头部/抛掷tick共享入口及隔离窗口清理。仅补F-04，不重做已关闭项、不进入S3、不提交推送。
- S2 R6 F-04补证（2026-11-09）：[整改记录](remediation/P3-001-response.md)追加R6段。CreatePetWindow改为internal供测试调用；TestCleanup新增_petWindows清理（移除Closed handler→Close→Clear）；MainWindow新增OnUpdateTickTest。4项新F-04测试通过生产CreatePetWindow：字典/PetId/独立对象/共享全局/保存回调路由、A/B双向身体拖动+轻放+抛掷tick、A/B双向头部转拖动、抛掷→tick→飞行重新抓取，另一只不串改。MultiPetIsolationTests 20/20，全量679/679，0回归。待监理复核，不提交推送。
- S2监理R7（2026-11-09）：[报告](reviews/P3-001-review.md)独立相关46/46，审查加强副本4项工厂/手势通过；这些补证接受不再重做。F-04仅剩TestCleanup跳过未显示窗口导致计时器未停（独立反例失败），修复/清理断言及正式抛掷断言固化后由新监理复核；交接文件已更新。S1/F-01～F-03保持关闭，不进入S3、不提交推送。
- S2监理R8（2026-11-09）：[报告](reviews/P3-001-review.md)关闭F-04并最终接收S2；独立专项21/21及清理探针1/1通过，未显示/已关闭窗口清理与正式抛掷前置断言已核实。全量680为开发自测。S1和F-01～F-03保持关闭；整体仍in-progress，S3须用户另行安排，不进入社交、不提交推送。
- S3 实现（2026-11-09）：[整改记录](remediation/P3-001-response.md)追加S3段。AddPet/RemovePet（上限2只、PetId `bear-{n}` 不冲突、位置偏移200 DIP不重叠）；OnPetWindowClosed改为单只移除（最后一只才ShutdownMainWindow）；MarkStateDirty(null)遍历所有pets刷新位置（不再只刷新pets[0]）；OnRecoverRequested多宠偏移确保找回不完全重叠；右键菜单Add Pet/Remove Pet + 托盘菜单Add Pet入口；OnPauseToggled/OnRecoverRequested改internal供测试调用。8项S3隔离测试：添加/上限/PetId不冲突/位置不重叠、移除/存档清理/剩余仍运行、移除后重新添加新ID、MarkStateDirty(null)多宠位置刷新、完整双宠保存恢复链（身份/性格/grievance/位置）、移除不复活、全局暂停双宠、全局找回双宠不重叠。MultiPetIsolationTests 29/29（S2 21 + S3 8），全量688/688（Core 351 + App 337），0回归。M-06实际双窗操作验收步骤已准备，待用户实机验证。不提交推送，不进入P3-002/社交。
- S3 R9 整改（2026-11-09）：[整改记录](remediation/P3-001-response.md)追加R9段。F-05 RemovePet保护原首只（拒绝移除返回false）、OnPetWindowClosed检查_isShuttingDown跳过删除、右键Exit走ShutdownMainWindow而非Close、右键Remove Pet只对非原首只显示；F-06 AddPet PetId改为bear-{Guid.NewGuid():N}不复用旧ID；F-07 OnRecoverRequested偏移后MarkStateDirty保存最终位置、坐标比较修正DIP一致；F-08新增4项隔离测试（v3→迁移→AddPet→操作→退出→重载→CreatePetWindow→RemovePet→再重载完整链、双宠暂停恢复无积压、全局退出两只均保留、RemovePet拒绝原首只）+修正4项S3测试。MultiPetIsolationTests 33/33，全量341/341，0回归。ARCHITECTURE §6已同步增删/退出/身份约定。M-06完整实机验收步骤已更新（10步），待用户实机验证。不提交推送，不进入P3-002/社交。

- S3监理R10（2026-11-10）：[报告](reviews/P3-001-review.md)关闭F-06；独立全量Core351+App341=692/692、原反例加强版3/3通过。F-05移除/退出代码及F-07最终保存修正接受；剩余生产正常退出、完整窗口边界、新运行时恢复/运动暂停tick证据及当前可运行隔离实机入口。F-05/F-07/F-08部分通过，整体in-progress，不进入P3-002，不提交推送。

- S3 R10 整改（2026-11-10）：[整改记录](remediation/P3-001-response.md)追加R10段。F-05 DiagnosticProbe新增`dual-pet-exit`场景+`DualPetExit_ProductionShutdown_PreservesBothPets`子进程测试验证生产`ShutdownMainWindow`→`Shutdown(0)`自然退出+双宠存档保存；F-07 `AddPet`去除1800硬编码改用`MonitorInterop`workarea完整窗口边界检查、`OnRecoverRequested`偏移检查`Left+Width`/`Top+Height`双向clamp；F-08 fullchain Phase 8加`Show()`触发`SourceInitialized`→`RestoreWindowPosition`断言实际坐标/grievance/IsPaused/RuntimeTracker恢复、暂停测试throw启物理+注入时间5000ms+`OnUpdateTickTest()`验证冻结+恢复后无积压；实机夹具`Run-ProbeLive.ps1`/`ProbeLiveProbe.cs`适配`_petWindows`字典。MultiPetIsolationTests 33/33，DiagnosticIsolationTests 8/8，全量Core351+App342=693/693，0回归。夹具`-BuildOnly`构建成功。待监理复核，不提交推送，不进入P3-002/社交。

### P3-002 相互注意与协调

- 状态：`done`（监理 [R6 最终接收及 R7 观感确认](reviews/P3-002-review.md)）。依赖：P3-001（done）。关联：R-05。
- 范围：双方识别/注意、短暂回应与拒绝、间距、唯一会话和双方取消；此时再落实社交协调器，不预做追逐全框架。
- 验收：温和角色差异可观察；慢回应不延迟抓取；任一方被抓起/暂停/移除均释放双方；明确触发/冷却/超时，透明点不误中断。

- 实现（2026-11-10）：可调初值已写入 [ARCHITECTURE §6](ARCHITECTURE.md) P3-002 社交协调器参数表和 [PRODUCT R-05](PRODUCT.md) 参数表。Core 层新增 `Social/SocialConfig.cs`（12 参数）、`Social/SocialSession.cs`（状态结构+枚举）、`Social/SocialCoordinator.cs`（状态机+Tick 去重+ForceCancel）。App 层：`PetStateContext` 新增 `SocialCoordinator` 属性；`App.xaml.cs` 在 `OnStartup`/`TestSetupIsolatedSave` 中创建、在 `CreatePetWindow` 中注入、在 `RemovePet` 中 `ForceCancel`；`MainWindow.xaml.cs` 新增 `EvaluateSocialTick`/`BuildPetSocialState` 方法，在 `OnUpdateTick` 稀有事件块后调用，`isSocialActive` 传入 `RareEventUpdateService.EvaluateTick` 阻止稀有事件；社交表现复用 `_curiosity.Start/Cancel`（好奇表情），不写位置。`RareEventScheduler.Evaluate` 已有 `isSocialActive` → `DenyReason.SocialActive`。Core 单元测试 21/21 通过（`SocialCoordinatorTests.cs`），App 集成测试 15/15 通过（`SocialIntegrationTests.cs`：正常回应、拒绝/超时、拖动中断、头部按压中断、暂停中断、移除取消、唯一会话、性格差异、社交阻塞稀有事件、距离限制、不写位置、单宠无社交、清理）。全量 729/729 通过（Core 351 + App 378），0 回归。构建 0 警告 0 错误。隔离预览 `-BuildOnly` 构建成功、自动探针全部通过（`ALL PROBE LIVE CHECKS PASS`），单宠场景社交功能正常不崩溃。未提交推送，不自行标 done，提交监理审查。

- 监理R1（2026-11-10）：[报告](reviews/P3-002-review.md)独立相关36/36通过，但生产窗口反例6项失败，确认可见表现未接线、即时取消遗漏、移除冷却以0计时、双窗社交期间仍消费稀有计划。F-01～F-04阻断本任务接收和P3-003；仅整改本范围，P3-001不重开，不提交推送。

- R1整改（2026-11-10）：[整改记录](remediation/P3-002-response.md) R1段追加。F-01 EvaluateSocialTick按PetId分发InitiatorExpression/TargetExpression到各自窗口，新增ResolveExpressionForPet/ApplySocialExpression/IsCuriosityActive；F-02 新增CancelSocialImmediately在StartDragging/头部按压/PauseAnimation/RecoverPet/RemovePet事件入口即时取消+双方窗口同步清理；F-03 RemovePet通过幸存窗口CancelSocialImmediately取消，冷却基准用窗口当前时钟；F-04 OnUpdateTick稀有事件块读coordinator.IsSocialActive替代本地_isSocialActive。追加11项生产接线测试（断言双方IsCuriosityActive/IsSocialActive）。构建0警告0错误，全量740/740通过，0回归。状态waiting-verification待监理复核，不提交推送。

- 监理R2（2026-11-10）：[报告](reviews/P3-002-review.md)独立相关47/47、原反例5通过1失败；关闭F-02/F-03/F-04。F-01角色分发部分接受，但生产tick后CurrentAction仍idle，IsCuriosityActive仅计时器，不证明可见动作。仅收尾真实动画/渲染链，之后再双宠实机，不重做已关闭项，不进P3-003/不提交推送。

- R2整改（2026-11-10）：[整改记录](remediation/P3-002-response.md) R2段追加。F-01 ApplySocialExpression Notice/Respond路径接入_animPlayer.PlayAction("curious")+UpdateFrame()，CurrentAction实际切换为"curious"；Reject/ReturnToIdle恢复PlayAction("idle")；CancelSocialImmediately双方窗口恢复idle动画。多tick不重启（CurrentActionId守卫），不取消悬停独立好奇，不覆盖拖动/探头/委屈。升级4项R1测试+新增7项R2测试，断言CurrentAction（非仅IsCuriosityActive），覆盖发起方显示/对方延迟回应/拒绝/正常结束/拖动取消/暂停取消/跨窗口分发/多tick不重启。构建0警告0错误，全量747/747通过（Core 372+App 375），0回归。状态awaiting-verification待监理复核，不提交推送。

- 监理R3（2026-11-10）：[报告](reviews/P3-002-review.md)接受真实动画切换，原idle反例1/1和正式接线33/33通过；F-01仅剩独立悬停表情被Reject/结束/取消误清的归属问题（生产悬停反例1失败）。只补该点，F-02～F-04不重开，之后再实机，不进入P3-003/不提交推送。

- R3整改（2026-11-10）：[整改记录](remediation/P3-002-response.md) R3段追加。F-01 表情来源归属：新增`_socialOwnsCuriosity`独立所有权标志（不依赖IsCuriosityActive或CurrentAction），Notice/Respond设true，Reject/ReturnToIdle/CancelSocialImmediately仅当true时才Cancel+恢复idle+重置标志，false时no-op不误清悬停独立好奇。悬停ShouldEnd规则不受影响。新增6项R3归属测试。社交专项39/39通过，全量753/753通过（Core 372+App 381），0回归。状态awaiting-verification待监理复核，不提交推送。

- 监理R4（2026-11-10）：[报告](reviews/P3-002-review.md)原拒绝反例1/1、正式社交39/39通过；F-01仍剩独立悬停被Notice/Respond无条件接管后结束提前清理（真实入口反例1失败）。仅补不接管现有独立好奇/保留原期限的最小收尾，不重做已接收项，暂不实机/不进P3-003/不提交推送。

- R4整改（2026-11-10）：[整改记录](remediation/P3-002-response.md) R4段追加。F-01 Notice/Respond接管已有独立悬停：`ApplySocialExpression` Notice/Respond路径新增borrow分支——`_curiosity.IsActive && !_socialOwnsCuriosity`时break借用当前显示，不重新Start、不设`_socialOwnsCuriosity`、不重置原期限；仅社交实际新启动的表现才设所有权由社交释放。悬停`ShouldEnd`→`Cancel`不检查`_socialOwnsCuriosity`，原到时和离开规则不变。新增4项R4悬停接管测试（真实悬停→社交回应→结束保留原好奇、真实悬停→Reject保留原好奇、真实悬停→社交取消保留原好奇、悬停离开按原规则结束）。全量757/757通过（Core 372+App 385），0回归。状态awaiting-verification待监理复核，不提交推送。

### P3-003 被拖走后寻找

- 状态：`done`（监理R7隔离实机通过，F-01～F-04已关闭）。依赖：P3-002（done）。关联：R-05。
- 范围：等待释放/物理结束、同底边可达判断、寻找靠近并留间距；悬空只表达关注，不在抓取期间追鼠标。
- 验收：有正反可达路径；目标离屏/移除/超时退出；两只不永久重叠，不与物理/探头抢位置。具体时长/步速先写规格再实现。
- R3整改（2026-11-11）：[整改记录](remediation/P3-003-response.md)追加R3段。F-01捕获/采样/物理链：3个新测试复用ReleaseSamplingIntegrationTests生产共享入口（BeginCapturedPointerInteraction→ProcessCapturedPointerMove→EndCapturedPointerInteraction），甩出测试断言物理实际Launch→推进生产tick至自然停止→过SeekReactDelayMs后验证寻找，轻放测试慢速不Launch→延迟后寻找，取消测试isCancellation=true不评估/不Launch/不建立候选。DPI一致性修复：UpdatePhysics和RestoreWindowPosition改用TestMonitorList ?? MonitorInterop.EnumerateMonitors()。F-04 Show恢复重写：移除手工坐标赋值，改用Show→RestoreWindowPosition生产恢复，按PetId比较保存前/读回档案/新窗口实际坐标及状态，同步修复PendingGrievanceExpression/AccumulatedRuntimeMinutes/RareEventPlan三处状态同步。全量810/810通过（Core 402 + App 408），0回归，构建0警告0错误。待监理R4复核，不提交推送。

- 监理R4（2026-11-11）：[报告](reviews/P3-003-review.md)独立相关118/118，默认超时手势3/3，关闭F-01。F-04接受生产恢复方法和保存前后比对，独立副本移除Show后额外调用仍恢复失败（存档Left=500，实际100）。仅收尾启动恢复验证。
- R4整改（2026-11-11）：[整改记录](remediation/P3-003-response.md)追加R4段。移除`Seek_Restart_NewWindowRestoresPosition_NoOldSeek`中`Show()`后两次额外`app.RestoreWindowPosition`调用。复现结果：去掉额外调用后`Show()→SourceInitialized→RestoreWindowPosition`生产路径已正确恢复坐标，测试通过。定位结论：生产启动接线正确，`CreatePetWindow`注册的`SourceInitialized` handler在`Show()`时触发，`_state.Pets`和`TestMonitorList`均已就绪，`RestoreWindowPosition`通过`FindPetById`和`TestMonitorList`正确恢复。R4报告的失败在监理独立副本中，当前代码不需要额外恢复调用。坐标断言在正常启动流程结束后直接执行，保留按PetId坐标/性格/委屈/状态比较及fresh coordinator不续跑旧寻找断言。全量810/810通过（Core 402 + App 408），0回归。待监理R5复核，不提交推送。

- 监理R5（2026-11-11）：[报告](reviews/P3-003-review.md)正式专项22通过1失败，恢复单项复现。观察副本将夹具退出模式设为OnExplicitShutdown后正常触发SourceInitialized，完整测试通过。仅修正正式隔离夹具退出模式并复验。

- R5整改（2026-11-11）：[整改记录](remediation/P3-003-response.md)追加R5段。根因：测试夹具App实例`ShutdownMode`默认`OnLastWindowClose`，`TestCleanup`关闭最后一个窗口触发WPF auto-shutdown（`_appIsShutdown=true`），后续测试`Show()`不触发`SourceInitialized`。修复：新增`ResetWpfLifecycleForTest()`用反射设置`_shutdownMode=OnExplicitShutdown`+`_appIsShutdown=false`，在`TestSetupIsolatedSave`和`ShutdownMainWindowForTest`两处调用（不在`TestCleanup`中调用，会导致testhost崩溃）。反射绕过`set_ShutdownMode`的`VerifyAccess()`（测试上下文中Dispatcher可能已失效）。恢复单项1/1通过，寻找专项23/23通过testhost未崩溃，全量810/810通过（Core 402 + App 408），0回归。状态保持awaiting-verification待监理R6复核，不提交推送。

- 监理R6（2026-11-11）：[报告](reviews/P3-003-review.md)独立恢复单项1/1、寻找专项23/23及全量810/810通过，关闭F-04。F-01～F-04已关闭，待隔离实机体验。

- 监理R7（2026-11-11）：[报告](reviews/P3-003-review.md)隔离实机轻放寻找留距、悬空不追、同档重启通过。物理和中断沿用已通过的生产路径自动测试，接收P3-003。

### P3-004 短暂追逐及双宠阶段验收

- 状态：`done`（监理R6通过M-10，阶段3完成），用户于 2026-11-11 授权开工。依赖：P3-003（done）。关联：R-05。
- 范围：仅实现低频邀请、接受或拒绝、同底边短追逐及双方结束冷却。可调初值见 [PRODUCT R-05](PRODUCT.md)，时序和位置写入见 [ARCHITECTURE §6](ARCHITECTURE.md)。争位置、吵架、关系数值、独立游走和新美术留给后续需求。
- 验收：按 [TESTING M-10](TESTING.md) 验证自动正反例、隔离实机和双宠性能。交付实际命令、结果、隔离入口、性能原始样本和环境限制。开发自测后转 `awaiting-verification`，实机与监理验收后结案。

- 实现摘要（2026-11-11）：
  - `SocialConfig`：新增 11 个追逐参数（`ChaseAttemptIntervalMs`/`ChaseInviteProbability`/`ChaseCooldownMs`/`ChaseDurationMs`/`ChaseMaxTravelDip`/`ChaseLeaderSpeedDipPerSec`/`ChaseFollowerSpeedDipPerSec`/`ChaseMinDistanceDip`/`ChaseDistanceMinDip`/`ChaseDistanceMaxDip`/`ChaseClearanceDip`），`Clone()` 已更新。
  - `SocialSession`：新增 `ChaseInviting`/`Chasing`/`ChaseArrived` 枚举；新增 `ChaseLeaderId`/`ChaseDirection` 字段和 `WithChase` 方法；`IsActive` 已扩展。
  - `SocialCoordinator`：`SocialTickResult` 新增 `ChaseLeaderDelta`/`ChaseFollowerDelta`/`ChaseLeaderId`/`ChaseFollowerId`；per-pair 追逐评估去重；`Tick` 主流程 seeking 之后插入 `TryStartChaseInvite`；`CheckInterrupt`/`ForceCancel`/`CancelSession` 扩展追逐冷却；`AdvanceSession` 新增 3 个追逐 case；Chasing 阶段 16ms 去重防双窗倍速；跟随者最小间距约束。
  - `MainWindow.xaml.cs`：`EvaluateSocialTick` 新增追逐双窗位移消费（leader + follower 各自守卫写入）。
  - Core 追逐测试 29/29 通过；App 追逐集成测试 20/20 通过；全量 859/859 通过（Core 431 + App 428），0 回归，0 警告 0 错误。
  - 隔离体验入口：`Run-ProbeLive.ps1 -Interactive -Chase` 预置双宠同底边。
  - 性能对照入口：`Run-PerfIsolated.ps1 -TwoPets` 预置双宠用于性能采集。
  - 未提交/推送，待监理复核。

- 后续扩展：争位置/吵架及长期关系记忆，待前述体验后由用户安排，不属P3-001～004交付门槛，不自行恢复好感度。

- 监理R1（2026-11-12）：[报告](reviews/P3-004-review.md)独立相关166/166，6个反例失败。修复方向及工作区成对校验、寻找优先及独立冷却、卡顿补步上限、隔离双宠种子和真实底边。复核后再实机及性能对照。

- R1整改（2026-11-12）：[整改记录](remediation/P3-004-response.md)。F-01 `TryStartChaseInvite` 基于真实工作区边界和 `ChaseClearanceDip` 选择领跑方向（两侧都不够则不启动）；`AdvanceChasing` 添加双方投影边界前置校验（任一越界双方停止）和最终裁剪后最小间距校验；`BuildPetSocialState` 传递 `workAreaLeft`/`workAreaRight`。F-02 新增独立 `_chaseCooldownEndByPet` 字典；追逐冷却只阻止追逐不阻止注意/寻找；释放候选未过期时 chase 不启动；`MinIdleMs` 稳定空闲门槛。F-03 `AdvanceChasing` 中 `elapsedMs` 超过 `MaxChaseStepMs=100.0` 时截断，丢弃积压。F-04 两套隔离夹具种子 `personality` 改数字 `0`、`windowTop` 运行时 Win32 API 计算真实底边；`Run-PerfIsolated.ps1` 启动接缝适配 `w.Show()`。全量 859/859 通过（Core 431 + App 428），0 回归。存档测试 43/43、v4 双宠 19/19 通过。隔离 `-Chase -BuildOnly` 和性能 `-TwoPets` 构建成功。状态保持 `awaiting-verification`，待监理复核，不提交推送。

- 监理R2（2026-11-12）：[报告](reviews/P3-004-review.md)独立相关166/166、原6反例通过，关闭F-03/F-04。F-01仍按中心判边界且App分开写入，F-02拒绝中断绕过追逐冷却。只收尾两项，再实机和性能对照。

- R2整改（2026-11-12）：[整改记录](remediation/P3-004-response.md)。F-01 `PetSocialState` 新增 `WindowWidth` 字段（`BuildPetSocialState` 传 `ActualWidth`）；`TryStartChaseInvite` 余量改为完整窗口边缘（`waRight - (center + halfW)`）；`AdvanceChasing` 投影校验改为窗口边缘（投影左缘/右缘越界则双方停住）；`EvaluateSocialTick` 双窗改为先统一核对双方均可写入再成对提交。F-02 `ForceCancel`/`CancelSession` 中 `wasChasing` 判定从三个活跃追逐阶段改为 `ChaseLeaderId is not null`，覆盖 `Rejected` 阶段中断。新增 6 个 Core 反例测试。全量 865/865 通过（Core 437 + App 428），0 回归。状态保持 `awaiting-verification`，待监理复核，不提交推送。

- 监理R3（2026-11-12）：[报告](reviews/P3-004-review.md)独立相关172/172，F-02关闭。已显示双窗边界正反例通过，未布局测试的ActualWidth为0，且App拒绝提交仍需同步结束会话。仅收尾F-01。

- R3整改（2026-11-12）：[整改记录](remediation/P3-004-response.md)。F-01 三个待修点：(1) `BuildPetSocialState` 统一有效几何（`ActualWidth > 0 ? ActualWidth : Width`），中心和宽度同源；(2) `TryStartChaseInvite` 和 `AdvanceChasing` 改用各自窗口宽度计算余量和投影（不再统一用 petA/stateTgt 的 halfW）；(3) `EvaluateSocialTick` 任一窗口不可提交时调用 `ForceCancel` 即时结束会话（不依赖下一 tick）。新增 2 个 App 层反例：窄工作区两侧不足不邀请、一方不可写入双方原位且会话结束。全量 867/867 通过（Core 437 + App 430），0 回归。状态保持 `awaiting-verification`，待监理复核，不提交推送。

- 监理R4（2026-11-12）：[报告](reviews/P3-004-review.md)独立174/174及原反例13/13通过。F-01几何和成对位置通过，兜底只取消共享状态仍残留窗口社交表情。现有CancelSocialImmediately对照通过。仅收尾该点。

- R4整改（2026-11-12）：[整改记录](remediation/P3-004-response.md)。F-01 提交兜底从 `coordinator.ForceCancel` 改为 `CancelSocialImmediately`，同步清理双窗 `_isSocialActive`、`_socialOwnsCuriosity`、`_curiosity.Cancel()` 和动画恢复，保留独立悬停表现。新增 `TestBlockChaseCommit` test-only 注入点模拟 `Tick` 返回后、提交前一方不可写入。`Chase_R4F01_CommitRefusalClearsBothSocialExpressions` 替换原 R3 反例，验证双方原位、会话结束、社交表情清理。全量 867/867 通过（Core 437 + App 430），0 回归。状态保持 `awaiting-verification`，待监理复核，不提交推送。

- 监理R5（2026-11-12）：[报告](reviews/P3-004-review.md)独立174/174，正式提交兜底反例通过，关闭F-01。F-01～F-04已关闭，剩M-10体验和性能对照。

- R5关闭依据（2026-11-12）：[整改记录](remediation/P3-004-response.md)已追加R5关闭段。F-01～F-04全部关闭，P3-004保持`awaiting-verification`，剩M-10隔离追逐体验和同环境单/双宠Release性能对照，等用户安排。本轮无代码修改，未重跑测试，不标记`done`，不提交推送。

- 监理R6（2026-11-12）：[报告](reviews/P3-004-review.md)默认自然追逐、停步留距、实际点击暂停及同档重启通过。单/双宠 Release 空闲及动态各两轮，8轮正常采集和退出，原始样本已归档。接收P3-004。

## 阶段 4：作息与体验

### P4-001 全局活跃会话与恢复

- 状态：`done`。监理 R7 实机验收通过，F-01～F-04 均已关闭。依赖：P3-004（done）。关联：[R-06](PRODUCT.md#r-06-作息与休息反馈阶段-4)、[D-019](DECISIONS.md#d-019先建立独立的全局作息会话)、[架构 §7](ARCHITECTURE.md#7-时间和存档)。
- 范围：Core 根据可注入的时间、输入空闲和生命周期状态累计活跃会话。App 统一采样与接收锁屏、睡眠、暂停和恢复状态，单宠和双宠共用同一会话。达到阈值只产生一个可测试的提醒意图。当前会话留在内存中。
- 边界：保留稀有事件计时和 P3 的社交、物理、位置写入。新统计不进 `MainWindow.OnUpdateTick`，本小步不播放表演、不改存档、不添加夜间行为或输入封锁。
- 验收：[M-11](TESTING.md#m-11-全局活跃会话与恢复p4-001) 的 Core 阈值、App 单/双宠和隔离生命周期验证。开发写明实际命令、结果及未实测项后转 `awaiting-verification`。
- 实现（2026-11-12）：
  - Core 新增 `ActivitySession`（`src/DesktopPet.Core/Events/ActivitySession.cs`）：活跃窗口 60s、空闲重置 5min、阈值 120min 只发一次意图、Suspend/Resume 从新基线开始不补发。
  - App 新增 `InputIdleSampler`（`src/DesktopPet.App/Platform/InputIdleSampler.cs`）：Win32 `GetLastInputInfo` 采样，对齐 `MonotonicClock` 域，`GetLastInputTickMs` 可测试覆写。
  - App 新增 `ActivitySessionService`（`src/DesktopPet.App/Platform/ActivitySessionService.cs`）：1s 采样定时器、暂停/锁屏/睡眠三路 Suspend、全清后 `_pendingResume` 延迟到下次 tick 用正确时间域 Resume、`RestReminderRequested` 事件 + `ReminderFiredCount` 计数。
  - App.xaml.cs 接线：`OnStartup` 创建并 `Start`、`SubscribeSystemEvents`（`Microsoft.Win32.SystemEvents.SessionSwitch`/`PowerModeChanged`）、`OnPauseToggled` 通知、`ShutdownMainWindow`/`OnExit` 释放订阅并 `Dispose`、`TestSetupIsolatedSave`/`TestCleanup` 管理测试生命周期。
  - 测试：Core `ActivitySessionTests` 21 项（活跃窗口、空闲停计、短暂离开续计、5min 清零、阈值只发一次、重置后可再发、Suspend/Resume 不补发、暂停锁屏重叠、无效输入拒绝、分数累积）；App `ActivitySessionServiceTests` 13 项（单/双宠一致、增删宠物不变会话、Dispose 释放、暂停恢复新基线、稀有事件独立、提醒计数、锁屏/睡眠模拟、暂停+锁屏重叠、睡眠+锁屏重叠、不补发提醒、采样 null、多次暂停恢复保持值）。
  - 构建 0 警告 0 错误。全量 901/901 通过（Core 458 + App 443），0 回归。
  - 未实测项：真实锁屏/解锁、真实睡眠/唤醒需人工验证；模拟通知与真实系统操作已分开记录。

- 监理R1（2026-11-12）：[报告](reviews/P4-001-review.md)正式专项34/34，独立5反例失败。修正缺测补计与误清零、真实睡眠枚举/启动暂停、输入计数回绕及App验收证据。

- 监理R2（2026-11-12）：[报告](reviews/P4-001-review.md)正式56/56，原5反例通过，新增3项反例失败。收尾缺测与回退计时、样本时间一致性、生命周期串行和可运行App验证。

- 监理 R3（2026-11-12）：[报告](reviews/P4-001-review.md)与[证据](reviews/evidence/P4-001-R3/)已更新。专项 60/60、全量 927/927，2 项独立反例复现。系统回调串行已通过，待完成缺测恢复、有效输入年龄及正常 App 隔离验证。

- 监理 R4（2026-11-12）：[报告](reviews/P4-001-review.md)与[证据](reviews/evidence/P4-001-R4/)已更新。专项 62/62，原独立反例 2/2 通过。F-03 关闭，F-01 缺测后空闲转换及 F-04 App 隔离证据待收尾。

- 监理 R5（2026-11-13）：[报告](reviews/P4-001-review.md)与[证据](reviews/evidence/P4-001-R5/)已更新。专项 64/64，原反例 1/1。F-01 关闭，F-04 App 接线及清理已接受，M-11 入口待修。

- 监理 R6（2026-11-13）：[报告](reviews/P4-001-review.md)与[证据及运行命令](reviews/evidence/P4-001-R6/)已更新。专项 64/64，M-11 观察入口通过，F-04 关闭，待真实锁屏和睡眠验收。

- 监理 R7（2026-11-13）：[最终报告](reviews/P4-001-review.md)及[实机证据](reviews/evidence/P4-001-R7/README.md)。M-11 真实锁屏/解锁及睡眠/唤醒通过，累计冻结、恢复无补计、提醒未补发。P4-001 验收完成。

### P4-002 可跳过的休息提醒动作

- 状态：`done`。监理 R11 完成 Windows 生命周期实机验收，F-01～F-05 已关闭，依赖 P4-001（done）。按 [R-06](PRODUCT.md#r-06-作息与休息反馈阶段-4)、[接入约定](ARCHITECTURE.md#p4-002-休息提醒接入约定待实现) 实施。
- 交付：App 提醒接线、默认小熊可运行的动作资源、可观察的短阈值隔离预览，以及 [M-12](TESTING.md#m-12-无弹窗休息提醒p4-002) 的自动与实机证据。缺素材时先提供有完整时序的可运行预览供观感确认。
- 边界：保留 P4-001 的正式计时与状态语义，不改存档，不扩展夜间行为。避免与提醒无关的窗口重构。
- 完成条件：自动测试验证只选一只、可正常结束与立即取消、释放状态后既有交互可继续。可见性、辨识度和实际输入由隔离预览验收。开发自测后转 `awaiting-verification`。
- 已有依据：R2 三项整改已收尾；F-01 二次收尾完成——锁屏/睡眠回调的「集合读取→快照→取消」整体编组到拥有窗口集合的 UI Dispatcher（`_petWindowsDispatcher`，生产即 App UI 线程），排队期间移除/关闭的窗口不触碰、期间新建的窗口被包含，新增测试 `QueuedLockCancel_WindowClosedAndAddedDuringQueue_SnapshotAtExecution`。F-03 选窗读共享 `SocialCoordinator` 权威状态且要求 `stretch`+`rest` 同时存在；M-12 右键菜单 Pause/Resume 入口。全量 981/981 通过（Core 486 + App 495），0 回归，探针构建 0 警告 0 错误。隔离预览 exe 实跑通过（stretch→rest→约 30s 自然回 idle），日志 `artifacts\p4-002-r2-preview\rest-reminder\`。未验证：真实锁屏/睡眠系统事件、真实鼠标输入、观感。[整改记录](remediation/P4-002-response.md)。未提交/推送，保留 P4-001 R4 整改的未提交修改。

- 监理 R1（2026-11-13）：专项 35/35，独立反例 3 项失败。详见[审查报告](reviews/P4-002-review.md)。

- 监理 R2（2026-11-13）：专项 44/44，原反例 3/3 通过，后台通知反例 1 项失败。可见预览与自然结束已验证。[报告](reviews/P4-002-review.md) · [证据](reviews/evidence/P4-002-R2/README.md)。

- 监理 R3（2026-11-13）：独立专项 50/50，原反例 4/4。F-03 关闭，F-01 剩窗口集合访问需在所有者线程完成。[报告](reviews/P4-002-review.md) · [证据](reviews/evidence/P4-002-R3/README.md)。

- 监理 R4（2026-11-13）：独立专项 51/51，含后台锁屏、睡眠和排队期间窗口增删。F-01 关闭，进入 M-12 实机验收。[报告](reviews/P4-002-review.md) · [证据及预览命令](reviews/evidence/P4-002-R4/README.md)。

- 监理 R5（2026-11-13）：隔离实机自然收尾通过，新增 F-04 开场资源问题。实机输入与观感待补。[报告](reviews/P4-002-review.md) · [证据](reviews/evidence/P4-002-R5/README.md)。
- F-04 整改（2026-11-13）：stretch_0/1/2 以 idle_0 为底 PIL 重绘为三帧打哈欠，消除首帧 y≈42-50 黑线；脚点 y=103-110 与 idle 逐像素一致，时序不变。全量 981/981，探针构建 0 警告 0 错误，隔离预览实跑 stretch→rest→约 30s 回 idle（日志 `artifacts\p4-002-r5-f04-preview\rest-reminder\`）。待监理复核，随后 M-12。[整改记录](remediation/P4-002-response.md)。

- 监理 R6（2026-11-13）：F-04 关闭。开场黑线消失，原尺寸三帧可读成打哈欠，脚点像素一致。剩 M-12 实机验证。[报告](reviews/P4-002-review.md) · [证据](reviews/evidence/P4-002-R6/README.md)。

- 监理 R7（2026-11-13）：新素材隔离运行 30 秒回 idle，70 秒清理退出，真实输入、生命周期及观感待补。[报告](reviews/P4-002-review.md) · [证据与补测步骤](reviews/evidence/P4-002-R7/README.md)。

- 监理 R8（2026-11-13）：短按、拖动、右键暂停/恢复通过，透明点命中下方窗口。[实机证据](reviews/evidence/P4-002-R8/README.md)。

- 监理 R9（2026-11-13）：捕获实机哈欠帧，右键移除通过；发现打开菜单会提前覆盖休息动画。[报告](reviews/P4-002-review.md) · [证据](reviews/evidence/P4-002-R9/README.md)。
- F-05 整改（2026-11-13）：`EndDrag` 仅在真实拖动结束（`_isDragging`）时重置动画；菜单开/关提取 `EnterContextMenuMode`/`ExitContextMenuMode` 与共享命令分发 `HandleContextMenuCommand`，新增测试缝 `TestOpenContextMenuLifecycle`。新增对照测试 3 项：菜单开/关未选命令保持 stretch/rest 与提醒状态并自然收尾、选 Pause 取消且恢复不重播、选 Recover 取消。找回/移除/退出命令路径未变。全量 984/984（Core 486 + App 498），专项 RestReminder 32/32。待监理复核。[整改记录](remediation/P4-002-response.md)。

- 监理 R10（2026-11-13）：专项 55/55，原生菜单打开/取消和暂停/恢复均实机通过，F-05 关闭。[报告](reviews/P4-002-review.md) · [证据](reviews/evidence/P4-002-R10/README.md)。

- 监理 R11（2026-11-16）完成真实锁屏、解锁和睡眠、唤醒验收。休息提醒取消后未续播。详见 [报告](reviews/P4-002-review.md) 与 [实机日志](reviews/evidence/P4-002-R11/README.md)。

### 后续阶段 4 小步

- `P4-003`：`done`，见下方专节。

### P4-003 夜间困倦与跨日验证

- 状态：`done`。监理 R6 已完成 M-13 及真实 Windows 唤醒复测，F-01～F-03 已关闭。依赖：P4-002（done）。关联：[R-06](PRODUCT.md#r-06-作息与休息反馈阶段-4)、[架构 §7](ARCHITECTURE.md#7-时间和存档)。
- 范围：本地 23:30（含）至次日 06:00（不含）宠物进入困倦状态，`sleep` 循环动作（浅睡→深睡→浅睡）作为夜间空闲底态；明确与拖动/抚摸、暂停、稀有事件、休息提醒、社交的优先级；交互结束、恢复或唤醒后按当时本地时间重新判断。
- 边界：不改存档格式，不改系统时间（预览用注入时钟），社交保留可打断睡眠，不新增状态标志（用 `CurrentActionId=="sleep"` 判定）。
- 验收：[M-13](TESTING.md#m-13-夜间困倦与跨日验证p4-003) 可控时钟边界测试与隔离预览。
- 实现（2026-11-16）：
  - Core 新增 `NightSchedule`（`src/DesktopPet.Core/Events/NightSchedule.cs`）：纯本地时间判断，`IsNight(t) = t >= 23:30 || t < 06:00`。
  - 素材：`assets/pets/bear/sleep_light.png`（idle_0 底＋闭眼弧）、`sleep_deep.png`（同底＋闭眼＋Z 角标），脚点行 y=103–110 与 idle_0 逐像素一致；`character.json` 注册 `sleep` 循环动作（2500/4000/2500ms）。
  - MainWindow：新增 `TestGetLocalNow` 注入缝；`OnUpdateTick` 末尾 `UpdateFrame` 前新增夜间接线（夜间且 idle 且非忙碌 → play sleep；非夜间且 sleep → idle）；hover/curiosity 分支与稀有事件 `Evaluate`、`StartProbe` 在睡眠期间跳过（`RareEventUpdateService.EvaluateTick` 新增 `isSleeping` 参数，计划不消耗）。
  - 优先级（高→低）：拖动/抚摸 > 暂停（冻结）> 委屈 > 探头 > 休息提醒表演 > 社交（可打断，结束经 idle 回睡眠）> 夜间睡眠 > idle。
  - 测试：Core `NightScheduleTests` 8 项（23:29/23:30/23:59/00:00/05:59/06:00/正午/深夜）；App `NightSleepyIntegrationTests` 10 项（23:29→23:30 入睡、午夜跨日持续、05:59→06:00 唤醒、正午不入睡、拖动打断后重入、hover 睡眠压制+白天对照、稀有事件睡眠压制+醒后补发、休息提醒优先+完成后同 tick 回睡、暂停跨 06:00 恢复唤醒、暂停跨 23:30 恢复入睡）。
  - 探针：`night-sleepy` 场景（注入虚拟本地钟 23:29 起，1 实秒=1 虚拟分钟跨 23:30，保持供人工拖动观察，跳 05:59→06:00 验证唤醒，60s 自动退出）。
  - 构建 0 警告 0 错误。全量 1002/1002 通过（Core 494 + App 508），0 回归。隔离预览实跑 exit=0，观测 jsonl 记录 idle→sleep（23:30）→sleep（05:59）→idle（06:00）。
  - 预览命令：`tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe <baseDir> night-sleepy <resultFile>`（约 5–35s 间可拖动小熊观察打断重入）。
  - 未实测项：真实系统时钟夜间体验、人工拖动打断的实机观感、双宠同时睡眠的观感需人工/监理验证；自动化预览已覆盖边界逻辑与拖动打断重入（集成测试走真实 EndDrag 路径）。
- 未提交/推送，待监理复核。

- 监理 R1（2026-11-16）独立专项 18/18，时段边界通过，头部交互与预览判定待整改。详见 [审查报告](reviews/P4-003-review.md)。

- R1整改（2026-11-16）：[整改记录](remediation/P4-003-response.md)。F-01 `BeginCapturedPointerInteraction` 入口统一打断：有效实体按压（头部按压与身体拖动两路）在守卫后若 `CurrentActionId=="sleep"` 立即 `PlayAction("idle")`，画面不再滞留睡眠帧；松手经 `EndPetting`/`EndDrag` 回 idle 后由夜间接线按当时本地时间重判（夜间回睡、过 06:00 保持 idle），无新状态标志，F-10 探头契约不受影响（探头期间动作是 `curious` 非 `sleep`）。F-02 探针 `NightSleepyVerification` 判定重写：新增 `realDragSeen`（逐秒采样 `IsDragging` 真实拖动证据），`dragInterruptSeen`/`sleepReentrySeen` 仅凭真实拖动序列置位且限定 05:59 保持窗口内；采样加密到 1s 并在 jsonl 记录 `isDragging`；新增 `night-sleepy-noinput` 无输入对照场景。新增 App 测试 3 项（头部按压打断+松手回睡、身体按压打断、松手过 06:00 不回睡）。全量 1005/1005 通过（Core 494 + App 511），0 回归，构建 0 警告 0 错误。探针两轮实跑：带拖动轮（SendInput 真实按压拖动松手）四标志全 true、exit=0；无输入对照轮 drag 相关三标志全 false、exit=0。产物 `artifacts/p4-003-preview-r1*/`。状态保持 `awaiting-verification`，待监理复核，随后 M-13 实机验收，不提交推送。

- 监理 R2（2026-11-16）专项 21/21，原反例 1/1，无输入对照无误报。F-01、F-02 已关闭，实机验收待补。详见 [报告](reviews/P4-003-review.md) 和 [证据](reviews/evidence/P4-003-R2/README.md)。

- 监理 R3（2026-11-16）双宠进出夜间和实机拖动回睡通过。新增 F-03 睡眠素材辨识问题，系统唤醒跨时段待补。详见 [报告](reviews/P4-003-review.md) 与 [证据](reviews/evidence/P4-003-R3/README.md)。

- R3整改（2026-11-16）：[整改记录](remediation/P4-003-response.md)。F-03 仅重绘 `sleep_light.png`/`sleep_deep.png`：基底 `idle_0`，眼区（x38-81, y40-55）整体复制 `blink_closed.png` 闭眼区域（瞳孔/高光全部消除），眼睑加厚 1px（睡眠重于眨眼）；deep 另附三枚 Z（48px）。脚点行 y103-110 与 idle_0 逐像素一致；`character.json` 时序与播放逻辑未改。像素断言全过：light vs deep 恰差 48px（仅 Z），无瞳孔残留。双宠观感复验（R3 隔离探针副本，构建 0 警告 0 错误，`night-sleepy` 实跑 exit=0）：两只小熊 23:30 入睡、06:00 唤醒，实屏抓取与新 `sleep_deep` 素材模板比对 0/6098 失配，浅/深差异仅 Z 标、120×120 下可辨认。状态保持 `awaiting-verification`，待监理复核，未提交/推送。

- 监理 R4（2026-11-16）确认两只小熊的实际浅睡、深睡画布，F-03 关闭，系统唤醒跨时段待验。详见 [报告](reviews/P4-003-review.md) 与 [证据](reviews/evidence/P4-003-R4/README.md)。

- 监理 R5（2026-11-17）真实睡眠、唤醒事件已记录，隔离探针提前退出，已修订观察时限并待复跑。详见 [报告](reviews/P4-003-review.md) 和 [证据](reviews/evidence/P4-003-R5/README.md)。

- 监理 R6（2026-11-17）真实 Windows S3 唤醒后两只小熊按隔离 06:00 回待机，M-13 验收完成。[报告](reviews/P4-003-review.md) · [实机证据](reviews/evidence/P4-003-R6/README.md)。

## 阶段 5：窗口生活

### P5A-PLAN 外部窗口读取与失效验证

- 状态：`done`（2026-11-18）。[监理 R5](reviews/P5A-PLAN-review.md) 关闭 F-03，F-01～F-03 全部关闭。M-14 两轮各 17/17，均为单屏 144 DPI；只读探针与证据验收完成。见[整改记录](remediation/P5A-PLAN-response.md)。按 [R-07](PRODUCT.md#r-07-窗口栖息阶段-5a) 和[架构 §9](ARCHITECTURE.md#9-后期-windows-能力边界)工作。
- 交付：一个独立的只读 Windows 探针。枚举普通、可见、未最小化的外部顶层窗口，记录候选筛选、可见外框、屏幕与坐标单位。以可见外框上缘作为候选栖息面，记录该处被遮挡时的判定及限制。
- 跟踪一个明确选择的测试窗口，检查移动、最大化、最小化、恢复、关闭的状态。关闭或失去资格后清除当前目标。恢复或发现同一数值句柄时须重新验证目标。
- 功能范围仅限探针与验证。栖息、晃动、攀爬和正式窗口行为留待各自任务。
- 按 [M-14](TESTING.md#m-14-外部窗口读取与失效验证p5a-plan) 交可执行命令、自动与实机证据，并记录混合 DPI、跨屏和遮挡的实测范围及限制。开发自测后转 `awaiting-verification`。
- R1 整改（2026-11-18，原始 sidecar 为单屏、主 DPI=96；多屏与混合 DPI 未测）：
  - F-01：update 基线改为上一次已上报外框（`PerchUpdateGate`），驱动补静止不重复/移回初始有更新/快速移动最终外框一致/真实鼠标拖动（`mouse_event` 注入，含光标回读+命中+前台三重前置校验）。
  - F-02：Core 拒绝 `WindowGone` 后同数值句柄自动恢复，并保留最小化及隐藏后的原窗口还原用例；探针自动重选分支待收尾。
  - 实机证据：记事本轮 `.temp/p5a-plan/m14/run-20261117-222604-notepad/` 16/16 ALL PASS；Edge 轮 `.temp/p5a-plan/m14/run-20261118-010338-edge/` 16/16 ALL PASS（含真实鼠标拖动、遮挡/解除、重开 reacquire 新句柄、探针 exit=0）。驱动正式入口 `tools/Invoke-M14WindowPerch.ps1`；重开窗口归属流程待 F-03 整改。
  - 限制：遮挡由自建覆盖窗模拟（真实用户窗口动态遮挡未测）；采样间隙内身份字段完全相同的句柄复现纯轮询不可区分。
- R2 整改（2026-11-18，实机单屏、主屏 96 DPI；多屏与混合 DPI 未测）：
  - F-02：移除探针 Cleared 状态下自动 Reset+Select 回退。Core 新增 `WindowPerchSelectionPolicy`（`WindowGone`/`HandleReused` 后同数值候选判 `RejectedSameHandle`，仅显式新选择或最小化/隐藏还原可重获）；探针同值被拒记 `reacquire-denied` 事件并保持 Cleared。新增 5 项策略单测（持续轮询 25 次不重绑、显式新选择才绑定、最小化/隐藏还原自动重获、新句柄自动重获），Core 529/529。
  - F-03：驱动 `tools/Invoke-M14WindowPerch.ps1` 重开窗口归属校验前置：候选窗口须宿主 PID ≠ 驱动进程且 StartTime ≥ t0−2s 才可操作；未过现有门槛的候选只读跳过；notepad 轮加入驱动同进程诱饵对照；重开检查收紧为 owned=True 且探针句柄一致。
  - 实机证据：记事本轮 `.temp/p5a-plan/m14/run-20261118-015936-notepad/` 17/17 ALL PASS（含诱饵对照 unchanged、externalSkipped=1、真实鼠标拖动、探针 exit=0）；Edge 轮 `.temp/p5a-plan/m14/run-20261118-020304-edge/` 16/16 ALL PASS（含真实鼠标拖动、重开 reacquire 新句柄）。事件日志在各 run 的 `window-perch/` 子目录。
  - 教训证据：失败轮 `run-20261118-014959-notepad/`（驱动进程 StartTime 落入容差导致诱饵误判）按当时版本保留。
  - 限制：真实用户窗口动态遮挡未测；仍需补启动后新进程的无关候选对照。
  - F-03 收尾（2026-11-18，R3 后补证，R4 已核对，两轮均为单屏 144 DPI）：初开/重开均以本轮独有标记文件 `P5AM14NP-<ts>` 绑定目标（`Start-Process notepad` 直接打开，删除全部 `SetWindowText` 改名），归属按宿主 `ProcessName` + `StartTime >= t0-2s` + pid 非驱动非诱饵确认，Win11 记事本启动器把窗口交给子进程故不按启动 pid 匹配；归属不明只读跳过。诱饵改为独立 pwsh 子进程的不同 PID 窗口（驱动启动后出现，标题符合旧规则 `*- Notepad`），终局断言其不被改标题/移动/关闭。新增防线：启动时清理历史 run 目录旧标记文件（防记事本会话恢复标签页标题漂移）、归属后 1.5s 标题漂移中止。实机补证：`run-20261118-090518-notepad/` 17/17 ALL PASS（真实鼠标拖动 updateCount=4；诱饵 `titleUnchanged=True rectUnchanged=True alive=True`）、`run-20261118-090839-edge/` 16/16 ALL PASS。过程教训按当时版本保留：`run-20261118-085006/085703-notepad`（Win11 记事本标签页占用标题栏中部，中心抓取点非 `HTCAPTION`，注入后窗口零位移；修复为 `WM_NCHITTEST` 确认 `HTCAPTION` 否则扫描可拖动点）、`run-20261118-080353-notepad`（旧标记被会话恢复为标签页，环境问题）、`run-20261118-024622-notepad`（pid 匹配过严 abort）。
  - F-03 Edge 收尾（2026-11-18，R4 意见：旧固定标题 `P5AM14Browser` 两轮并行会互相误选）：Edge 页面标题改为本轮唯一标记 `P5AM14Browser-<ts>`（run 目录 html `<title>`），页面/探针/驱动共用；初开/重开归属确认前置——宿主 msedge 命令行须含本轮 profile 路径 `--user-data-dir="<runDir>\edge-profile"`（带引号精确匹配）+ `StartTime >= t0-2s` + pid 非驱动，其他轮/其他 profile 按归属不明只读跳过。其他轮对照：另一个真实 msedge（独立 profile `edge-profile-other`、旧固定标题 `P5AM14Browser`、驱动启动后出现），终局断言其被跳过、零写入（标题/位置不变、存活、从未被探针选中），本轮标记窗口正常通过全部检查。实机补证：`run-20261118-092951-edge/` 17/17 ALL PASS（`otherPid=31708 driverPid=46672 titleUnchanged=True rectUnchanged=True alive=True neverSelected=True`；本轮 select/reacquire 句柄 0xF41374/0x3210FA；真实鼠标拖动 updateCount=4；探针 exit=0）。Core/探针未改，记事本轮证据沿用 `run-20261118-090518-notepad/`。

### P5A-001 窗口栖息首版

- 状态：`done`（2026-11-18）。[监理 R5](reviews/P5A-001-review.md) 关闭 F-02，F-01～F-04 全部关闭，接收窗口栖息首版。见[开发整改记录](remediation/P5A-001-response.md)。范围见 [R-07](PRODUCT.md#r-07-窗口栖息阶段-5a)。
- R4 收尾整改（2026-11-18，F-02 跨窗口时序）：`MainWindow` 新增 `IsInSharedSocialSession`（读共享协调器权威会话，及时生效），`IsPerchWriteAllowed` 与 `ApplyPerchFoot` 追加该判定——A 的生产追逐实际移动 B 时，B 即使未 tick 也拒绝栖息初选/跟随/落位；会话结束即恢复，无额外状态复位。补 App 反例 `Perch_R4F02_ChaseMovesB_BeforeBTick_BRejectsPerchWrites_RestoresAfter`（生产追逐链路，B 全程未 tick）。验证：构建 0/0，Core 548/548、App 528/528，四轮 M-15 证据沿用不重跑。待监理复核，未提交/推送。
- R3 两项收尾整改（2026-11-18）：F-02 栖息/社交共用位置所有权判定——`MainWindow` 收拢 `HasCorePositionConflict` 五旗互斥核心与 `IsPerchOwned`，新增 `IsSocialPositionWriteAllowed => !核心冲突 && !IsPerchOwned`；seek/chase 写入点逐旗守卫替换为该共享判定（chase paired-commit 与 `TestBlockChaseCommit` 钩子语义不变），先到者持有位置、另一方让出。F-04 生产轮询跟随遮挡补证——`PetWindowPerch` 新增 `TestSnapshotSource` 测试缝（仅替换快照采集，其余 `OnTick` 生产路径不变）。补 App 单测 2 项（同一宠物两种位移请求只有获准路径写入；目标移到被遮挡位置后生产轮询零 `follow`、恰一条 `occluded-detach`、小熊保持原位、绑定解除）。验证：构建 0/0，Core 548/548、App 527/527（`WindowPerchOwnershipTests` 14→16），四轮 M-15 证据沿用不重跑。待监理 R4 复核，未提交/推送。
- R2 剩余项整改内容：`RecoverPet` 入口 `_petPerch?.Detach("recovered")`（找回取回位置所有权，目标再移动不拉走）；`TrySelectExplicit` 重选失败保留旧绑定（旧跟踪器/计时器/栖息前位置快照恢复，下一轮轮询不读空目标，恢复失败完整停用）；补 App 单测 5 项（重选失败反例、找回解绑、F-03 真实存档往返——落盘栖息前坐标、新窗口不续跑旧绑定、F-04 初选被盖零位移、跟随被盖先脱离零写入）。验证：构建 0/0，Core 548/548、App 525/525（`WindowPerchOwnershipTests` 9→14），四轮 M-15 证据沿用不重跑。
- R1 整改内容：F-01 完整宠物矩形可见落位（最大化容不下→`detached(no-landing)` 宠物保持完整可见，`Screen.FromPoint` 动态屏界断言）；F-02 栖息接入位置所有权守卫（暂停/物理/拖动/社交等占用时拒绝初选与跟随写入，不报 `landed`）；F-03 关闭清理（`OnClosed` 停轮询解绑，退出先恢复栖息前位置再存档，重启不续接旧目标）；F-04 先校验后写入（落脚区遮挡采样=实际落脚区，只遮落脚区外远端不脱离，落脚区被盖拒绝或 `occluded-detach`）。
- R1 整改后验证：构建 0 警告 0 错误，Core 548/548、App 520/520（新增 Core 8 项、App 9 项反例，App 新建 `WindowPerchOwnershipTests`）；M-15 四轮实机 ALL PASS（notepad×2、edge×2，每轮 15 项，新增最大化 no-landing、远端遮挡不脱离断言，含真实鼠标拖动接管）：`.temp/p5a-001/m15/run-20261118-161637-notepad/`、`run-20261118-163221-notepad/`、`run-20261118-163324-edge/`、`run-20261118-163417-edge/`。探针写状态竞争崩溃已修（`WindowPerchLiveScenario.cs` WriteState 重试，测试工具，产品代码未改）。复跑命令见 [TESTING M-15](TESTING.md#m-15-窗口栖息首版隔离预览p5a-001)。
- 初版实现（R1 前）：Core 三策略（`WindowPerchLandingPolicy` 上缘中点落位、`WindowPerchOcclusionPolicy` fraction<1 脱离、`PerchDragPreemptionPolicy` 拖动取消句柄不自动重选）；App `Platform/WindowPerchInterop.cs`（只读采集）+ `Platform/PetWindowPerch.cs`（120ms 轮询、跟随、遮挡采样脱离、`CancelForDrag`/`DetachForPause`/`Detach`）；MainWindow 接线（`StartDragging` 内 `CancelForDrag`、Perch 属性、`ApplyPerchFoot`）。R1 前历史证据：`.temp/p5a-001/m15/run-20261118-123626-notepad/`、`run-20261118-150731-edge/`（各 14 项 PASS，最大化断言后被 R1 判定误导）。
- 未测项：跨屏、混合 DPI、真实用户窗口动态遮挡（遮挡由自建覆盖窗模拟）。晃动、主动靠近留待后续，攀爬仍在 R-08。未提交/推送。
- 首步用隔离预览显式选中一个合格外部窗口，使一只宠物落位在其可见外框上缘，目标移动或最大化时随之更新位置。沿用探针验证过的只读窗口数据，核对物理像素到宠物窗口 DIP 的换算。
- 初版遮挡策略采用脱离。目标关闭、最小化、失去资格、上缘落位处被遮挡，或没有可用落点时解除绑定，回到可见区域。用户开始拖动时立即交回位置控制；暂停、退出或恢复数据后不续接旧目标。目标识别不把 HWND 当作永久身份，绑定只在内存中。
- 沿用受守卫的位置写入边界。外部窗口查询或事件回调只提供观测；仅改变宠物自身的位置。首步先验证落位、跟随、脱离；晃动与主动靠近留待同阶段后续安排，攀爬仍在 R-08。
- 有针对性的自动验证覆盖落位、目标移动、失效、遮挡和拖动抢占。提交可运行的隔离预览，用自建记事本与浏览器窗口记录移动、最大化、最小化、遮挡、关闭和拖动后无回拉。记录实际缩放及跨屏验证范围，通过自测后转 `awaiting-verification`。

### P5A-002 窗口栖息晃动反馈

- 状态：`done`（2026-11-18）。[监理 R4](reviews/P5A-002-review.md) 关闭 F-03，F-01～F-03 全部关闭，接收窗口栖息晃动反馈。见[整改记录](remediation/P5A-002-response.md)。
- R3 补充取证（2026-11-18，监理 R4 已接收）：F-03 探针在接管处理前后各写一条带时间戳角度记录（`WindowPerchLiveScenario`：`drag-preempted` 同步回调写 `preempt-angle-pre`，`Dispatcher` 排队在 `CancelForDrag` 返回后写 `preempt-angle-post`；驱动直接断言 pre 非零、post 归零）。证据 `.temp/p5a-001/m15/run-20261118-214848-notepad/`：同 tick 内 `drag-preempted` t=15702 → pre `wobbling=true angle=-4.19` t=15703 → post `wobbling=false angle=0` t=15707（5ms，远早于 0.7s 衰减），清单第 20 条 `preNonZero=True zeroAtPost=True` PASS。同轮唯一 FAIL「远端遮挡」为用户浏览器盖住落脚区的环境干扰（occBy 留痕，产品按规格正确脱离），该项沿用 R2 轮 PASS。R1 补证轮清单条数更正为 19 条（此前误记 22）。
- R2 整改（2026-11-18，监理 R3 已关闭 F-01）：F-01 旋转中心与帧偏移收敛到 `UpdateFrame` 同一次更新（`WobbleTransform.CenterX/Y = _character.FootAnchor + 帧偏移`，紧随 `FrameOffsetTransform` 设置；`UpdatePerchWobble` 只更新角度），消除同 tick 中心/偏移失配窗口（切帧素材脚点横移 ~1.17 DIP 根因）；单测 `PerchWobble_FrameSwitchDuringWobble_MaterialFootStaysAtFrameOffsetOnlyPosition`（晃动中切 `curious`：中心==footAnchor+新帧偏移、原始素材脚点经完整 TransformGroup 与只应用帧偏移位置一致、窗口不动）。F-03 驱动 `drag-preempted` 后取 `tMs>dragEv.tMs` 第一条心跳为接管时刻读数（`zeroAtPreempt`）。验证：`PerchWobbleTests` 7/7；全量 Core 548/548、App 535/535（1083/1083）；M-15 R2 补证轮 `.temp/p5a-001/m15/run-20261118-212632-notepad/` 19 项 ALL PASS（拖中晃 6.876→drag-preempted→接管首心跳归零 `zeroAtPreempt=True wobAtPreempt=False angleAtPreempt=0`）。本轮实机环境变为 1920×1080@96DPI（100%），驱动 `Find-ClearSpot` 补屏幕适配过滤并扩充 1080p 候选点（测试工具修复）；此前失败轮 `run-20261118-205750-notepad/` 为环境变化致宠物矩形出右屏被 F-01 出屏保护正确拒绝，非 R2 回归。
- R1 整改（2026-11-18）：F-01 旋转中心改为帧偏移补偿后的素材脚点（`UpdatePerchWobble` 每 tick 设 `WobbleTransform.CenterX/Y = _character.FootAnchorX/Y + FrameOffsetTransform.X/Y`；单测补证非零角度下 TransformGroup 作用于素材脚点不动、窗口位置不变）。F-02 `NotifyPerchFollowLanded` 晃动中不重置起始时刻（保留相位，未晃才启动）；单测补连续 follow 时序：起始时刻不重置、四分之一周期正/四分之三周期负、停止后归零。F-03 驱动步骤 12 补实机打断：拖动前小位移目标触发 follow，确认正在晃（|angle|>0.2）后立即抓住拖动，断言接管后 `wobbling=false` 角度=0；实际清单 19 条（含新增「拖动时小熊正在晃」）。
- R1 整改验证（2026-11-18）：构建 0/0；`PerchWobbleTests` 6/6（原 4+新增 2）；全量 Core 548/548、App 534/534（1082/1082）；M-15 notepad 补证轮 19 条 PASS：新证据「拖动时小熊正在晃 wobbling=True angle=4.509」+「拖动接管打断后无残留 wobbling=False angle=0」，`.temp/p5a-001/m15/run-20261118-190108-notepad/`。F-01 素材脚点单测采用运行时 `_character.FootAnchorX/Y`（与监理引用的 bear 素材一致），非硬编码。
- 任务 ID 与名称：P5A-002 窗口栖息晃动反馈。
- 依赖与需求 ID：依赖 [P5A-001](#p5a-001-窗口栖息首版)（已 done）；范围见 [R-07](PRODUCT.md#r-07-窗口栖息阶段-5a) 后续安排（晃动；主动靠近与攀爬不在本任务）。
- 范围与实现：窗口移动并成功跟随（follow Landed）时小熊短暂晃动——纯视觉 `WobbleTransform`（`RotateTransform` 绕帧偏移补偿后的素材脚点 `_character.FootAnchor + FrameOffset`，脚底贴住目标上沿），不改窗口 Left/Top、不进存档、不另建计时器（复用 `OnUpdateTick`）。衰减振荡 10°/260ms/τ220ms，≤1.2s 自动回稳，角度归零；晃动中 follow 保留相位（R1 F-02）。拖动接管（`StartDragging`/`CancelForDrag`）、暂停（`DetachForPause`）、一切脱离（`DetachInternal`/`RestorePrePerchPositionForExit`）立即结束并归零。
- 接线：`MainWindow.xaml` RenderTransform 改 `TransformGroup`（`FrameOffsetTransform`+`WobbleTransform`）；`MainWindow.xaml.cs` 晃动状态机（`NotifyPerchFollowLanded`/`EndPerchWobble`/`UpdatePerchWobble`）；`PetWindowPerch.cs` follow Landed 消费基线后触发、脱离路径收尾，新增 `TestLandingOcclusionSampler` 测试缝（单测伪造句柄落脚区必然被他窗命中，仅替换遮挡采样来源）。
- 验证：构建 0 警告 0 错误；`PerchWobbleTests` 4/4（跟随启动+纯视觉不改位+脚底锚定、停下回稳归零、拖动/暂停/遮挡打断归零）；全量 Core 548/548、App 532/532（1080/1080）；M-15 notepad 原始清单有 18 条 PASS，新增 3 条晃动断言：移动时晃（maxAngle=7.489°）、停下回稳归零、拖动接管打断无残留，证据 `.temp/p5a-001/m15/run-20261118-183234-notepad/`（首轮 `run-20261118-183008-notepad/` 晃动断言采样时序错误判 FAIL，驱动修正后复跑通过，产品代码两轮间未变）。
- 未测项：暂停打断的实机路径仅单测覆盖（M-15 无暂停步骤）；跨屏、混合 DPI 沿用 P5A-001 未测边界。主动靠近、攀爬留待后续。

### P5A-003 窗口栖息正式入口

- 任务 ID 与名称：P5A-003 窗口栖息正式入口。
- 状态：`done`（2026-11-19）。[监理 R2](reviews/P5A-003-review.md) 关闭 F-01、F-02，接收正式菜单拾取与主动离开。见[整改记录](remediation/P5A-003-response.md)。
- 依赖与需求 ID：依赖 [P5A-001](#p5a-001-窗口栖息首版)（done）、[P5A-002](#p5a-002-窗口栖息晃动反馈)（done）；范围见 [R-07](PRODUCT.md#r-07-窗口栖息阶段-5a)（正式入口：用户显式选窗 + 主动离开）。
- 范围与边界：右键菜单新增「Perch on Window」进入拾取模式（左键点击合格外部窗口即显式选中，`PetWindowPerch.TrySelectExplicit` 复用；15s 超时、点中宠物自身/拖动/开菜单/暂停取消；进入拾取时左键已按下须先释放再等新按下——边沿触发）；栖息中菜单显示「Leave Perch」→ `Detach("user-left")` 宠物留在当前位置。`PetWindowPerch` 落位/跟随/脱离/晃动语义零改动（只调用）。主动靠近、攀爬不做。
- 可观察验收条件：从正式入口选记事本/浏览器→落位上缘跟随；遮挡或关窗按既有语义脱离；拖走后不回拉；「Leave Perch」脱离后目标移动不拉走。单屏环境，跨屏未测须注明。
- 验证方式与证据：App 单测专项 PerchEntryTests 10 项 + 全量回归 Core 548 / App 546 通过；实机 M-16 驱动（notepad、edge）`m16-checklist.txt` 每轮 12 项（run-20261119-085140-notepad、run-20261119-085219-edge，2560×1440 单屏，测试分发入口）；R1 F-02 真实菜单补证轮 run-20261119-192641-notepad 12/12 ALL PASS（真实右键菜单「Perch on Window」→ 真实点击目标 → 真实菜单「Leave Perch」→ `detached(user-left)` 原位解绑）；复核确认轮 run-20261120-013233 与前台归属诊断确认轮 run-20261120-132156（驱动补齐 `LASTINPUTINFO` 前台诊断后，attempt 1 成功、「no menu」未复现）均同命令 12/12 ALL PASS（1920×1080@96DPI 单屏）。最小化/恢复、无效点击与 15s 超时仅单测覆盖。跨屏、混合 DPI 未测。
- 本轮实机修复的产品缺陷：`UpdatePerchPick` 生产轮询路径在左键未按下时提前返回、不清 `_perchPickAwaitRelease`，导致真实用户路径拾取点击全部被吞、15s 超时（测试缝路径无此问题，自动测试未覆盖；实机驱动定位并修复）；栖息中宠物自己的右键菜单打开期间挂起稳态遮挡判定（菜单弹层盖落脚区被误判 `occluded-detach`，右键选「Leave Perch」前先行脱离）。

### P5B-001 窗口有限攀爬（R-08 首版）

- 任务 ID 与名称：P5B-001 窗口有限攀爬（R-08 首版）。
- 状态：`done`（2026-11-22；[监理R2](reviews/P5B-001-review.md) 按用户明确指定由同助手复核接收，F-01～F-04全部关闭；不是独立复核）。
- 依赖与需求 ID：依赖 [P5A-001](#p5a-001-窗口栖息首版)、[P5A-002](#p5a-002-窗口栖息晃动反馈)、[P5A-003](#p5a-003-窗口栖息正式入口)（均 done）；范围见 [R-08](PRODUCT.md#r-08-窗口攀爬阶段-5b)。
- 必读章节：PRODUCT R-08；ARCHITECTURE §4/§5/§9；TESTING（M-17 登记后）。
- 输入/已有条件：工作区已有攀爬实现——Core `Windows/WindowClimbPolicy.cs`（纯逻辑折线，含上缘水平分支与 R1 整改折线长度/有限补步）、App `Platform/PetWindowClimb.cs`（控制器，含起攀/更新同时间域、找回取消）、`MainWindow.xaml.cs`（`IsClimbWriteAllowed`/`ApplyPerchFoot(fromClimb)`/`RecoverPet` 所有权与找回）、`WindowClimbTests`/`WindowClimbAppTests`（含监理 5 反例）、探针 `WindowClimbLiveScenario`（window-climb-live）。
- 范围与明确边界：表演式路线：靠近目标可见外框侧边 → 沿侧边爬升到外框上缘 → 沿上缘到顶部落位脚点（`WindowPerchLandingPolicy` 上缘中点，落位无可见跳动），落位交接 `PetWindowPerch` 既有栖息语义；每 tick 重观测目标（移动重建路线、失效/无落点/遮挡取消回可见区）；拖动立即接管不恢复位置、暂停结束路线、退出恢复爬行前位置；攀爬期间持有位置所有权。不做：真实边框连续寻路（R-08 升级项）、多宠攀爬、任何 Win32 写入目标窗口。
- 预期产物：Core 纯逻辑策略 + App 控制器 + Core/App 单测 + 探针实机场景 + 实机验证记录。
- 可观察验收条件：起攀成功后宠物沿折线爬到顶部落位、落位交接栖息跟随（脚点一致无跳动）；目标移动路线按最新快照重建；目标关闭/最小化取消并回可见区；拖动接管、暂停结束；超距/遮挡/不合格目标拒绝起攀且不移动。
- 验证方式与证据去向：构建 0 警告 0 错误 + Core/App 全量单测；探针实机验证（TESTING 登记后执行），证据入 `.temp`。
- 阻塞条件（如有）：无。

- 验证证据（2026-11-21）：R1 整改后构建 0 警告 0 错误；专项 `WindowClimbTests` Core **15/15**、`WindowClimbAppTests` App **23/23**（含监理 5 个 `Reviewer_Climb_` 反例全过）；全量 Core **563/563**、App **569/569**；详见 [整改记录](remediation/P5B-001-response.md)。上缘卡死修复基线：M-17 实机 `run-20261121-141812-notepad` **15/15 ALL PASS**（含 ③ 路线推进 fraction 递增 0.171→0.339、④ 顶部交接脚点=[690,160] 与上缘中点一致无跳动；⑤~⑮ 跟随/拖动/拒绝/移动/最小化/遮挡/暂停/退出全部 PASS，探针 exitCode=0），保留为 R1 前实机证据，本轮未重跑实机。

- `P5B-PLAN`：`todo`，依赖 R-07 验收。按 R-08 做有限攀爬，复杂寻路后置。

- 未通过窗口可行性验证前，不给社交或物理系统加入 HWND 依赖。

### P5B-002 窗口攀爬正式入口

- 任务 ID 与名称：P5B-002 窗口攀爬正式入口。
- 状态：`done`（2026-11-22；[监理R3](reviews/P5B-002-review.md) 关闭R2 F-01，接收正式入口；M-18原18/18通过，原反例1/1及当前入口回归24/24通过）。
- 依赖与需求 ID：依赖 [P5B-001](#p5b-001-窗口有限攀爬r-08-首版)（done）、[P5A-003](#p5a-003-窗口栖息正式入口)（done）；范围见 [R-08](PRODUCT.md#r-08-窗口攀爬阶段-5b)，正式入口为 R-08 后续延伸。
- 必读章节：PRODUCT R-08；ARCHITECTURE §4/§5/§9；TESTING（正式入口实机场景登记后）。
- 输入/已有条件：复用 P5A-003 窗口拾取流程（右键菜单进入拾取→左键点击合格外部窗口显式选中，`TrySelectExplicit`；15s 超时、点中宠物自身/拖动/开菜单/暂停取消；进入拾取时左键已按下须先释放再等新按下）+ `PetWindowClimb` 既有攀爬路线与守卫（起攀距离、所有权守卫、找回取消、超距/遮挡/不合格目标拒绝起攀且不移动）+ MainWindow 右键菜单接线。
- 范围与明确边界：右键菜单新增「Climb on Window」进入拾取模式，左键点击合格目标后按既有攀爬路线执行（靠近侧边→外框上缘→顶部落位→交接 `PetWindowPerch` 栖息）；无效目标、超距、超时及用户取消安全结束拾取，不移动宠物；拖动、暂停、找回、退出继续可接管。既有「直接栖息」入口（P5A-003「Perch on Window」）保留为平行入口。不做：自主选窗、自动靠近、专用攀爬美术、多宠攀爬、习惯学习；不扩大多屏支持声明。
- 预期产物：App 正式入口接线 + 正式入口测试（App 单测专项 + 真实菜单关键链实机验证）+ 权威文档同步。
- 可观察验收条件：从正式入口选记事本/浏览器/独立夹具→按既有路线攀爬落位、交接栖息（脚点一致无跳动）；无效点击/超距/超时取消且宠物不移动；拖动/暂停/找回/退出可接管；「直接栖息」入口行为不变。单屏环境，跨屏未测须注明。
- 验证方式与证据：构建 0 警告 0 错误 + Core/App 全量单测；正式入口专项测试；真实菜单关键链实机验证（真实右键菜单→真实点击目标，手法参照 M-16 R1 补证轮）；实机占用鼠标前先告知用户；证据入 `.temp`。
- 阻塞条件（如有）：无。

- 实现证据（2026-11-22）：构建 `DesktopPet.slnx -c Debug` 0 警告 0 错误；新增专项 `ClimbEntryTests` **12/12**（菜单进入→点击合格近距目标→`started`+攀爬激活、点自身取消、不合格/超距/遮挡拒绝且不移动、超时取消、占用拒绝进入、开菜单/暂停取消、攀爬进行中重复进入拒绝、平行入口「Perch on Window」回归 `selected`、完整关键链到达交接栖息 `climb-completed` 落位无跳动）；栖息拾取回归 `PerchEntryTests` **10/10**；全量 Core **563/563**、App **582/582**，0 失败 0 跳过。实现方式：拾取状态机泛化（`WindowPickIntent.Perch/Climb` 共享超时/边沿触发/取消路径，仅在点击后分派 `Perch.TrySelectExplicit` 或 `Climb.TryStart`）；新增 `MenuIdClimbOnWindow=8` 菜单项与命令分支、`StartClimbPick` 入口、`IsClimbPickActive` 观测；探针 `WindowClimbLiveScenario` 增补 `pick-entry`/`menu-entry` 命令与攀爬拾取心跳（真实菜单关键链实机手法）。未提交/推送。
- 实机验证证据（M-18，2026-11-22）：最终轮 `run-20261122-152310-fixture` **18/18 ALL PASS**，探针 exitCode=0，证据 `.temp\p5b-002\m18\run-20261122-152310-fixture\`（driver-transcript.log / m18-checklist.txt / probe-result.json）。真实右键「Climb on Window」→ 真实点击目标 → 攀爬路线（steps=19 递增）→ 顶部交接栖息（脚点=上缘中点±6 无跳动）；「Perch on Window」平行入口 real-menu（idx=2）→ selected+perchActive 攀爬不触发；拒绝/取消 ⑧-⑮ 逐项 PASS（rejected-ineligible、rejected-too-far、点自身取消 status=cancelled〔实机语义见 TESTING M-18，`cancelled-self` 仅测试缝可达〕、cancelled-timeout、真实菜单 Esc、暂停、拖动、攀爬拖动抢占）。诱饵存活证明清理未越界。晚于最终轮的失败自测轮保留在 `.temp` 为开发证据，不覆盖最终轮。状态保持 `awaiting-verification`，待监理复核；未提交/推送，未改 docs/reviews 及证据。
- R2 F-01 整改证据（2026-11-22）：`RecoverPet()` 新增 `EndPerchPick("cancelled")` 同步结束待选拾取；新增测试 `RecoverPet_DuringClimbPick_EndsPickAndOldClickDoesNotClimb`（找回→旧点击零起攀/零位移+Tick 零位移）与 `PerchPick_RecoverDuringPick_EndsSelectionNoParallelRegression`（共享栖息入口找回取消、无回归）。验证：`ClimbEntryTests` **14/14**、`PerchEntryTests` **10/10**、App 全量 **584/584**（原 582+新增 2）、Debug warnaserror 0 警告 0 错误。详见 [整改记录](remediation/P5B-002-response.md)。

### P5B-003 攀爬动作表现

- 状态：`done`（2026-11-25；[监理R21](reviews/P5B-003-review.md) 接收，F-06关闭；R21右侧/land拖动与R18左侧、R15其余有效证据组合验收。原始窄轮4/5、exit1系固定截图数量门槛假阴性，原件保留；临时美术按R6用户豁免，不称定稿）。
- 必读章节：PRODUCT R-08；ARCHITECTURE §5（动画播放器/脚点约定）；TESTING（本任务隔离预览登记后）。
- 输入/已有条件：已接收的攀爬路线与正式入口（P5B-001/P5B-002）；现有小熊素材（`assets/pets/bear/`，idle/blink/curious/sleep 等帧与 `character.json` 动作注册）；动画播放器与脚点布局约定（待本任务调研确认）。
- 范围与明确边界：为攀爬流程提供专用动作表现——**侧边爬升**、**上缘移动**、**顶部落位**三阶段最小动作集。先交付素材与隔离动作预览（默认 120×120，必要时放大对比）供观感确认，**确认后再接入完整攀爬流程**。动画只改变局部画面，不修改世界位置、速度、窗口尺寸或路线；保持脚点稳定。拖动、暂停、找回、取消和退出后不得残留攀爬帧。没有对应专用动作时保留明确兼容回退（可回退到现有 idle/既有帧）。保持现有小熊风格；左右侧按既有能力镜像（不新增右侧专用素材）。不做：重新设计已接收路线与正式入口、自动靠近、多宠攀爬、习惯学习、复杂寻路、新窗口尺寸/新计时器。
- 预期产物：三阶段最小动作集素材 + 隔离动作预览交付物 + 观感确认后的流程接入（接线到既有攀爬状态机与取消/打断路径）。
- 可观察验收条件：预览中侧边爬升/上缘移动/顶部落位动作可辨认、脚点稳定、与路线/窗口尺寸/速度无关；取消类操作后立即回到非攀爬帧无残留；无专用动作的场景有回退不崩溃。
- 验证方式与证据去向：素材像素断言（脚点行/尺寸与既有帧一致）+ 隔离预览实跑（截图/帧序列，120×120 默认，必要时 2× 放大对比）+ 接入后 App 单测专项与全量回归；证据入 `.temp` 与跟随交付说明。
- 阻塞条件（如有）：无。

- **第一阶段（素材+隔离预览，2026-11-22）**：状态 `in-progress`（[监理R2](reviews/P5B-003-review.md) 退回后 F-01/F-02 已整改、自测通过，`awaiting-verification`；观感通过后才进第二阶段接线）。Git 核实：工作区**无任何 ScaleTransform/RenderTransform 代码引用**（镜像留第二阶段）；本阶段新增＝8 素材 + `character.json` 三动作注册 + 探针 `ClimbPreviewScenario` + 驱动 `Invoke-P5B003ClimbPreview.ps1` + Verify 脚本。
  - 素材（PIL 派生，idle_0 为底局部重绘，全程 y<=102 不动脚带）：`climb_side_0/1/2`（R1 F-01 后为连体毛绒爪臂：左伸高抓→双爪承重→右伸高抓）、`climb_edge_0/1/2`（身体 y<=102 整体横移 ±3px+低伸爪：探左/居中/探右）、`climb_land_0/1`（收脚两帧渐收，末帧直接引用 idle_0 站稳，未改动）。像素断言：side/edge 脚带 y104-110 与 idle_0 逐像素一致；land 收脚行中心≈60、不越出 idle、y110 仍落地。旧方案（垂臂+叠加圆环、耳/脸/额误绘色块、edge lean 曾逐字节相同）为 R1 F-01 反例，已被连体爪臂方案替换，不复用。
  - 注册（`character.json`）：`climb_side` loop 3×300ms、`climb_edge` loop 3×300ms、`climb_land` once（land_0 260ms 收脚 → land_1 340ms 收脚 → idle_0 2400ms 站稳 → 播完保持末帧），全 offset 0。
  - 隔离预览（真实 MainWindow + CreatePetWindow + 生产 UpdateFrame + RenderTargetBitmap 120×120，反射取 `_animPlayer`/`PetImage`/`_updateTimer`，探针无 InternalsVisibleTo）：34 帧（idle 基准 1 + side 9 + edge 9 + land 15）；stop 生产 33ms tick；`PreviewSetField` 置 `_idleBlink`/`_dragRelease` 为 null（消除表达式合成不确定）；每阶段首帧前 `InitStage` 预热渲染（消除 Image.Source 换图首帧滞后伪影，diag_pixels8 实证 side_00 眼区与 idle diff 由 0→309）。
  - 渲染级断言（`Verify-P5B003RenderPreview.py`，F-01 后最终 run `run-20261122-185508`）**34/34 ALL PASS**：idle_base 未引入位移（bbox±1、真实底行=110〔过滤渲染链 alpha≤3 边缘插值伪差行〕、内容量 6395→6553 在 ±5% 内）；side/edge 各 9 帧脚带 y104-110 中心≈60±1.5、不越出 idle±1、y110 落地；land 15 帧收脚中心≈60、不越出 idle、y110 落地。脚点稳定判据＝渲染像素逐帧测量（非 Offset=0/非脚底行相等），跨素材逐像素断言已三次证伪（渲染链系统性 ±1 扰动）不采用。
  - 交付物（`.temp\p5b-003\run-20261122-185508\deliverables\`）：`preview_side.gif`/`preview_edge.gif`/`preview_land.gif`（120×120 动态）＋ `climb_keyframes_4x.png`（34 行 4× 放大）＋ `climb_zoom_compare_2x.png` ＋ `render-foot-stability.json`/`render-summary.txt`（allPass=true）。未接线 `PlayClimbAction`/`EndClimbAction`，未改路线/位置/中断规则。未提交/推送。
  - **R1 F-01 整改（2026-11-22）**：候选 6 帧（`climb_side_*` 左伸高抓/双爪承重/右伸高抓、`climb_edge_*` 身体整体横移+低伸爪）经用户确认造型后批量导出覆盖 assets；素材由真实肩部（左 (25,61)/右 (95,61)）连体毛绒爪臂构成，脸/耳/脚带 y104-110 零改动，side_1/2 右臂肘外移 x106 避脸颊；`climb_land_*` byte-identical 保留。素材层校验 ALL OK（新增像素簇与身体连通无悬浮圆、耳区无新绘、脚带逐像素同 idle、面部/耳区与基准零差异，edge 基准为同 dx 平移 idle）；assets 与定稿帧 SHA256 全等。详见 [整改记录](remediation/P5B-003-response.md)。状态 `in-progress`、`awaiting-verification`，待监理复核观感；未提交/推送，未改 docs/reviews 及证据。
  - **R1 F-01 整改驳回 → R2 F-01/F-02 整改（2026-11-22）**：R2 判定 F-01 仍四臂（以 idle 为底 `im.copy()` 叠加新臂）且新臂细长硬边，F-02 最终 run-185508 渲染仍旧图（与 run-182213 33/34 字节相同）。整改：
    - F-01 v2（用户确认造型后批量）：真正收缩体侧缘（`L_BAND=(10,44)`/`R_BAND=(76,110)`）消除 idle 垂臂，从肩部连出恰好两条粗短圆润前肢，脸 y<44、脚带零改动；`climb_side_1`/`climb_edge_0` 与候选 `side_cand_highgrab_v2`/`edge_cand_lowmove_v2` 逐字节一致（diffPx=0）。
    - F-02 根因：探针 `FindAssetDirectory` 首位＝exe 输出目录 `tests\...\bin\Debug\net10.0-windows\assets\pets\bear`，旧副本未随源更新。`sync_assets_to_bin.py` 同步 6 张：src==final==bin SHA-256 前 16 位全一致（A4F15A45…/938086F7…/124862F9…/7A5279C6…/BAC250A5…/D1AD86FF…）。
    - Verify 增强（`Verify-P5B003RenderPreview.py`）：①bin SHA==源 ②idle 基线 MAJOR%≈0 ③渲染帧内容一致（素材映射以 events.jsonl `imagePath` 为权威；切换帧取同阶段素材集合最小 MAJOR%——`diag_switch_frames.py` 6×3 矩阵证明 6 帧为动画 300ms/帧×采样 100ms/帧的切换时序伪影、与相邻素材 MAJOR% 0.05–0.17，旧图反验证仍 18/18 FAIL）④新旧 run 字节对比。
    - 新 run `run-20261122-194743`：探针 exitCode=0、34 帧齐全；Verify **ALL PASS**（idle 位移、①、②0.59%、③ 18 帧 best-match 0.05–0.17%、脚带 34 项、④side/edge 18/18 与旧 run 字节不同）；`render-foot-stability.json` allPass=true。状态 `in-progress`、`awaiting-verification`，待监理复核；未提交/推送，未改 docs/reviews 及证据。详见 [整改记录](remediation/P5B-003-response.md)。
- **第二阶段（动作接线+镜像，2026-11-23）**：状态 `in-progress`/`awaiting-verification`（整体验收待监理复核）。实现范围：`climb_side`/`climb_edge`/`climb_land` 按既有攀爬阶段分派；左右镜像用 `ScaleTransform(-1,…)` 不新增右侧素材；取消/结束立即回 idle 无残留（含 land 播完回 idle 时镜像复位）；缺失动作回退 idle 不崩溃；优先级让位（好奇/委屈/睡眠不覆盖攀爬）。
  - 实现（`MainWindow.xaml.cs`）：`PlayClimbAction(actionId, mirrorLeft)` 仅在 `(actionId, mirrorLeft)` 变化时重播；阶段分派与 `BuildRoute` 同源（上缘判据 `footY<=BoundsTop && footX∈[BoundsLeft,BoundsRight]`；edge/land 方向用 landingX vs footX，side 用最近侧边 vs footX）；到达分支 `StopCore(keepLand:true)` 交栖息不打断 land；镜像复位在 `OnActionCompleted` 的 climb_* 分支。`PetWindowClimb.cs` 供事件/状态；`MainWindow.xaml` 配 RTransform。
  - 专项测试（`ClimbActionTests` **11/11**）：阶段分派映射（side/edge/land）、镜像翻转标识、land 播完回 idle 镜像复位、各取消路径（暂停/拖动/找回/目标失效/退出）立即回 idle 无残留、缺失动作回退、优先级让位。测试时间戳须递增（`TryStart(nowMs:0)` 置 `_lastTickMs=0`，重复 `Tick(200)` dt=0 被单调性守卫拦截——测试缺陷非产品缺陷）。
  - 回归中发现的布景问题（已修，非产品逻辑回归）：`ClimbEntryTests` 2 用例原失败，根因=未 Show 测试窗口 HWND=0 使 DPI 查询固定 1.0，在 150% 实屏上菜单路径 `EndDrag→ClampWindowToWorkArea` 经 `WindowPositionAdapter` 前向 source DPI=1.0/反向 target DPI=1.5 双向不一致把宠物错位搬移（实测 (600,500)→(380,293.33)，起攀距离 40→260px 致 `rejected-too-far`）。修复：`Setup` 注入 `TestMonitorList` 单一 100% 监视器（1920×1080/工作区 1040/DpiScale 1.0），clamp 变 no-op；产品 clamp 逻辑不动。
  - 验证：Debug warnaserror 构建 0 警告 0 错误；专项 `ClimbEntryTests` **14/14** + `ClimbActionTests` **11/11**；全量 Core **563/563** + App **595/595** = 1158/1158 0 失败 0 跳过；资源内容一致性 bin SHA==源（9 素材+character.json）全 OK。三处临时诊断（`CLIMBDIAG`：enter-menu/startpick/clamp-in）已全部移除，`git diff` 无残留。未提交/推送，未改素材/`character.json`/`docs/reviews`。
  - **R7 技术整改（2026-11-23，[监理R7](reviews/P5B-003-review.md) F-03～F-05 阻断）**：修复动作生命周期/实际播放恢复/侧爬镜像方向，已整改并自测通过，待监理复核。
    - F-03 land 播放中生产中断清理：`PetWindowClimb` 新增 `IsPresentingClimb`（`IsActive || IsClimbActionId(CurrentAction)`）解耦路线活跃与攀爬呈现所有权；`CancelForDrag/Pause/Recover/Exit` 先决条件放宽为 `!IsPresentingClimb`；`EndClimbAction` 所有清理路径无条件 `ScaleX=1`、仅当前确为 climb_* 才回 idle。
    - F-04 播放重试/hover 不覆盖：`PlayClimbAction` 返回 bool（守卫拒绝/`_character.Actions` 缺失→false 不改动作不改镜像）；`TryStart`/`DispatchStageAction` 缓存只在实际成功播放后更新，临时表达释放后下一 tick 经生产 tick 补播；hover 分支加 `_petClimb.IsActive` 与 `IsClimbAction(CurrentActionId)` 双守卫。
    - F-05 贴边不翻转：非顶边镜像改 `Math.Abs(w1x-footX)<=0.5 ? _lastClimbMirrorLeft : w1x>footX`（SideWallEpsilonPx=0.5），同墙垂直上爬保持朝向；顶边仍按实际水平方向。
    - 验证：新增 8 项测试走生产入口（`PauseAnimation`/`OnUpdateTickTest`/`TestGetCursorPos`）；`ClimbActionTests` **18/18**、`ClimbEntryTests` **14/14**、全量 **1165/1165**（Core 563 + App 602，复跑两次稳定；首跑一次 VSTest MSB4181 任务层偶发无具体用例）、Debug warnaserror 0 警告 0 错误、资源一致性 mismatch=0、无临时代码残留。详见 [整改记录](remediation/P5B-003-response.md)。未重做路线/正式入口，未改素材/`character.json`/`docs/reviews`/证据，未提交/推送。
  - **R8 限定补边（2026-11-23，[监理R8](reviews/P5B-003-review.md) F-03/F-04 剩余边界）**：仅补两个边界，已整改并自测通过，待监理复核。F-05 已关闭、F-01 临时美术豁免及 F-02 关闭结论不变，未重做已通过项。
    - F-03 栖息实际脱离释放攀爬落位表现：`PetWindowClimb` 新增 `CancelForPerchDetached()`（`IsPresentingClimb` 守卫 + `StopCore()` + Emit(`climb-cancelled`,`perch-detached`)，不移动宠物）；`PetWindowPerch.DetachInternal` 尾部（`_hasPrePerchPosition=false` 后、Emit 前）统一调用 `_owner.ReleaseClimbForPerchDetach()`（`MainWindow` internal 包装，`_petClimb?.` 不触发懒创建）；Pause/Recover/Drag 先经 Perch 再调既有 `CancelFor*`，新清理后守卫使后调 no-op，不误清独立表情。
    - F-04 缓存相等仍核对真实播放：`DispatchStageAction` 缓存相等时核对 `_owner.CurrentAction == actionId && (ClimbMirrorTransform.ScaleX < 0) == mirrorLeft`，一致才 return，不一致重试 `PlayClimbAction`（守卫通过则补播并更新缓存）；临时表情覆盖释放后经生产 tick 补播，正常同阶段播放不每 tick 重启。
    - 新增 4 项测试走生产路径（`ClimbActionTests` 22 项）：F-03 反例 `LandPlayback_TargetGoneViaPerchOnTick_ReleasesClimbPresentation`（反射 `PetWindowPerch.OnTick` 生产轮询，`TestSnapshotSource=null` 目标失效→Perch 脱离+清洁 idle）、正例 `LandPlayback_TargetValidViaPerchOnTick_KeepsClimbPresentation`（目标外框变化触发 OnTick follow 分支→Landed 跟随不误清）；F-04 反例 `StagePlayedThenOverridden_ReleaseReplaysStage`（已播 climb_side→curious/idle 覆盖释放→`climb.Tick(50)` 生产推进补播）、正例 `SameStageTick_DoesNotRestartPlayback`（两次采样 `_actionStartMs` 相对比较 B==A、C==B 不重启）。
    - 验证：先重建测试项目再专项（`ClimbActionTests` **22/22**、`ClimbEntryTests` **14/14** `--no-build`）、全量 **1169/1169**（Core 563 + App 606）、Debug warnaserror 0 警告 0 错误、资源一致性 `assets\pets` 35 文件双向 SHA mismatch=0。详见 [整改记录](remediation/P5B-003-response.md) R8 段。未重做已通过项，未改 `docs/reviews` 及 R8 证据副本，未提交/推送；实机待代码复核通过后另行安排。

## 阶段 6：习惯学习

- **本版阶段已完成（2026-11-27）**：P6-001～004 均 done；P6-004 最终接收见[监理 R4](reviews/P6-004-review.md)。用户按 [D-020](DECISIONS.md) 确认保持纯气泡，主动靠近延期至后续独立待办，不作为本版未完成项。已完成范围不等于预测效果已经真实长期验证。
- **P6-PLAN 状态：`done`（2026-11-25，[监理R3](reviews/P6-PLAN-review.md) 接收）**：四步分别验收；R1 F-01～F-04规划问题在R2关闭，用户已明确统计默认关闭。参数与行为以[PRODUCT R-09](PRODUCT.md#r-09-习惯学习阶段-6)为准，技术约定见[ARCHITECTURE §7](ARCHITECTURE.md#p6-001-使用习惯本地统计约定待实现)。P6-001依赖解除，实际开工时改in-progress；当前仅文档完成，不称产品已实现，不进入P6-002～004。
- 阶段 5（P5B-003）已验收 done，P6 不再阻塞于阶段 5；P6 各任务依赖按本列表顺序推进。
- 精确桌面/任务栏软件图标定位另立探索任务，不做为预测正确性前提，失败时退回气泡反馈。

### P6-001 本地统计：事件采集、关闭与清除

- 状态：`done`（2026-11-27；[监理R11](reviews/P6-001-review.md) 最终接收，F-01～F-06关闭；既有菜单/前台/清除恢复/退出及R10锁屏、R11真实睡眠生命周期均通过，隔离进程exit0并清理）。依赖：P6-PLAN已接收。关联：[PRODUCT R-09](PRODUCT.md#r-09-习惯学习阶段-6)。
- 范围（只细化本步，P6-002~004 留待各步实现时展开）：
  - 事件来源四类互不混用：`app-start`＝桌宠自身启动；`unlock`＝解锁/会话恢复；`resume`＝睡眠恢复（不并入 unlock）；`boot`＝操作系统开机，须独立可验证来源，来源尚不可用时如实不记录，不安排开机启动/常驻服务补测，首版不收集全进程创建事件。外部应用一律按前台观测记录，不称启动。
  - 统计口径：仅观测前台应用；应用标识＝规范化可执行文件基名（不存绝对路径/PID 作长期身份），同名合并，识别失败丢弃该应用归属区间并在覆盖统计中计数（单一口径，不保留 unknown 归属分支，与 PRODUCT R-09 一致）；明示为近似前台活跃时长。
  - 采样已定值（监理 R2 确认，无需再裁决，已写入 PRODUCT）：前台 5 秒采样、输入年龄 60 秒阈值、事件边界即时处理；锁屏/睡眠/暂停/缺样区间不归属应用、不补计。
  - 持久化：日历日期/本地时间带时区偏移，时长为单调差值；跨午夜拆段、跨重启不接续。
  - 采集字段白名单：日期、活跃时间段（单调起止与时长）、应用标识、事件类型。黑名单：不采集窗口标题、网页地址、正文或按键；应用标识获取避免窗口标题探测。
  - 保留与聚合边界：原始 7 天（仅有限校验），聚合 30 天按固定桶（已定 30 分钟，2026-11-26 依监理 R2 采纳，与 PRODUCT 一致）增量幂等落盘；过期按被统计日期清理，统计关闭时清理仍执行。
  - 关闭：停止新采样/统计更新/主动预测反馈，保留受保留期约束的既有数据，保留期清理仍生效。清除：仅删原始/聚合/派生预测与待写缓存，保留开关与配置；旧定时器/异步保存/备份不得回写（串行化或代次边界）；开启后新数据是新记录非复生；重启不恢复已清除历史。入口 P6-001 即提供。
  - 默认开关：默认关闭、由用户主动启用，用户2026-11-25明确决定，见PRODUCT。
- 验收：四类事件可区分、外部应用不称启动；字段未触黑名单；关闭后不新增且旧数据保留；清除后不复生并有证据；未安排开机启动/常驻服务补测。样本不足等无样本场景不得宣称已实现习惯预测。
- 验证方式与证据去向：Core 纯逻辑测试（注入时钟/事件源）＋ App 接线验证（真实 `SessionSwitch`/`PowerModeChanged` 与启动时序）；证据写入 `docs/history/` 或任务整改记录链接，STATUS 引用。

- 实现证据（2026-11-26）：Core 新增 `UsageStatsCollector`/`UsageStatsArchive`（stats.json schemaVersion=1、temp→replace 安全写、5s/60s/15s/7 天常量、四类事件、缺样不补计、generation guard 防旧写回、Restore 不恢复已清除）；`PetStateArchive` L161 新增 `UsageStatsEnabled=false`（默认关闭、随设置持久化）。App 新增 `UsageStatsService`（独立 5s 定时器、Dispatcher 串行化、`%LOCALAPPDATA%\DesktopPet\stats.json` 独立存储）与 `ForegroundAppSampler`（规范化基名、识别失败丢弃+计数）；App.xaml.cs 接线 `InitializeUsageStats`（L611，仅开关开启才启用）/系统事件转发/`SetUsageStatsEnabled`（L1264）/`ClearUsageStats`（L1287）/测试缝/清理；MainWindow 右键菜单 `MenuIdStatsToggle`/`MenuIdStatsClear`（L1002-1017，动态标签，不继续往更新循环堆逻辑）。专项 `UsageStatsServiceTests` 13/13（默认关闭/启停持久化/缺测不补计/跨日/保留期/清除防回写/重启不恢复/App 转发/不动宠物存档），`UsageStatsCollectorTests` 已入 Core；全量 Core 580/580 + App 623/623，构建 0 警告 0 错误。未做真实锁屏/睡眠/前台观测与实机菜单点击（单测覆盖）。状态 `awaiting-verification`，不自行标 done、不提交/推送。开发整改记录见 [P6-001-response](remediation/P6-001-response.md)。
- 隔离实机入口（2026-11-26，监理 R4）：探针新增 `usage-stats-live` 场景——独立 run 目录+唯一实例锁，`settings/state/stats` 及 `.corrupted/.tmp` 全部限定 run 目录；生产 `CreatePetWindow`/`Show`、生产 `InitializeUsageStats`（真实 5s timer、真实空闲/前台采样）、`SessionSwitch`/`PowerModeChanged` 转发、真实窗口右键菜单；jsonl 观测 + sidecar/cleanup sidecar。App 新增 internal `InitializeUsageStatsForIsolatedProbe` 与 `UsageStatsService.IsSuspended` 只读转发（生产逻辑零改动）。无鼠标构建 0 警告 0 错误（App 与 DiagnosticProbe）。命令与操作清单见 [P6-001-live-entry](history/P6-001-live-entry.md)。真实菜单/前台/锁屏睡眠由监理与用户约时段执行。
- R2 限定边界整改（2026-11-26，已自测通过待监理 R3 复核）：Service 挂起期间不读 idle/前台采样器（门控前置）、App 默认关闭暂停启动初始化无条件同步且开启不解除挂起；Core 单调回退拒绝并重置基线防重复累计；清除副本删除失败经 `LastWriteError` 可见、快照成功不掩盖、`DrainPending` 有界重试闭环。新增 Core 2 条、App 3 条专项反例；专项 Core **28/28**、App **18/18**，全量 Core **591/591** + App **628/628**，构建 0 警告 0 错误。详见 [整改记录 R2 段](remediation/P6-001-response.md)，未改监理报告与证据。
- R3 剩余 F-02 整改（2026-11-26，自测通过待监理复核）：Core 新增 `_monotonicRollbackFloorMs`（自上次 `ClearAll` 起已接受的最高单调上界，与 `_lastSampleMs` 基线分离）；`SampleTick` 回退判定改为 `< floor`，低于上界连续 tick 拒绝并 `ResetBaseline()`（保留 floor），回到/越过上界按既定缺测重建恢复、只累计未计过区间；`Enable/Disable/ResetBaseline`/挂起保留 floor，仅 `ClearAll` 重置（新时间域），`Restore` 不设。新增 Core 4 条专项（R3 反例 0→5000→1000→2000→5000 期望 5000、持续低于上界、越过上界恢复、生命周期）；专项 Core **32/32**、R3 验证器隔离目录干净复跑 **14/14 pass**。详见 [整改记录 R3 段](remediation/P6-001-response.md#r3-整改段2026-11-26对应-监理r3-唯一剩余-f-02-连续回退窗口)，App 层零改动，未改监理报告与证据、未提交推送。
- R5 F-05 实机入口整改（2026-11-26，自测通过待监理预检）：①live 场景区分首轮/重启——重启经 `LoadArchivesForIsolatedProbe`（生产 `LoadState→LoadSettings→_isPaused`，不覆盖磁盘）+ `TestEnsureProbeRuntimeObjects`（只补运行对象）；②系统订阅走共享 `StartForIsolatedProbe`（`InitializeActivitySession`→`SubscribeSystemEvents`），退出沿用 `ShutdownMainWindow` 退订；③sidecar 改收尾时刻从服务当前值写、删预写 `usedRealContextMenuDispatch`（如实 `realContextMenuDispatchConfirmed=false`），obs/sidecar/cleanup 按 session 编号不覆盖；④清单拆 E「关闭保留」/F「重新开启后清除保留 On」（clear 记 generation/时间、旧记录消失、toggle 保持 On、之后新记录允许）、app-start 仅启动已启用才记录不补造、重启"不恢复旧历史、允许新记录"、标记应用由操作者自建精确归属。App 新增 internal `LoadArchivesForIsolatedProbe/TestEnsureProbeRuntimeObjects/TestSettings`（生产逻辑零改动）。新增无窗口 `usage-stats-wiring` 场景。无鼠标构建 0 警告 0 错误；wiring 连跑两次验证：预存 On→加载保持 On、`subscribedAfterStart=true`→cleanup `subscribedAfterCleanup=false`、appStartEventCount=1、session-1/2 三件套并存不覆盖。详见 [整改记录 R5 段](remediation/P6-001-response.md#r5-整改段2026-11-26对应-监理r5-唯一剩余-f-05-隔离实机入口缺口)；未改监理报告与证据、未提交推送、不进入 P6-002。
- R8 F-06 隔离探针生命周期整改（2026-11-26，自测通过待监理预检）：①根因——探针 `new App()` 未加载 App.xaml→默认 `OnLastWindowClose`；首轮经 `ResetWpfLifecycleForTest` 反射对齐，重启分支（`LoadArchivesForIsolatedProbe`+`TestEnsureProbeRuntimeObjects`）未对齐，`ShutdownMainWindow` 先关窗（L1599-1601）后设 `ShutdownMode`（L1609）在已关闭应用上抛 `InvalidOperationException`；②修法——`usage-stats-live` 场景建窗前用公开属性 `ShutdownMode = ShutdownMode.OnExplicitShutdown` 显式对齐生产约定（首轮/重启共用一行，不新增 WPF 私有字段反射、不用 try/catch 吞异常、不用 `Environment.Exit(0)`），探针由裸 `Dispatcher.Run()` 改真实 `Application.Run()` 以承载生产 `Shutdown(0)` 正常返回，`MainWindow.MenuIdExit` 仅放宽为 internal（生产行为零变化），不改 `ShutdownMainWindow` 顺序；③回归——新增 `usage-stats-exit` 子进程级场景（真实 `Application.Run()`+生产菜单分发 Exit），同目录连跑两轮 RUN1/RUN2 退出码均 **0**、`isRestart=false/true` 覆盖首轮与重启，两轮 `applicationRunReturned/shutdownViaMenuDispatch/unsubscribedAfterExit/usageStatsNullAfterExit/activitySessionNullAfterExit` 全 true、`usedEnvironmentExitZero=false`、`exceptionLoggedInDiagnosticLog=false`、logs 目录 0 文件；④对照实验（临时移除 `ShutdownMode` 赋值、同目录跑重启分支）精确复现 `InvalidOperationException: 正在关闭或已经关闭应用程序时无法设置ShutdownMode`（`App.xaml.cs:1609`，退出码 **-532462766**），证回归可检出 F-06，随后恢复并复跑通过；⑤构建 0 警告 0 错误、`dotnet test tests/DesktopPet.App.Tests` **628/628**。证据 `%TEMP%\codefree-agent\p6-001-r8-final\usage-stats-exit\`（对照 `p6-001-r8-control3\`、首次失败 `p6-001-r8-f06\`，本机临时目录不入库）。详见 [整改记录 R8 段](remediation/P6-001-response.md#r8-整改段2026-11-26对应-监理r8-唯一剩余-f-06-隔离重启退出异常)；未重做已通过项、未改监理报告与证据、未提交推送、不进入 P6-002。

### P6-002 聚合：7 天原始 → 30 天聚合

- 状态：`done`（2026-11-27，[监理 R1](reviews/P6-002-review.md) 接收，无阻断项）。依赖：P6-001（done）。关联：[PRODUCT R-09 细化段](PRODUCT.md#r-09-习惯学习阶段-6)、[ARCHITECTURE §7 P6-002](ARCHITECTURE.md#p6-002-聚合与保留约定已实现)。
- 范围（本轮展开）：
  - **固定 30 分钟桶**：本地零点对齐 [:00,:30)/[:30,:60)，桶键 `AppId+BucketStartLocal`；每段已验证区间提交时同时切到午夜与 30 分钟桶，及时增量聚合，不等原始过期。
  - **频次定义（一次使用＝一个会话，非 5 秒采样次数）**：会话＝同一应用标识下无中断的连续已验证前台区间链；中断（缺样/空闲/锁屏/睡眠/暂停/关闭/生命周期事件）或应用切换结束会话，下一次已验证区间即新会话；会话计数只记在会话起始所在桶，跨桶/跨午夜只分裂时长不重复计次。
  - **双保留按被统计日期**：原始段/事件 7 天，聚合桶 30 天，独立判定；`PruneToRetention` 每次落盘执行两套清理，采集关闭时仍执行；不等原始记录过期才聚合，也不因持续生成延长旧桶寿命。
  - **幂等/不重不漏**：整份已提交快照覆盖写；重试/重复 drain 不叠加，桶内时长增量累加、会话起始一次性计数；`Restore` 只恢复已提交数据、不重建基线与会话，重启不重计已保存段/桶，新活动是新会话。
  - **沿用 P6-001 约束**：不设独立聚合开关，单一全局开关；关闭不新增但保留期内旧桶保留 + 过期清理继续；清除 (`ClearAll`) 同清原始/事件/聚合桶并立即落盘、generation 代次守卫防回写、重启不恢复；聚合不写宠物存档、清除不碰宠物存档。
- 验收：7 天原始/30 天聚合双保留边界正确；聚合不重不漏，不承诺恢复细粒度轨迹。
- 验证方式与证据去向：注入时钟覆盖跨桶、跨午夜、应用切换、空闲中断、30 天按被统计日期过期、清除与重启不重计；隔离目录 App 测试验证落盘/恢复/关闭清理/清除。证据见下并与 STATUS 链接。
- 实现与验证证据（2026-11-26，开发自测通过、待监理复核）：
  - Core：`UsageStatsArchive` 升级 `schemaVersion=2` 新增 `UsageStatsBucket`/`buckets`（v1 缺失反序列化为空、附加式无迁移）；`UsageStatsCollector` 新增 `BucketMinutes=30`/`AggregatedRetentionDays=30`、`Buckets` 只读视图、`_buckets` 字典、会话跟踪（`_sessionOpen`/`_lastCommittedAppId`）、`CommitVerifiedInterval`→`AddSegment`→`AddBuckets` 及时切桶、`BeginOrContinueSession` 会话界定、`ResetBaseline` 关会话、`PruneBuckets` 30 天清理、`Restore`/`ClearAll` 处理桶。App：`UsageStatsService` 新增 `Buckets` 只读转发，`PersistSnapshot` 写入 `Buckets`；无其它生产逻辑改动。
  - 测试：Core 新增 7 条（同桶多采样只计 1 会话、跨桶时长分裂会话只在起始桶、跨午夜会话只在起始日、应用切换新会话、空闲中断新会话、30 天按被统计日期且与 7 天原始独立、清除清桶无复生）；App 新增 3 条隔离目录（落盘+重启恢复不重计、关闭时按被统计日期清理过期桶、清除清桶保留开关）。Core **602/602**、App **631/631** 全通过，构建 0 警告 0 错误（App/Core/DiagnosticProbe）。
  - 隔离存储验证：App 测试在 `%TEMP%/DP6Stats_*` 独立目录运行，断言 `stats.json` 中 `buckets` 内容、重启恢复、关闭清理与清除后为空；不写 `state.json`/`settings.json` 之外的宠物存档。
  - 未验证/限制：未做真实长时间（跨月）实机聚合观测；跨桶/跨午夜/保留用注入时钟覆盖。监理 R1 判定当前验收无需实际等待跨月，不将长期观察新增为阻断条件。详见 [整改记录 P6-002-response](remediation/P6-002-response.md)。
  - 监理 R1 接收（2026-11-27）：重新编译统计专项 Core 39/39、App 21/21；补充隔离验证 6/6（含 6000 次受控采样比对、失败重试、重启新增会话、v1 兼容与双保留精确边界）。[报告及限制](reviews/P6-002-review.md)、[证据](reviews/evidence/P6-002-R1/)。未重跑无关全量；P6-003/004 不自动推进。


### P6-003 频次预测：样本不足不预测

- 状态：`done`（2026-11-27 [监理 R2](reviews/P6-003-review.md)关闭 F-01，接收 P6-003；未提交/推送）。依赖：P6-002（done）。关联：[PRODUCT R-09 频次判断](PRODUCT.md#r-09-习惯学习阶段-6)、[ARCHITECTURE §7 P6-003](ARCHITECTURE.md#p6-003-频次判断约定已实现)、[交付记录](remediation/P6-003-response.md)。
- 范围（本轮展开，首版保守起步值，未经真实预测效果验证）：
  - **仅复用 P6-002 聚合桶**：不新增采集路径、不改桶语义；判断为 Core 纯逻辑（`UsageStatsFrequencyJudge`，无状态/无缓存），App `UsageStatsService.GetFrequencyPredictions()` 按需转发。
  - **窗口**：当前本地日期之前 30 个完整日期的名义回看，**与既有“含今天保留 30 天”聚合保留上限取交集**——保留区间 `[today−29, today]`，排除尚未结束的当天后实际可用 `[today−29, today−1]`，即**最多 29 个已完成日期**；当天、未来与超出保留期的日期均不参与（既不作支持也不作反对）。不延长数据保留、不为查询恢复已过期记录，过期桶从第一次查询起即排除（纯只读过滤，不依赖 drain）。
  - **覆盖门槛**：窗口内至少 **7 个不同日期**存在有效统计数据（`ActiveMs>0` 的桶）。“有记录的日期”不等于“完整观察了一天”，缺失数据不得当作用户没使用。
  - **候选门槛**：同一应用在**相同半小时时段**至少出现于 **5 个不同日期**且该时段累计会话数至少 **5 次**；“出现”＝该 (`AppId`, 时段) 桶 `ActiveMs>0`，会话数取 `SessionCount` 之和（一次使用＝一个会话）。
  - **输出**：应用标识、时段（`HH:mm`）、支持日期数、会话数，附 `ObservedDateCount`/`WindowDays`；`WindowDays` 为实际生效的完整日期数（默认配置下为 29，不冒称完整 30 日历史），`ObservedDateCount` 为有记录日期数；仅统计依据，不包装为预测准确率、不伪称知道用户意图。
  - **开关/清除语义**：沿用单一全局统计开关（无独立预测开关）；关闭即返回空；清除后旧预测与缓存立即失效（不保留预测缓存），重启由恢复桶重算。
  - **明确不做**：气泡、移动宠物、图标定位、自动启动应用；不进入 P6-004。
- 验收：样本不足（覆盖日期 <7 或支持日期 <5 或会话 <5）时不预测、不输出可被误读为“已可靠预测习惯”的内容；有足够样本时预测仅表达“统计上的时间偏好”。当天/过期数据不误计；关闭不输出；清除后立即失效；重启不恢复旧预测。
- 验证方式与证据去向：Core 注入时钟/构造桶覆盖门槛正反例、当天排除、30 天边界、关闭、非法桶跳过、排序；App 隔离目录覆盖落盘后重算、重启、关闭、清除。证据写入[整改记录 P6-003-response](remediation/P6-003-response.md) 并由 STATUS 链接；无真实长期样本时明确未验证项。
- 实现与验证证据（2026-11-27，开发自测通过、待监理复核）：
  - Core：新增 `UsageStatsFrequencyJudge`/`UsageStatsFrequencyOptions`/`UsageStatsFrequencyResult`/`UsageStatsFrequencyCandidate`（无状态、无缓存；有效窗口 = 名义 `[today−30, today−1]` 与保留上限 `[today−29, today]` 的交集，默认即 `[today−29, today−1]`；覆盖 ≥7 日期、支持 ≥5 日期且会话 ≥5；跳过非法/空/零活跃桶；按支持日期数→会话数→AppId 排序；`WindowDays` 输出实际生效完整日期数）。
  - App：`UsageStatsService.GetFrequencyPredictions()` 按需计算（无预测缓存，关闭/清除后立即为空，重启由恢复桶重算）；内部构造器新增可选 `localNow` 注入，生产默认 `DateTimeOffset.Now`。
  - 测试：Core 新增 11 条（关闭、6 日期不足、4 支持日期、4 会话、达标字段、当天排除含反证、窗口边界（today−29 计入 / today−30 过期）、非法桶、null、排序、会话求和）；App 新增 6 条隔离目录（达标+重启重算、默认关闭/开启/关闭即空、ClearAll 即空且落盘、当天/未来/过期忽略（today−29 为有效起点）、today−30 从首查即排除且 drain 前后+重启一致、today−29 在 drain 前后+重启均有效）。Core **613/613**、App **637/637** 通过，构建 0 警告 0 错误。
  - 未验证/限制：无真实长期使用数据，门槛为保守起步值，不宣称预测准确；未真实等待跨月；时区/夏令时未专项验证；未接展示（P6-004）。

- 监理 R1（2026-11-27）：Core 频次专项 11/11、App 统计专项 25/25；隔离生产清理反例证实首次查询可用过期 `today−30` 补足门槛，正常 drain 后候选消失。限定整改与监理起步指令更正见[报告 F-01](reviews/P6-003-review.md)。保持既有保留期和 7/5/5 门槛，统一查询有效日期交集及文档/边界测试；P6-001/002 接收保持，不进入 P6-004。
- F-01 开发整改（2026-11-27，开发自测通过、待监理复核）：`UsageStatsFrequencyOptions` 新增只读 `RetentionDays=30`；`UsageStatsFrequencyJudge.Evaluate` 将名义窗口与保留区间 `[today−29, today]` 取交集（默认 `[today−29, today−1]`），过期桶从首查即排除，不依赖 drain；`WindowDays` 改为输出实际生效完整日期数（默认 29）。修正 Core 窗口边界与 `WindowDays` 断言，App 新增 today−30 首查即排除、today−29 有效的 drain 前后+重启正反例。PRODUCT/ARCHITECTURE/STATUS/交付记录同步交集口径。详见[整改记录 F-01 段](remediation/P6-003-response.md#f-01-整改2026-11-27)。

- 监理 R2 接收（2026-11-27）：F-01 关闭；重新编译 Core 频次专项 11/11、App 统计专项 27/27，原隔离反例重跑首次查询/清理后均为覆盖6/候选0，对照均覆盖7/候选1；正式测试含 today−29/today−30 清理及重启正反例。权威文档及实际日期交集一致。[报告 R2](reviews/P6-003-review.md)、[证据](reviews/evidence/P6-003-R2/)。仅接收规则实现，不宣称真实预测可靠；P6-004 保持 todo。

### P6-004 展示：气泡反馈与统计入口

- 状态：`done`（2026-11-27 [监理 R4](reviews/P6-004-review.md)最终接收；R3 实机通过，F-01/F-02 关闭；用户按 [D-020](DECISIONS.md) 明确将靠近延期，不是宣称靠近已实现）。依赖：P6-003（done）。关联：[PRODUCT R-09](PRODUCT.md#r-09-习惯学习阶段-6)、[ARCHITECTURE §7 P6-004](ARCHITECTURE.md#p6-004-展示气泡与统计入口约定第一步已实现)、[交付记录](remediation/P6-004-response.md)。
- 本版范围：用气泡反馈及主动查看入口表达统计事实与有样本支持的时间偏好；沿用统计关闭/清除入口，不移动宠物、不恢复好感度；靠近另记 FUT-001，图标定位仍另行验证。
- 验收：展示内容与实际记录一致、区分记录与预测；统计关闭后无相关主动反馈；图标定位失败时不虚假宣称已定位。
- 验证方式与证据去向：隔离预览/实机人工步骤，按 TESTING 约定记录；证据入历史文档并链接 STATUS。

#### 第一小步：气泡反馈与统计查看入口（2026-11-27 起）

- **主动查看入口**：右键菜单新增「Usage Statistics」，打开只读查看窗，分两段展示：
  - 「已记录的统计」：有记录的日期数、近似前台活跃时长、会话次数、涉及应用数（全部来自已提交聚合桶，名称沿用 R-09 口径）。
  - 「有样本支持的时间偏好」：P6-003 候选列表（应用、时段、支持日期数、会话次数），标注为“统计依据，非预测准确率”。
  - 统计关闭时入口仍可打开，但如实显示未启用/无记录；不把缺失数据说成“没有使用”。
- **主动气泡门槛**：仅使用 P6-003 结果中 `SlotStart` 等于当前本地半小时时段且已达标的候选；无候选时保持安静，不输出“样本不足”提醒，不提示用户打开应用。
- **文案**：只表达统计依据，模板「过去有记录的日期里，你有 {N} 天在 {HH:mm–HH:mm} 这个时段使用 {app}」；不出现“你现在该打开它了”等指令式措辞，不包装准确率或“知道用户意图”。
- **展示参数（保守初值）**：约 8 秒自动消失，提供关闭按钮；主动气泡每个本地日期全局最多展示一次——去重日期持久化在独立 `stats.json`，跨重启同样生效；多宠时只选一只“可接受展示”的空闲宠物（拖动/抚摸/物理/委屈/探头/休息提醒/菜单/攀爬/社交/暂停任一进行中即不可用），其余宠物不展示。
- **抑制与撤销**：暂停、锁屏、睡眠期间不展示，恢复后不补播积压（仅在下一 tick 按当时状态重新判定）；统计关闭后立即隐藏当前气泡并丢弃待展示内容（保留当天已展示标记）；清除统计后同理隐藏，并重置已展示标记。
- **不抢焦点/不阻挡**：气泡为独立透明置顶窗口（不激活、不占任务栏），放置在宠物窗口外侧且不与宠物矩形重叠，不拦截宠物拖动与透明区命中，不覆盖已有高优先级交互；关闭按钮只关闭气泡。
- **明确不做（当前范围）**：本版不移动宠物、不做图标定位、不自动启动外部应用；原待讨论的「靠近」已由用户明确延期（D-020），移至 FUT-001。下列 R1～R3 记录保留当时分步状态，当前结论以本任务状态行与 R4 为准。
- 验收（第一小步）：查看入口区分“已记录统计”与“有样本支持的时间偏好”且名称准确；气泡仅由当前时段达标候选驱动，无候选静默；文案含依据且无准确率/意图宣称；8 秒自动消失、可关闭、每本地日期最多一次且重启去重；统计关闭/清除立即撤销；暂停/锁屏/睡眠不展示且不补播；不抢焦点、不阻挡宠物拖动与透明区命中。
- 验证方式与证据去向：Core 规则测试（时段匹配、每日去重、抑制、文案）+ App 隔离测试（去重持久化/重启、关闭与清除撤销、多宠单选）+ DiagnosticProbe 隔离预览；证据入交付记录并由 STATUS 链接。
- 第一小步实现与自测（2026-11-27，开发自测通过、待监理复核；P6-004 仍 `in-progress`，「靠近」待讨论）：
  - Core 新增 `UsageStatsBubblePlanner`（判定/文案/汇总），`UsageStatsArchive.BubbleLastShownLocalDate`；App `UsageStatsService` 恢复/记录/清除、`StatsBubbleWindow`、`StatsViewerWindow`、`MainWindow`「Usage Statistics」入口与可用性判定、`App` 判定定时器与暂停/锁屏/睡眠/关闭/清除撤销接线。
  - 验证：Core 规则 25/25、App 隔离 7/7；DiagnosticProbe 新增 `usage-stats-bubble` 隔离预览（当前时段候选驱动、每日一次含重启去重、关闭保留/清除重置、查看窗两段准确名称）；全量 Core **638/638**、App **644/644**；App 与探针构建 0 警告 0 错误。详见[交付记录](remediation/P6-004-response.md)。
  - 未验证：8 秒自动消失与关闭按钮真实点击、真实长期样本、多屏/DPI 组合；本小步未移动宠物、未定位图标、未启动外部应用。

- 第一小步监理 R1（2026-11-27）：重新编译 Core 规则 25/25、App 隔离 7/7；补证复现社交共享状态尚未同步本地旗时选窗守卫误放行、查看汇总首查计入过期桶。F-01/F-02 限定整改见[报告](reviews/P6-004-review.md)及[证据](reviews/evidence/P6-004-R1/)。离屏真实 WPF 消息泵 8.5 秒后定时隐藏通过；真实关闭按钮/焦点/宠物输入留待代码复核后集中实机。不重开 P6-001/002/003，不推进靠近。

- 第一小步 F-01/F-02 开发整改（2026-11-27，开发自测通过、待监理复核）：
  - F-01：`MainWindow.IsAvailableForStatsBubble` 追加 `!IsInSharedSocialSession`，复用共享社交权威会话（与 `IsPerchWriteAllowed` 同源），保留本地 `_isSocialActive` 与其余全部守卫。新增 App 隔离正反例：共享会话活跃且本地旗未更新时不选/不展示/不消费每日标记，会话结束恢复；另有真正空闲且未参与会话的宠物时按原规则选取。
  - F-02：`UsageStatsBubblePlanner.SummarizeRecorded` 增加必填本地时间参数，从首查即按含今天窗口 `[today−29, today]` 过滤（与 `UsageStatsCollector.PruneToRetention` 同口径，未来日期也排除，不套用预测的“排除今天”）；`UsageStatsService.GetRecordedSummary` 传入 `_localNow()`，查询不写盘、不延长保留、不复活过期记录。新增 Core 边界用例（today−30 首查排除、today−29/今天保留、未来排除、窗口随日期右移）与 App 用例（首查即过滤、drain 前后一致、关闭可见期限内旧记录、重启一致、清除为空）。
  - 回归：Core **641/641**、App **647/647**，构建 0 警告 0 错误；探针 `usage-stats-bubble` 干净目录重跑 exit 0。详见[交付记录第 6 节](remediation/P6-004-response.md#6-r1-限定整改f-01--f-022026-11-27)。真实关闭按钮/焦点/输入体验待代码复核通过后再集中安排。

- 第一小步监理 R2（2026-11-27）：F-01/F-02 关闭；重新编译专项 Core 28/28、App 10/10，原隔离验证器四组全部通过（共享会话不误选、会话结束恢复、汇总首查/清理一致、离屏真实定时隐藏）。[报告 R2](reviews/P6-004-review.md)、[证据](reviews/evidence/P6-004-R2/)。第一小步仍待真实关闭按钮/焦点/宠物拖动与透明区命中及观感，约用户可占鼠标时段再执行；不推进靠近、不标 done、不提交推送。

- 第一小步监理 R3 接收（2026-11-27）：用户授权后真实点击气泡关闭、默认约8秒隐藏、不抢前台、气泡可见时实体拖动/透明角落穿透、真实原生菜单查看入口及两段内容、正常退出通过。[报告 R3](reviews/P6-004-review.md)、[证据](reviews/evidence/P6-004-R3/)。本监理初轮夹具漏隔离启动导致一次正式主存档写入，已停止并恢复测试前主文件、复核哈希不变；更早备份未能恢复，完整事故/恢复限制亦见报告，不归为产品问题。随后两轮正确隔离数据通过且无测试残留。整体仍 in-progress，靠近待用户讨论，不提交推送。

- 监理 R4 最终接收（2026-11-27）：用户明确“按这个方案收尾”，本版保留纯气泡及查看，靠近转后续独立待办；R2 代码复核与 R3 真实输入证据已满足调整后的全部范围，P6-004 标 done。纯文档更新，未改代码/资源、未重测、未提交推送；R3 事故和恢复限制保留，旧历史记录不改写。

## 后续独立待办（未排期，不计入已完成阶段 6 本版）

### FUT-001 习惯反馈主动靠近

- 状态：`todo`（2026-11-27 用户按 [D-020](DECISIONS.md) 决定从本版延期，未授权开工）。
- 依赖：P6-004 已接收；先由用户确认靠近目标、触发/冷却、位置所有权和接管规则。精确图标定位如确有需要，另设可行性验证，不默认可用。
- 当前仅登记：不实现移动、不补“占位靠近”、不以本待办阻塞 P6 本版收尾。验收/拆分待用户另行安排后制定，不自动进入下一阶段。

## 新任务模板

```text
任务 ID 与名称：
状态：todo
依赖与需求 ID：
必读章节：
输入/已有条件：
范围与明确边界：
预期产物：
可观察验收条件：
验证方式与证据去向：
阻塞条件（如有）：
```


P5B-001 R1直接整改补完（2026-11-22）：用户授权本助手实施，专项39/39、全量1133/1133、M-17独立夹具18/18全部通过；新增证据与限制见 [整改记录](remediation/P5B-001-response.md)。保持awaiting-verification，需独立复核，不代签原R1问题关闭。


P5B-001 监理R2收尾（2026-11-22）：用户指定本助手继续复核，原R1四项全部关闭，首版接收；当前状态以本任务done状态行及R2报告为准。保留历史整改进度，不推进后续功能，未提交/推送。


P5B-002 M-18监理确认（2026-11-22）：[R1验证记录](reviews/P5B-002-review.md)，实机确认轮18/18通过；当前保持awaiting-verification，待整体交付审查，实机时段等待已解除。


P5B-002监理R3（2026-11-22）：R2 F-01关闭，正式入口接收，当前状态以任务done状态行与R3报告为准。未重复实机，未提交/推送。


P5B-003范围调整（2026-11-23，用户明确决定）：当前美术临时使用，视觉改进后置，不再等待候选造型批准。原“观感确认后接线”前置条件由R6用户接受替代；可生成必需帧并接入动作/镜像/中断。脚点、阶段切换、内容一致性、动作释放及缺失资源回退验收仍保留，不将临时美术称定稿。



















