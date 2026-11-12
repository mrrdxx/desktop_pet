# P3-002 R3 独立证据（2026-11-10）

HEAD a0f84f9后未提交版本。未改产品或正式测试、未用真实用户存档、未做真实鼠标社交体验，未提交推送。开发全量747为自测，本轮未独立全量。

- `dotnet test artifacts/p3-002-r1/probe/SocialProbe.csproj --filter 'FullyQualifiedName~Reviewer_SocialNotice_MustActuallyDisplayCurious' --logger 'trx;LogFileName=original-idle.trx' --results-directory artifacts/p3-002-r3`：当前源码构建，退出0，原idle反例1/1通过，CurrentAction已curious。原源码副本original-reviewer-tests.cs.txt。
- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter 'FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFileName=social-r3.trx' --results-directory artifacts/p3-002-r3`：含构建，退出0，33/33通过，0跳过。
- `dotnet test artifacts/p3-002-r3/ownership/SocialProbe.csproj --filter 'FullyQualifiedName~Reviewer_SocialReject' --logger 'trx;LogFileName=ownership.trx' --results-directory artifacts/p3-002-r3`：含构建，退出1，1项表情归属反例失败，0跳过。源码ownership-tests.cs.txt，末尾Reviewer方法。

反例通过工厂创建两窗；先关闭社交触发概率，B注入鼠标(260,165)、生产tick0→1200ms触发正常悬停curious（断言已成立）；再开启社交概率，A在1300ms发起，1450ms B拒绝。B自身悬停表情本应仍在2秒生命周期内，实际被ApplySocialExpression(Reject)清为idle，断言失败。未用反射手工伪造表情状态。
