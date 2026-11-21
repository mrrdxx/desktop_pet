# P4-001 R4 独立证据

审查版本为 `versions.json` 中的 HEAD 加所列未提交文件。本机 Windows / .NET 10。

- 专项：`dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~ActivitySession|FullyQualifiedName~ReviewP4001' --logger 'trx;LogFilePrefix=p4-001-r4' --results-directory artifacts/p4-001-r4`，Core 28/28、App 34/34，含开发侧保存的旧 10 项反例和隔离探针用例。
- 独立复跑：将 [R3 原始项目和源码](../P4-001-R3/)去掉 `.txt` 复制至 `artifacts/p4-001-r4/probe/`，`dotnet test artifacts/p4-001-r4/probe/Review.csproj --logger 'trx;LogFileName=reviewer-r3-replay.trx' --results-directory artifacts/p4-001-r4`，原两项 2/2 通过。
- 新反例：将本目录 `ReviewR4.cs.txt` 保存为同一项目下的 `ReviewR4.cs`，`dotnet test artifacts/p4-001-r4/probe/Review.csproj --no-restore --filter 'FullyQualifiedName~ReviewP4001R4' --logger 'trx;LogFileName=reviewer-r4.trx' --results-directory artifacts/p4-001-r4`，累计预期 61000ms，实际 62000ms。
- 探针：`dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --no-restore`，0 警告 0 错误。随后运行 `dotnet tests/DesktopPet.DiagnosticProbe/bin/Debug/net10.0-windows/DesktopPet.DiagnosticProbe.dll artifacts/p4-001-r4/manual-probe activity-session artifacts/p4-001-r4/manual-probe/result.json`。退出码 0，退出前 sidecar 累计 999.636ms，结果文件未生成。执行前曾尝试运行既有 bin，旧二进制不识别该场景，重建后成功。
- 全量 929/929 为开发自测，本轮仅独立跑专项和定点反例。未执行真实锁屏或睡眠。
