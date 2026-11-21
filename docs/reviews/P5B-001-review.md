# P5B-001 审查报告

## R1：2026-11-21 首轮独立审查

- 对象：HEAD `f59e7e2` 加当前未提交 P5B-001 代码、驱动、M-17 自测材料。审查文件 SHA-256 见 [版本清单](evidence/P5B-001-R1/source-sha256.json)。此前误建开发聊天的改动已撤销，本报告针对用户本次新交付。
- 方法：读取 PRODUCT R-08、ARCHITECTURE §4/§5/§9、TASKS、TESTING M-17、独立整改记录及实际代码；核对 run-20261121-141812-notepad 原始清单和事件流；复制当前 src/tests/assets 到 `.temp/supervisor-p5b001-r1`，仅在副本添加监理反例，使用单独构建目录。
- 结果：Core 原攀爬 15/15、App 原攀爬 10/10 通过；监理新增 5 个反例全部失败，0 跳过。原 3 个上缘回归通过，接受该局部修复，不宣告整个任务完成。开发报告全量 563+556 为开发自测，本轮未重跑全量。
- 限制：本轮未操作真实鼠标、未启动 M-17 驱动、未改产品源码。开发实机清单确为 15 项 PASS、探针 exitCode=0，但其覆盖有下述边界。跨屏、混合 DPI 继续未测。
- **结论：暂不接收 P5B-001，保持 awaiting-verification。F-01～F-04 均阻断当前任务关闭；不重开 P5A，不进入后续功能。**

### F-01（高，未关闭）：攀爬位置所有权及找回中断未闭环

位置：`MainWindow.xaml.cs` 的 `IsClimbWriteAllowed`、`ApplyPerchFoot`、`RecoverPet`；`PetWindowClimb.TryStart`。

- `IsClimbWriteAllowed` 首个 `!HasCorePositionConflict` 分支不检查栖息、本地社交或共享社交会话；仅后面的攀爬持有分支检查社交，且始终未检查栖息。已有栖息绑定时起攀返回 Started，绑定与攀爬并存；本地社交为真但其他核心旗为假时也允许起攀，与注释约定不符。
- `RecoverPet` 只解除栖息，不终止攀爬；找回完成后路线仍活跃，后续 Tick 可再把宠物拉回原目标。
- `ApplyPerchFoot` 直接提交处没有攀爬所有权守卫，当前攀爬复用该入口，不能简单给它加一条会导致攀爬自我阻塞的条件。

独立证据：`Reviewer_Climb_AlreadyPerched_MustRejectWithoutChangingBinding`、`Reviewer_Climb_LocalSocialActive_MustRejectStart`、`Reviewer_Climb_Recovery_MustCancelRoute` 均失败，见 [最终 App TRX](evidence/P5B-001-R1/review-final_net10.0_20261121142834.trx)。

整改目标：明确当前持有者的提交入口及共享换算边界；起攀拒绝已栖息、本地/共享社交占用；攀爬持有时非攀爬位移不得提交；找回同步取消路线，之后移动旧目标和推进 Tick 均不回拉。保留既有 chase paired-commit 与栖息跟随语义，不泛化框架。补首次起攀、跨窗口共享会话、提交和找回后零写入的正反例。

### F-02（高，未关闭）：直线到达判定、积压与时间域造成越步

位置：`PetWindowClimb.Tick` 的 dt、remaining、交接分支；探针 `TryStart(...stopwatch.ElapsedMilliseconds)` 与 MainWindow 生产 Tick 的 `MonotonicClock`。

- 距离到达判断用终点欧氏距离，却按侧边折线执行。独立布景脚点(660,620)、目标[700,420,1000,670]、终点(850,420)：欧氏距离约276px，路线390px。667ms预算300.15px时直接交接，跳过剩余折线；反例 `Reviewer_Climb_MustNotShortcutPolylineWhenDirectDistanceFitsBudget` 失败。
- Tick 不限制长间隔，10s停顿可一次推进4500px并直接落位；`Reviewer_Climb_LongStall_MustDiscardBacklog` 失败。须遵守 ARCHITECTURE §5 有限补步、丢弃积压。
- 探针起攀用自己新建的 Stopwatch，而生产 Tick 用另一个起点的单调钟。开发本轮原始事件 t=1289 start foot=[160,220]，t=1300 首步 foot=[251.95282,220]，仅11ms却位移91.95px，按默认速度预算应约4.95px，已出现实证。见 [首步计算](evidence/P5B-001-R1/first-step-analysis.json)。
- 驱动 `noJump` 比的是 completed 事件与800ms后心跳，只证明落位后稳定，不能排除进入交接前的跳跃。

