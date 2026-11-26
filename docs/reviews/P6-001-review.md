# P6-001 本地统计审查

## R1：2026-11-26 首次交付审查——四项阻断，暂不进入实机或P6-002

对象：HEAD `0aa994dd371ac7e804ca863836b71eeadf877788`加当前未提交工作区。实际读取STATUS、当前TASKS、PRODUCT R-09/ARCHITECTURE §7、独立整改记录与Core/App实现；相关文件SHA见[源码摘要](evidence/P6-001-R1/source-hashes.json)。只审本任务，不重开P5，不改开发产品/测试源码。

### 本轮实际验证与覆盖

监理独立输出目录重建并运行：Core UsageStatsCollectorTests **17/17**、App UsageStatsServiceTests **13/13**，0失败0跳过，TRX见[证据目录](evidence/P6-001-R1/)。开发全量Core580+App623为开发结果，本轮未重复全量，不署为监理实跑。

新增只读行为验证器使用当前产品项目引用、仅隔离目录、假输入/前台及假时间，程序集名沿用测试friend访问以调用现有内部缝，不创建App窗口/不启动计时器/不采真实前台。13条关键边界断言全部不符合预期，见[原始JSON](evidence/P6-001-R1/counterexamples/results.json)、[源码](evidence/P6-001-R1/counterexamples/Program.cs)和[项目](evidence/P6-001-R1/counterexamples/Review.csproj)。这是13条受控断言失败，不是篡改官方测试或称其失败。验证器build0警告0错误；退出0仅表示验证器执行完成，判断须读JSON的pass字段。

接受已到位的边界：默认关闭、开启后才调用前台采样；前台仅查询句柄/进程图像并保留规范化基名；不读标题/URL/正文/按键；独立stats.json与配置开关；独立服务、不堆入MainWindow tick；unlock/resume名称分开、没有猜测boot。真实菜单/前台采样/系统锁屏睡眠仍未验证，本轮不占鼠标、不触碰真实用户存档。

### F-01（高）：生命周期只关段不保持挂起，暂停/锁屏后仍可重新采集

位置：UsageStatsService.SampleTick、OnPauseToggledCore/OnSessionSwitchCore/OnPowerModeChangedCore；App.InitializeUsageStats。

服务仅在事件发生时调用SuspendForLifecycle关一次段，没有暂停/锁屏/睡眠状态门控；下次5秒timer仍在IsEnabled下采样并开新段。受控生产服务路径：0/5秒正常采样后pause=true（或lock=7/suspend=3），在10/15秒继续生产同款SampleTick，20秒关闭；累计由5000变15000ms。暂停+锁屏→解锁但尚未恢复暂停也同样错误。睡眠时真实timer通常不运行不构成该逻辑正确的证明，短睡眠/消息排队/交叠仍须按状态处理。初始化也未把已保存_isPaused传给统计服务，需一并核对暂停档启动。

整改目标：保留独立paused/locked/suspended原因并组合判定；任一原因有效均不读前台/不累计，解除一个原因不能解除其他原因。初始全局暂停必须生效；恢复最后原因后重建基线，不补暂停期间。挂起时保留期清理仍运行；生命周期事件可按真实通知分别记录，不把它们作为无条件采集恢复。补生产timer等价路径/交叠/暂停档初始化反例。

### F-02（高）：缺测被当活跃、失败区间归属旧应用、长缺口丢失已验证历史

位置：UsageStatsCollector.SampleTick/CloseActiveSegment/ResetActive、UsageStatsService采样与drain。

1. lastInputMs=null时跳过idle判断而继续记时，NaN/Infinity/未来值也无有效性检查。受控null/NaN/Infinity/未来100000四种输入各被累计10000ms，预期0。现有App夹具默认null并明确写“跳过空闲判定”，恰好把缺测当正常，解释了官方专项虽绿却没抓问题。
2. 前台识别失败时CloseActiveSegment(nowMs)把尚未确认的末区间记给旧应用。0ms、5000ms基线后在10000ms识别失败，累计10000ms而非保留已验证5000ms。不是要求精确知道五秒内每次切换，而是不能将明确缺测区间补给旧应用。
3. 正常0/5000/10000ms三次采样，60000ms发生长缺口，ResetActive直接丢弃整段，最终0ms，连此前已验证10000ms也丢失。长时间同前台采样也一直不提交有效段：十秒有效采样后stats.json的segments仍0，直至换应用/停止才提交；因而一次后续长缺口即可抹掉此前大量正常历史，也无有界持久化检查点。

整改目标：明确“已确认区间”和当前基线，缺测/无效输入丢弃不可靠区间并重建基线；保留并有界持久化此前有效时长，不补计、不丢旧值、不双计。单调时间回退/非有限值一并拒绝，不按坏基线计算；保持跨午夜拆分。新增有效输入驱动正例，null用作缺测负例，不再通过null绕过活动判定。无需做P6-002聚合；本步只保证原始时段可靠。

### F-03（高）：清除遗漏保留的损坏统计副本，历史内容仍留磁盘

