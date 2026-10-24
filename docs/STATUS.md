# 当前状态

更新时间：2026-10-24（Asia/Shanghai）。任务状态唯一出处为 [TASKS](TASKS.md)。

## 当前接手任务

P1-004（第一个稀有事件——从屏幕边缘探头）实现完成，状态 `awaiting-verification`。按 PRODUCT R-03 和 ARCHITECTURE §7 v3 存档约定，实现了 Core PeekScheduler 调度逻辑、存档 v3 字段及兼容读取、App OnUpdateTick 接入及行为优先级。全部 334 项自动测试通过。待监理审查。

## 本轮验证与边界

- Core 208/208 + App 126/126 = 334/334 通过（`dotnet test --no-build`，沿用已有构建产物）。
- 桌宠进程 PID=85644 运行中，DLL 锁定导致无法重新构建；未杀进程，未做实机验证。
- PeekScheduler 测试使用可控 Random 种子和合成时间增量，不要求用户等待几十分钟。
- 探头动画复用 curious 占位，正式探头美术待后续阶段。
- 未验证项：实机探头触发、跨天日期切换实机、暂停恢复后探头计时补偿实机。

## 实际实现及限制

- 透明非激活窗口、PNG 动画、拖动、原生 Recover/Exit、PMv2、工作区约束。
- 单实例、托盘 Pause/Resume/Recover/Exit、存档防抖/恢复/高版本只读。
- 鼠标悬停/甩动、Alpha 命中；情绪记忆、抚摸/角落计分、衰减、次日表达及打断、v2存档与v1兼容、暂停冻结恢复。
- P1-004：PeekScheduler（候选抽取 20–120 分钟、50% 概率、有效运行时间累计、日期切换/回拨、延迟/跳过/打断、5 秒探头持续）；存档 v3 五字段 + v1/v2 兼容；App 接入行为优先级（拖动/抚摸/菜单/暂停/委屈表达期间不启动）；暂停补偿 peek 计时；拖动打断 peek 标记额度消耗。
- 探头复用 curious 占位，未完成最终探头美术；不把占位动画说成最终观感。
- 主动靠近由用户明确延后阶段2，见 PRODUCT R-02、DECISIONS D-008。
- 新版本100%/混合DPI多屏、真实锁屏/睡眠未新增实测。

## Git 与接续

- 当前本地 main HEAD 为 91a7b63；P1-003 收尾已提交。本轮 P1-004 全部修改（Core/App/测试/文档）尚未提交。
- 保留已提交的监理报告与证据，按既定 Git 纪律在可验证小步后本地提交；后续 push 仍需用户对应授权，不把提交当验收。
- P1-004 待监理审查通过后标记 done；不提前进入阶段2。

## 主要入口

- [PRODUCT](PRODUCT.md)、[ARCHITECTURE](ARCHITECTURE.md)、[TESTING](TESTING.md)、[REVIEW_PROCESS](REVIEW_PROCESS.md)。
- [P1-003 审查](reviews/P1-003-review.md)、[开发整改](remediation/P1-003-response.md)、[实机验收与证据](history/P1-003-live-validation.md)。
- 复跑方式见 tests/Integration/README.md；需要用户授权暂占鼠标。正常桌宠仍运行。
