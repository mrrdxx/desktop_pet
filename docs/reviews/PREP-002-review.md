# PREP-002 有限App职责整理审查

## S1 共享单调时钟 R1（2026-11-07）：接受本小步

对象为当前未提交的MonotonicClock及App/MainWindow调用替换。独立构建并执行高分辨率时钟、释放接线、暂停语义及暂停动作切换专项 **52/52通过**，0失败/跳过、未见编译警告/错误；全量629/629为开发自测，本轮未重跑。文件摘要与TRX见[证据](evidence/PREP-002-S1-R1/)。本轮未执行系统鼠标实机烟测，不将专项当完整重构已验收。

- App原3处MainWindow.GetElapsedMsPublic已全部改为MonotonicClock.Instance.GetElapsedMs，没有经MainWindow静态取时钟的生产依赖。
- MainWindow私有包装和兼容公开访问器转调同一单例，TestGetElapsedMs调用方保留，AnimationPlayer仍消费原包装委托。单位double毫秒、首次调用懒起点、暂停不停钟/由调用方补偿的约定保持。
- 时钟未引入系统墙钟或低精度整毫秒，未改物理参数/存档格式/输入行为；保留旧公开入口供既有测试兼容，不要求本轮删除或重写测试。

S1代码接受，无新增阻断项。PREP-002整体仍in-progress，后续状态访问/更新职责整理及任务末全量回归和必要实机烟测尚未完成。TASKS此前仍todo、架构仍将共享时钟写成拟采用，本轮由监理同步为实际小步边界；不把它们当已实现全部重构。

后续只做一个内聚小步：收拢单宠状态访问/保存通知，明确依赖归属与App.Current回退，不一次迁移所有字段/拆整个心跳，不改变真实行为/时间注入/存档；不将单例helper包装等同于完成按宠物实例隔离。PetId/集合迁移留P3-001-S1，不进入PREP-003或双宠。

非阻断文档提醒：GetElapsedMsPublic注释仍称App使用它，应随下一小步改成兼容入口；Stopwatch分辨率依运行环境，不把“亚微秒”写成普遍承诺。现有实现依当前UI调用方式沿用懒原点，不据此宣称任意并发首次调用的线程安全保证。

未改产品代码/开发整改记录，未提交推送。PREP-001关闭结论保留，不重开。

## S2 复核 R2（2026-11-07）：部分接受，F-01待修

独立构建并执行情绪运行时/暂停语义/稀有事件持久化相关专项47/47通过，无失败/跳过、未见编译警告/错误；另构建真实MainWindow tick的上下文正反例。全量629/629为开发自测，本轮未重跑。摘要/TRX/探针见[PREP-002-S2-R2](evidence/PREP-002-S2-R2/)。本轮无系统鼠标操作，不宣称PREP-002整体实机验收通过。

接受部分：PetStateContext只引用状态对象和Action回调、不耦合Archive/SaveCoordinator；App创建窗口后、恢复暂停和Show之前显式赋值。常规完整上下文独立探针：实际OnUpdateTick推进tracker累计1分钟，创建scheduler计划并调用dirty回调1次，证明非空新路径有实际作用，而不只依赖原空上下文测试。S1共享时钟结论保持。

### F-01 中：多个依赖共用首对象判空，违反逐对象三级优先

OnUpdateTick中仅在runtimeTracker为null时才读取StateContext.RareEventScheduler；若只注入TestRuntimeTracker，rareScheduler仍null，上下文中的scheduler永远不被取用，整段更新被跳过。同类模式见情绪组三对象（只在emotionAcc为空时补corner/grievance）和InterruptGrievanceExpression（只在grievanceExpr为空时补emotionAcc）。

独立反例：使用同一完整StateContext，tracker.StartActivePeriod(0)，注入60000ms，正常时planCreated=true/accumulated=1/dirty=1；仅把同一个tracker再赋给TestRuntimeTracker，其余上下文不变，结果planCreated=false/accumulated=0/dirty=0。优先覆盖一个对象不应使其他依赖消失。当前生产完整上下文常规路径可用，本问题不冒充已证实生产双宠故障。