位置：UsageStatsService.ClearAll/TryLoadArchive。

损坏stats.json会被保留为stats.json.corrupted，ClearAll仅重写主stats.json。受控样本带旧历史sentinel且JSON尾部损坏，Start→ClearAll后stats.json.corrupted仍含完整旧payload。当前清除承诺是删除全部本地统计，不仅让主列表看起来为空；该副本也没有七天内容清理。

整改目标：清除覆盖本统计存储拥有的历史副本与临时待写内容（确切路径、仅本存储范围），保留开关/宠物存档/用户其他文件；代次/串行边界阻止旧内容回写。正常保留/损坏归档也不能绕过统计数据保留约定。加损坏副本→清除→重启及待写副本反例，不要求扩大清理到任意目录。

### F-04（中）：可选统计写盘异常未隔离，可从UI timer/菜单/退出路径抛出

位置：UsageStatsService.DrainPending/PersistSnapshot/SafeWrite，及其菜单/停止调用者。

SafeWrite同步File.WriteAllText/File.Move，调用链无I/O异常隔离。隔离目录中以stats.json同名目录模拟不可写目的地，Disable直接抛UnauthorizedAccessException；实际访问拒绝/磁盘不可写也经过同一路径，不能让可选统计异常扩散到桌宠UI/退出。当前drain先清pending，写失败后还需有明确重试/失败状态，而非丢弃提交意图或向用户假报清除成功。

整改目标：在统计服务边界处理文件操作失败，保留可恢复状态/有界重试或明确错误结果，保证P4、宠物操作和退出不受牵连；失败的清除不得宣称已清干净。补写失败后恢复写入、清除失败可见结果、退出不抛路径。不要用catch-all吞掉结果，也不必新建泛化存储框架。

### 文档与后续范围

开发报告称File.Replace/单一异步写入队列，当前实际是同步File.Move(overwrite:true)；应按最终实现改正描述。同步Dispatcher串行本身可以是合法方案，不强制新增异步队列，generation字段存在不等于已验证旧异步写回场景。

P6-001保持awaiting-verification；F-01～F-04阻断本任务接收和P6-002开工。开发按报告路径+编号追加独立整改记录，修复→补针对性生产路径正反例→相关回归，交监理复核。当前不安排用户使用真实统计目录或锁屏/睡眠验证；技术复核后准备隔离菜单/前台/系统事件入口，再约时段。不扩大到聚合/预测/展示，不提交推送，保留全部已有修改。


## R2：2026-11-26 四项整改复核——主要反例转绿，限定收尾边界仍未关闭

对象：HEAD 0aa994d加当前未提交工作区，相关实现SHA见[R2摘要](evidence/P6-001-R2/source-hashes.json)。监理独立输出目录重建并实跑Core专项26/26、App专项15/15，0失败0跳过；原R1隔离验证器新目录13/13 pass，TRX与原输出在[R2证据](evidence/P6-001-R2/)。开发全量589+625为开发自测，本轮未重复。未占鼠标、不读真实前台、不触碰真实统计目录/宠物存档，未改产品/正式测试。

### 已接受部分

- F-01：三原因组合状态与解除单一原因不恢复其他原因的累计门控已有效。新增验证使用与collector同域的有效输入，并先断言5000ms真实累计后再挂起，四种pause/lock/sleep/交叠均维持5000ms，不以“始终零累计”假装通过。
- F-02：null/NaN/Infinity/未来输入丢弃、识别失败不延展、长缺口保留已确认时长及有界原始检查点通过；逐tick模型方向接受，不要求重写。
- F-03：正常可删除的.corrupted/.tmp精确清除通过，不扩大清理目录。
- F-04：普通写盘失败不抛、保留内存并下次drain恢复写入通过；同步File.Move描述已更正。不是要求新增异步框架。

### F-01：部分通过，剩挂起期前台读取与默认关闭的暂停启动接线

1. UsageStatsService.SampleTick只检查IsEnabled便读idle/foreground，再由collector.IsSuspended拒绝累计。实际挂起后两次生产同款tick，前台采样器调用次数2→4，四种原因均如此。应在服务读取采样器之前检查组合挂起，仍执行retention/drain。R1已经明确要求不读前台/不累计，不是本轮新增功能。
2. App.InitializeUsageStats仅在settings统计已开启分支同步_isPaused。隔离App按真实“已读暂停=true、统计默认false”状态调用该生产初始化，随后调用生产SetUsageStatsEnabled(true)，collector.IsSuspended仍false，开启后可在全局暂停期间采集。证据通过生产初始化方法/开启方法验证，没有声称跑完整OnStartup或真实存档。初始化应无论开关都同步暂停，或开启时同步当前状态；不通过测试手工SetPaused掩盖生产缺口。

### F-02：部分通过，仅剩单调回退防重复累计

