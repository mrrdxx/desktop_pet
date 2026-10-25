# INPUT-002 头顶与双耳拖动修复交接

后续版本提示：本页构建路径/PID/测试数量是INPUT-002当轮记录；当前运行版及好奇退出修复见 [STATUS](../STATUS.md) 和 [INPUT-003](INPUT-003-curiosity-exit-fix.md)，不要将桌面入口切回旧输出。

2026-10-25，Asia/Shanghai。用户反馈眼睛连线上方不能点击拖动并要求修正，决定与当前手势契约见 [D-010](../DECISIONS.md#d-010头部和双耳允许拖动并保留抚摸)、PRODUCT R-02。本助手为本次实现者，以下是自测与实机执行证据，不代签独立监理通过。

## 原因与修复边界

原实现并非PNG缺失：可见头顶和耳朵进入抚摸候选，转拖动前先结束候选并释放捕获，又以转换时位置重算抓取偏移；只看相邻移动距离也使慢慢拖动一直被当成抚摸。旧R-02还要求从身体开始拖动，现按用户要求明确为所有可见部位可拖。

MainWindow事件边界先做当前帧Alpha检查，捕获成功后调用共享的 `BeginCapturedPointerInteraction` 保存最初screen DIP位置与偏移。`ProcessCapturedPointerMove`处理手势：

- 身体按下立即拖动，头部先进入抚摸候选。
- 相对按下点累计位移超过8DIP、单次位移超过8DIP或移出头部区域时，直接接续拖动；不先释放捕获，不用新位置重算抓取点，同一事件就应用首段完整位移。
- 转拖动清空抚摸累计值，避免拖动误记+5；小范围来回有效抚摸仍能计分。短按或静止不计分。
- 抬起经 `EndCapturedPointerInteraction` 统一结束；捕获丢失与关闭仍沿用原清理路径。透明空白不扩成点击区，未改PNG或全窗口穿透策略。

当前内部 `ProcessPettingMove` 只证明小幅抚摸计时；不能再以其“候选结束”断言冒充完整头部转拖动。新增测试直接驱动生产事件共用的坐标入口，另有实际WPF捕获和真实输入证明。

## 本轮验证

```powershell
dotnet build DesktopPet.slnx -c Debug --artifacts-path artifacts/head-drag-fix/build -m:1 --no-restore --nologo -v minimal
dotnet test DesktopPet.slnx -c Debug --artifacts-path artifacts/head-drag-fix/build --no-build --no-restore -m:1 --nologo -v minimal
```

构建0警告/错误；**Core174+App144=318/318通过，0失败/跳过**。新增16项包括idle/curious的头顶/双耳实际PNG点、好奇offsetY=9、2DIP慢移累计、8DIP边界、跨出头部/图像、首段抓取偏移、短按、抚摸+5/转拖动不计分、透明点、身体立即拖动和真实WPF捕获保持/释放。捕获测试显示自己的窗口后调用实际CaptureMouse，没有移动系统鼠标。

更新 `tests/DesktopPet.InteractionProbe/Program.cs`，增加三处头部的实际按钮与位移检查，并在每次按钮按下前校验WindowFromPoint所属PID。该项目与动画观察宿主均独立重编译零警告/错误。

实际运行：

```powershell
dotnet artifacts/head-drag-fix/probe-build/bin/DesktopPet.InteractionProbe/debug/DesktopPet.InteractionProbe.dll artifacts/head-drag-fix/head-drag-live-result.txt
```

**150%（实际HWND DPI144），26项全部PASS，failures=0，退出码0**。包含三处按下被接收→拖动保持捕获及(24,12)DIP位移→松手结束，以及透明角落、驻留/重触发、D-009快速移鼠不切表情、身体拖动、原生菜单/Esc和Pause/Resume。Probe使用实际MainWindow、系统鼠标、真实窗口事件/时间/DPI，未注入坐标或时钟；普通Application不创建正式存档/托盘。结束恢复指针并正常关闭自己的窗口。原始日志见 [实机结果](../history/evidence/INPUT-002-head-drag/head-drag-live-result.txt)。

正式桌宠旧PID174432正常退出，新PID181968启动，可见窗口1个/DPI144，记录见 [正常重启](../history/evidence/INPUT-002-head-drag/restart-result.json)。PID只为本轮记录，不复用。桌面入口仍调用tools/Start-DesktopPet.cmd，已指向 `artifacts/head-drag-fix/build/bin/DesktopPet.App/debug/DesktopPet.exe`，路径检查通过。

源文件、正式二进制与探针所用二进制分别记录 [SHA256](../history/evidence/INPUT-002-head-drag/versions.json)。验证后仅更新了一段源码说明注释，手势实现不变；不因文档/注释变化重复全量输入测试。

## 接手与限制

- 无美术修改，无新依赖或存档格式变动；D-009快速移鼠受惊删除保留。未开始P1-004，未提交/推送。
- 未新增100%或混合DPI、多屏实机结果；本轮不替代真实Shell托盘点击/不抢焦点/其他阶段的独立验收。
- ART-001眨眼轮廓与节奏建议保留。本轮未改好奇退出；后续已按INPUT-003修复，详见页首提示，不能用本轮旧输出覆盖它。
- 工作区叠加既有P1-004撤回、小熊接入、INPUT-001等修改，禁止为接手直接reset其他文件。开发AI先核对本记录与实际diff，保留共享手势入口、捕获保持及新增用例，不能退回“头部只能抚摸、拖动必须从身体开始”。
