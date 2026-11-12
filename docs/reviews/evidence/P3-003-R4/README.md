# P3-003 R4 独立复核证据

2026-11-11，HEAD a0f84f9 加当前未提交工作区。源码 SHA256 见 versions.json。

- 正式相关测试：`dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~Seek_|FullyQualifiedName~MultiPetIsolationTests|FullyQualifiedName~ReleaseSamplingIntegrationTests' --logger 'trx;LogFilePrefix=p3-003-r4' --results-directory artifacts/p3-003-r4`，Core30+App88=118/118通过。
- 独立副本：`dotnet test artifacts/p3-003-r4/probe/SeekProbe.csproj --filter 'FullyQualifiedName~RealCaptureChain|FullyQualifiedName~Seek_Restart_NewWindowRestoresPosition' --logger 'trx;LogFileName=reviewer-chain.trx' --results-directory artifacts/p3-003-r4`。手势三项全部通过，恢复一项失败。
- 副本仅改变两组独立验证条件：三个真实手势用生产默认 20000ms 寻找冷却及超时；恢复测试删除 Show 后两次直接调用 RestoreWindowPosition。后者首次坐标断言预期 500、实际 100，证明当前夹具中的 Show 路径无法独立恢复该坐标。尚未定位原因或外推正式启动。
- 副本源码存为 reviewer-tests.cs.txt，复现时改扩展名为 .cs，与 SeekProbe.csproj 同目录。工作区正式代码及正式测试保持原样。