原R1已要求单调回退/坏基线拒绝。本轮有效输入序列now=0→5000→1000→5000，已确认5000ms被回退后的基线重复计算为9000ms。代码CommitVerifiedInterval对end<=start拒绝了当前区间，但外层仍把_lastSampleMs降到1000，后续再加4000。应拒绝回退而不降低已验证时间边界，恢复后不补重复区间；补负例及正常推进正例。真实MonotonicClock目前单调，本项是既定输入/注入边界稳健性收尾，不需要新时间框架。

### F-03/F-04：正常清除/异常隔离接受，但“副本删失败”的清除结果与重试未闭环

DeleteSideArtifacts吞IOException/访问拒绝仅Trace，随后主文件SafeWrite成功会将LastWriteError清null、retry=false，即便副本仍含旧数据。监理隔离stats.json.corrupted写旧sentinel，以FileStream(FileShare.Read)暂时占用，ClearAll不抛且主文件清空，但旧副本存留、LastWriteError=null；释放占用后DrainPending仍未清掉副本。**占用期间删除不了本身是正常OS行为，缺陷是未报告清除不完整、未保留待完成状态/后续重试**，不是要求绕过文件锁。

整改只需将清除作为一个可观察的完成单元：副本删除与主快照任一步失败都保留明确未完成/错误状态，不能被另一步成功掩盖；释放障碍后有界重试完成精确路径清理并清错。通过可查询状态/返回结果让App清除入口能识别失败，不仅日志静默。保持“不外抛”与“不碰开关/宠物档”。加锁副本失败→可见未完成→解锁→重试完整清除→重启不恢复正反例，不重做普通写失败测试。

### 证据与下一步

增强验证器[源码](evidence/P6-001-R2/counterexamples/Program.cs)/[项目](evidence/P6-001-R2/counterexamples/Review.csproj)，结果[JSON](evidence/P6-001-R2/additional-counterexamples.json)：13条检查4通过9不符合预期，分别为上述重复/关联断言，不是9个新问题。有效输入同域；假前台仅计调用次数；副本占用仅本轮临时目录；App只构造隔离实例并调用生产初始化/开关，未创建窗口。原R1验证器仍保留原件；增强不是修改原历史证据。各验证器进程0仅表示执行结束，按JSON判定。

F-01～F-04均保持部分通过、未关闭，仅上述收尾影响P6-001接收；不要求重做已接受部分，不进入P6-002～004。开发追加独立R2整改记录、补限定正反例和相关回归后交监理；技术收尾通过再准备隔离实机，当前不需用户锁屏/睡眠。P6-001仍awaiting-verification，未提交推送，全部已有修改保留。

## R3：2026-11-26 限定边界复核——关闭F-01/F-03/F-04，F-02只余连续回退窗口

审查当前未提交实现，HEAD0aa994d，源码摘要及证据见[R3目录](evidence/P6-001-R3/)。实际独立输出重建Core专项28/28、App专项18/18，0失败0跳过；原R2增强验证器新目录13/13 pass。开发全量591+628为开发结果，本轮未重复。未占鼠标/实机，不读真实前台、不触碰真实数据，未改产品/正式测试、未提交推送。

F-01复核通过关闭：服务IsEnabled&&!IsSuspended先于idle/foreground读取，挂起四组合前台调用次数不增长且已提交5000ms保持；App初始化不依赖统计开关同步暂停，默认关闭+暂停状态初始化后开启仍挂起，实际生产初始化/开关隔离反例转绿。

F-03复核通过关闭：精确副件清理失败保持_clearRetryPending/LastWriteError，主快照成功不掩盖；释放文件占用后DrainPending重试删除并清错。F-04复核通过关闭：普通写失败隔离/重试及清除未完成可查询状态闭环通过，没新增异步框架。以上已关闭项无需重做。

### F-02部分通过，唯一剩余：清空基线并未保留已计时上界

当前回退分支ResetBaseline→return，使原0→5000→1000→5000反例通过。但连续回退区间0→5000→1000→2000→5000，真实当前collector累计8000ms，预期5000ms。清空基线后2000被接纳成新基线，随后5000又把已计过的2000～5000计一次。原R2“拒绝回退而不降低已验证时间边界，恢复后不补重复区间”仍未完全满足。

只读隔离增强验证器14条检查13通过1不符，[原始JSON](evidence/P6-001-R3/rollback-edge.json)，[源码](evidence/P6-001-R3/counterexamples/Program.cs)。这是同一F-02收尾，不新增采样架构，也不把真实MonotonicClock会回退当作本机事实；生产时钟当前单调，此为已登记输入/注入边界防重计。

限定修复：单独保留已接受/已计时单调上界，不让ResetBaseline丢掉防重信息；低于上界的连续tick不建可重计基线，回到/越过上界后按既定缺测重建规则恢复，只累计未计过的可靠区间。具体实现自行选择，勿靠增加固定忽略tick数量。明确开启/关闭/清除时此边界的生命周期，测试当前序列、持续低于上界多tick、回到上界、越过上界正常恢复及正常单调正例。不要重写已接受逐tick模型、不重做其他F项、不跑无关全量。

