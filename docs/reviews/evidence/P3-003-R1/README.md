# P3-003 R1 独立证据（2026-11-10）

HEAD a0f84f9叠加全部既有未提交工作区。未改应用/正式测试、未操作真实存档、未做实机、未提交推送。

- `dotnet test DesktopPet.slnx --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-003-r1' --results-directory artifacts/p3-003-r1`：含当前源码构建，退出0，Core37+App54=91/91通过，0跳过。开发全量784/784为自测，本轮不独立全量。
- `dotnet test artifacts/p3-003-r1/probe/SeekProbe.csproj --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-seek.trx' --results-directory artifacts/p3-003-r1`：构建成功，退出1，4/4反例断言失败，0跳过。使用生产工厂及正式测试现有可控配置/monitor辅助函数，新增4项Reviewer方法，末尾源码见reviewer-tests.cs.txt。

实际结果：无任何拖动释放事件，静置到5000ms仍开始Seeking；Seeking中的B Top由700变600失去支撑，下个tick Left从104继续108；A Left5000离屏仍IsOnBottomEdge=true；同一约33ms区间仅A两次tick位移8DIP、A/B交错四次tick位移16DIP。

静态证据：Seek_SaveDuringSeek_RestartDoesNotResumeOldSeek没有保存/加载文件或重新建窗口，只new SocialCoordinator并将SeekReactDelayMs设置99999；不能称为保存/重启链通过。新P3-003-response.md本轮尚不存在，开发已交付的文档信息来自STATUS/TASKS/参数表，未伪造开发整改快照。
