# P3-004 R3 独立复核证据

2026-11-12，HEAD a0f84f9 加未提交工作区。源码 SHA256 见 versions.json。

- 正式相关命令 `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-004-r3' --results-directory artifacts/p3-004-r3`，Core86+App86=172/172。
- 独立 Core 命令 `dotnet test artifacts/p3-004-r3/core-probe/Review.csproj --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-core.trx' --results-directory artifacts/p3-004-r3`，5/5。
- 最终 App 命令 `dotnet test artifacts/p3-004-r3/app-probe/Review.csproj --no-restore --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-app-corrected.trx' --results-directory artifacts/p3-004-r3`，6通过2失败。分别将对应源码及项目副本放回 Tests.cs 和 Review.csproj。两个种子测试使用 R2 的原始隔离种子，可替换为 R2 证据目录中的同名种子。
- 修正此前窄工作区反例，MonitorRectPx 第三参为宽度。左界1350、右界1920，应给宽570。增加 Show 对照，按实际 HWND DPI 1.5 缩放测试工作区，明确断言实际宽120、双方支撑。Show 两项均通过。未 Show 两项仍失败，日志记录 ActualWidth=0/0，配置宽 Width=120/120。
- 本轮未独立全量复跑、未做实际鼠标与性能采样。