完成该小改及Core专项/原增强反例即可交监理，之后准备隔离菜单/前台/系统通知实机入口，当前不要求用户锁屏。P6-001保持awaiting-verification，F-02仅此项未关闭；不进入P6-002～004，保留全部未提交修改，不提交推送。

## R4：2026-11-26 F-02复核通过，技术问题全部关闭，剩隔离实机

对象：当前未提交Core collector与测试，HEAD0aa994d；[源码SHA](evidence/P6-001-R4/source-hashes.json)。读取整改R3及_monotonicRollbackFloorMs实现：已接受最高单调边界独立于逐tick基线；低于边界的连续tick拒绝并清基线，边界保留；回到/超过边界后按既定缺测恢复，不使用固定忽略tick数。Enable/Disable/挂起保留、ClearAll重置、Restore不反推跨进程单调域的生命周期已有明确测试。本轮不改产品/正式测试。

实际独立输出重建Core专项**32/32**，0失败0跳过；重建原R3隔离验证器，传全新GUID目录执行，**14/14 pass**，包括连续回退8000ms反例转为5000ms、挂起前台不读取、默认关闭暂停初始化及清除失败重试。TRX/原始JSON见[R4目录](evidence/P6-001-R4/)。App专项18/18沿用R3实跑（本轮App未改），不重复无关全量。验证器新目录避免恢复已有stats.json，不删除或改写旧证据；本轮构建验证器0警告0错误。

**F-02复核通过关闭，F-01～F-04现全部关闭。** 不重做已关闭项；技术修复接收不等于真实菜单/前台/system-event端到端已验收。P6-001保持awaiting-verification，尚不标done，不进入P6-002。

### 下一步仅准备最小隔离实机入口

当前TESTING/DiagnosticProbe尚无P6-001专用可用命令。开发复用既有隔离预览机制准备入口及简短操作清单：用独立run目录与独立实例锁，settings/state/stats及副本都限定该目录；沿用生产UsageStats初始化/真实5秒timer/前台与输入采样/系统通知转发、窗口菜单分发，不用假前台或手工SampleTick充当实机。默认关闭→真实菜单启用→自有标记测试应用活跃与切换→关闭保留/零增长→清除保留开关→同目录重启不恢复旧历史；生命周期以隔离观测记录挂起/恢复基线与unlock/resume，按用户可用时段另测锁屏/睡眠，缺哪些如实列出。

提供可复制命令、实际绝对数据路径、精确进程归属/正常退出清理和观察输出；没有必要再建大型通用驱动或重复全套功能。无鼠标构建/启动接线补证可先做，真实菜单与输入/系统锁屏睡眠由用户确认时段后监理执行，不启动真实用户目录统计，不更改开机启动/系统时间，不占用户屏幕准备轮。只做P6-001验证，不顺便聚合/预测/展示。

本轮未占鼠标/启动实机、未读取真实前台或用户数据、未提交推送，保留全部既有修改。开发只需追加独立整改记录引用R4关闭依据及验证准备说明，无需再次修F-01～F-04。

## R5：2026-11-26 隔离实机入口预检——F-01～F-04保持关闭，新增F-05验证入口缺口

本轮检查usage-stats-live场景、App探针包装、操作清单及既有生产初始化。未启动实机窗口/鼠标操作，未触碰真实用户目录。构建只读验证器0警告0错误，复用场景的非窗口setup步骤实跑两条接线反例，见[R5 JSON](evidence/P6-001-R5/no-window-wiring.json)、[源码](evidence/P6-001-R5/counterexamples/Program.cs)及源码SHA。输出windowCreated=false；这里只证明非窗口接线，未把阅读代码称实机通过。

### F-05（中）：探针未恢复设置/未订阅系统通知，收尾证据写入时机与操作清单不符

位置：DiagnosticProbe.Program.UsageStatsLiveVerification、App.InitializeUsageStatsForIsolatedProbe、docs/history/P6-001-live-entry.md。仅阻断P6-001最终实机证据，不重开已关闭产品问题。

1. 场景调用TestSetupIsolatedSave重置_settings=new、_isPaused=false，并种子覆盖_state；没有LoadSettings/LoadState。监理隔离目录预存usageStatsEnabled=true且ArchiveService.LoadSettings确认true，执行同款setup+生产包装后IsUsageStatsEnabled=false。因此“同目录重启保留开关”不能由此入口证明，状态比较也被种子重置掩盖。首轮新目录可seed，但重启必须从既有独立settings/state生产加载，不能先覆盖。
2. Program入口ProbeSetupIsolated不订阅系统事件；本场景只调用InitializeUsageStatsForIsolatedProbe→InitializeUsageStats（构造/Start/暂停同步），没有SubscribeSystemEvents。实际同款setup后TestSystemEventsSubscribed=false。真实Win+L/唤醒不会经本场景接到统计服务，现清单所称“系统事件转发已就绪”不成立；生产应用的既有订阅本身未被判为有缺陷。
3. enabledAtShutdown/generationAtShutdown/segmentsCountAtShutdown等sidecar在创建shutdownTimer之前、启动时立即写入，恒为初始快照。应在真正收尾时从当前服务读取，并区分配置了生产入口与实际执行了真实菜单；usedRealContextMenuDispatch=true目前也是操作前预写，不能当成已操作证据。重启还File.WriteAllText覆盖同一观察JSONL/sidecar，应按启动session/run编号保留两段证据，数据目录可以共享、观察文件不能覆盖前段。
4. 清单自相矛盾：E关闭后F清除却要求开关仍On；启动后才手动启用却要求出现app-start（当前生产只在启动时已启用才记录，按默认关闭语义不应补造启动事件）；启用状态清除后继续新采样，却要求30秒后退出/重启总段数仍0。正确检验是“不恢复旧历史、允许开启后的新记录”，不是禁止合法新数据。标记应用只是采样对象，应明确自己创建/精确归属，不借用用户未保存窗口。

