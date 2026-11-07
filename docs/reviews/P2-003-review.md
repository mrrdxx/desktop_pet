# P2-003 App物理接入审查

## S1 首轮审查（2026-11-06）

对象为190b065之后未提交的App/测试/文档。独立构建并执行ReleaseSamplingIntegrationTests专项8/8通过，0失败/跳过，未见编译警告/错误；另驱动真实MainWindow手势方法复现2个采样反例，见[evidence/P2-003-S1-initial](evidence/P2-003-S1-initial/)。本轮未重跑全量569（为开发自测报告），未运行真实鼠标/窗口实机、未改产品代码或提交/推送。

范围正确：只记录释放结果，不启动物理/新增美术。这部分接受；P2-001/P2-002纯Core结论不重开，问题在实际App采样接线。

### F-01 中 仅MouseMove采样，缺少开始基准和真实静止检测

- StartDragging仅Reset，未记录按下位置/时间；ProcessCapturedPointerMove只在实际MouseMove事件更新样本，非“每tick持续采样”。EndDrag也只是Evaluate，没有补齐释放/停留语义。
- 独立生产手势反例1：按下@0→一次快速移动@10→释放@35，只有1样本，ShouldThrow=false。单次有效快速移动也应能与按下基准构成轨迹，不应要求至少两次移动事件。
- 反例2：移动@10/@30后静止不产生MouseMove，@96释放，采样器保留两段快移，ShouldThrow=true/Vx=2/RawSpeed=5。与Core中“连续静止样本检测停止”不同，App从未提供真实停留证据。
- 现StopBeforeRelease测试在250ms手工调用同位置Move，且已超过窗口，不证明真实静止链；注释称同位置重复样本被时间校验拒绝也不准确，时间递增就可接受。
- 整改：统一明确采样时机，拖动开始记录真实物理脚点/单调时间；真实静止时也能判停（可用实际tick采样或等效近期运动/静止时长策略）。不能靠测试虚构MouseMove，不得简单每次释放追加一个零位移末段而把正常“快移后5ms释放”全部判成停止。保留头部转换的原始抓取点，只有实际进入drag后采样，不恢复计分。
- 验收：一次快Move+按下基准即可甩；快Move后不再调用Move、仅推进真实tick/时间再释放能轻放；正常快拖后短间隔立即释放仍甩出；慢放、重新拖动重置、头部转拖动有实际入口正反例。

### F-02 中 仅布局变化清样本不等于DPI上下文清理

- 当前仅WM_DISPLAYCHANGE/WM_SETTINGCHANGE路径调用OnDisplayLayoutChanged清采样；移动到另一DPI显示器时，显示器布局未改变，通常走窗口DPI变化而非这些消息。代码未处理WM_DPICHANGED/WPF DpiChanged，也未在采样时比较最新与此前DPI上下文；旧/新比例物理脚点仍可能混算。
- 独立静态核对确认采样时每次GetWindowDpiScale直接换算后RecordSample，未比较上下文；现测试只反射调用布局变化handler，不能证明真正DPI变化接线。
- 整改：在生产边界可观察的窗口DPI/坐标上下文变化时调用ClearSampleContext，接入前清理旧坐标；提供最小可控DPI来源用于测试，生产与测试共用变化检测与转换路径，不在测试另写清理逻辑。延续Core调用方契约，不要求本轮实际双屏。
- 验收：布局不变时DPI1→1.5，旧样本清空，立即释放不抛；新上下文后真实运动可重新抛；同DPI正常拖动不会反复被清空。真实混合DPI仍如实列未实测。

### 后续接窗口运动前的注意项（非本轮新增阻断编号）

EndDrag同时被菜单/捕获丢失/关闭等取消路径复用。S1仅记录值尚未启动物理，但S2不能把这些取消也当正常释放Launch；后续接线须区分正常鼠标释放与取消，沿用既有打断优先级，不扩展产品含义。

结论：S1部分接受，F-01/F-02阻断接入窗口物理运动；只修本小步和相关测试/ARCHITECTURE，不提前S2/S3。P2-003保持in-progress，开发方新建docs/remediation/P2-003-response.md按编号回应，不改本报告或代签关闭。监理同步STATUS/TASKS，不提交/推送。

## S1 复核 R1（2026-11-06）

