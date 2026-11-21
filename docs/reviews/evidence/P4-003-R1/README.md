# P4-003 R1 独立复核证据

2026-11-16（Asia/Shanghai）。HEAD 和文件摘要见 versions.json。

- 专项使用独立输出执行 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p4-003-r1/build --filter 'FullyQualifiedName~NightSchedule|FullyQualifiedName~NightSleepy'`，Core 8/8、App 10/10。全量 1002/1002 为开发自测。
- 将 Review.cs.txt 和 Review.csproj.txt 去掉 .txt 后置于 `artifacts/p4-003-r1/reviewer/`，执行 `dotnet test artifacts/p4-003-r1/reviewer/Review.csproj --artifacts-path artifacts/p4-003-r1/independent`。生产共享鼠标入口接受头部命中点 (60,25)，后续窗口 tick 仍为 sleep。断言失败录于 reviewer.trx。
- 以 `dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-003-r1/preview-build` 构建，0 警告、0 错误。以生成的 exe 运行 `night-sleepy` 场景并传新隔离目录和结果文件。无鼠标输入。虚拟 23:29 待机、23:30 入睡、05:59 仍睡、06:00 回待机，结果记录为 0，清理记录完整。动作日志无 drag，sidecar 却把 dragInterruptSeen 和 sleepReentrySeen 都写成 true。
