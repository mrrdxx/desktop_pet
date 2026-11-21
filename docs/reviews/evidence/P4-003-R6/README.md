# P4-003 R6 Windows 唤醒复测

2026-11-17（Asia/Shanghai）。使用 R5 修订的隔离探针，构建命令为 `dotnet build artifacts/p4-003-r5/harness/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-003-r5/build-retry`，0 警告、0 错误。HEAD 与 R5 记录中的源码、资源哈希均匹配。隔离探针提供双宠共用的虚拟本地钟，Windows 系统时间照常运行。

13:22:42 请求 S3 睡眠，Windows 电源日志确认睡眠及约 8 秒后唤醒。探针收到 Suspend 时，两只小熊在虚拟 05:59 均为 sleep。真实 Resume 到达后，隔离副本仅把注入时间推进至 06:00。推进当下两只仍为 sleep，约 125ms 后的下一条定期采样已都为 idle，随后保持待机直到退出。未在此次探针存活期间记录到解锁通知。

探针退出码 0，标准错误为空。清理记录确认服务和订阅已释放，采样记录中的首只位置保持 (100,100)。
