# P4-002 R7 新素材隔离运行

R6 清单中的 HEAD 及文件哈希均匹配。执行 `dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-002-m12-r7/build`，0 警告、0 错误。运行新构建的探针 exe，参数为新隔离目录、`rest-reminder`、隔离结果文件。0 秒 stretch、2 秒 rest、30 秒 idle、70 秒自动清理，进程退出码 0，提醒计数始终为 1。

Windows Computer Use 的应用清单仍未提供可选的小熊窗口，本轮没有取得运行画面或执行鼠标输入、真实锁屏或睡眠。自然收尾以日志为证，观感和输入仍待现场补测。

## 手动补测

从仓库根目录在 PowerShell 运行已构建的实例，每次使用新目录。小熊一出现就开始表演。

```powershell
$probe = (Resolve-Path 'artifacts/p4-002-m12-r7/build/bin/DesktopPet.DiagnosticProbe/debug/DesktopPet.DiagnosticProbe.exe').Path
$run = Join-Path (Get-Location) ('artifacts/p4-002-m12-' + [guid]::NewGuid().ToString('N'))
& $probe $run rest-reminder (Join-Path $run 'result.json')
```

1. 第一遍看开场哈欠、安静姿态及脚底是否稳定。留到自然结束。
2. 第二遍在表演期间点透明边角，应穿透且表演继续；点头部实体，应立即结束表演。重新启动再试身体拖动，应取消表演并正常移动。
3. 重新启动，在表演期间右键 Pause，再右键 Resume，确认不续播。窗口关闭/移除及真实锁屏、睡眠另作生命周期场景。
