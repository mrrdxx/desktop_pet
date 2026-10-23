# 当前状态

更新时间：2026-10-23（Asia/Shanghai）。任务状态唯一出处为 [TASKS](TASKS.md)。

## 当前接手任务

P1-003 情绪记忆：`todo`，依赖 P1-002 已解除。先核实 R-02 范围和验收条件，再执行一个可验证的小步。
P1-002 已完成（`done`）：监理 R4 关闭 F-01～F-04；帮手实机修复透明角落误触，接手者核对 diff 并重验 163/163 通过；用户人工体验后明确接受交互功能并要求收尾。P1-001 已完成。

## P1-002 收尾

- 监理 [复核 R4](reviews/P1-002-review.md) 已关闭 F-01～F-04。
- 帮手实机修复（透明角落误触）：新增 `PetImageGeometry` Alpha 命中、`HoverDetector` 可选 `isOverEntity`、`MainWindow` 坐标改用 `PointToScreen/PointFromScreen`。接手者核对 diff、重验构建 0 警告 0 错误、163/163 测试通过。
- 用户反馈：已人工体验，暂未发现阻碍继续开发的问题；接受的是 P1-002 交互功能，不代表最终美术/表情/动作观感验收。用户要求收尾标 done，构成对真实鼠标手感验收项的明确豁免（REVIEW_PROCESS §5）。
- 帮手修复的独立核对由接手者完成（非帮手本人），不代签监理结论。详见 [整改记录](remediation/P1-002-response.md)。

## 本轮验证

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 测试：Core 108 + App 55 = **163/163**，0 失败，0 跳过。
- 帮手实机自动输入 18/18 两轮通过、十场景生命周期通过（沿用帮手记录，本轮未重跑实机）。

## 实际实现及限制

- WPF 透明非激活置顶窗口、PNG 动画、拖动、原生菜单、PMv2、工作区约束与 Recover。
- 单实例、托盘 Pause/Resume/Recover/Exit、本地配置/位置存档、损坏恢复和高版本只读保护。
- P1-002 采样/悬停/甩动和中断链已接入；透明角落误触已修复（Alpha 命中替代整窗矩形）。
- 资源仍是程序导出的占位美术；用户接受交互功能但不代表最终美术/表情/动作观感验收。
- 100% DPI 新版本、混合 DPI 双屏、真实锁屏/睡眠恢复仍未重测。
- KL-001：拖动中 Alt+Tab 仍跟随的用户接受范围见 [P0-002 R2](reviews/P0-002-review.md)，未扩大豁免。

## Git 状态

- 基线 main 的 `35528cf`（已推送），本轮 P1-002 收尾改动（帮手代码 + 文档）尚未提交。
- 后续授权：每个完成必要验证的小步可自动本地提交；仅用户明确要求时才 push。

## 接续动作

1. P1-002 已 done，开始 P1-003 情绪记忆。
2. 先核实 R-02 范围和验收条件，再执行一个可验证的小步。
3. 不提前实现后续阶段，遵守文档纪律。

## 主要入口

- [PRODUCT](PRODUCT.md)、[ARCHITECTURE](ARCHITECTURE.md)、[TESTING](TESTING.md)、[REVIEW_PROCESS](REVIEW_PROCESS.md)。
- [P1-002 原审查](reviews/P1-002-review.md)、[开发整改记录](remediation/P1-002-response.md)。
- [本轮修复交接](handoffs/P1-002-live-input-fix.md)、[实机自测结果](history/P1-002-live-validation.md)、[通用接续](CONTEXT_HANDOFF.md)。