### 限定开发动作

只补当前最小场景与文档，不建大驱动、不改F-01～F-04逻辑：提供同目录真实settings/state恢复及生产系统订阅的共享接线（必要internal窄包装可用，退出走既有退订）；退出时采集新鲜sidecar并保留每次启动观察记录。操作清单拆清“关闭保留”和“重新开启后清除保留On”两种状态，记录清除代次/时间与旧记录消失，不无条件要求空列表；app-start只验证正确触发语义，不在运行中开关补事件。

补无窗口接线检查：隔离预存On→初始化保持On；当前系统事件已订阅→正常清理后退订；可写用观测独立session文件不覆盖。Sidecar时机可通过受控流程检查，别为了预检提前占屏幕/鼠标。命令直接指向实际构建exe或包含完整复制依赖步骤（现示例.temp/probe路径未给复制命令），保证可复制复现。

完成后更新独立整改记录，交监理预检，随后约真实菜单/前台和另行锁屏睡眠时段。P6-001保持awaiting-verification，F-05待整改；F-01～F-04关闭结论及统计默认关闭保持。不进入P6-002～004、不提交推送，保留全部已有修改。本轮监理未改产品/探针/正式测试。

## R6：2026-11-26 实机入口整改预检通过，F-05关闭，待实际操作验收

读取R5整改、LoadArchivesForIsolatedProbe/TestEnsureProbeRuntimeObjects、live/wiring场景、session编号及操作清单。监理独立输出构建DiagnosticProbe Debug -warnaserror 0警告0错误；首个--no-restore命令因新artifacts目录未有project.assets.json报NETSDK1004，移除该参数完成还原后构建成功，这是监理命令前置不满足，非产品失败。未重复全量。

本轮无窗口/无鼠标执行新构建usage-stats-wiring，同一全新隔离baseDir连续两次、两个进程均exit0；日志/obs/sidecar/cleanup/metadata与SHA见[R6证据](evidence/P6-001-R6/)。两轮toggleOnAfterLoad/enabledAfterInit/subscribedAfterStart均true，cleanup均subscribedAfterCleanup=false、serviceNullAfterCleanup=true、shutdownCalled=true；session1/2三件套同时存在，没有覆盖。第二轮appStartEventCount=2是保留前次启动记录后本次再加一次，正确口径是每次启动增1，不是每轮历史总数永远1。本轮不读真实前台、不启动真实宠物窗口、不触碰用户统计目录。

### F-05复核通过，关闭

重启分支从隔离ArchiveService生产加载state/settings而不种子覆盖，运行对象补齐与状态加载分离；生产共享StartForIsolatedProbe完成系统通知订阅，正常退出生产路径退订；live侧sidecar移到实际shutdownTimer读取当前值，不预写已执行真实菜单；观测/sidecar/cleanup编号留存。清单已拆关闭保留与重新启用清除，说明新记录合法、不补造运行中app-start、标记应用自建归属；命令含构建/依赖复制方式。接受该最小入口，无需继续搭驱动或改已关闭产品。

这是入口预检/接线实跑，不是live场景真实菜单/前台/锁屏睡眠已通过。F-01～F-05全部关闭，P6-001仍awaiting-verification，用户体验/系统真实路径未完成前不标done，不进入P6-002。

下一步监理先与用户约约5分钟的鼠标时段，运行usage-stats-live：真实菜单启停/清除、两个自建标记应用前台使用及切换、同目录恢复并保留两session原件；核对实际开关、累计/冻结、清除旧数据与后续新数据、正常退出。真实锁屏/睡眠另约明确允许的时段，避免打断工作，不把“允许鼠标”自动解释为允许锁屏睡眠。开发无需继续整改，可仅追加R6接收引用。不提交推送，全部已有修改保留；本轮监理未改产品/探针或正式测试。

## R7：2026-11-26 菜单/前台实机尝试中止——默认关闭观测通过，UI控制工具受限

用户本轮“现在可以”授权菜单/前台约5分钟鼠标时段，明确不含锁屏/睡眠。监理使用R6已重建探针启动usage-stats-live，独立数据目录，并自建两个带本轮唯一标记的WinForms测试窗口（分别由pwsh/powershell承载，用于区分两个应用基名），未借用用户未保存窗口。

