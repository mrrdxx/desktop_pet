# 当前状态

更新时间：2026-10-23（Asia/Shanghai）。任务状态唯一出处为 [TASKS](TASKS.md)。

## 当前接手任务

P1-003 情绪记忆：`in-progress`。F-01 已修正（日期回拨不再误触发次日表达）。G-01/G-02 已落实（浮点累计精度 + 角落停留重置）。下一步：接入 App 层（抚摸检测、角落判定、情绪更新接入 OnUpdateTick）。
P1-002 已完成（`done`）。P1-001 已完成。

## 本轮验证

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 测试：Core 199 + App 58 = **257/257**，0 失败，0 跳过。
- F-01 新增 12 项测试（回拨/同日/次日/跨月跨年/非法日期/存档恢复）。
- G-01 新增 10 项测试（浮点精度/分步累计一致性/取整不丢失/回拨/增量）。
- G-02 新增 7 项测试（新拖动取消旧停留/离开角落/完整序列/不重复计分）。

## 实际实现及限制

- WPF 透明非激活置顶窗口、PNG 动画、拖动、原生菜单、PMv2、工作区约束与 Recover。
- 单实例、托盘 Pause/Resume/Recover/Exit、本地配置/位置存档、损坏恢复和高版本只读保护。
- P1-002 采样/悬停/甩动和中断链已接入；透明角落误触已修复。
- P1-003 Core 情绪模型已实现并修正 F-01/G-01/G-02，尚未接入 App 层；情绪行为尚未在运行时可见。
- 资源仍是程序导出的占位美术；用户接受 P1-002 交互功能但不代表最终美术/表情/动作观感验收。
- 100% DPI 新版本、混合 DPI 双屏、真实锁屏/睡眠恢复仍未重测。

## Git 状态

- 基线 main 的 `35528cf`（已推送），领先 origin/main 本地提交：`3efc046`、`e83e3e6`、`b81666e`、`4882efc`、本轮 F-01/G-01/G-02 修正待提交。
- 后续授权：每个完成必要验证的小步可自动本地提交；仅用户明确要求时才 push。

## 接续动作

- 阶段检查见 [P1-003 Core 审查](reviews/P1-003-review.md)；整改记录见 [P1-003-response](remediation/P1-003-response.md)。
- F-01 已修正待监理复核；G-01/G-02 已在 Core 层覆盖，App 接入时验证运行时行为。
- 1. P1-003 进行中：下一步接入 App 层。
- 2. 不提前实现后续阶段，遵守文档纪律。

## 主要入口

- [PRODUCT](PRODUCT.md)、[ARCHITECTURE](ARCHITECTURE.md)、[TESTING](TESTING.md)、[REVIEW_PROCESS](REVIEW_PROCESS.md)。
- [P1-003 审查](reviews/P1-003-review.md)、[整改记录](remediation/P1-003-response.md)。
- [通用接续](CONTEXT_HANDOFF.md)。
