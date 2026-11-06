# EMOTION-001：完全移除好感度

日期：2026-10-26（Asia/Shanghai）。依据用户本轮明确要求及[D-013](../DECISIONS.md#d-013完全移除好感度系统)。这是实现者交接和自测记录，不是独立监理签字。

## 改动边界

- Core删除亲近度字段、阈值、增量、半衰期、浮点累计及相关API。EmotionState/EmotionDecayAccumulator只处理独立委屈记忆；回拨保护、12小时半衰期、暂停及暂停退出冻结不变。
- App删除头部手势的累计时长、+5计分、冷却及暂停补偿；头部小幅移动仍可保持手势，超过8DIP或移出头部接续拖动，最初抓取点和捕获规则保留。该手势不产生数值、解锁或保存；按下打断正在表达的委屈仍按既有规则清理。
- PetStateArchive不再包含已取消字段，生产加载/保存映射同步移除。state保持v2：旧v1默认恢复路径保留；旧v2的多余字段忽略，正常保存后不再输出；位置、暂停、显示器、委屈及待表达日期保留。未知高版本仍只读。不会直接清洗真实用户目录，旧备份和损坏原件按原策略保留。
- 已取消D-008中的高亲近主动靠近计划，不保留默认关闭开关或未来待办。头部非计分手势与双宠社交不以好感度解锁。
- 已有ART-003温和脸、松手恢复、素材/代码/测试均保留；其真实鼠标链和新表情观感缺口没有借本轮测试关闭。P1-004仍未开始。

## 文档与接手

当前产品行为见PRODUCT R-02，格式/API见ARCHITECTURE §7–8，任务状态只见TASKS。README、TESTING、DECISIONS、AGENTS和新对话指南已同步；最相关旧交接已加D-013修订提示。

旧P1-003审查/整改及实机日志证明的是当时包含计分的版本，原报告和证据原样保留。不因历史中存在“50→55”“+5”“高亲近”就恢复功能，也不把历史体验结果移作当前版本通过依据。根目录早期《Windows桌宠项目要求.md》继续按用户要求忽略，不作为规格、不修改或自动提交。

## 本轮验证

- .NET 10.0.402，Windows 11 x64；隔离启动实际HWND DPI=144（150%）。
- 完整构建零警告/错误，正式MSTest **Core173+App159=332/332通过，0失败/跳过**。测试总量下降来自删除取消功能的专用用例；保留委屈、回拨、暂停、位置、透明命中及头部拖动回归，不靠删除现行需求的失败测试消除问题。
- 新增7个移除回归结果：旧v2带数字/null/对象字段的3组加载→生产映射→保存→重载；未来版本原文保护；损坏主档从旧备份恢复；编译后模型/窗口无旧字段和计分API；多轮头部微移的完整持久化快照不变。
- 五个隔离生产生命周期场景全部通过：normal、paused-start、archive-cases、readonly-state、readonly-settings，均退出码0，真实用户存档目录快照不变。normal/paused-start实际生产保存中无已取消字段。托盘采用实际菜单事件PerformClick，不宣称Explorer图标点击。
- 调整后的EmotionLive夹具在Windows PowerShell 5.1下`-BuildOnly`构建通过；动画观察宿主构建通过。两者仅验证可构建，未运行系统鼠标输入。首次BuildOnly暴露旧脚本使用当前宿主没有的GetRelativePath，已改为源目录内相对路径截取并重建通过。
- 初次编译发现一个测试仍读取旧元组，已改为委屈整数快照；初轮精度测试把约0.48的衰减误写成整数必须下降，已改为独立公式比较浮点值并验证取整仍为80，完整重跑通过。失败材料保留，不伪称初次即通过。
- 原始命令输出、TRX及摘要见[本轮证据](../history/evidence/EMOTION-001-affinity-removal/)；完整本机构建/日志在`artifacts/affinity-removal/`，该目录不由Git自动备份。

```powershell
dotnet build DesktopPet.slnx -c Debug --artifacts-path artifacts/affinity-removal/build -m:1 --ignore-failed-sources --nologo -v minimal
dotnet test DesktopPet.slnx -c Debug --artifacts-path artifacts/affinity-removal/build --no-build --no-restore -m:1 --nologo -v minimal
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Integration/Run-EmotionLive.ps1 -BuildOnly
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Integration/Build-BearAnimationReview.ps1
& { . ./tests/Integration/Run-Lifecycle-UTF8.ps1 -Scenarios @('normal','paused-start','archive-cases','readonly-state','readonly-settings') }
./tools/Start-DesktopPet.cmd --check
```

## 运行与交还

桌面脚本继续调用仓库`tools/Start-DesktopPet.cmd`，现指向`artifacts/affinity-removal/build/bin/DesktopPet.App/debug/DesktopPet.exe`，路径检查通过。已有正式旧实例未被替换；先从其托盘Exit正常退出，再双击桌面入口。单实例保护保留，不强杀进程。

本轮未提交、未推送；基线HEAD为4b25681，其后已有ART-003与本轮移除工作都在工作区，不能回退或覆盖。独立监理如需审查，应对当前diff及本轮证据检查，不修改旧签字来适配新实现。未补100%/混合DPI/多屏及ART-003真实输入/主观观感，不要求为已取消的抚摸计分补体验。
