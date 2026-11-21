# P4-003 R2 独立复核

- 独立输出执行 `dotnet test DesktopPet.slnx --artifacts-path artifacts/p4-003-r2/build --filter 'FullyQualifiedName~NightSchedule|FullyQualifiedName~NightSleepy'`，Core 8/8、App 13/13。另将 R1 的原反例项目与源码复制到 `artifacts/p4-003-r2/reviewer/` 并重新编译，1/1 通过。
- 使用 `dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-003-r2/preview-build`，零警告、零错误。以构建输出的探针 exe、独立目录和 `night-sleepy-noinput` 启动，退出码 0。入睡、清晨回待机为 true，三个拖动相关标志为 false，退出清理完成。
- 开发拖动原始日志显示第 10 秒为 drag 且 isDragging=true，第 14 秒回 sleep 且 isDragging=false，当时仍为虚拟 23:33。变量按一秒采样，人工验收应看状态序列。全量 1005/1005 为开发自测。
- 本轮独立运行覆盖无输入对照。真实鼠标操作、两只小熊同时播放的观感、系统唤醒跨时段仍待 M-13。复核版本见 versions.json。
