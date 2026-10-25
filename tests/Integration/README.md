# 生命周期隔离集成验证

## 小熊动画观察宿主（ART-001）

运行 `./tests/Integration/Build-BearAnimationReview.ps1` 仅构建观察宿主，引用 STATUS 所指已构建的小熊 DLL；随后可启动 `artifacts/bear-animation-review/bin/Debug/net10.0-windows/BearAnimationReview.exe`。没有重新构建产品，开始前必须核对 DLL/资源版本。

它实际创建生产 MainWindow，仅修改标题、初始位置和 ShowInTaskbar 以便桌面工具选择；使用普通 Application，不创建正式托盘、存档或单实例锁，不替代生产启动链验证。自身无系统鼠标注入。真实鼠标操作需用户授权，由操作者/桌面工具完成；用户并发操作时停止自动输入。

自身输出目录 `review-yyyyMMdd-HHmmss` 保存 state.json、timeline.jsonl 和 Source 变化记录。诊断驱动可向该目录 command.json 写入带唯一整数id的对象，等待 ack-id.txt 后再发下一条：`play`配action、`capture`配name，以及`live`、`pause`、`resume`、`grievance`、`close`。`play`将逻辑鼠标置远，只证明播放；`live`解除鼠标注入；`grievance`在隔离宿主内设置昨日待表达及近距离鼠标，只证明生命周期，不能冒称自然跨天或真实鼠标触发。`capture`保存实际Canvas透明渲染，不采集下层用户窗口。

结束发送 `{"id":999,"command":"close"}` 正常关闭宿主，避免留下诊断实例。结果及完整边界见 [动画检查](../../docs/history/ART-001-animation-review.md)。该工具不是独立监理签字入口。

## P1-003 真实抚摸与拖动输入验证

```powershell
./tests/Integration/Run-EmotionLive.ps1
```

此命令需要用户授权占用系统鼠标和已解锁桌面。约二十秒内不要同时操作鼠标。它构建隔离 App/Core 副本，使用独立存档/互斥锁，在生产 Show 后插入 EmotionLiveProbe；实际 App、鼠标事件、时钟、计分及保存路径不替换，不设置 Test 注入字段。真实微移检查 +5、静止不计分、冷却、透明区退出、身体拖动和松手。托盘 Pause/Resume/Exit 通过实际菜单 PerformClick 验证事件，不冒称 Explorer 图标点击。测试结束恢复指针并正常退出自建实例，比较真实存档前后摘要；不强杀其他进程、不调整系统设置。
只检查构建可用 `-BuildOnly`，不占用鼠标。日志/隔离存档位于 artifacts/emotion-live/run-*/data/，latest-run.txt 指向最近一轮；保留失败材料，任一检查失败不得宣称通过。2026-10-24 验收及可随 Git 保存的证据见 [实机结果](../../docs/history/P1-003-live-validation.md)。

## 生命周期隔离验证

