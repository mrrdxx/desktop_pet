# P3-001-S3 R12 独立证据

2026-11-10，HEAD a0f84f9加未提交工作区；版本见versions.json，开发原整改记录快照见developer-response.md。未修改产品/正式测试、未提交推送、未接触真实用户存档或正式宠物。真实鼠标双宠体验未执行。

- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter 'FullyQualifiedName~MultiPetIsolationTests|FullyQualifiedName~DiagnosticIsolationTests' --logger 'trx;LogFileName=r12.trx' --results-directory artifacts/p3-001-s3-r12`：含当前源码构建，退出0，41/41通过，0失败0跳过。开发全量693/693为开发自测，本轮未独立全量。
- `dotnet run --project artifacts/p3-001-s3-r12/natural-exit/NaturalExit.csproj -- C:/Users/mrrdxx/Desktop/pet/artifacts/p3-001-s3-r12/natural-data`：退出0。独立STA程序创建App并使用现有ProbeSetupIsolated阻断正式启动，TestSetupIsolatedSave只配置隔离数据；设置生产App.xaml的OnExplicitShutdown，调用Application.Run，通过Startup创建并显示两只真实生产窗口，Dispatcher排队调用原ShutdownMainWindow。成功路径无Environment.Exit/Process.Kill/手写退出副本。
- lifecycle.txt依次记录BeforeApplicationRun→Startup→ProductionShutdownRequested→ProductionShutdownCallReturned→OnExit:0→ApplicationRunReturned:0。生产函数同步返回是正常异步关闭过程的一部分，不能据此认为关机失败，也不能在返回后立刻Environment.Exit替代后续生命周期。
- 独立解析natural-exit-state.json：恰有bear-1(150,120,grievance61)、bear-2(350,220,grievance72)，父命令已返回，进程无残留。生产自然退出行为由此独立证实。
- 正式DiagnosticProbe仍在shutdownCompleted后Environment.Exit(0)，其父测试关于Dispatcher.Run的注释与实际不符；不采纳该用例为自然退出证明。此项不否定上述独立直接生产证据。
- 夹具静态核对：Interactive不注入探头；auto以Regex.Replace(count:1)限定启动一次；Restart复用既有suiteRoot/data，不写seed或重建。开发报告的交互启动和两次重启属开发自测；本轮未操作用户实机、不重复创建交互窗口或更改latest-run指针。R11已有独立BuildOnly/auto烟测继续按其版本保留。
