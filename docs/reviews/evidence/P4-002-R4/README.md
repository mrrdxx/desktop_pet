# P4-002 R4 独立复核证据

Windows，2026-11-13（Asia/Shanghai）。审查版本见 `versions.json`。

`dotnet test DesktopPet.slnx --artifacts-path artifacts/p4-002-r4/build --filter 'FullyQualifiedName~RestReminder' --logger 'trx;LogFilePrefix=p4-002-r4' --results-directory artifacts/p4-002-r4`。使用独立构建输出，Core 22/22 + App 29/29。App TRX 包含后台锁屏、睡眠和通知排队期间关闭及新建窗口的用例。全量 981/981 为开发自测。

## M-12 隔离预览

从仓库根目录在 PowerShell 运行。每次重复运行使用新隔离目录。

```powershell
dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj
if ($LASTEXITCODE -ne 0) { throw 'Preview build failed' }
$probe = (Resolve-Path 'tests/DesktopPet.DiagnosticProbe/bin/Debug/net10.0-windows/DesktopPet.DiagnosticProbe.exe').Path
$runDir = Join-Path (Get-Location) ('artifacts/p4-002-m12-' + [guid]::NewGuid().ToString('N'))
& $probe $runDir rest-reminder (Join-Path $runDir 'result.json')
Get-Content (Join-Path $runDir 'rest-reminder/rest-reminder-observations.jsonl')
```

小熊显示后立即开始表演，约 70 秒自动退出。先观察开场辨识度、脚点及自然收尾。另开预览分别验证透明区穿透、按压或拖动跳过、右键 Pause/Resume 后不续播。会实际改变桌面状态的锁屏或睡眠另行安排。本轮未启动预览或执行实机操作。
