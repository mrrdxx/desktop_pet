# P5B-002 整改记录

维护者：开发 AI。流程见 [审查协作规则](../REVIEW_PROCESS.md)。

- 对应审查报告：[P5B-002-review.md](../reviews/P5B-002-review.md)
- 更新时间/时区：2026-11-22 Asia/Shanghai
- 本轮代码版本：未提交/推送；工作区修改 `src/DesktopPet.App/MainWindow.xaml.cs`、`tests/DesktopPet.App.Tests/ClimbEntryTests.cs`（Debug warnaserror 构建 0 警告 0 错误）
- 当前边界：**已整改，监理 R3 接收**（非项目任务状态；TASKS 已标 done）

## docs/reviews/P5B-002-review.md#F-01

- 开发处置：已整改，待复核
- 修改说明与文件位置：
  1. `src/DesktopPet.App/MainWindow.xaml.cs` — `RecoverPet()`（约 3350 行）在既有 `_petPerch?.Detach("recovered")` 与 `_petClimb?.CancelForRecover()` 之后新增 `EndPerchPick("cancelled")`，同步结束待选目标的攀爬/栖息拾取（两种意图共用同一拾取状态机）。注释引用 P5B-002 R2 F-01，并说明缓存入口 `App.OnRecoverRequested` 直接遍历调用 `RecoverPet`、不经 `EnterContextMenuMode`，因此不能依赖开菜单隐式清理。手法与既有 `PauseAnimation()`（P5A-003 R1 F-01 教训）一致。
  2. `tests/DesktopPet.App.Tests/ClimbEntryTests.cs` — 新增两个测试：
     - `RecoverPet_DuringClimbPick_EndsPickAndOldClickDoesNotClimb`：正式菜单入口进入攀爬拾取 → 直接调生产 `RecoverPet` → 断言拾取结束（status=cancelled、IsPerchPickActive/IsClimbPickActive/IsPerchPickAwaitingRelease 均 false）→ 找回后旧点击（合格目标）零起攀、零事件、零位移 → 推进 `climb.Tick` 不启动旧路线、不再移动宠物。
     - `PerchPick_RecoverDuringPick_EndsSelectionNoParallelRegression`：共享栖息拾取待选阶段 → `RecoverPet` → 背书共享状态机语义一致（status=cancelled，不残留）→ 找回后旧点击不重新落位/不起攀/不移动宠物，确认平行入口无回归。
- 自测环境：Windows 11，本机 1920×1080@96DPI；MSTest。
- 实际命令、退出码及关键结果：
  - `dotnet test tests\DesktopPet.App.Tests\DesktopPet.App.Tests.csproj --filter "FullyQualifiedName~ClimbEntryTests" -c Debug --no-restore`：**14/14 通过**（原 12 + 新增 2）。
  - `dotnet test ... --filter "FullyQualifiedName~PerchEntryTests" -c Debug --no-restore`：**10/10 通过**（共享栖息入口回归）。
  - `dotnet test tests\DesktopPet.App.Tests\DesktopPet.App.Tests.csproj -c Debug --no-restore`：**584/584 通过**，0 失败 0 跳过（原 582 + 新增 2）。
  - `dotnet build DesktopPet.slnx -c Debug -warnaserror`：0 警告 0 错误。
  - 本轮未改动 Core，未重跑 Core 全量（R2 已核 Core 563/563，本变更零 Core 影响）；未重跑 M-18 实机（R2 明确无需机械重跑 18 项，本次仅改找回/拾取链）。
- 人工步骤、观察者和实际现象：无（App 级同步单测覆盖；本修复在消息泵外同步执行，测试缝直接驱动生产 `RecoverPet` 与 `UpdatePerchPick`）。
- 未验证项及原因：实机托盘真实点击找回→旧点击场景未跑（R2 结论：修复后无需重跑 M-18 全套，且实机占用鼠标需用户授权时段）；跨屏/混合 DPI 未测（延续任务既有限制）。
- 与监理反例关系：R2 隔离副本 `Reviewer_RecoverDuringClimbPick_MustEndPendingSelection`（当时 1/1 失败）断言「找回后 `IsClimbPickActive` 必须 false」——本修复后该断言在生产代码上通过；作为正式测试落在 `RecoverPet_DuringClimbPick_EndsPickAndOldClickDoesNotClimb`，并扩展「旧点击零起攀/零位移 + Tick 零位移」。
- 证据路径：本条记录所列测试输出为本次实际运行；监理反例及 R2 原始证据见 `docs/reviews/evidence/P5B-002-R2/`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：核对 `RecoverPet()` 的 `EndPerchPick("cancelled")` 落点与注释、新增两条测试断言，以及专项 14/14 + 栖息回归 10/10 + App 全量 584/584 输出。若复核通过，F-01 可关闭；P5B-002 状态由监理在报告确认。

## 后续轮次与复核引用

按日期追加。收到监理结论后只链接原报告的复核节，不在本文件代写或改写监理结论。

- R3（2026-11-22）：监理复核 [P5B-002-review.md](../reviews/P5B-002-review.md) 的 [R3 节](../reviews/P5B-002-review.md#r3-2026-11-22-r2-f-01复核接收)：`RecoverPet` 同步取消待选拾取与原反例 1/1 转绿、攀爬入口 14 项 + 栖息入口 10 项 = 24/24 通过；**R2 F-01 通过、关闭，P5B-002 接收，TASKS 可标 done**。监理声明"开发只追加 R3 关闭引用即可"，本轮未改代码/测试、未重跑。证据目录 `docs/reviews/evidence/P5B-002-R3/`（source-sha256.json + TRX，只读未改动）。