整改目标：统一起攀与更新的单调时间域；基于真实剩余折线长度推进/判定到达，交接前脚点到达落点后再绑定；有限补步并丢弃积压，回退不产生后续重复消费。正反例覆盖斜线短于折线、正常小步、停顿、时钟一致性，并补末次步进→交接前后脚点证据，不把落位后稳定称作交接无跳动。

### F-03（高，未关闭）：M-17 驱动清理范围超出本轮所有权

位置：`tools/Invoke-P5B001ClimbPreview.ps1` 的无窗口 Notepad 预检、`Invoke-CleanupMarkerWindows`、标记文件清理及 finally。

- 预检遍历所有 Notepad 并强杀“当前无可见窗口”的进程，未要求本轮创建或归属证明。
- 清理匹配任意历史 `P5B001NP-*` 标题，可能发送关闭、不保存按键或强杀；还删除其他 run 的标记文件。标题前缀、通用空白窗口标题不足以证明当前驱动拥有进程，尤其 Win11 标签页可共用进程。
- 当前成功轮不能证明其他轮或用户窗口不受影响；本轮监理因此未运行该驱动。

整改目标：仅清理由当前驱动创建并能确认归属的文件、窗口及子进程；其他轮、无窗口外部进程、同进程用户标签页一律不写不关不杀。删除跨轮清理与通用无窗口进程清理；必要时用独立测试宿主避免记事本进程复用。补其他轮/外部诱饵存活、标题/位置/文件不变的对照证据。无需操作真实用户文件。

### F-04（中，未关闭）：完整退出/关闭验收缺证据，异常提前退出可显示不完整 ALL PASS

位置：探针 `WriteSummaryAndExit`、App测试 `Climb_ExitRestoresPreClimbPosition`、驱动最终汇总与 M-17 文档。

- 第15项是 `Environment.Exit` 的探针退出码；既有单测只直接调 `CancelForExit` 检查窗口内存位置。两者均未证明生产正常退出→FlushFinalState→state.json→加载恢复的攀爬存档链。当前代码有生产接线，不将缺证据直接定性为该链已坏。
- M-17 要求路线中目标关闭/失效；本轮仅有最小化，未有 WindowGone 及对应可见区回退的明确验证。
- finally 的 ALL PASS 只检查已记录条目无 FAIL，没有核对15项完整集合与流程走到末尾。前面已有PASS、后续抛错时仍可打印部分 ALL PASS，虽然异常可能使进程非零退出，汇总文字仍会误导。
- TESTING M-17 整改链接 `../remediation/...` 实际指向仓库根 remediation，须更正为 `remediation/...`；TASKS 输入描述仍写“骨架未验证”，与新证据不同步。

整改目标：补隔离生产退出保存/读档验证，核对爬行前坐标、身份及重建后不续跑；补目标真实关闭或确定失效源的生产取消测试并明确证据层次。汇总要求固定完整条目、确已走到结束、无异常且探针成功，提前抛错须明确失败。更新证据口径及链接，保留旧失败轮。无需重复全部历史实机；与修复有关的关键链重新验证即可。

## 证据与复跑

- [隔离副本测试源码](evidence/P5B-001-R1/WindowClimbAppTests.reviewer-copy.cs.txt)，新增方法均以 Reviewer_Climb_ 开头；未写回主工作区 tests。
- [Core TRX](evidence/P5B-001-R1/review-final_net10.0_20261121142828.trx)：15通过；上述 App TRX：10通过、5失败。
- 命令：`dotnet test .temp/supervisor-p5b001-r1/DesktopPet.slnx --artifacts-path C:/Users/mrrdxx/Desktop/pet/artifacts/supervisor-p5b001-r1 --no-restore --filter "FullyQualifiedName~WindowClimbAppTests|FullyQualifiedName~WindowClimbTests" --logger "trx;LogFilePrefix=review-final" --results-directory docs/reviews/evidence/P5B-001-R1`。
- [开发原始清单副本](evidence/P5B-001-R1/developer-m17-checklist.txt)、[原始事件副本](evidence/P5B-001-R1/developer-climb-live-events.jsonl)。15项PASS按实际覆盖接受，不抹除开发已经取得的证据，也不外推为完整验收。
- 下一步：开发按本报告F编号在独立整改记录追加修复、自测、限制及复核请求；监理独立复核。未提交/推送，不创建或委派新开发聊天。
