# INPUT-003 好奇表情离开后自动恢复

后续D-011/ART-002已将提前离开退出细化为0.3秒，并增加约2秒自然结束；离开0.5秒仍用于重新允许触发。本页保留原版实现/证据，当前代码、运行输出与交接见 [ART-002](ART-002-natural-expressions.md) 和 [STATUS](../STATUS.md)，不要恢复旧构建或旧行为覆盖新规格。

2026-10-25，Asia/Shanghai。用户反馈：“熊好奇以后把鼠标移开还是好奇，得点它才能恢复”。这是按既有授权修复互动缺陷，不是将自然观感讨论全部转为实现。本助手是实现者，以下为自测/实机证据，不代签ART-001独立验收。

## 原因与修改

`HoverDetector`原先只返回驻留触发，没有离开结束事件；curious是单帧Loop，因此鼠标移开不会切回idle。点击/拖动之所以能恢复，是其他输入路径覆盖了动作，不能当作正常退出。

- Core新增 `HoverTrigger.Left`：首次outside开始计时，连续离开0.5秒只报告一次，同时结束旧驻留。当前帧透明点由App的Alpha命中输入判为outside。短暂离开返回不结束、不重播。
- 采样在离开期限前后有间隔、返回时才观察到超时，也报告旧驻留结束，从返回时重新计1.2秒驻留。
- App实际 `OnUpdateTick` 消费Left，仅在当前动作仍为curious时切idle；同次渲染清除curious偏移，不覆盖drag/grievance/happy等动作。
- 暂停/菜单/抚摸等原有中断仍Reset检测器。Reset后的新outside段也可发Left，避免旧好奇脸失去退出途径；暂停画面冻结，恢复后重新计算0.5秒离开期限。没有App侧重复计时器。
- 未改PNG/JSON、存档或依赖；保留D-009删除与D-010头顶/双耳拖动。未实现随机眨眼、好奇固定时长结束或身体动作，不进入P1-004。

## 自动验证

先新增App反例（移到远处/窗口内透明点），修复前两项均实际失败：期待idle，实际curious。修复后完整构建零警告/错误，**Core178+App154=332/332通过，0失败/跳过**。

```powershell
dotnet build DesktopPet.slnx -c Debug --artifacts-path artifacts/curiosity-exit-fix/build -m:1 --ignore-failed-sources --nologo -v minimal
dotnet test DesktopPet.slnx -c Debug --artifacts-path artifacts/curiosity-exit-fix/build --no-build --no-restore -m:1 --nologo -v minimal
```

相对INPUT-002新增14项：Core4项检查离开边界、一次报告、短暂返回、Reset与透明点；App10项检查真实tick驱动的远处/透明离开、499/500ms边界、图像偏移归零、重新完整驻留、其他动作不被覆盖，以及暂停/菜单/抚摸中断后仍可退出。App自动用例注入时间和坐标，不冒充真实鼠标输入。

## 实机输入与启动

```powershell
dotnet build tests/DesktopPet.InteractionProbe/DesktopPet.InteractionProbe.csproj -c Debug --artifacts-path artifacts/curiosity-exit-fix/probe-build -m:1 --ignore-failed-sources --nologo -v minimal
dotnet artifacts/curiosity-exit-fix/probe-build/bin/DesktopPet.InteractionProbe/debug/DesktopPet.InteractionProbe.dll artifacts/curiosity-exit-fix/curiosity-exit-live-result.txt
```

探针构建零警告/错误，当前**HWND DPI144（150%），31/31 PASS，failures=0，退出码0**。新增5项真实输入检查：短暂移开仍好奇、快速返回不闪/不重播、只移开不点击即idle且偏移归零、暂停好奇后移开仍冻结、恢复后自动退出。原26项保留，包括头顶/左右耳拖动保持捕获与首段位移、D-009不受惊、身体拖动、菜单/Esc及Pause/Resume。

该探针直接创建实际MainWindow，用系统鼠标、真实时钟和窗口事件，无坐标/时间注入；按钮按下前核对窗口所属PID，结束恢复鼠标并正常关闭自建窗口。普通Application不创建正式存档/托盘，不将此结果当作生产存档或Shell图标验收，不将状态断言等同于主观美术观感结论。

正式旧PID181968经WM_CLOSE正常退出，新PID181684启动，可见窗口1个/DPI144，记录时间05:46（PID仅为当轮记录，后续须重新查）。桌面包装脚本仍调用仓库 `tools/Start-DesktopPet.cmd`；当前程序为 `artifacts/curiosity-exit-fix/build/bin/DesktopPet.App/debug/DesktopPet.exe`。动画观察宿主也已重编译并改为引用当前产品输出，但本轮未用其另跑美术检查。

证据：

- [实机原始日志](../history/evidence/INPUT-003-curiosity-exit/curiosity-exit-live-result.txt)
- [正常重启](../history/evidence/INPUT-003-curiosity-exit/restart-result.json)
- [版本SHA256](../history/evidence/INPUT-003-curiosity-exit/versions.json)
- [构建/自动测试结果摘录](../history/evidence/INPUT-003-curiosity-exit/verification.txt)

## 接续与限制

- HEAD仍为a2412f7，加既有P1-004撤回、小熊接入、INPUT-001/002及本次未提交修改。不要把整个diff归于本次，不reset来源不明修改。未提交/推送，未改旧监理报告。
- PRODUCT R-01是行为规则权威出处；ARCHITECTURE/TESTING与运行入口已同步。先读STATUS及本页，保留离开事件、curious保护和回归测试，不切回旧构建。
- 当前版100%/混合DPI、多屏和新资源完整抚摸计分实机集合仍未补测；原历史证据保留其版本边界。眨眼轮廓和固定节奏仍待后续讨论，ART-001独立核对不由本次自测代签。
