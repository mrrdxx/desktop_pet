# P4-002 R3 独立证据

工作区源码及 HEAD 见 `versions.json`。分别使用独立构建输出，未停止其他测试进程。

- 从 [R2 证据](../P4-002-R2/README.md)复制两份复核源码到 `artifacts/p4-002-r3/reviewer/` 并去掉 `.txt`。运行 `dotnet test artifacts/p4-002-r3/reviewer/Review.csproj --artifacts-path artifacts/p4-002-r3/build-independent --logger 'trx;LogFileName=reviewer-r2-replay.trx' --results-directory artifacts/p4-002-r3`，4/4 通过。
- 运行 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p4-002-r3/build-specialty --filter 'FullyQualifiedName~RestReminder' --logger 'trx;LogFilePrefix=p4-002-r3' --results-directory artifacts/p4-002-r3`，Core 22/22 + App 28/28 = 50/50。
- 与 R2 版本清单比较，App、窗口、探针和 App 测试源码均已变化。因此本次通过覆盖当前版本。开发报告中的旧 DLL 锁定工况、全量 980/980 均未独立复跑。未执行 M-12 实机。
