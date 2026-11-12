# P3-004 R1 独立复核证据

2026-11-12，HEAD a0f84f9 加未提交工作区。源码 SHA256 见 versions.json。

- 正式相关命令 `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-004-r1' --results-directory artifacts/p3-004-r1`。Core 80 + App 86 = 166/166 通过。
- 隔离构建命令 `& .\tests\Integration\Run-ProbeLive.ps1 -Interactive -Chase -BuildOnly`。0 警告 0 错误，目录 artifacts/probe-live/run-d187b1e0，原始种子为 chase-seed.json。
- 反例命令分别为 `dotnet test artifacts/p3-004-r1/core-probe/Review.csproj --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-core.trx' --results-directory artifacts/p3-004-r1` 和对应 app-probe、reviewer-app.trx。Core 3 项与 App 3 项均失败。副本为完整正式测试源码加 Reviewer 方法。复现时按目录分别放回 Tests.cs 和 Review.csproj。App 的种子反例使用原始种子的临时副本，重放时可将文件指向本目录 chase-seed.json。
- Core 反例为逆序领跑、有效释放等待中被追逐抢占、追逐冷却阻止新释放。App 反例为仅 5 DIP 空间仍向边缘邀请、完整窗口越界、双宠种子读回一只。参见各 TRX。
- 未运行完整测试套件、实际桌面操作及单/双宠性能采集。
