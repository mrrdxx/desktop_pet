# P4-001 R6 独立复核证据

- 专项：`dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~ActivitySession|FullyQualifiedName~ReviewP4001' --logger 'trx;LogFilePrefix=p4-001-r6' --results-directory artifacts/p4-001-r6`，Core 28/28，App 36/36。全量 931/931 为开发自测。
- Release 探针构建 0 警告、0 错误。独立启动使用同一 Release exe、独立隔离目录及相同场景参数。累计在 6.1 秒达到 6008ms。确认启动和周期日志后，监理结束该探针进程，未在这次短运行检查自动清理或完整阈值过程。
- 从开发报告指明的临时目录保存原始 JSONL 和清理文件。开发使用系统鼠标输入注入，生产采样在 38.1～152.1 秒保持 34998ms，154.1 秒恢复输入时累计仍为 34998ms，156.1 秒增 1996ms。180 秒产生唯一提醒意图，240 秒仍为 1 次。未执行真实锁屏、睡眠。

## M-11 实机运行命令

在仓库根目录用 PowerShell：

```powershell
dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'M-11 probe build failed' }
$probeExe = (Resolve-Path 'tests/DesktopPet.DiagnosticProbe/bin/Release/net10.0-windows/DesktopPet.DiagnosticProbe.exe').Path
$probeDir = Join-Path $env:TEMP ('DesktopPet_M11_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Write-Host "Log: $probeDir\activity-session-m11\m11-observations.jsonl"
& $probeExe $probeDir activity-session-m11 (Join-Path $probeDir 'm11-result.json')
```

探针约 240 秒自动退出。先移动鼠标或按键确认累计增长，记录锁屏/解锁和睡眠/唤醒大致时刻。每次恢复后保持短暂输入以确认累计继续。锁屏期间观察 `isSuspended=true` 与累计冻结。睡眠期间进程可能停止输出，以睡眠前、唤醒后相邻记录核对间隔、累计无跳增、提醒次数未补发、唤醒后可继续累计。若系统太快进入睡眠，记录是否捕获到挂起状态，并保留原始日志。
