# P3-001-S2 R8 独立复核证据

日期：2026-11-09（Asia/Shanghai）。HEAD a0f84f9 加未提交工作区；关键文件哈希见 versions.json，开发整改记录快照见 developer-response.md。

实际执行（两条 dotnet test 均包含当前源码构建，退出码均为 0）：

- `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --filter 'FullyQualifiedName~MultiPetIsolationTests' --logger 'trx;LogFileName=s2-r8.trx' --results-directory artifacts/p3-001-s2-r8`：21/21 通过，0 失败、0 跳过。
- `dotnet test artifacts/p3-001-s2-r8/probe/ReviewProbe.csproj --logger 'trx;LogFileName=reviewer-cleanup.trx' --results-directory artifacts/p3-001-s2-r8`：独立探针 1/1 通过，0 失败、0 跳过。

探针通过生产工厂创建两只未显示窗口，直接反射取得 DispatcherTimer 并确认初始运行；先解绑关闭回调、关闭 A 且保留其注册，再调用生产 TestCleanup。断言 A/B 计时器均停止、实际字典 Count=0，再次清理不抛异常。finally 显式关闭保留的窗口引用，避免依赖被验证清理方法完成自身善后。

生产测试新增的清理回归同时覆盖两个未显示窗口；两处抛掷核心验证均有必须激活的前置断言。R7 已接受的工厂/手势设计不重新审查。

开发报告全量 680/680 为开发自测，本轮未独立重跑全量。未操作真实存档或真实鼠标双窗，未修改产品/正式测试，未提交推送。
