# P4-003 R5 首次 Windows 睡眠复测

2026-11-17（Asia/Shanghai）。R4 清单中的 HEAD、源码和资源哈希均匹配。隔离副本以共享注入时钟观察双宠。05:59 时两只都进入 sleep，随后请求 S3 睡眠。Windows 记录 02:07:14 请求和休眠转换、08:32:16 恢复，间隔约 6 小时 25 分钟。

首次探针的退出时限按含系统睡眠的 Stopwatch 计算。唤醒后先运行了退出计时器，真实 Resume 虽写入事件日志，其 UI 回调尚未把虚拟时钟推进到 06:00。探针记录 exitCode 1，两只仍在 05:59。该轮没有验证到应用跨时段行为。

隔离副本已修订，Suspend 后按 Dispatcher 计时器实际回调次数留出等待窗口，确保长睡眠恢复后仍可处理 Resume。命令 `dotnet build artifacts/p4-003-r5/harness/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-003-r5/build-retry`，0 警告、0 错误。正式代码未改动。
