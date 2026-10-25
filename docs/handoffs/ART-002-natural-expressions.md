# ART-002 自然眨眼与短暂好奇交接

2026-10-25，Asia/Shanghai。用户粘贴自然观感方案并批准“很好，这部分按你说的做”，决定见D-011，当前行为权威出处是PRODUCT。首版先统一底图与眨眼、结束好奇，不扩展身体动作或P1-004。本助手是实现者，以下为自测/实机结果，不代签独立美术验收。

## 实际修改

- 图像：以原idle_0为固定底图，内置imagegen生成的眼部母版存 `assets/art/bear-v1/blink-source.png`，通过 `tools/Export-BearBlinkAssets.ps1` 导出half/closed。仅两块眼部RGB变化，眼部外RGBA及全图Alpha完全相同，普通眨眼保持原嘴形。九种表情保留，另增两张眼部帧；未替换角色原画或添加身体动作。
- JSON：idle只留中性图，blink新增Once序列（半闭→闭→半开→睁眼）；content不再参加普通眨眼。格式未加新字段，default原型回退保留。
- Core `IdleBlinkScheduler`：消费JSON眨眼序列，只返回渲染覆盖帧；App仍保持CurrentAction=idle，不争夺动作/位置所有权。间隔、低概率双眨及暂停规则见PRODUCT。更新中断超过500ms时弃旧计划并重排，不补演；切回idle也重新等待。
- Core `CuriosityReaction`：显示寿命与悬停重触发分离。到时或提前离开请求结束；App只结束当前curious并清除渲染偏移，不重置已触发驻留，因此持续悬停不重播。暂停冻结寿命，恢复的驻留事件不重启仍在进行的好奇。
- 保留D-009移除、D-010头顶/双耳拖动、抚摸计分/情绪/存档。拖动仍为既有受惊脸；拖动专用温和脸、松手缓冲、抚摸成功笑脸、委屈新素材及身体动作未新增。

## 自动验证

```powershell
dotnet build DesktopPet.slnx -c Debug --artifacts-path artifacts/bear-natural/build -m:1 --ignore-failed-sources --nologo -v minimal
dotnet test DesktopPet.slnx -c Debug --artifacts-path artifacts/bear-natural/build --no-build --no-restore -m:1 --nologo -v minimal
```

构建0警告/错误，**Core191+App159=350/350通过，0失败/跳过**。相对INPUT-003新增18项：Core13项覆盖间隔边界/开闭时序/双眨/中断/暂停/卡顿及好奇时限；App5项覆盖持续悬停退出、300ms退出与500ms重触发区分、真实Image.Source与暂停、好奇剩余寿命及实际PNG像素/Alpha不变。旧悬停用例按D-011调整299/300ms及2秒终止断言，未删其有效边界。

第一次完整运行确实出现2个失败，保留原TRX：半透明轮廓复制取整差、恢复后的新驻留重启好奇。前者改为逐像素RGBA复制，后者限制已在curious时不得重新启动；同两项随后通过。探针第一次编译有WPF/WinForms Image名称冲突，显式别名修正后构建0警告/错误。未删除测试或放宽像素容差。

## 真实窗口与输入

```powershell
dotnet build tests/DesktopPet.InteractionProbe/DesktopPet.InteractionProbe.csproj -c Debug --artifacts-path artifacts/bear-natural/probe-build -m:1 --ignore-failed-sources --nologo -v minimal
dotnet artifacts/bear-natural/probe-build/bin/DesktopPet.InteractionProbe/debug/DesktopPet.InteractionProbe.dll artifacts/bear-natural/natural-live-result.txt
```

**实际HWND DPI144（150%），37/37 PASS，failures=0，退出码0**。先观察26秒idle的真实Image.Source，实际显示idle/half/closed且不含content；四轮眨眼起点间隔约4133/6557/4899ms（含前一轮长度和更新取整），符合安静间隔。再以真实鼠标验证好奇自动结束、持续不重播、提前移开/短暂返回、暂停恢复，以及身体/头顶/双耳拖动、捕获、菜单/Esc、快速移鼠不受惊。

探针直接创建生产MainWindow，真实鼠标/时钟/窗口事件，未注入坐标或时间；仅按下自己PID的窗口，结束恢复指针并正常关闭。普通Application无正式存档/托盘服务，不替代Shell图标、生产存档或用户主观观感验证。

正式旧版已正常WM_CLOSE/WaitForExit退出，新PID183036启动，最终确认可见窗口1个/DPI144。启动后第2秒检查尚未找到窗口，未强杀或重复启动；稍后枚举确认正常可见，记录该就绪延迟，不将首次探测写成成功。桌面包装脚本仍调用tools入口，已指向 `artifacts/bear-natural/build/bin/DesktopPet.App/debug/DesktopPet.exe`，--check通过。动画观察宿主已重新引用当前输出并构建通过，本轮未用其另跑美术手动预览。

证据目录 [ART-002-natural](../history/evidence/ART-002-natural/) 包含原始输入日志、失败与通过TRX、重启、验证摘要和源码/二进制SHA256。图像来源/原提示词见 [眨眼素材说明](../../assets/art/bear-v1/blink-source.md)，静态对照见 [预览](../design/images/bear-natural-blink-preview.png)。

## 接续边界

- 用户随后反馈“我觉得现在很不错，按你说的做”，现版自然观感已获接受，ART-002完成依据见TASKS/D-012。状态断言与用户审美接受分别记录；ART-001未由作者代签独立通过。
- 当前100%/混合DPI、多屏、新资源完整抚摸计分实机集合仍未补；本轮不重跑无关的跨天/存档全套实机，也不把历史证据挪作当前结论。
- HEAD a2412f7，加既有撤回P1-004、小熊接入、INPUT-001/002/003及本轮未提交工作区；未提交/推送，未修改旧监理报告。不能reset/checkout整个diff或恢复旧构建/快划检测。
- 接手先读AGENTS、STATUS、D-009～011及本页，按实际代码和最新PRODUCT核对，保留新增资源和测试。暂不进入P1-004；待用户反馈再安排下一小步。
