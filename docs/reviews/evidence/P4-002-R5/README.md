# P4-002 R5 验收证据

- 在仓库根执行 `dotnet build tests/DesktopPet.DiagnosticProbe/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-002-m12/build`，0 警告、0 错误。以构建产物 `artifacts/p4-002-m12/build/bin/DesktopPet.DiagnosticProbe/debug/DesktopPet.DiagnosticProbe.exe` 运行 `rest-reminder` 和独立数据目录。
- 探针实机日志在 0 秒为 stretch，2 秒为 rest，30 秒为 idle。提醒计数为 1。约 70 秒自动启动清理并退出，进程退出码 0。两份原始输出随附。
- Windows Computer Use 的窗口清单没有返回隔离小熊窗口，未获取运行画面，也未发送鼠标输入。按钮、拖动、穿透、右键暂停/恢复、关闭/移除和真实锁屏/睡眠尚未在本轮验证。
- 独立按原尺寸查看运行包内 PNG。stretch_0 脸下边缘可见一条横贯脸宽的黑线，stretch_1/2 的变化集中在嘴和耳部，四肢无清楚的伸展动作。rest_0 可辨认闭眼和睡眠符号。以 alpha ≥128 量测，idle、三个 stretch 帧及 rest 的最底行均为 y=110，资源契约的 Y 偏移均为 0。这是素材级检查。