对象为本轮未提交整改，文件摘要及独立证据见 [P2-003-S1-R1](evidence/P2-003-S1-R1/)。独立构建并运行 App 释放采样专项 **13/13 通过**，0 失败/跳过；另运行真实 MainWindow 手势入口探针。全量 574/574 为开发自测报告，本轮未独立重跑；未执行系统鼠标或混合 DPI 双屏实测，未改产品代码、未提交/推送。

### F-02：通过并关闭

生产 SourceInitialized 已订阅 WPF DpiChanged，handler 进入与测试共用的 NotifyDpiChanged → ClearSampleContext；专项包含清空后立即释放、新轨迹恢复及同 DPI 保留样本。当前 App 清理边界接受，不把这一结论写成混合 DPI 双屏实测通过。该项不重复整改。

### F-01：部分通过，剩余头部转拖动首次采样

- 身体按下@0 → 单次快移@10 → 释放@35，独立复验 count=2、ShouldThrow=true、Vx=2；快移@10/@30 后不再发送 Move、@96 释放，ShouldThrow=false。初始身体基准、真实静止及正常短间隔甩出修复接受。
- 头部路径仍失败：按下实体头部 local=(30,9)、screenDIP=(130,109) @0，单次越过容差的快移到 screenDIP=(260,165) @10，@35 释放，count=1、ShouldThrow=false。此时已经转入真实拖动，仍不能用有效的首次位移甩出。
- 原因：ConvertPettingToDrag 在首个 Move 内调用 StartDragging；StartDragging 用当前时刻@10记录旧位置基准，同次 Move 更新位置后仍用@10记录，后者被 Core 的相同时间校验拒绝。身体按下时已有@0基准，故现有身体测试无法发现此路径。
- 仅补该接线：为头部转拖动保留可追溯的真实起点位置/时间或等效有效轨迹，沿用原始抓取偏移和捕获，不改变 Core 相同时间拒绝规则，不伪造时间差、不恢复计分。
- 验收：通过实际 BeginCapturedPointerInteraction → 单次 ProcessCapturedPointerMove 转拖动 → EndCapturedPointerInteraction，验证头部快移可抛及方向正确；头部轻点/容差内微移仍不误抛，已有身体快移和真实静止测试保持通过。

结论：F-02 关闭，F-01 仅剩上述头部转换分支，仍阻断 S2/S3。P2-003 保持 in-progress。开发方只在原整改记录追加本项措施和相关验证，不重做已接受部分，不改本报告。

## S1 复核 R2（2026-11-06）：F-01 通过并关闭

复核对象为本轮未提交头部转换整改，版本摘要和独立运行证据见 [P2-003-S1-R2](evidence/P2-003-S1-R2/)。独立构建并执行 ReleaseSamplingIntegrationTests **15/15 通过**，0 失败/跳过，未见编译警告/错误。全量 576/576 为开发自测报告，本轮未独立重跑。

- BeginCapturedPointerInteraction 保存真实按下单调时间，StartDragging 使用该时间记录旧位置脚点基准；头部转换后首次位移仍由实际 Move 记录，不修改 Core 的相同时间拒绝规则，原始抓取偏移保持。
- 原 R1 独立手势探针复验：实体头部按下@0 → 单次快移转拖动@10 → 释放@35，样本数由 1 变为 2，ShouldThrow=true，Vx≈1.8368、Vy≈0.7912，方向与实际位移一致。
- 同探针确认身体单次快移仍可甩出（Vx=2）；快移后真实静止 66ms、不发送额外 Move，仍为轻放。专项同时包含头部容差内微移不转拖动、不误抛的反例。

F-01 通过并关闭；F-02 沿用 R1 关闭结论，不重复整改。S1 采样与释放判定接线接受，P2-003 整体仍 in-progress，未实现或验收窗口物理运动。下一步可按既定小步进入 S2；S2 须区分正常释放与菜单/丢捕获/关闭取消，不能一律 Launch。S3 打断协调仍按后续范围实施。

本轮未修改产品代码或开发整改记录、未提交/推送；未执行真实系统鼠标或混合 DPI 双屏实测，手感仍留 P2-004。

## S2 首轮复核 R3（2026-11-06）：部分接受

独立构建并执行 ReleaseSamplingIntegrationTests **20/20 通过**，0 失败/跳过，未见编译警告/错误。另驱动真实 MainWindow 手势、OnUpdateTick、Close 和 UpdatePhysics 入口验证。代码摘要、TRX 及探针见 [P2-003-S2-R3](evidence/P2-003-S2-R3/)。全量 581/581 为开发自测，本轮未独立重跑；本轮不是系统鼠标实机测试。