首轮run-20261126-153200以Hidden启动后控制工具未发现新窗口，精确停止本轮进程并保留目录；随后以允许的可见交互方式新建run-20261126-153241，shell能看到自建fixture句柄/标题，但sky.list_windows/list_apps及cua.getState仍未列出本轮fixture或宠物窗口。只读核对运行与工具均同一Windows主机/用户，shell与fixture为Default desktop；未凭shell句柄构造假的工具Window对象。尝试按sky文档刷新系统powershell应用时，工具明确拒绝`product policy blocks this app: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`。未绕过此限制，未改用自制输入注入替代已被工具拒绝的操作，也未触碰其他用户窗口。

### 实际证据及未完成范围

第二轮[原始观测](evidence/P6-001-R7/observations-session-1.jsonl)114条，覆盖至218.2秒，enabled始终false、segments/events均0、lastWriteError为空，独立场景目录没有stats.json。这证明当前实际运行默认关闭且未产生统计；不是菜单启停/真实前台切换已通过。运行元数据、真实结果及源码SHA见[R7目录](evidence/P6-001-R7/)。没有保存用户窗口标题清单或截图作为证据。

由于无法绑定本轮窗口，真实菜单启用/关闭/清除、前台活跃切换、同目录重启恢复均未执行，未模拟命令替代真实菜单。锁屏/睡眠本轮也未授权且未执行。没有生成正常退出sidecar或result，不能称正常quit/exit0。

自建fixture已按精确PID请求关闭；确认探针路径匹配后监理中止本轮精确探针PID，最终probe79288/fixture92828/82344均无残留。属于控制工具受限后的中止清理，不是正常生产退出验收；原件保留，不把缺步骤写PASS。鼠标已归还，用户不需继续等待。

### 当前结论与接续

F-01～F-05既有技术/入口预检关闭保持，本轮没有证据支持新增产品缺陷，不让开发为工具可见性问题重写探针或搭大型驱动。P6-001仍awaiting-verification，不进入P6-002。剩真实菜单/前台/清除重启及生命周期；若当前工具限制持续，改为用户实际点击隔离菜单、监理负责观测日志与判定，或待窗口控制可绑定后再执行。不能仅靠用户再次说“可以”宣称工具策略已解除；不在未授权时继续后台测试。

未提交推送，未改产品/探针/正式测试，全部既有修改保留；本轮没有启用真实用户目录统计。

## R8：2026-11-26 用户协作菜单/前台验收——统计主要链通过，重启后Exit异常待修

用户授权由其实际操作菜单，监理启动独立usage-stats-live并读取日志。数据base为run-20261126-160614-user，session1 PID87440、session2 PID93232。实际代码/编译产物沿R6已核对SHA；[本轮证据](evidence/P6-001-R8/)保留两轮观测、清除前后stats、settings、sidecar/cleanup、第二轮实际退出码与异常日志。没有用注入AppId/手动SampleTick替代真实采样，菜单点击为用户操作自述+时间对应状态变化，不称监理自动化操作；探针sidecar的realContextMenuDispatchConfirmed=false原值保持不改写。

### 接受的实际覆盖（后续不重复）

- 首轮默认Off到t42秒零记录；用户开启后真实5秒timer记录前台使用，notepad与msedge均有多个约5秒区间（保存清除前快照时分别7段约35秒、13段约65秒）；用户确认“已切换”。其他前台应用基名也是测试期间合法采样，不读窗口标题/正文。
- 用户关闭/重新开启/清除/再关闭，t230.2 Off→232.2 On→236.2 generation1且On、segments0；最后t258.2至285秒Off、2段保持不变、无错误。首次Off只有约2秒，不单独声称其长时间冻结；最后Off约26.8秒已跨多个采样周期，确证停止增长且保留数据。
- 清除时开关保持On、旧记录消失。之后两段新记录startLocal晚于清除时点，是合法新数据，不判“复生”。首轮正常收尾sidecar/cleanup齐全（订阅false、服务null、shutdownCalled=true）；进程已自然消失，但当时未保留进程句柄，故首轮OS exit code未直接采集，不伪写exit0。
- 监理确认第一进程结束后同目录重启，session2原生加载Off，保留清除后的2条新记录，清除前快照25条旧记录无一复生；初始/连续观测一致。session文件并存，没有覆盖。app-start首轮启动Off不记，运行中开启不补造，符合预期。

### F-06（中）：隔离重启未对齐WPF退出模式，真实菜单Exit异常退出

用户按要求在session2右键Exit并回复“已退出”。监理持有该进程句柄实测退出码**-532462766**，异常日志DispatcherUnhandled/AppDomainUnhandled指向App.ShutdownMainWindow设置ShutdownMode（当前App.xaml.cs:1609），message“正在关闭或已经关闭应用程序时无法设置ShutdownMode”。无session2正常cleanup/sidecar，不将已消失进程当正常退出。精确PID93232已不存活，用户不需继续等待，无本轮残留实机。

