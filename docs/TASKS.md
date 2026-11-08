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

- 状态：`todo`。依赖：PREP-003（P2-004已完成）。关联：R-05、D-017、D-018。
- 范围：最多两只、稳定ID/性格配置、独立输入/动画/物理/委屈状态、添加/移除第二只、全局操作及兼容存档。不实现社交/自主移动/关系值或新美术。
- S1：核对单宠全局字段与存档v3，明确实例/全局数据归属；实现最小身份/集合及旧档迁移，写回架构/存档契约并验证。不开第二窗口前先交付可核对迁移证据。
- S2：将运行时状态按PetId隔离，创建第二窗口；通过真实共享输入入口证明拖动/抛掷A不改B；保留单宠回归和位置单写入。
- S3：接入添加/移除、全局暂停/恢复/找回/退出和实际保存/重载链；隔离文件验证旧档→双宠→移除→重启，最后实际双窗操作验收。
- 验收：按R-05共存目标与M-06执行，稳定ID/数量/角色恢复、独立状态、最多两只、移除后不复活、找回不完全重叠；新格式未知高版本/损坏保护不退化。自动/隔离/实际UI证据分开记录。无实测保持awaiting-verification，不自行签监理。
- 第一个允许交付范围仅S1；用户发出具体开工后执行，未验收不自动把S2/S3打包完成。

### P3-002 相互注意与协调

- 状态：`todo`。依赖：P3-001。关联：R-05。
- 范围：双方识别/注意、短暂回应与拒绝、间距、唯一会话和双方取消；此时再落实社交协调器，不预做追逐全框架。
- 验收：温和角色差异可观察；慢回应不延迟抓取；任一方被抓起/暂停/移除均释放双方；明确触发/冷却/超时，透明点不误中断。

### P3-003 被拖走后寻找

- 状态：`todo`。依赖：P3-002。关联：R-05。
- 范围：等待释放/物理结束、同底边可达判断、寻找靠近并留间距；悬空只表达关注，不在抓取期间追鼠标。
- 验收：有正反可达路径；目标离屏/移除/超时退出；两只不永久重叠，不与物理/探头抢位置。具体时长/步速先写规格再实现。

### P3-004 短暂追逐及双宠阶段验收

- 状态：`todo`。依赖：P3-003。关联：R-05。
- 范围：邀请/接受/拒绝/短追逐/结束冷却，低频温和；阶段末记录双宠性能，不实现争位置/吵架。
- 验收：追逐有距离/时间上限，不无限逃跑；底边支撑/打断/暂停/移除链健全，实际体验认可。与单宠性能基线说明同环境比较限制。

- 后续扩展：争位置/吵架及长期关系记忆，待前述体验后由用户安排，不属P3-001～004交付门槛，不自行恢复好感度。

## 阶段 4：作息与体验

- `P4-PLAN`：`todo`，依赖阶段 3 验收。
- 将 R-06 拆成活跃会话统计、休息表演、夜间表现、锁屏/睡眠恢复；不实现输入封锁。

## 阶段 5：窗口生活

- `P5A-PLAN`：`todo`，依赖阶段 4 验收。先验证 R-07 的窗口读取和失效恢复，再做栖息与晃动。
- `P5B-PLAN`：`todo`，依赖 R-07 验收。按 R-08 做有限攀爬，复杂寻路后置。
- 未通过窗口可行性验证前，不给社交或物理系统加入 HWND 依赖。

## 阶段 6：习惯学习

- `P6-PLAN`：`todo`，依赖阶段 5 验收；用户可明确调整优先级。
- 按 R-09 分开实现可关闭的统计、聚合、频次预测和展示；精确图标定位另立探索任务。
- 没有真实样本/对照验证，不能把频次统计写成已经可靠预测习惯。

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