已接受：正常甩出 Launch、轻放不 Launch、显式取消清采样、foot-center 工作区边界公式、物理回写窗口路径。独立真实 OnUpdateTick 连续推进观察到 62 个位置变化 tick 后停止，位置由 (300,100) 到 (1421.9307,912)。没有据此宣称多屏/DPI或手感通过。S1 F-01/F-02 保持关闭。

### F-03 中：菜单和关闭生产取消入口仍会 Launch

- EndDrag(bool isCancellation=false) 已有区分，但 ShowNativeContextMenu（约545行）和 OnClosed（约2017行）仍调用 EndDrag()，默认正常释放。只改 LostMouseCapture 不足以覆盖明确要求的菜单/关闭取消路径。
- 独立实际关闭反例：身体按下@0 → 快移@10 → @35 Close()，关闭后 IsPhysicsActive=true、IsPhysicsLaunched=true、LastReleaseResult.ShouldThrow=true。关闭动作错误启动了抛掷；菜单入口静态核对为同样的默认参数调用。现测试直接传 true，不能覆盖入口漏传。
- 整改：梳理生产调用点，菜单/关闭/捕获丢失等取消显式走取消路径，只有已定义的正常松手可评估并 Launch；可使参数必须显式指定以避免遗漏，不要求改变 S1 的速度规则。
- 验收：实际关闭入口及菜单取消使用的生产公共入口均有有区分度测试；快拖后取消不 Launch，正常快拖松手仍 Launch。不要用测试手工传 true 替代证明生产调用正确。

### F-04 中：Rejected 后仍永久保持物理活动，与文档声称不符

- UpdatePhysics 在 result.Rejected 时直接 return，没有停止或清理；ARCHITECTURE 和交付报告却写 IsResting/Rejected 时停止。
- 独立反例：正常 Launch 后令窗口宽度大于工作区，BuildWorkArea 得到左右颠倒的非法 anchor 边界，连续 3 次 UpdatePhysics 后 IsPhysicsActive/IsPhysicsLaunched 仍为 true。后续 tick 重复拒绝，ClampWindowToWorkArea 又因物理活动跳过，无法自行恢复。
- 整改：对不可用工作区/拒绝结果提供明确的停止和安全位置处理，清理活动状态与模拟器；不把缺少显示器的 (0,0,0,0) 默认为正常可落点而移动窗口到原点。若处理短暂拒绝选择其他策略，须明确有限恢复路径并同步规格，不能无限挂起。这里只要求当前 S2 输入/边界失败路径，不扩展 S3 显示器变更完整协调。
- 验收：合法区域正常 tick 可移动并收敛；窗口大于工作区/不可用区域或明确 Rejected 路径不会无限 active，也不写入无效坐标。相关文档与真实行为一致。

### G-01 验证说明（非独立阻断项）

现 S2_PhysicsDriven_WindowMovesUntilRest 反射调用 UpdatePhysics，并未如注释所述驱动 OnUpdateTick；leftAfterRelease 未用于断言，最终只断言 inactive。建议改为实际 tick，断言位置确实变化及整个窗口处于合法工作区，再断言最终停止；当前独立探针已确认普通 tick 运动链可用，不因此另设一轮门槛。

结论：S2 部分接受，F-03/F-04 阻断本小步接受与 S3。开发方仅补这两项、相关测试和文档，在原整改记录追加，不改监理报告；不重做 S1、不自动进入 S3、不提交/推送。暂停/探头/退出存档整体协调仍留既定 S3。

## S2 复核 R4（2026-11-06）：F-03/F-04 通过并关闭

对象为本轮未提交 F-03/F-04 整改。独立构建并执行 ReleaseSamplingIntegrationTests **24/24 通过**，0 失败/跳过，未见编译警告/错误；原 R3 生产入口探针复验通过。证据与文件摘要见 [P2-003-S2-R4](evidence/P2-003-S2-R4/)。全量 585/585 为开发自测，本轮未独立重跑；未运行真实系统鼠标或混合 DPI/多屏实测。

