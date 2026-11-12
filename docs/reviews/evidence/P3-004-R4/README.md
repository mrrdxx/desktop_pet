# P3-004 R4 独立复核证据

2026-11-12，HEAD a0f84f9 加未提交工作区。源码 SHA256 见 versions.json。

- 正式命令 `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-004-r4' --results-directory artifacts/p3-004-r4`，Core86+App88=174/174。
- R3 Core/App 副本更新基底后分别运行 `dotnet test artifacts/p3-004-r4/core-probe/Review.csproj --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-core.trx' --results-directory artifacts/p3-004-r4`，App 替换目录与日志名。Core5/5，App8/8。App 种子读取原R2夹具路径，亦可替换成R2证据中的原种子。
- 兜底反例仅在隔离 App 副本增加测试标志，于协调器已产生位移意图后、写入守卫前模拟一方不可写。现有 ForceCancel 下，坐标与共享会话断言通过，窗口仍拥有社交好奇，1项失败。随后仅在该隔离副本将此处改为 CancelSocialImmediately，对照1/1通过。测试不代表该分支已在实机自然触发。
- 兜底命令 `dotnet test artifacts/p3-004-r4/guard-probe/Review.csproj --filter 'FullyQualifiedName~Reviewer_CommitRefusal' --logger 'trx;LogFileName=reviewer-commit.trx' --results-directory artifacts/p3-004-r4`。对照同命令，改用 reviewer-commit-comparison.trx。guard-comparison.patch 包含测试注入及对照的一行变更。复制正式 App 源码到 app-guard-copy 后应用差异，将项目引用及资源指向原仓库。原反例保留 ForceCancel 一行。
- 本轮未独立全量复跑、未做实际鼠标与性能采样。
