# 生命周期隔离集成验证

在 Windows 项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Integration/Run-Lifecycle-UTF8.ps1
```

需要本项目 .NET SDK 和桌面运行能力。默认不改变真实存档、显示缩放、系统工作区或开机自启。

当前可执行入口为 `Run-Lifecycle-UTF8.ps1`，阶段 0 最终核查已在 Windows PowerShell 5.1 下验证完整十场景。旧 `Run-Lifecycle.ps1` 保留历史，不作为运行入口：其当前编码在该宿主下会导致解析错误。不要因文件名相近而交替使用两份驱动；旧交接里的命令以本文最新入口为准。

脚本从当前生产源码建立唯一的 `artifacts/p0-004-fix/run-*/isolated-app` 副本，将 ArchiveService 的默认目录改为场景专属目录，将互斥锁名称改为测试专属名称，并在 Show 后添加 LifecycleProbe 观察入口；没有替换原生命周期、托盘菜单事件处理或保存逻辑。源码接线变化导致文本定位失效时立即停止，须人工核对夹具。

场景覆盖：Pause/Resume、暂停下找回、保存暂停后启动、屏外旧位置启动、脏状态立即退出、已完成防抖后再找回并退出、正常文件重启恢复、高版本不同字段类型的加载/退出保护、损坏文件备份恢复和真实重复进程互斥。

托盘操作使用真实 ToolStripMenuItem.PerformClick。它验证实际事件处理，不等于在 Explorer 通知区域点击图标。高版本提示检查通知内容配置，不宣称操作系统一定展示了气泡。双屏/真实热插拔不在本夹具范围。

任一断言失败、进程异常或退出位置不一致，驱动退出码为 1；成功为 0。所有自己启动的实例通过正常菜单/WM_CLOSE 退出，不强杀未知进程。`latest-run.txt` 指向最新测试目录。原始 `probe.log`、stderr、存档及预期位置均保留，不自动递归清理。

`restart-position` 依赖同一轮先执行 `fast-final`；指定 `-Scenarios` 子集时须保留这个顺序。结果须注明执行者、版本和覆盖范围；修复作者运行本套脚本仍是作者自测，不能自行代签独立监理通过。

从 PowerShell 指定多个场景时，使用数组参数：

```powershell
& ./tests/Integration/Run-Lifecycle-UTF8.ps1 -Scenarios @('offscreen-start','paused-start','normal')
```

使用 `Out-File` 保存输出后，终端可以没有文字；应读取日志中的场景结果和真实退出码，不能凭终端是否显示文字判断成功或异常。探针记录的 `GetDpiForWindow` 才是本次宠物窗口的实际 DPI，不用宿主进程 DPI 或文件名代替。