当前代码链与异常一致的定位：生产App.xaml配置OnExplicitShutdown；手动new App()的探针没有加载该XAML，WPF默认OnLastWindowClose。首轮TestSetupIsolatedSave会经既有测试初始化对齐该模式；重启分支LoadArchivesForIsolatedProbe+TestEnsureProbeRuntimeObjects没有同样对齐。ShutdownMainWindow先Close所有窗口，之后才设置ShutdownMode，默认模式下可能已自动进入shutdown，于是setter抛异常。官方默认/自动关闭语义见[Microsoft ShutdownMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.shutdownmode?view=windowsdesktop-10.0)。以上为源代码/官方语义与本次实机日志共同支持的定位，应以针对性复现验证；不据此声称正式生产App.xaml启动也有同样缺陷、不重开已接收旧阶段。

限定开发只修隔离探针初始化/收尾，使首轮与重启都在创建/关闭窗口前按生产OnExplicitShutdown约定建立生命周期；优先显式公共属性初始化，不为新探针流程新增WPF私有字段反射，不用catch吞异常或Environment.Exit(0)掩盖真实退出。若确需移动共享退出中的时机，限定改动并覆盖其他退出回归，不能顺手重构。

补重启分支→生产菜单分发Exit的子进程级针对性回归，要求真实退出码0、无异常日志、服务停止/通知解绑，不用纯无窗口wiring替代有窗口生命周期。已通过的启停/切换/清除/恢复无需用户再重复；仅复验退出及后续另约的真实锁屏/睡眠。如测试需占可见窗口时段先告知。开发追加独立整改记录，不改本报告及原始失败证据。

F-01～F-05关闭不变，F-06仅阻断当前隔离退出/最终实机接收；P6-001保持awaiting-verification、不进入P6-002。锁屏/睡眠本轮未授权且未执行，仍未验证。监理本轮未改产品/探针/正式测试、未提交推送；已有修改保留，真实用户统计目录未启用。

## R9：2026-11-26 以实际仓库核对退出整改，F-06关闭；只剩真实锁屏/睡眠

本轮用户粘贴交付仍按F-05描述；实际STATUS/整改记录及源码已有R8 F-06修复与usage-stats-exit回归。监理以磁盘/证据为准，没有要求重做F-05，也没有重复R8已接受的启停/前台/清除/恢复。STATUS先前在监理签发前写“F-01～F-06已关闭”不构成当时关闭依据；F-06有效关闭依据从本R9追加起成立，历史R8保留。

### 实际核对与证据归属

- 监理实际独立输出重建DiagnosticProbe Debug -warnaserror，0警告0错误。核对live与exit场景在首轮/重启分支之前明确公开属性ShutdownMode=OnExplicitShutdown，再建窗口；没有新增私有WPF字段反射，没有以catch吞原异常。MainWindow只开放MenuIdExit常量访问，不改产品退出顺序/服务逻辑。
- 退出回归为有窗口子进程，CreatePetWindow/Show→生产统计初始化→TestOpenContextMenuLifecycle(MenuIdExit)→生产异步菜单分发→ShutdownMainWindow→Application.Run返回；成功没有Environment.Exit(0)，仅失败守卫/断言使用非零退出。不是声称本轮新做了原生TrackPopupMenu点击，原生点击由R8用户完成。
- 读取开发提供的同目录两轮原始JSONL及result：session1 isRestart=false、session2 true，均OnExplicitShutdown，post-exit applicationRunReturned=true、unsubscribedAfterExit=true、两个服务均null、无异常日志，result均0。两轮OS exit0由开发报告，监理本轮未再次运行这些可见窗口子进程，不冒称监理独立实跑；采信源码路径与既有原始证据进行复核，避免未经时段许可占屏幕。
- 开发去掉赋值的对照日志与JSONL也存在：session2 pre-exit为OnLastWindowClose，原ShutdownMode异常复现。对照目录result.json仍是首轮成功0，不能拿它当第二轮退出状态；第二轮失败由异常日志与开发父进程报告支持。未为重做对照改产品或删除旧文件。

上述原件只读副本及SHA见[R9证据](evidence/P6-001-R9/)，developer-*明确标明开发证据。F-06整改复核通过，**F-06关闭；F-01～F-06现全部关闭。** 不重开F-01～F-05。

### 剩余验收与下一步

P6-001保持awaiting-verification，唯一剩余人工验收是真实锁屏/解锁、睡眠/唤醒下的统计冻结/恢复基线与unlock/resume事件归属。当前源代码/专项及系统事件订阅证明不能替代真实系统操作。R8已接受菜单启停/真实前台/清除/同目录恢复，本次退出修复及回归接受，不需用户再重复整套。

