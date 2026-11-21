# P4-002 R2 独立证据

- 专项：`dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~RestReminder' --logger 'trx;LogFilePrefix=p4-002-r2' --results-directory artifacts/p4-002-r2`，Core 22/22、App 22/22。
- 独立复跑：将 [R1 源码](../P4-002-R1/README.md)去掉 `.txt` 后置于 `artifacts/p4-002-r2/reviewer/`，运行 `dotnet test artifacts/p4-002-r2/reviewer/Review.csproj --logger 'trx;LogFileName=reviewer-r1-replay.trx' --results-directory artifacts/p4-002-r2`，3/3 通过。换成本目录的 `Review.cs.txt`，同命令追加 `--filter 'FullyQualifiedName~WorkerLockNotification'` 并改 TRX 文件名为 `reviewer-r2.trx`，1 项失败，调用栈证实 WPF 跨线程访问异常。
- 预览：`dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --no-restore`，0 警告 0 错误。构建所得 exe 运行 `rest-reminder` 场景，使用新的隔离目录。按 PID 枚举 Win32 顶层窗口并用 `IsWindowVisible` 确认一个可见窗口。日志记录 `stretch → rest → idle`，32 秒时表演标志为 false，提醒次数为 1。未执行鼠标交互、观感、真实锁屏或睡眠。
