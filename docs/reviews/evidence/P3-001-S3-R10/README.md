# P3-001-S3 R10 独立证据

日期：2026-11-10（Asia/Shanghai）；HEAD a0f84f9加未提交工作区。版本见versions.json，开发整改快照见developer-response.md。未修改应用/正式测试、未访问真实存档、未做真实鼠标体验、未提交推送。

- `dotnet test DesktopPet.slnx --logger 'trx;LogFilePrefix=r10' --results-directory artifacts/p3-001-s3-r10`：含当前源码构建，退出0；Core351/351、App341/341，共692/692，0失败0跳过，详两份r10_net10.0*.trx。App中MultiPetIsolationTests为33项。开发所称全量341只对应App数量。
- `dotnet test artifacts/p3-001-s3-r10/probe/ReviewProbe.csproj --logger 'trx;LogFileName=reviewer-r10.trx' --results-directory artifacts/p3-001-s3-r10`：含当前源码构建，退出0，3/3通过。重跑R9原三条断言；找回反例增加直接取得生产SaveCoordinator并Flush，再经ArchiveService加载文件核对最终Left，未调用FlushFinalState重新捕获，确认该原缺陷修复。
- 静态读取Run-ProbeLive.ps1：启动复制夹具要求生产源码包含`_mainWindow.Show();`，当前App源码Contains为false；按脚本会报“Production startup seam changed; inspect fixture first.”。本轮仅核对接缝，未执行夹具、未启动或替换用户宠物。

限制：正式全量中的F08退出测试调用另写的ShutdownMainWindowForTest，并不是生产退出函数；无真实正常退出进程证据。恢复测试新建窗口未Show/EnsureHandle，也未检查新窗口坐标/情绪，未触发SourceInitialized恢复位置；暂停测试没有注入推进时间或启动运动和tick。上述为测试覆盖审查，不把它们描述为已复现生产运行失败。
