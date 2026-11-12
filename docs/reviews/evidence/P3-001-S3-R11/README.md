# P3-001-S3 R11 独立证据

日期2026-11-10（Asia/Shanghai）；HEAD a0f84f9后未提交工作区，关键SHA256见versions.json。未改应用/正式测试、未提交推送。真实鼠标和完整双宠体验未进行。

## 实际执行

1. `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter 'FullyQualifiedName~MultiPetIsolationTests|FullyQualifiedName~DiagnosticIsolationTests' --logger 'trx;LogFileName=r11.trx' --results-directory artifacts/p3-001-s3-r11`：含当前源码构建，退出0，41/41通过，0跳过。开发全量693为开发自测，本轮未独立全量。
2. `& 'tests/Integration/Run-ProbeLive.ps1' -BuildOnly`：退出0，0警告0错误；构建副本artifacts/probe-live/run-530b4337。该副本App.xaml.cs第491及639行均注入RunProbeLiveAsync，分别为启动和AddPet，证明全局文本替换范围超过启动一次。
3. `dotnet test artifacts/p3-001-s3-r11/strict/ReviewProbe.csproj --filter 'FullyQualifiedName~F08_GlobalPause_DualPet_NoAccumulation|FullyQualifiedName~ReviewerBoundaryTests' --logger 'trx;LogFileName=strict-r11-verified-final.trx' --results-directory artifacts/p3-001-s3-r11`：退出0，2/2通过。边界探针在当前单屏150%下四角添加及全局找回，检查两只完整矩形与不完全重叠。独立App显式设置生产XAML的OnExplicitShutdown，避免测试未载XAML时默认OnLastWindowClose污染重复创建；不把早期夹具试跑失败判成产品失败。暂停加强副本将OR改为AND，并在恢复后推进20ms（超过1000/60ms固定步）断言实际移动；早期10ms未移动符合固定步机制，不判产品缺陷。最终源码和TRX均已存。
4. `& 'tests/Integration/Run-ProbeLive.ps1'`：自动隔离启动/稀有探头/保存/托盘PerformClick退出，退出0，ALL PROBE LIVE CHECKS PASS。套件artifacts/probe-live/run-05809828，完整日志fixture-auto.log。Windows单屏2560×1600、工作区2560×1528、150%；驱动只读快照对比确认真实存档不变，未写真实存档、未替换/终止正式宠物。此自动单宠探头不是双宠真实鼠标验收，也不是重启同一存档验证。
5. 独立直接启动artifacts/prep001-probe/DesktopPet.DiagnosticProbe.exe，参数为artifacts/p3-001-s3-r11/exit-check、dual-pet-exit、该目录result.json；使用ProcessStartInfo隐藏窗口，WaitForExit(15000)，实际自然返回到父进程时ExitCode=0，未由父进程Kill。但子进程结果exit-result.json明确记录“ShutdownMainWindow returned without process exit (unexpected).”，随后源码执行Environment.Exit(0)。所以这是子进程自调用强制退出，不能据父进程未Kill认定WPF自然退出。exit-state.json确认双宠位置150/120、350/220及身份已保存。

## 剩余静态证据

- Run-ProbeLive.ps1每次生成新suiteId/data并写v3 seed；未提供同一隔离data重启入口。
- 全局Replace(w.Show())也注入AddPet，每次添加会重跑旧稀有探头检查，interactive模式仍先执行该自动流程，预置plan已consumed时会出现与双宠体验无关失败。
- 全局暂停的正式OR断言允许一个轴超大或实际不移动；独立加强版本已通过，可固化正式断言，不要求重新实现暂停。