- **F-03 关闭**：菜单和 OnClosed 均显式传 isCancellation:true。专项通过实际菜单方法及 OnClosed 覆盖取消入口，正常松手正例仍 Launch；独立 Close() 原反例结果 active=false、launched=false、throw=false。菜单测试只证明菜单前置取消接线，未实际弹出菜单，开发记录已如实说明，不将其冒充可见菜单实机操作。
- **F-04 关闭**：Rejected 路径清理活动标志并 Reset，不回写位置；专项验证非法工作区后位置不变。独立原反例连续推进结果 active=false、launched=false，已无无限活动。合法区域实际 OnUpdateTick 仍有 62 次位置变化后停止，未破坏普通运动链。

S2 当前范围接受，F-01～F-04 全部关闭。P2-003 整体仍 in-progress，下一步可进入既定 S3，不能标 done。S3 须完成重新抓取锚点、暂停恢复无积压、探头延迟、工作区变化及退出/保存/重启静止协调。

范围澄清：R3 F-04 的 Rejected 收尾已关闭；空显示器列表当前仍返回零面积 WorkArea，属于显示器不可用的后续协调缺口，本轮未验证其安全恢复，不宣称已覆盖。S3 既有工作区变化范围内应明确处理“无可用显示器/区域”，保留有效位置、停止物理，不能把零矩形当真实工作区；不另增 S2 阻断或要求独立重复审查。G-01 测试改进建议保留为非阻断，下一步测试宜驱动实际 tick 并检查位移/边界。

本轮仅维护监理报告、证据和 STATUS/TASKS，未修改产品代码、开发整改记录，未提交/推送。

## S3 首轮复核 R5（2026-11-07）：未通过

独立构建并执行 ReleaseSamplingIntegrationTests **31/31 通过**，0 失败/跳过，未见编译警告/错误；另运行真实 MainWindow 手势/tick/暂停恢复入口探针。全量 592/592 为开发自测，本轮未独立重跑。代码摘要、TRX及原始探针见 [P2-003-S3-R5](evidence/P2-003-S3-R5/)。未执行真实鼠标、双屏实机，未修改产品代码或提交/推送。F-01～F-04 保持关闭。

接受的局部内容：身体重新抓取停止物理且初始位置不跳；布局变化停止物理后调用既有位置约束；退出辅助方法/OnClosed 有停止处理；无显示器时不再构造合法零矩形。以下问题属于当前 S3，须一起整改而非逐轮新增范围。

### F-05 中：暂停实现成终止，恢复不继续原运动

- PRODUCT R-04 明确“暂停冻结当前运动，恢复仅计新的活跃时间”；ClearTimeAccumulation 设计为清时间余量、不清运动状态。当前 PauseAnimation 调 ForceStop 清速度并撤销 active，ResumeAnimation 不恢复，改变了既定产品语义。
- 独立反例：飞行中暂停、5秒后恢复并推进33ms，active=false、窗口 delta=(0,0)，永久悬停而非继续原轨迹。现测试反而要求恢复不激活，锁定了错误行为。
- 整改：区分暂停冻结与终止，保留位置/速度/运动状态，暂停期间不推进；恢复重设单调时间基准并清 carry，仅计算新的活跃间隔。重新抓取/退出等明确终止仍可停止，不持久化速度。
- 验收：暂停前后状态保留；恢复后轨迹与无暂停、相同活跃时间对照一致，无5秒补帧；实际 tick 在暂停中不写位置。同步 ARCHITECTURE/测试，不能为了贴合实现把 PRODUCT 改成暂停即取消。

### F-06 中：物理期间跳过展示，却提前消费探头计划

- StartProbe 加 !isPhysicsActive 只挡表现。上游 Evaluate 仍执行且有消费副作用，ShouldFire 后又 MarkConsumed。PRODUCT R-04 要求原未消费计划等待合适时机。
- 独立注入可触发的真实 scheduler/tracker 后驱动实际 tick：active=true、probing=false、consumed=true。一次彩蛋被无声丢弃。
- 整改：物理持有位置时延迟有消费副作用的评估，沿用委屈表达的延迟语义；有效时间与日期计划维护照常。消耗/标脏仍在真正评估结果变化时执行，保留日期/roll/delay/baseline，不重抽。
- 验收：物理期间用确定可触发计划，检查未消费且原快照不变；物理结束后同计划正常启动并消费。补 roll-failed 对照，证明也是结束后才消费。现 S3_ProbeDelayedDuringPhysics 未设置计划，只检查没探头，不能证明此规则。

### F-07 中：头部重新抓取未取得冻结位置控制

