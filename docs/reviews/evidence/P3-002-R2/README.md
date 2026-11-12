# P3-002 R2 独立证据（2026-11-10）

HEAD a0f84f9后未提交工作区。未改产品/正式测试、未用真实存档、未做真实鼠标社交体验，未提交推送。开发全量740/740为自测，本轮不独立全量。

1. `dotnet test DesktopPet.slnx --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-002-r2' --results-directory artifacts/p3-002-r2`：含构建，退出0，Core21+App26=47/47通过，0失败0跳过。
2. `dotnet test artifacts/p3-002-r1/probe/SocialProbe.csproj --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-r2.trx' --results-directory artifacts/p3-002-r2`：含构建，退出1，原反例5通过+1失败，0跳过。用同一R1反例源码重新引用当前生产源码构建，源码副本在reviewer-tests.cs.txt。

仍失败Reviewer_SocialNotice_MustActuallyDisplayCurious：coordinator活跃，连续两次生产tick后CurrentAction实际idle，应为约定curious。其他原Pause/Recover/头部按压即时取消、长运行移除再添加冷却、共享社交阻止B消费稀有计划5例通过。

静态核对ApplySocialExpression：Notice/Respond仅_curiosity.Start，Reject/ReturnToIdle仅_curiosity.Cancel，未调用_animPlayer.PlayAction/UpdateFrame。新增正式F01用例仅检查IsCuriosityActive等内部标志，与真正显示动作不是同一覆盖。
