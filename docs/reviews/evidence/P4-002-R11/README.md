# P4-002 R11 Windows 生命周期实机证据

2026-11-16（Asia/Shanghai）。按 R10 版本清单核对四份源码哈希一致。构建标准 DiagnosticProbe 及隔离观察副本，均为 0 警告、0 错误。副本仅增加真实 SystemEvents 及 Dispatcher 处理后的状态日志，70 秒自动退出时限沿用原探针。构建命令为 `dotnet build artifacts/p4-002-r11/harness/DesktopPet.DiagnosticProbe.csproj --artifacts-path artifacts/p4-002-r11/instrumented`。

- 两轮各启动一个带新隔离目录的 `rest-reminder` 场景。在日志确认累计时间约 2 秒、表演为 rest 且活跃后，分别调用 `LockWorkStation()` 和 `SetSuspendState(false,false,false)`。
- 第一次锁屏后，SessionLock 状态行立即为 idle、active=false。50.8 秒收到 SessionUnlock 后仍为 idle、active=false，计时服务解除挂起。接下来约 16 秒直到第二轮系统操作前，提醒仍未重播。
- 第二轮在休息姿态时请求睡眠。Windows 先发 SessionLock，表演随即取消，再发 Suspend。电源日志确认 S3 睡眠和唤醒。Resume 时仍锁屏，提醒保持 idle、active=false、累计触发 1 次。该轮探针在收到 Resume 后约 3 秒自动退出，未记录该轮解锁。单独 Suspend 取消活跃表演的对照沿用 R4 后台通知用例，本次实机覆盖锁屏与睡眠重叠。
- 两轮触发次数均为 1。清理记录显示订阅已解除、服务引用为空。标准错误为空，检查时两个隔离探针均已退出。