- 停物理仅在 StartDragging；头部按下先进入非计分候选，不调用 StartDragging，OnUpdateTick 又只排除 isDragging，继续物理推进。
- 独立反例：飞行中实体头部按下被接受，candidate=true，下一 tick active=true，窗口 delta≈(100,5) DIP，宠物从已抓住的鼠标下移走。身体测试不能覆盖头部。
- 整改：接受实体抓取时即可停止/接管旧物理，同时保持头部容差/原始抓取偏移、捕获以及短按规则；不可因头部候选尚未转 drag 而继续旧运动，不扩大透明点命中。
- 验收：实际头部按下→保持/微移→tick 不移动；头部后续转拖动首段不跳，身体重新抓取保持正常，透明按下不错误打断。

### F-08 中：S3 必需的无显示器及退出重启证据缺失

- S3_NoMonitor_PhysicsRejectedAndStopped 实际设置 Width=100000，重复 S2 非法尺寸反例，并没有输入空显示器列表。只能证明非法区域拒绝，不能标记无显示器分支已验证。
- S3_ExitDuringPhysics_StopsAndSavesValidPosition 只调用 RestoreProbePositionForExit，并检查 Left/Top 有限；未写文件、未重载、未建立新窗口，有限值也不等于位于合法工作区。布局测试也只断 active=false，缺位置合法性断言。
- 补证：以最小可控显示器来源或适配边界驱动真实 BuildWorkArea/相关布局路径，覆盖空列表和恢复后的可用区域，检查原有效位置不被写成原点/非法值；重用已有隔离 ArchiveService/SaveCoordinator 与生产 FlushFinalState/恢复映射，验证飞行→退出保存→文件重载→新窗口静止恢复，检查完整窗口边界及不持久化/补演速度。不得接触用户真实存档。
- 这是本轮原计划内的验收补证，不要求真实拔显示器或重跑无关全套测试；旧 S1/S2 结论不重开。

结论：S3 未通过，F-05～F-08 阻断 P2-003 完成与 P2-004 开始。开发方一起处理上述现有范围，修复和自测追加原整改记录，保留历史修订轨迹；测试不应仅确认错误实现。不自行改报告/标 done，不提交/推送。暂停语义以 PRODUCT 为准，ART 历史未验证项不扩大本轮范围。

## S3 复核 R6（2026-11-07）：F-06/F-07 关闭，F-05/F-08 部分通过

独立构建并运行 ReleaseSamplingIntegrationTests **32/32 通过**，0 失败/跳过，未见编译警告/错误；另重跑生产手势/tick探针，证据见 [P2-003-S3-R6](evidence/P2-003-S3-R6/)。全量 593/593 为开发自测，本轮未独立重跑。未执行真实鼠标/混合 DPI/多屏实测。F-01～F-04 保持关闭。

### F-06：通过并关闭

Evaluate 入口已同时排除物理活动，日期维护/有效时间推进保留。独立注入确定可触发计划，实际 tick 结果：物理期间 active=true、probing=false、consumed=false；推进至落地后 active=false、probing=true、consumed=true。同一计划从未消费到可展示时正常消费，原错误已修复。RollFailed 共用同一 Evaluate 守卫，不另设重复复核门槛。

### F-07：通过并关闭

实体头部候选被接受时即 ForceStop 并释放旧物理活动；实际 tick 也排除头部候选。独立头部按下反例复验 accepted=true、candidate=true、active=false，下一 tick delta=(0,0)，不再从鼠标下移走。原始抓取偏移仍在按下时保存，既有头部转拖动专项保持通过。接受本次实际修复，不因尚缺专门命名回归测试另增阻断。

### F-05：部分通过，仅剩暂停 tick 守卫与有效时间对照

暂停保留速度/活动状态，恢复重置时间基准并清 carry 的方向接受。但 OnUpdateTick 的物理分支仍没有 !_isAppPaused 条件。停 DispatcherTimer 不等于 tick 方法本身在暂停时不会推进；R5 明确要求实际 tick 在暂停中不写位置。

独立反例：飞行中@80 PauseAnimation → @180 调实际 OnUpdateTick，active=true，窗口 delta≈(166.6667,11.1111) DIP。这里只证明若 tick 进入则会移动，不宣称用户正常暂停时必然发生；当前是生产更新入口状态守卫缺口。

仅补物理更新的暂停守卫；测试在暂停中实际调用 tick，确认位置/速度不变，再恢复以相同活跃时间无暂停对照验证轨迹。现“恢复位移非零且小于500 DIP”只能粗略证明会动，不能证明暂停间隔被正确排除。不要重新改为 ForceStop，也不重做 F-06/F-07。