开发无需继续写产品/驱动，可只在独立整改记录追加R9接收引用。下一步与用户另约明确不影响工作的时段，在现有隔离实例中按真实系统事件观察；睡眠/解锁需用户配合恢复，不把鼠标许可当作锁屏睡眠许可。用户也可明确决定豁免真实生命周期体验，但当前尚无此豁免。不进入P6-002、不提交推送，保留全部修改。本轮未占鼠标/启动窗口、不读取真实前台或用户统计目录。

## R10：2026-11-27 补记用户真实锁屏/解锁验收（实际执行2026-11-26）

用户允许不影响工作时段测试后，监理启动独立run-20261126-214322-lifecycle（PID65588），仅该隔离目录预置统计On，不改变正式默认关闭/用户设置。用户在记事本活动后手动Win+L并回复“已解锁”；操作由用户执行，监理只读取原始timer观测/档案，没有模拟SessionSwitch或注入采样。

实证：t124.2秒起isSuspended=true、segmentsCount=12，后续约74秒保持12；t198.3解除挂起并出现真实unlock，恢复后的首段重新从解锁后的本地时间开始，约5秒/段，无锁屏区间补计。该轮只有app-start与unlock，没有把unlock记成resume或boot。自然285秒收尾，父进程捕获exit0，cleanup退订false/服务null/shutdownCalled=true，PID已结束。见[R10原件](evidence/P6-001-R10/lock-unlock/)、[判定](evidence/P6-001-R10/lock-verdict.json)及父进程process-exit.json。

锁屏/解锁这项接收。随后用户尚未执行睡眠的run-20261126-214737-sleep自然exit0，但只有app-start，无resume；其正常退出不能当作睡眠通过。历史均保留不混用。F-01～F-06保持关闭，此时仅余真实睡眠/唤醒；不改产品、不提交推送。

## R11：2026-11-27 真实睡眠/唤醒通过，P6-001最终接收

用户明确要求“你来做”，监理先告知实际睡眠会断开连接、需用户按键/电源唤醒。确认上一轮进程均已结束后，启动独立run-20261127-001006-supervisor-sleep（PID84136），隔离settings On、真实生产采样/系统通知；新统计没有写入正式用户目录。相关源码与R9复核一致，SHA/命令/观测/结果在[R11证据](evidence/P6-001-R11/)。没有修改产品/探针/正式测试。

### 实际系统操作与证据

powercfg /a只读确认S3可用。开始菜单快捷键未得到可绑定的菜单，因此本轮不是“通过Windows菜单点击睡眠”；监理使用公开Windows Forms Application.SetSuspendState(PowerState.Suspend,false,false)，非强制、不禁用唤醒事件，不改持久电源配置或设置唤醒任务。接口定义见[Microsoft API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setsuspendstate?view=windowsdesktop-10.0)。它是实际系统电源请求，不是调用产品的OnPowerModeChanged模拟通知。

- 00:11:19.998请求，Windows System日志Kernel-Power事件42于00:11:20.507确认“系统正在进入睡眠状态，睡眠原因Application API”，接口返回true，墙钟等待83.392秒（含进入/恢复过程，不把此数精确等同睡眠驻留时长）。原始request/result及OS事件已保存。
- 真实采样t74.1挂起时已有11段；t156.5接收到resume时仍挂起、仍11段（唤醒后尚未解锁）；00:12:58.099另记unlock，t173才解除挂起、仍11段。证明睡眠原因解除不抢先解除锁屏原因，unlock/resume分开，各1条，不伪造boot。
- 统计原始区间没有与OS睡眠→resume重叠，也没有与resume→unlock锁定区间重叠。恢复后按新基线约5秒提交，最大段时长5008.867ms，无一次性补计约83秒；采样错误/覆盖缺失未增加。见[lifecycle-verdict](evidence/P6-001-R11/lifecycle-verdict.json)。不声称CPU睡眠期间每两秒还能采样，证据是睡眠前后冻结和原始区间缺口。
- 实例285.2秒正常自动退出，父进程实测exit0，cleanup为订阅false/服务null/shutdownCalled=true，logs无异常文件、精确PID84136无残留。用户未由监理代输登录凭据，恢复/登录由用户完成。

### 最终结论及保留范围

**真实锁屏/解锁与睡眠/唤醒均接收；P6-001完成，TASKS标done。** F-01～F-06既有关闭保持。本轮组合R4专项/边界、R6入口、R8真实菜单/前台/清除恢复、R9退出修复以及R10/R11真实生命周期，形成当前任务完整覆盖；不重测已通过步骤，不把较早失败/未操作轮改写为通过。

统计仍为默认关闭、由用户主动启用；本轮On是独立测试目录夹具配置，不修改正式用户统计开关。仅完成近似前台本地统计/关闭/清除，未实现P6-002聚合、P6-003预测或P6-004展示，也不宣称习惯预测可靠。多屏/混合DPI、其他机器/Windows电源状态没有泛化验证。

下一步开发仅可在独立整改记录追加R11接收引用，P6-002依赖解除但仍todo，是否启动由用户安排。无需继续改P6-001或制作新测试驱动；未提交推送，保留全部未提交修改，未动真实用户存档。
