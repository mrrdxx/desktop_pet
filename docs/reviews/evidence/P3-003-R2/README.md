# P3-003 R2 独立复核证据（2026-11-10）

HEAD a0f84f9，加当前未提交工作区；源码摘要见 versions.json。

- 正式相关：`dotnet test DesktopPet.slnx --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-003-r2' --results-directory artifacts/p3-003-r2`，Core47+App59=106/106通过。
- 独立副本：`dotnet test artifacts/p3-003-r2/probe/SeekProbe.csproj --filter 'FullyQualifiedName~Reviewer_' --logger 'trx;LogFileName=reviewer-seek.trx' --results-directory artifacts/p3-003-r2`，4通过1失败。
- 原反例的正例前置适配新释放机制：采用当前 InjectSeekSocial 的释放候选；无释放反例显式 simulateRelease:false。原R1证据未改。
- 新反例 Reviewer_UnreachableReleaseMustExpire_NotSeekOneMinuteLater：释放目标悬空时不寻找；60秒后仅位置变为可达、无新释放，仍进入Seeking（失败）。证明候选无失效，不是证明真实鼠标或物理链。
- 独立项目全源码存为 reviewer-tests.cs.txt，复现时复制为 .cs 与 csproj 放在同目录；项目引用当前绝对生产路径。未修改正式测试/应用源文件。
- 首次生成副本因换行匹配失败导致编译失败，修正副本提取后上述测试正常运行；未用此工具错误作产品缺陷。
- 本轮未全量重跑、未实机交互、未操作真实存档、未提交推送。
