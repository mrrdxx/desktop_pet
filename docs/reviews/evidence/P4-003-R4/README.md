# P4-003 R4 睡眠素材及双宠画布复核

- 两张 PNG 均为 120×120。ARGB 比较得两张相差 48 像素，全部位于 Z 区域，y=103～110 与 idle_0 的差异为 0。与 R3 清单比对，接线源码、测试源码和 character.json 的哈希一致。两张睡眠图均已去掉旧瞳孔和高光。
- 将 R3 隔离副本加上 WPF 画布采集，在每个窗口的 PetImage 实际切到对应素材时各采一张，调整虚拟时钟时程以便短跑。执行 `dotnet build artifacts/p4-003-r4/harness/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-003-r4/build`，0 警告、0 错误。以构建所得 exe 运行 `night-sleepy`，退出码 0。
- 四份 PNG 均为运行中的 WPF 画布输出。两只小熊在浅睡时闭眼，深睡时增加三枚 Z，身份徽记可辨。日志记录双宠跨午夜及清晨回待机，退出清理通过。
- 本轮未触发 Windows 睡眠或唤醒。沿用 R3 真实鼠标证据。
