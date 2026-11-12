# P3-004 R5 独立复核证据

2026-11-12，HEAD a0f84f9 加未提交工作区。源码摘要见 versions.json。

运行 `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~SocialCoordinatorTests|FullyQualifiedName~SocialIntegrationTests' --logger 'trx;LogFilePrefix=p3-004-r5' --results-directory artifacts/p3-004-r5`。Core 86 + App 88 = 174/174。正式反例 Chase_R4F01_CommitRefusalClearsBothSocialExpressions 通过。测试仅在 Core 输出追逐位移后、App 成对写入守卫前注入不可写状态。断言两只位置不变、共享会话结束、两只清除社交好奇、本窗口社交标志清除。对方本地标志清理由 CancelSocialImmediately 既有代码核对。独立悬停归属沿用该函数与现有回归。

未独立复跑全量、未执行 M-10 体验及采样。
