# R1 隔离反例复现

本目录仅是监理验证器，不属于产品或正式测试程序集。OutputType为Exe，AssemblyName沿用已有测试friend访问；不创建窗口、不启动采样timer，假前台/输入和时钟，数据仅写显式独立目录。

从仓库根目录执行：

    dotnet build docs/reviews/evidence/P6-001-R1/counterexamples/Review.csproj --artifacts-path artifacts/p6001-counter-replay
    artifacts/p6001-counter-replay/bin/Review/debug/DesktopPet.App.Tests.exe .temp/p6-review-replay-unique

用新的隔离目录避免旧夹具数据干扰。结果JSON每项pass才是预期比较结果，进程0仅表示完成执行。原始13项全部false；不得称为官方专项失败。官方Core17/App13通过的TRX另存于上级目录。验证器不会自动删除生成数据，原件保留供审阅。