整改：每个状态独立按自身Test* → StateContext对应成员 → App对应成员解析，不用首个依赖判空控制其他依赖回退；可提取最小解析helper避免重复，不扩展成容器框架。保持保存通知明确选择规则，避免同时调用上下文和App回调。

验收：新增非空上下文相关回归，实际tick验证上下文对象被使用、保存回调命中且只命中目标；只覆盖单个Test*时，其他依赖仍来自上下文；覆盖Runtime/Scheduler和Emotion/Corner/Grievance组合及打断路径。空上下文旧测试仍通过，不降低既有优先级、不混改时间/存档/物理参数。

### 报告准确性（随整改更正，不另设阻断）

MainWindow中Application.Current is App仍有多处，NotifyStateDirty自身也保留正常App.Current回退；“搜索结果为零”与实际不符。不要求为了这个口头目标删除兼容回退。正确描述是“生产显式上下文优先，保留App.Current回退”，不可宣称已完成按PetId实例隔离。

结论：S2部分接受，F-01阻断本小步接受与后续更新职责抽取；PREP-002保持in-progress。开发方在docs/remediation/PREP-002-response.md按报告/编号回应，不改监理报告。仅修独立解析和非空上下文正反例，不重做S1/PREP-001，不进入PREP-003/P3-001，不提交推送。

本轮仅写监理文档/证据与STATUS/TASKS，不改产品代码。独立探针第一次因可空数组声明编译失败，仅修artifacts探针后成功运行，未修改产品。

## S2 复核 R3（2026-11-08）：F-01关闭，S2接受

独立构建并执行上下文7项及情绪/暂停/稀有持久化相关专项54/54通过，0失败/跳过、未见编译警告/错误。另重跑原S2上下文正反例，证据与摘要见[PREP-002-S2-R3](evidence/PREP-002-S2-R3/)。全量636/636为开发自测，本轮未独立重跑；未执行真实鼠标烟测。

Resolve<T>逐个解析自身Test值、对应Context值、App回退，所有状态取用点已不依赖首个对象判空；NotifyStateDirty仍只选择上下文回调或App回退，不双发。新增专项包含非空上下文、单项覆盖、打断、暂停恢复及旧空上下文路径。

原反例实际OnUpdateTick复验：不覆盖/仅覆盖TestRuntimeTracker，两种情况均planCreated=true、accumulated=1、dirty=1，证明覆盖单个对象不再丢失另一个依赖。F-01通过并关闭，S2本范围接受，S1/PREP-001旧结论保持；不再补同一问题或删除必要兼容回退。

PREP-002整体仍in-progress。下一小步限定一个内聚范围：稀有事件计划评估及有效运行时间推进的更新职责抽取，先在架构明确依赖、返回结果、保存通知和位置所有权，再实施该同一小步；探头窗口位移/输入/物理统一写入仍留原有App入口，不重写整个心跳、不创建通用行为框架或双宠集合。已有暂停/拖动/委屈/物理期间延迟评估、每日共享额度和消费/保存语义必须保持，相关生产入口验证不得只测新helper。完成后评估剩余职责范围，不以行数减少冒充整个重构结束。

任务末既有全量回归/必要实机烟测尚待完成；不提前PREP-003/P3-001，不提交推送。本轮仅报告/证据/STATUS/TASKS，未改产品或开发整改记录。

## S3 复核 R4（2026-11-08）：抽取接受，进入最终烟测

对象为当前未提交RareEventUpdateService与MainWindow稀有事件调用替换。独立构建并执行稀有事件持久化/探头表现/上下文/释放接线专项 **81/81通过**，0失败/跳过，未见编译警告/错误。全量636/636为开发本轮自测，监理未重复；TRX及摘要见[PREP-002-S3-R4](evidence/PREP-002-S3-R4/)。本轮无真实系统鼠标烟测，不把静态/自动结果冒充实机通过。

