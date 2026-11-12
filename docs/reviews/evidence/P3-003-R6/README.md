# P3-003 R6 独立复核证据

2026-11-11，HEAD a0f84f9 加当前未提交工作区。源码 SHA256 见 versions.json。

- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --no-restore --filter 'FullyQualifiedName~Seek_' --logger 'trx;LogFileName=formal-seek.trx' --results-directory artifacts/p3-003-r6`，23/23通过。
- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --no-build --filter 'FullyQualifiedName~Seek_Restart_NewWindowRestoresPosition_NoOldSeek' --logger 'trx;LogFileName=formal-standalone.trx' --results-directory artifacts/p3-003-r6`，复用上一步构建，1/1通过。
- `dotnet test DesktopPet.slnx --no-restore --logger 'trx;LogFilePrefix=p3-003-r6-full' --results-directory artifacts/p3-003-r6`，Core402+App408共810/810通过，App段约46秒。
- 现场鼠标、真实桌面进程同档重启和多屏未在本轮执行。
