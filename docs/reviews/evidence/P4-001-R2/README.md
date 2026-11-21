# P4-001 R2 独立复核证据

- 2026-11-12，HEAD f59e7e2 加未提交整改，源码摘要见 versions.json。
- `dotnet test DesktopPet.slnx --no-restore --filter 'FullyQualifiedName~ActivitySession|FullyQualifiedName~ReviewP4001' --logger 'trx;LogFilePrefix=p4-001-r2' --results-directory artifacts/p4-001-r2`，Core28+App28=56/56，含已纳入正式测试的原5反例。
- `dotnet test artifacts/p4-001-r2/probe/Review.csproj --logger 'trx;LogFileName=reviewer-r2.trx' --results-directory artifacts/p4-001-r2`，3项追加反例失败。复现时去除副本文件的 .txt 后缀并按项目引用路径放回目录。
- 第三项以同一个 Windows tick 表示输入年龄0，使用可控采样器调用现有生产定时器回调，观察两次有效样本被判未来。未操作真实系统睡眠或锁屏，未独立全量复跑。