- 服务按原顺序推进有效运行时间、维护日期计划，并在委屈/物理时不调用有消费副作用的Evaluate；拖动等拒绝仍交scheduler原规则处理。暂停仍由MainWindow外层门控，未新增推进路径。
- ShouldFire/消费返回后由MainWindow保留探头启动二级守卫、超时恢复和位置写权限，不移动输入/物理/探头实际位移；服务不持窗口或调用NotifyStateDirty。
- RollFailed消费虽然不使服务自身StateChanged为true，MainWindow仍OR比较PlanDate/PlanConsumed和旧缓存，再统一更新缓存/通知保存；对应生产保存链专项通过，未漏掉失败抽签消费，不引入每tick标脏。
- 每次EnsurePlanForDate原有new Random调用行为保留，本轮不借重构调整概率/阈值/每日额度。尚不是按PetId隔离或完整共享行为协调器，不作相关完成声明。

S3有限抽取接受，无新增阻断项；F-01沿用R3关闭，S1/S2/PREP-001不重做。PREP-002代码整理本轮到此收口，不继续为减少MainWindow行数扩展重构；整体awaiting-verification，剩余任务末必要实机烟测和证据汇总。

开发方可沿用本轮同版本636/636全量结果，不因监理通过再原样重跑；若烟测发现问题并改代码，再运行相关回归及必要检查。烟测需当前构建、当前环境：可见启动/悬停正常、头部及身体拖动/轻放/一次抛掷、暂停恢复、菜单找回/正常退出。用隔离实例/存档执行并说明正常生产启动与观察宿主区别；不强杀用户当前实例，不为探头等待一天或重做整个P0/P2体验。

完成后提供本轮构建标识、实际操作/结果和未验证环境，再做最终接收；此时不提前标PREP-002 done或进入PREP-003/P3-001，不提交推送。本轮监理仅文档/证据，未改产品代码。

## 最终复核 R5（2026-11-08）：PREP-002接受

本轮只核对提交的烟测材料和源码对应关系，不重跑S1～S3专项或原样全量，不修改产品代码。现场执行方为开发AI安排的隔离交互宿主，鼠标体验“全部正常”为其转述的用户反馈，不冒称本监理亲自操作。原日志/隔离存档/版本对应见[稳定证据](evidence/PREP-002-final-R5/)。

- 原始run-bb4af09a日志覆盖2026-11-08 02:27～02:34 Asia/Shanghai，实际HWND DPI144（150%），单屏2560×1600、工作区2560×1528，模式interactive。探头curious启动→2.89秒恢复原位置/idle，consumed=true，checksFailures=0。
- 执行方报告用户完成启动/悬停、身体/头部拖动、轻放/抛掷、暂停恢复、找回及正常退出，均正常；交互模式不属于auto全自动PASS，不将探针首段检查替代后续用户体验。
- 日志REAL_ARCHIVE_BEFORE/AFTER的state.json与bak摘要一致，证明被快照文件未改变，不扩张成“进程从未访问任何正式目录”。隔离档schemaVersion3，最后位置1426/898.6667，暂停false，累计156.6207，计划已消费。
- 当前MainWindow、MonotonicClock、PetStateContext、RareEventUpdateService、DiagnosticLogger与隔离副本逐字节一致；夹具新增复制Diagnostics属正常适配，未修改产品逻辑。App启动的目录/锁名/观察入口差异为既定隔离机制，不代表未经修改的正式exe冷启动实测。
- PROCESS_EXIT_CODE=0为执行方报告，当前保存live.log没有该进程码字段，监理没有独立重放；原日志已记录窗口保持交互和最终存档摘要，结合正常退出用户反馈采纳，准确保留证据归属，不为此制造重复整轮测试。

合并S1 R1独立52/52、S2 R3独立54/54和F-01关闭、S3 R4独立81/81，以及同版本开发全量636/636（本轮不重跑）与必要烟测，**PREP-002当前有限整理范围验收通过，标done**。MainWindow仍有多项职责，不把本轮小步接受冒充完整行为架构/双宠状态隔离已实现；本轮重构到此停止。

限制保留：本轮单屏单DPI144只补当前构建烟测，不证明混合DPI/热切换；Shell通知区域图标真实点击未覆盖，菜单/方法入口不可冒充Shell；半身可见为人工判断，ART历史限制不代签。P2/其他历史未测不因本轮自动消除。

开发方可在原整改记录追加R5最终依据，不重复申请关闭或扩大重构；下一任务PREP-003 Release性能基线须按用户安排开工，当前没有性能改善的测量证据，不开始双宠。不提交/推送。本轮仅保存证据并更新报告/STATUS/TASKS。
