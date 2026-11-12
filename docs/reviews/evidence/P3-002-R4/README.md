# P3-002 R4 独立证据（2026-11-10）

当前HEAD a0f84f9后未提交版本。未改产品或正式测试、未用真实存档、未做实机社交观感、未提交推送。开发全量753为自测，本轮不独立全量。

- `dotnet test artifacts/p3-002-r3/ownership/SocialProbe.csproj --filter 'FullyQualifiedName~Reviewer_SocialReject' --logger 'trx;LogFileName=original-ownership.trx' --results-directory artifacts/p3-002-r4`：退出0，原拒绝归属反例1/1通过。
- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter 'FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFileName=social-r4.trx' --results-directory artifacts/p3-002-r4`：含构建，退出0，39/39通过。
- `dotnet test artifacts/p3-002-r4/ownership/SocialProbe.csproj --filter 'FullyQualifiedName~Reviewer_SocialRespondFinish' --logger 'trx;LogFileName=respond-ownership.trx' --results-directory artifacts/p3-002-r4`：含构建，退出1，1项回应接管后的结束归属反例失败。

反例用实际悬停入口：B鼠标保持(260,165)，tick0→1200ms触发curious（原有效期应到3200ms）；A1300ms发起，1450ms B回应；2000ms社交正常结束，B实际idle，仍有效的原悬停被清。没有手工伪造字段或动作状态。源码ownership-tests.cs.txt末尾Reviewer_SocialRespondFinish_MustRestoreStillValidHover；同副本也保留原Reject方法。