### F-08：空显示器拒绝接受，生产退出恢复链仍缺

TestMonitorList 注入空列表，经真实 BuildWorkArea → Rejected → Reset，专项验证原位置不变；该部分接受，不重复超大窗口/空列表测试。

新增退出用例虽执行真实 ArchiveService I/O，但仍由测试自己 new PetStateArchive 填 WindowLeft/Top 并 SaveState，再 new MainWindow 手工赋 Left/Top。没有调用生产 SaveCoordinator/FlushFinalState，也没有执行生产恢复入口。即使删除生产退出前恢复/标脏接线，这个测试仍可通过，不能作为 R5 要求的生产闭环证据。

同时目前只比较保存前后坐标/尺寸相同，没有工作区包含断言；保存一个屏外有限位置也能通过，与“完整窗口边界已验证”不符。

仅补隔离真实生产链：飞行→生产退出/FlushFinalState→文件→生产加载与位置适配→新窗口；断言整个窗口位于指定合法工作区、没有恢复速度且首 tick 静止。复用既有 TestSetupIsolatedSave/已有隔离生命周期工具，最小注入必要依赖；不另写测试专用保存/恢复算法，不接触真实存档。把断开生产链会失败作为有区分度验证；同步准确描述验证范围。

结论：F-06/F-07 关闭；F-05/F-08 剩余部分继续阻断 P2-003 完成与 P2-004。仅补这两处，相关自动验证即可，不要求用户重复体验、不新增功能、不重跑无关全套测试。未改产品代码/开发整改记录、未提交/推送。

## S3 复核 R7（2026-11-07）：F-05/F-08 关闭，P2-003 接受

对象为本轮未提交 F-05/F-08 整改，代码/文档摘要及独立证据见 [P2-003-S3-R7](evidence/P2-003-S3-R7/)。独立构建并执行 ReleaseSamplingIntegrationTests **32/32 通过**，0 失败/跳过，未见编译警告/错误；另运行原真实入口反例和隔离生产保存/恢复探针。全量 593/593 为开发自测，本轮未独立重跑。未运行系统鼠标实机、混合 DPI/多屏测试。

### F-05：通过并关闭

OnUpdateTick 物理分支已有 !_isAppPaused 守卫，暂停保留物理状态，恢复重设时间引用并清时间余量。原反例复验：暂停中实际 tick 的 delta=(0,0)，恢复后33ms delta≈(33.3333,1.1111)，运动继续而非终止/补演暂停间隔。专项增加无暂停与暂停前后活跃 tick 对照并通过（位置容差1 DIP）；接受当前固定步长/清 carry 约定下的暂停行为，不宣称任意异步切点都做严格解析轨迹相等。

### F-08：通过并关闭

退出用例已复用生产 TestSetupIsolatedSave → TestSetMainWindow → FlushFinalState/SaveCoordinator → ArchiveService.LoadState → RestoreWindowPosition，移除了测试手写保存/位置恢复算法。专项包含完整窗口四边工作区断言、新窗口物理未激活且首 tick 不动。

独立隔离探针进一步确认：退出前飞行，真实 FlushFinalState 后 active=false；文件位置与退出清理后窗口完全匹配（470.3333333,193.5555556），生产 RestoreWindowPosition 恢复相同坐标，新窗口 active=false/ launched=false。仅使用 artifacts 下的隔离存档目录，未调用正常 OnStartup/真实存档目录。原空显示器拒绝结论沿用 R6。

结论：F-05/F-08 通过并关闭，F-01～F-08 全部关闭。S1/S2/S3 范围的代码接入及自动/隔离验证接受，**P2-003 可标 done**，监理同步 TASKS；开发方在原整改记录追加本轮关闭依据即可，不重复申请关闭。下一任务 P2-004 专门验证真实轻放/抛掷/反弹/抓取/暂停的手感并校准初值，当前不代表该体验已通过，不替 ART 历史美术未验证项签字。

测试说明：开发退出测试仍可加强“文件坐标等于退出窗口位置”的直接回归断言，本轮独立探针已验证该事实，作为非阻断建议，不另设审批轮次。

本轮未修改产品代码/开发整改记录，未提交或推送；独立保存探针首次因漏 App.Persistence 命名空间编译失败，仅修正 artifacts 下探针后重新运行成功，未改产品实现。P2-004 本轮尚未启动。
