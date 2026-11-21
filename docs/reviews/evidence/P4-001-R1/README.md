# P4-001 R1 独立证据

日期 2026-11-12。HEAD f59e7e2 加当前未提交工作区，源码 SHA256 见 versions.json。

- `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~ActivitySession' --logger 'trx;LogFilePrefix=p4-001-r1' --results-directory artifacts/p4-001-r1`，Core21+App13=34/34。
- `dotnet test artifacts/p4-001-r1/probe/Review.csproj --logger 'trx;LogFileName=reviewer.trx' --results-directory artifacts/p4-001-r1`，5项独立反例均失败。复现时将两个副本放回同目录，移除 .txt 后缀。真实 PowerModes 枚举值通过当前运行时测试取证，不代表本轮操作了系统睡眠。
- 无真实系统锁屏、睡眠或隔离实机操作，未复跑开发全量。
