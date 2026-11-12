# P3-002 R5 独立证据（2026-11-10）

HEAD a0f84f9后未提交版本，哈希见versions.json，开发记录快照developer-response.md。未改产品/正式测试、未用真实存档、未做真实鼠标社交体验、未提交推送。

- `dotnet test artifacts/p3-002-r4/ownership/SocialProbe.csproj --filter 'FullyQualifiedName~Reviewer_SocialRespondFinish' --logger 'trx;LogFileName=original-borrow.trx' --results-directory artifacts/p3-002-r5`：当前源码构建，退出0，原回应接管归属反例1/1通过。
- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter 'FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFileName=social-r5.trx' --results-directory artifacts/p3-002-r5`：含构建，退出0，43/43通过，0失败0跳过。

原反例通过生产悬停入口使B于1200ms产生curious，1450ms社交Respond不接管、不重置独立期限，2000ms会话结束仍curious。新增正式4项另覆盖原期限到期/离开恢复idle与Reject/取消借用保护。开发全量757为自测，本轮不独立全量。
