# P4-001 R3 独立证据

审查版本为 `versions.json` 中的 HEAD 加所列未提交文件。Windows，使用本机 .NET 10。

- 专项：`dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~ActivitySession|FullyQualifiedName~ReviewP4001' --logger 'trx;LogFilePrefix=p4-001-r3' --results-directory artifacts/p4-001-r3`。Core 28/28，App 32/32，含原 8 项反例和 activity-session 子进程。
- 独立反例：把本目录两份源码去掉 `.txt` 后复制到 `artifacts/p4-001-r3/probe/`。执行 `dotnet test artifacts/p4-001-r3/probe/Review.csproj --logger 'trx;LogFileName=reviewer-r3.trx' --results-directory artifacts/p4-001-r3`。两项复现失败，分别观察到 2000ms 和 0ms，预期均为 1000ms。
- 全量：`dotnet test DesktopPet.slnx --no-build --logger 'trx;LogFilePrefix=p4-001-r3-full' --results-directory artifacts/p4-001-r3`。Core 465/465，App 462/462，共 927/927。
- 本轮未执行真实桌面锁屏或睡眠。
