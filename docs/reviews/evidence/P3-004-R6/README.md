# P3-004 R6 M-10 证据

日期 2026-11-12（Asia/Shanghai），HEAD a0f84f9 加现有未提交工作区。监理获授权代测。R5 四份关键文件哈希在本轮开始时一致。真实用户 state/settings 文件前后 SHA256 一致，仅保留摘要。

## 运行与比较

- 在仓库根运行 `& .\tests\Integration\Run-PerfIsolated.ps1 -Configuration Release -TwoPets`，隔离目录 artifacts/perf-baseline/run-bb7ac89c。随后用 Prepare.ps1 仅在隔离源码打开 ShowInTaskbar，添加观察和正常退出接缝。首次编译的观察变量名冲突已修正。最终 Release 构建0警告0错误。
- 单、双宠共用同一 Release DLL（versions.json）。由相同隔离 v4 种子裁出单宠，首宠位置与状态一致。两者禁用预先触发的稀有事件种子，默认配置未变，试验双宠分别为主动和慢半拍。观察计时器每100ms读状态，仅在状态变化时写行，正常采集过程均使用相同观察方式。ShowInTaskbar 会改变任务栏/焦点条件。
- Run-Sample.ps1 给每轮独立档案，通过 dotnet 启动该 DLL，预热12秒，用 Collect-Perf.ps1 的 -TargetPid、-ExpectedDll 核对准确进程。空闲每轮 -Duration 300、动态 -Duration 120，交错顺序双→单→双→单和单→双→单→双。各轮测完由隔离 stop.request 调用生产 ShutdownMainWindow，采集退出0、目标退出0，两者均有记录。CSV、meta、汇总及日志按轮次保存。
- Windows 11 26200，i7-14650HX 16核心24逻辑核，WindowsDesktop .NET 10.0.15，主屏150%。所有窗口日志报告相同 DPI，位置使用 DIP。未控制环境中其他系统负载，未采集 FPS 或长时内存。
- 动态每轮实际用 computer-use 给蓝徽 bear-1 两次同形鼠标拖动（水平30 DIP、向上70 DIP，按下、拖动、松手），保存档案均为 bear-1 (230,828.67)。动作在各轮的相对时间不同，100ms观察器可能漏掉短暂 Dragging，操作由 UI 反馈和终态支持。无物理抛掷，无追逐；双宠第一轮自然寻找让第二只移至 x=350。默认追逐出现在两轮双宠空闲样本，分别2次和1次。
- CPU 单核百分比 = 100 × 首末 CPU 累计毫秒差 / 同一首末区间经过毫秒。总逻辑核归一值再除以24。内存、句柄和线程为有效样本算术平均。summary.csv 由 Summarize.ps1 生成。

## 实际观感、中断和重启

- 默认参数双宠空闲首轮约第31.25秒出现一次静止邀请，约2.08秒后进入同向追逐，约2.4秒后停下。首轮首个追逐结束坐标 (440,898.67) / (599.33,898.67)，间距159.33 DIP。默认两轮共3次，只描述该观察窗内频率。
- experience-controlled 为调试参数：禁普通注意、尝试间隔1秒、邀请与回应概率1、追逐冷却7秒，速度、边距、持续时长不变。40ms观察显示运动同向且领跑在前，所记录最小间距120 DIP。
- 对移动窗口尝试头部按压时工具拒绝了移动后失效的截图或窗口几何，未把这些尝试计成成功。Prepare-Control.ps1 另建隔离副本，增加静止的 Pause/Resume/Exit 控制窗。真实鼠标点击这些按钮分别走生产 OnPauseToggled 与 ShutdownMainWindow，不计入性能样本。
- experience-pause 日志中 51868ms 点击 Pause 时仍 Chasing，51873ms 双方已 paused、共享会话 Idle，坐标同前一帧 (724.67,898.67)/(844.67,898.67)，约21秒保持。Resume 后仍为同坐标且 Idle，随后因调试冷却已过另起新邀请。Exit 正常退出。复用同档以默认参数重启，两个 PetId 和位置 (964.00,898.67)/(1084.67,898.67) 一致，首次约35秒未续演旧追逐，再用 Exit 正常结束。before-restart-positions.json 保存重启前档案坐标。
