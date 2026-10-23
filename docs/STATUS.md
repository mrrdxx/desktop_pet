# 当前状态

更新时间：2026-10-24（Asia/Shanghai）。任务状态唯一出处为 [TASKS](TASKS.md)。

## 当前接手任务

P1-003 情绪记忆：`in-progress`。监理 R7 认可 F-06 正常向前冻结/未暂停对照，剩余回拨保护恢复边界已补：暂停恢复前移条件增加 `utcNow > lastDecayUtc` 防止倒退，新增连续测试覆盖回拨→恢复→超过原时刻只计新间隔。F-01～F-05 保持关闭，不重做。详情见审查 R7，不进入 P1-004。
P1-002 已完成（`done`）。P1-001 已完成。

## 本轮验证

- 构建：`dotnet build DesktopPet.slnx -c Debug` 成功，0 警告 0 错误。
- 测试：Core 208（未重跑）+ App 126 = **334/334**（App 独立运行 126/126 通过）。
- F-06 R7 新增 1 项连续测试（回拨边界：存档基准晚于 restoreUtcNow → 不倒退 → 回到原时刻不衰减 → 超过只计新间隔，亲近度+委屈值）。
- 移除修复后 R7 测试失败已验证（LastDecayUtc 被倒退），确认测试有区分度。

## 实际实现及限制

- WPF 透明非激活置顶窗口、PNG 动画、拖动、原生菜单、PMv2、工作区约束与 Recover。
- 单实例、托盘 Pause/Resume/Recover/Exit、本地配置/位置存档、损坏恢复和高版本只读保护。
- P1-002 采样/悬停/甩动和中断链已接入；透明角落误触已修复。
- P1-003 Core 情绪模型 + App 接入已完成：抚摸检测（F-03 完整头部边界 + Alpha 检测）、情绪衰减、角落判定、委屈表达 10 秒持续与拖动按下即打断（F-04 表达优先级抑制 hover/swipe）、存档 v2 读写（F-05 生产复用共用映射）、暂停语义 SkipInterval 补偿（G-03 统一时间源 + 跨对象隔离恢复验证）。
- 资源仍是程序导出的占位美术；委屈表达使用 curious 动作作为占位，最终"背对用户"美术待后续阶段。
- 100% DPI 新版本、混合 DPI 双屏、真实锁屏/睡眠恢复仍未重测。

## Git 状态

- 当前本地 main HEAD 待提交（F-06 R7 整改 + 文档同步），未推送。
- 监理报告 `docs/reviews/P1-003-review.md` 已纳入本地提交，原样不修改。
- 后续授权：每个完成必要验证的小步可自动本地提交；仅用户明确要求时才 push。

## 接续动作

- 待监理复核 F-06 R7 整改；F-01～F-05 保持关闭，不重做。
- 阶段检查见 [P1-003 Core 审查](reviews/P1-003-review.md)；整改记录见 [P1-003-response](remediation/P1-003-response.md)。
- 1. P1-003 进行中：F-06 R7 整改完成，待复核收尾。
- 2. 不提前实现后续阶段，遵守文档纪律。

## 主要入口

- [PRODUCT](PRODUCT.md)、[ARCHITECTURE](ARCHITECTURE.md)、[TESTING](TESTING.md)、[REVIEW_PROCESS](REVIEW_PROCESS.md)。
- [P1-003 审查](reviews/P1-003-review.md)、[整改记录](remediation/P1-003-response.md)。
- [通用接续](CONTEXT_HANDOFF.md)。
