# P3-003 R5 独立复核证据

HEAD a0f84f9 加当前未提交工作区，2026-11-11。源码 SHA256 见 versions.json。

- 正式项目重构建运行 `dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --no-restore --filter 'FullyQualifiedName~Seek_'`，22通过1失败。单跑 `FullyQualifiedName~Seek_Restart_NewWindowRestoresPosition_NoOldSeek` 同样失败。均为档案 Left=500，Show 后实际 Left=100。
- 观察副本仅增加 SourceInitialized、LocationChanged 与 Show 返回后坐标记录。失败时只记录 After Show A: 100,100。
- 对照副本在关闭旧窗口前记录并切换 App.ShutdownMode，原值 OnLastWindowClose。设置 OnExplicitShutdown 后，事件记录 SourceInitialized observer: pos=500,700 archive=500 monitors=1，Show 返回后 500,700，完整测试1/1通过。
- 两个副本使用同一 SeekProbe.csproj。复现时分别把 observer-original.txt 或 observer-explicit-shutdown.cs.txt 复制为 SocialIntegrationTests.cs 放在项目目录，每次只编译其中一个。运行 `dotnet test <副本项目路径> --filter 'FullyQualifiedName~Seek_Restart_NewWindowRestoresPosition_NoOldSeek'`。
- 正式 App.xaml 已配置 OnExplicitShutdown。本轮只修改监理副本，未测试真实桌面进程重启。