在 Windows 项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Integration/Run-Lifecycle-UTF8.ps1
```

需要本项目 .NET SDK 和桌面运行能力。默认不改变真实存档、显示缩放、系统工作区或开机自启。

当前可执行入口为 `Run-Lifecycle-UTF8.ps1`，阶段 0 最终核查已在 Windows PowerShell 5.1 下验证完整十场景。旧 `Run-Lifecycle.ps1` 保留历史，不作为运行入口：其当前编码在该宿主下会导致解析错误。不要因文件名相近而交替使用两份驱动；旧交接里的命令以本文最新入口为准。

脚本从当前生产源码建立唯一的 `artifacts/p0-004-fix/run-*/isolated-app` 副本，将 ArchiveService 的默认目录改为场景专属目录，将互斥锁名称改为测试专属名称，并在 Show 后添加 LifecycleProbe 观察入口；没有替换原生命周期、托盘菜单事件处理或保存逻辑。源码接线变化导致文本定位失效时立即停止，须人工核对夹具。

场景覆盖：Pause/Resume、暂停下找回、保存暂停后启动、屏外旧位置启动、脏状态立即退出、已完成防抖后再找回并退出、正常文件重启恢复、高版本不同字段类型的加载/退出保护、损坏文件备份恢复和真实重复进程互斥。

托盘操作使用真实 ToolStripMenuItem.PerformClick。它验证实际事件处理，不等于在 Explorer 通知区域点击图标。高版本提示检查通知内容配置，不宣称操作系统一定展示了气泡。双屏/真实热插拔不在本夹具范围。

## P1-002 真实鼠标轨迹验证

在已解锁的 Windows 桌面、可使用鼠标输入的会话中运行：

```powershell
dotnet run --project tests/DesktopPet.InteractionProbe/DesktopPet.InteractionProbe.csproj -c Debug -- artifacts/interaction-live/result.txt
```

此入口会创建实际 MainWindow 并移动系统鼠标，执行悬停、快速移鼠不切表情（D-009）、左键拖动、右键菜单和 Esc。运行约半分钟内不要同时移动鼠标。测试仅点击自己创建的窗口，结束恢复指针位置并正常关闭；不启动生产 App 或读写真实存档，不改变系统显示缩放。暂停通过真实 PauseAnimation/ResumeAnimation 方法执行，不代表已点击 Shell 托盘图标。旧快划/冷却通过记录只代表历史版本，当前探针不再要求受惊触发。

INPUT-002新增头顶、左右耳朵的实际按下→拖动→松手检查，包含鼠标捕获与首段位移一致性；所有按钮按下前核对WindowFromPoint所属PID，不匹配时中止，不点击其他程序。

INPUT-003增加真实鼠标移开后不点击自动恢复idle、清除好奇偏移、短暂移出返回不重播，以及好奇暂停→鼠标移开→恢复后的自动退出检查。构建和当前执行入口见 [INPUT-003交接](../../docs/handoffs/INPUT-003-curiosity-exit-fix.md)；不将普通Application探针结果当作Shell托盘点击或真实存档验证。

ART-002在此基础上增加26秒真实idle观察，记录Image.Source变化、眨眼周期起点及间隔，确认半闭/闭合帧而非content笑脸；好奇按D-011增加持续悬停自动结束且不重播检查。当前构建/运行路径以 [ART-002交接](../../docs/handoffs/ART-002-natural-expressions.md) 为准，整轮约一分钟，旧INPUT-003时长仅作历史说明。
要求 SendInput 可用，不支持锁屏/无交互桌面。任一断言失败退出码为 1。它使用生产 GetCursorPos、实际时钟、当前窗口 DPI 和 DispatcherTimer，未使用 TestGet 注入；输出是实机自动输入证据，不是人类主观观感评价。当前 150% 单屏结果见 [验证记录](../../docs/history/P1-002-live-validation.md)。

任一断言失败、进程异常或退出位置不一致，驱动退出码为 1；成功为 0。所有自己启动的实例通过正常菜单/WM_CLOSE 退出，不强杀未知进程。`latest-run.txt` 指向最新测试目录。原始 `probe.log`、stderr、存档及预期位置均保留，不自动递归清理。

`restart-position` 依赖同一轮先执行 `fast-final`；指定 `-Scenarios` 子集时须保留这个顺序。结果须注明执行者、版本和覆盖范围；修复作者运行本套脚本仍是作者自测，不能自行代签独立监理通过。

从 PowerShell 指定多个场景时，使用数组参数：

```powershell
& ./tests/Integration/Run-Lifecycle-UTF8.ps1 -Scenarios @('offscreen-start','paused-start','normal')
```

使用 `Out-File` 保存输出后，终端可以没有文字；应读取日志中的场景结果和真实退出码，不能凭终端是否显示文字判断成功或异常。探针记录的 `GetDpiForWindow` 才是本次宠物窗口的实际 DPI，不用宿主进程 DPI 或文件名代替。
