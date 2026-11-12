# P3-002 R1 独立证据（2026-11-10）

HEAD a0f84f9后未提交P3-001/P3-002工作区。关键SHA256见versions.json，开发回应快照见developer-response.md。未改产品或正式测试、未用真实用户存档、未做真实鼠标/美术体验、未提交推送。

- `dotnet test DesktopPet.slnx --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-002-r1' --results-directory artifacts/p3-002-r1`：含当前源码构建，退出0，Core社交21/21+App社交15/15=36/36通过，0失败0跳过。开发全量729为开发自测，本轮未独立全量。
- `dotnet test artifacts/p3-002-r1/probe/SocialProbe.csproj --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-final.trx' --results-directory artifacts/p3-002-r1`：构建成功，退出1，6/6反例断言失败，0跳过。独立副本复用正式测试隔离工厂/可控配置辅助函数，新增Reviewer方法；源码与TRX在本目录。

实际反例：

1. 会话激活，发起方连续tick后CurrentAction仍idle，应为约定的可见curious。
2. 全局Pause后不手工tick已停止的timer，coordinator仍active。
3. 全局Recover完成后coordinator仍active。
4. 头部按下已返回成功、IsPettingCandidate成立，但coordinator仍active，直到后续tick才取消。
5. 运行单调时间100000ms时移除对方并AddPet，100001ms即重开社交；ForceCancel(PetRemoved,0)使幸存者冷却基准错误。
6. A启动共享社交后，给B共享RuntimeTracker恢复150分钟并注入今日合法未消费计划（delay20、roll0、baseline0），B下个生产tick实际Consumed=true，违反社交期间稀有事件不消费约定。

原正式36通过与独立反例6失败是不同覆盖，不混写为全量失败。未把单宠自动探针称为双宠社交实机证据。
