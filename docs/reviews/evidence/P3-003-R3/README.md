# P3-003 R3 独立复核证据

日期2026-11-11，HEAD a0f84f9加未提交工作区，源码SHA256见versions.json。

正式专项命令 `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~Seek_' --logger 'trx;LogFilePrefix=p3-003-r3' --results-directory artifacts/p3-003-r3`，Core30+App20=50/50通过。

原候选反例使用R2副本，命令 `dotnet test artifacts/p3-003-r3/probe/SeekProbe.csproj --filter 'FullyQualifiedName~Reviewer_UnreachableReleaseMustExpire' --logger 'trx;LogFileName=reviewer-expiry.trx' --results-directory artifacts/p3-003-r3`，1/1通过。副本源码存为reviewer-tests.cs.txt，复现时改扩展名.cs并与项目文件同目录。

静态复核定位
- MainWindow.xaml.cs约2745，TestSimulateDragRelease直接设置_isDragging和Left/Top，再EndDrag。
- SocialIntegrationTests.cs约3396，Seek_Restart_NewWindowRestoresPosition_NoOldSeek未Show，约3562直接赋值newWA/newWB.Left/Top。保存前四个坐标变量未用于保存后相等断言。
- 现有ReleaseSamplingIntegrationTests使用BeginCapturedPointerInteraction/ProcessCapturedPointerMove/EndCapturedPointerInteraction。
- 现有MultiPetIsolationTests约1595使用Show触发生产位置恢复，可供开发复用。

本轮范围为候选失效与原验收补证复核。开发807/807为自测，本轮运行50专项及1原反例。实机与真实多屏尚待后续体验。
