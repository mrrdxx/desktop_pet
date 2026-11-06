# ART-003 温和拖动表情与松手恢复

> 2026-10-26接续修订：好感度及抚摸计分已按[D-013](../DECISIONS.md#d-013完全移除好感度系统)移除。以下原版本证据/计分描述保留为历史，不再要求实现或补测；当前运行与验证入口见[移除交接](EMOTION-001-remove-affinity.md)及STATUS。其他美术/输入规则仍按当前规格执行。

2026-10-25，Asia/Shanghai。用户“我觉得现在很不错，按你说的做”接受ART-002观感并同意后续，依据D-012。先前截至ART-002的基线已提交并普通推送到GitHub main，实际核对为 **4b2568188369e451ee0d9be7f8665e71d54a758c**；包含已有P1-004撤回和INPUT修复，不重写历史。以下ART-003是随后新增的未提交/未推送修改。

## 实现边界

- 内置imagegen生成两格面部母版，保存项目副本；复用局部合成器新增IncludeBrows和输出命名参数。原idle_0为固定底图，两处眼/眉区域外RGBA、全图Alpha完全不变；嘴/鼻/身体/耳朵/脚点不重画。原眨眼默认导出仍逐文件SHA256一致。来源/成功提示词见 [素材说明](../../assets/art/bear-v1/drag-source.md)。
- bear的drag改为drag_gentle，release序列为gentle/settle/idle。时序与中断规则以PRODUCT为准；原startled保留但不注册drag/dodge，未恢复D-009快划功能。
- Core `DragReleaseFeedback`只管理短序列进度。App实际EndDrag首先解除拖动/捕获、约束位置并保存，再且仅在确有拖动且未开菜单时启动面部覆盖；CurrentAction立即为idle，没有松手位置延迟。
- UpdateFrame在合法idle下优先显示release，再显示blink，最后普通idle；高优先级动作不被替换。新头/身体抓取、菜单或其他动作取消旧过渡，不会稍后复活。暂停冻结当前进度，暂停中结束拖动的新过渡从自身起点冻结；没有多重位置写入、新依赖或存档格式变化。
- 头顶/双耳抓取点、8DIP手势分流、抚摸计分、情绪和好奇规则保留。没有抛掷、走路、呼吸、歪头、抚摸成功新表情或P1-004推进。

## 已完成自动验证

```powershell
dotnet build DesktopPet.slnx -c Debug --artifacts-path artifacts/bear-drag-feedback/build -m:1 --ignore-failed-sources --nologo -v minimal
dotnet test DesktopPet.slnx -c Debug --artifacts-path artifacts/bear-drag-feedback/build --no-build --no-restore -m:1 --nologo -v minimal
```

构建0警告/错误，**Core197+App167=364/364通过，0失败/跳过**。新增14项：Core6项检查90/180/250ms边界、中断/新释放、暂停/暂停中启动及超时不重播；App7项驱动实际共享手势入口、真实Image.Source、位置松手即停、头/身体重新抓取、菜单/委屈打断、暂停和无拖动不启动；另1项逐像素比较真实素材及JSON映射。

新App用例没有给未捕获的窗口写恒假断言来冒充捕获释放：它们证明手势状态/位置/渲染；完整测试集中既有HeadDragInteractionTests包含实际WPF捕获保持/释放。这里不将两者合并称为系统鼠标端到端通过。

## 实机输入尚未完成

```powershell
dotnet build tests/DesktopPet.InteractionProbe/DesktopPet.InteractionProbe.csproj -c Debug --artifacts-path artifacts/bear-drag-feedback/probe-build -m:1 --ignore-failed-sources --nologo -v minimal
dotnet artifacts/bear-drag-feedback/probe-build/bin/DesktopPet.InteractionProbe/debug/DesktopPet.InteractionProbe.dll artifacts/bear-drag-feedback/drag-live-result.txt
```

探针构建通过，但本轮三次输入运行均中止/未通过，不作成功结论：

- 首次完成idle观察后，多项悬停断言失败；按钮前的窗口PID保护不符，未发送按钮按下，整轮failures=6。
- 补充Move后的指针核对后，首次无等待读取位置不能排除SendInput排队影响；该轮failures=1，保留日志。
- 增加40ms队列处理等待后再次运行，仍得到目标(180,548)、实际(1133,849)，保护立即中止，failures=1。当前环境无法维持测试指针位置，具体原因未判定，不武断归因产品或用户操作。

没有点击非本项目窗口或关闭其他软件。保护改为只释放探针确实按下的左键，干扰时不强行恢复指针；未持续重试占用鼠标。Computer Use只读窗口枚举也没有返回正式桌宠的可操作窗口，因此未继续尝试通过其他窗口控制它，未强杀旧版或伪造“新版已运行”。

原始中止日志、正式TRX及SHA256见 [证据目录](../history/evidence/ART-003-drag-feedback/)。原ART-002 37项通过是旧版证据，不能抵消本次未完成的实机验证。静态素材预览见 [对照](../design/images/bear-drag-feedback-preview.png)；它不能替代实际松手手感。

## 当前运行与接续

- 正式进程仍为bear-natural旧输出（本轮只读查询PID180840，PID后续须重查）。新产物已构建到bear-drag-feedback，未替换正在运行的旧实例。
- 桌面包装脚本仍调用tools/Start-DesktopPet.cmd，入口已指向新输出，--check可检查存在性；先从旧版托盘Exit正常退出，再双击桌面“启动小熊桌宠.cmd”。不要把路径检查当作新版本启动验证。
- TASKS保持ART-003 awaiting-verification：待稳定鼠标环境下补系统输入链，再请用户看新表情/短过渡；当前实现和自动验证可以独立接手，不必重复ART-002观感确认。
- 本轮ART-003未提交、未推送；本次Git授权保存的是之前已认可的基线，未扩成以后每轮自动推送。先读STATUS/PRODUCT/本页，保留新增素材/测试与原始失败日志，不回退D-009/010或进入P1-004。
