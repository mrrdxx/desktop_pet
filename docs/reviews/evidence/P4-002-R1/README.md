# P4-002 R1 复核证据

- 专项：`dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~RestReminder' --logger 'trx;LogFilePrefix=p4-002-r1' --results-directory artifacts/p4-002-r1`，Core 22/22，App 13/13。
- 独立反例：将本目录两份源码去掉 `.txt` 后放入 `artifacts/p4-002-r1/reviewer/`，运行 `dotnet test artifacts/p4-002-r1/reviewer/Review.csproj --logger 'trx;LogFileName=reviewer.trx' --results-directory artifacts/p4-002-r1`，3 项失败。
- 预览：`dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --no-restore`，0 警告 0 错误。构建所得 exe 以新隔离目录、`rest-reminder` 场景和结果文件路径启动，5 秒时进程仍运行、主窗口句柄为 0，日志已有 stretch 和 rest。监理结束探针，未检查自然结束、真实输入或观感。
