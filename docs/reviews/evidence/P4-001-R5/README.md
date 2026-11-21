# P4-001 R5 独立证据

Windows / .NET 10，审查对象见 `versions.json`。

- `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~ActivitySession|FullyQualifiedName~ReviewP4001' --logger 'trx;LogFilePrefix=p4-001-r5' --results-directory artifacts/p4-001-r5`，Core 28/28，App 36/36。
- 在 `artifacts/p4-001-r5/probe/` 用 R3 的独立项目文件与 R4 的原反例源码，执行 `dotnet test artifacts/p4-001-r5/probe/Review.csproj --logger 'trx;LogFileName=reviewer-r4-replay.trx' --results-directory artifacts/p4-001-r5`，1/1 通过。
- `dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --no-restore`，0 警告 0 错误。运行构建输出的 `DesktopPet.DiagnosticProbe.dll`，传参为绝对隔离目录、场景名（`activity-session` 或 `activity-session-m11`）、绝对结果文件路径。前者退出码 0，累计 2987.62ms，增删前后累计相同，清理后订阅已撤销、服务引用为空。后者在 2s 至 120s 各样本均为 1996ms / 0 次提醒，完成自动清理并退出。完整原始记录见 `m11-observations.jsonl`、`m11-cleanup.json`。
- 全量 931/931 为开发自测。未执行真实锁屏或睡眠。
