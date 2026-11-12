# P3-004 R2 独立复核证据

2026-11-12，HEAD a0f84f9 加未提交工作区。源码 SHA256 见 versions.json。

- 正式相关命令 `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-004-r2' --results-directory artifacts/p3-004-r2`，Core80+App86，共166/166通过。
- 独立命令 `dotnet test artifacts/p3-004-r2/core-probe/Review.csproj --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-core.trx' --results-directory artifacts/p3-004-r2`。App 同样替换为 app-probe 和 reviewer-app.trx。Core4通过1失败，App4通过2失败。复现时将对应副本放回目录并命名 Tests.cs 和 Review.csproj，种子路径可指向本目录原始种子。
- 原6个反例都通过。额外5项中，2.5秒间隔只消费100ms及随后的33ms无补偿、性能种子生产读档通过。拒绝中断冷却、完整窗口邀请余量和选定方向边缘断言失败。
- 独立构建 `& .\tests\Integration\Run-ProbeLive.ps1 -Interactive -Chase -BuildOnly`，目录 artifacts/probe-live/run-957ceee6。独立构建 `& .\tests\Integration\Run-PerfIsolated.ps1 -TwoPets -Configuration Release`，目录 artifacts/perf-baseline/run-89eb4932。两套种子原样留存。
- 分别对上述两份隔离副本执行 Observe-Startup.ps1，参数 -Suite 为相应运行目录，-Configuration 分别 Debug 和 Release。脚本为一次性观察，重放须新生成隔离副本。只在生产 Show 后记录实际位置与支撑，随后排队正常退出。两个进程均退出码0，两个实际窗口均可见且处于 DISPLAY1 同一底边，Top=898.67 DIP。未交互、未采样性能。
