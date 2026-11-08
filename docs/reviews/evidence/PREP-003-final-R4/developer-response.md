# PREP-003 监理 R1/R2 整改记录

## 监理报告

- 报告路径：`docs/reviews/PREP-003-review.md`
- 审查轮次：R1（2026-11-08）、R2（2026-11-08）
- 阻断项：F-01（中）—— 缺少四轮采样与目标进程的可追溯绑定
- R2 结论：F-01 部分通过，新入口已接受，历史归属尚未补齐，新入口需一次收尾

## R1 整改（已完成，R2 部分接受）

### 脚本整改（R1 → R2 已接受）

文件：`tests/Integration/Collect-Perf.ps1`

R1 整改内容：
1. 显式 PID 入口（`-TargetPid` 参数）
2. CSV 包含 pid 列
3. 元数据保存（`_meta.json`：PID/进程名/启动时间/命令行/逻辑核数/计时器类型）
4. Stopwatch 单调时钟
5. 进程退出/采样错误记录到 CSV

### R1 PID 归属补证（R2 未接受，已更正）

R1 整改记录曾用"CPU 累计递增 + 终端 PID + 存档累计时间"三项间接证据主张四轮归属 PID=65656。

**R2 监理判定**：CPU 累计递增只与同一进程解释相容，不证明进程相同；不同进程也可能分别拥有递增的累计 CPU。隔离存档运行 31 分钟证明副本运行过，不能证明 Get-Process 选中了它。`Run-PerfIsolated.ps1` 只输出 DLL/环境，不启动进程、不打印运行 PID，因此"启动脚本打印 PID"来源不实。

**更正**：
- "空闲 R1 终端明确输出 PID=65656（启动时 Run-PerfIsolated.ps1 打印）"→ 更正为：`Run-PerfIsolated.ps1` 不打印运行进程 PID；旧脚本 `Collect-Perf.ps1` 在采集时打印 `Collecting perf data for PID=65656`
- "四轮数据均可追溯至 PID=65656，无需重采"→ 更正为：仅 dynamic-r1 有原始终端输出可恢复（显示 PID=65656）；其他三轮原始终端输出无法恢复，CSV 不含 PID 字段，归属无法确认
- CPU 累计递增保留为相容性线索，不再作为归属证明

## R2 整改：F-01 收尾

### 1. 原始终端 PID 材料恢复结果

尝试恢复四轮采集时的原始终端输出：

| 轮次 | 原始终端输出 | PID 可恢复 |
|------|------------|-----------|
| idle-r1 | 无法恢复（终端历史不可回溯） | 否 |
| idle-r2 | 无法恢复 | 否 |
| dynamic-r1 | `terminal_last_command` 获取，显示 `Collecting perf data for PID=65656` | 是 |
| dynamic-r2 | 无法恢复 | 否 |

检查范围：
- `terminal_last_command`：仅能获取最后一条命令（dynamic-r1）
- `docs/reviews/evidence/PREP-003-R1/`：有 CSV、summary、脚本快照，无原始终端输出文本
- `.codefree/tooloutput/`：177 个文件，无包含 `65656` 的文件
- VS Code workspace storage：未找到终端历史文件
- summary 文件：不含 PID 字段

**诚实结论**：仅 dynamic-r1 有原始终端 PID 材料可恢复。idle-r1、idle-r2、dynamic-r2 三轮无法确认归属，需用修正入口补采。旧四轮数据保留为历史参考。

### 2. 脚本收尾（R2 要求三项）

文件：`tests/Integration/Collect-Perf.ps1`

**收尾 1：采集前核对预期目标 DLL + 采集中身份变更检测**
- 新增 `-ExpectedDll` 参数，采集前比对进程命令行是否包含预期 DLL 路径
- 不匹配时输出警告，退出码 2（IDENTITY_MISMATCH），元数据记录 `dllMatched: false`
- 采集中检测进程 `StartTime` 变化（PID 复用），身份变更时停止采集并记录 `IDENTITY_CHANGED`

**收尾 2：终点样本 + 首末有效区间统计**
- 循环结束后，若进程仍存活，补采一个 `ENDPOINT` 样本
- 摘要区分"首末有效区间"（首末样本 elapsed 差）和"脚本总耗时"（Stopwatch 总时长）
- 不再用脚本总耗时冒充采样窗口

**收尾 3：退出码区分 + 结构化错误行**
- 退出码：0=COMPLETED, 1=PROCESS_NOT_FOUND, 2=IDENTITY_MISMATCH, 3=PROCESS_EXITED_EARLY, 4=INSUFFICIENT_SAMPLES
- CSV 新增 `status` 列（OK/ENDPOINT/PROCESS_EXITED/IDENTITY_CHANGED/SAMPLE_ERROR），替代在 CSV 中写入原始异常消息
- 元数据记录 `exitCode` 和 `exitCodeMeaning`
- 提前退出时仍生成摘要（标注进程退出），但退出码非 0

**兼容编码**：文件写入改用 UTF8 BOM（`[System.Text.UTF8Encoding]::new($true)`），兼容 Windows PowerShell 5.1 中文显示

### 3. 短时正反例验证

验证目录：`artifacts/perf-baseline/verify/`

**正例**（`verify-positive`）：用当前 PowerShell 进程 PID 采集 3 秒
- 4 个样本（3 个 OK + 1 个 ENDPOINT），终点样本已捕获
- 首末有效区间 3.094s（首 0.019s → 末 3.113s），接近请求 3 秒
- 退出码 0（COMPLETED），中文摘要无乱码

**反例 1**（`verify-bad-pid`）：不存在的 PID=999999
- 退出码 1（PROCESS_NOT_FOUND）

**反例 2**（`verify-early-exit`）：短时进程采集 6 秒，进程 ~2 秒后退出
- 2 个 OK 样本 + 1 个 PROCESS_EXITED 行
- 退出码 3（PROCESS_EXITED_EARLY），不再 exit 0
- 摘要标注"进程退出: True"

**反例 3**（`verify-wrong-dll`）：有效 PID 但命令行不含 `DesktopPet.dll`
- 输出身份核对警告，显示实际命令行
- 退出码 2（IDENTITY_MISMATCH），元数据 `dllMatched: false`

### 4. 报告余项修正（R2 要求）

文件：`docs/history/PREP-003-perf-baseline.md`

| 位置 | 原表述 | 修正后 |
|------|--------|--------|
| 动态 R1 经过时间 | "120 秒（≥2 分钟）" | "CSV elapsed 舍入 120 秒，原时间戳窗口约 119.94 秒（约2分钟）" |
| 动态 R2 经过时间 | "120 秒（≥2 分钟）" | "CSV elapsed 舍入 120 秒，原时间戳窗口约 119.97 秒（约2分钟）" |
| 空闲两轮对比 - 工作集 | "首轮含启动后 GC 稳定/第二轮已稳定" | "可能解释包括首轮启动后内存分配尚未稳定、GC 回收时机差异等，但无 GC 日志或内存诊断观测支持具体归因" |
| 动态两轮对比 - 工作集 | "R1 稳定/R2 可能含 WPF 渲染缓冲/GC 波动" | "可能解释包括 WPF 渲染缓冲分配、GC 回收时机差异等，但无 GC 日志或渲染诊断观测支持具体归因" |
| 退出结果 - 存档初值 | "初始 156.62 + 运行约 25 分钟" | "预置初值 150.0 + 运行约 31 分钟"（按 Run-PerfIsolated.ps1 第 54 行实际预置值） |
| 环境 - DPI 来源 | "通过 .NET Graphics.DpiX 确认" | "据执行方环境报告（.NET Graphics.DpiX），非目标窗口 HWND 实测" |
| 环境 - 进程身份 | 仅写 PID=65656 | 补充 PID 归属材料缺证说明：仅 dynamic-r1 有原始终端输出可恢复，其他三轮无法确认 |
| 限制段 - R2 工作集 | "可能含 WPF 渲染缓冲/GC 波动" | "可能解释包括 WPF 渲染缓冲/GC 波动，但无诊断观测支持具体归因" |

## R3 整改：限定修正 + 补采

### R3 限定修正三项

**修正 1：终点失败不标 COMPLETED**
- 终点采样前进程消失：写入 PROCESS_EXITED 行，设 exitCode=3，endpointFailed=true
- 终点采样前身份变更：写入 IDENTITY_CHANGED 行，设 exitCode=2，endpointFailed=true
- 终点采样异常：写入 SAMPLE_ERROR 行，sampleErrors++，endpointFailed=true
- 循环内 sampleErrors > 0：exitCode=4（INSUFFICIENT_SAMPLES）
- 退出码判定逻辑：只有 endpointCaptured=true + 无身份变更 + 无进程退出 + sampleErrors=0 + sampleCount>=2 才 exitCode=0
- 终点采样也核对 StartTime（PID 复用检测）

**修正 2：异常行统一 9 列字段**
- PROCESS_EXITED/IDENTITY_CHANGED/SAMPLE_ERROR 行从 `,,,,,` (5 空逗号, 8 字段) 改为 `,,,,,,` (6 空逗号, 9 字段)
- status 列正确落在第 9 列，不再写入 thread_count 位置
- 用 Import-Csv 验证所有行 9 列结构正确

**修正 3：dynamic-r1 原始输出未留存**
- 搜索 .codefree/tooloutput/ 180 个文件，无包含 65656 的文件
- 搜索 docs/ 和 artifacts/perf-baseline/，仅整改记录转述，无原始终端导出
- 撤回"dynamic-r1 恢复成功"声明，将 dynamic-r1 也列入补采

**修正 4：脚本源文件 BOM**
- 脚本首字节从 `# P` 改为 UTF8 BOM (0xEF 0xBB 0xBF)

### R3 短时正反例验证

验证目录：`artifacts/perf-baseline/verify/`

| 场景 | 描述 | 退出码 | CSV 验证 |
|------|------|--------|---------|
| 正例 | 当前进程 3 秒采集 | 0 (COMPLETED) | 4 行 (OK×3+ENDPOINT×1)，9 列，status 正确 |
| 反例1 | 不存在 PID=999999 | 1 (PROCESS_NOT_FOUND) | — |
| 反例2 | 短时进程 6 秒采集，进程 ~2 秒退出 | 3 (PROCESS_EXITED_EARLY) | 3 行 (OK×2+PROCESS_EXITED×1)，9 列，status 正确 |
| 反例3 | 有效 PID 但命令行不含 DesktopPet.dll | 2 (IDENTITY_MISMATCH) | 3 行 (OK×2+ENDPOINT×1)，9 列，dllMatched=false |
| 反例4 | 短时进程 4 秒采集，进程 ~3 秒退出 | 3 (PROCESS_EXITED_EARLY) | 4 行 (OK×3+PROCESS_EXITED×1)，endpointCaptured=false |

所有 CSV 用 Import-Csv 验证 9 列结构正确，status 列正确填充。

### R3 补采结果

四轮补采于 2026-11-08 21:52-22:11 完成，使用 R3 修正版脚本：

| 轮次 | PID | DLL 匹配 | 采样数 | 首末区间 | 退出码 | 终点样本 |
|------|-----|---------|--------|---------|--------|---------|
| idle-r1 | 69800 | 是 | 295 | 300.554s | 0 | 是 |
| idle-r2 | 88644 | 是 | 297 | 300.090s | 0 | 是 |
| dynamic-r1 | 88644 | 是 | 120 | 120.704s | 0 | 是 |
| dynamic-r2 | 88644 | 是 | 120 | 120.608s | 0 | 是 |

- idle-r1 使用第一个隔离实例（PID=69800），该实例在采集结束后意外退出（-NoNewWindow 启动方式）
- idle-r2/dynamic-r1/dynamic-r2 使用第二个隔离实例（PID=88644），用户通过托盘菜单正常退出
- 每轮 CSV 含 pid 列，_meta.json 含 dllMatched=true/endpointCaptured=true/exitCode=0
- 隔离存档退出后 accumulatedRuntimeMinutes=196.66（预置 150.0 + 运行约 46.66 分钟）
- 旧四轮数据保留为历史参考，不作为基线依据

## 未修改项

- 产品代码/行为未修改
- 旧 CSV 数据未改写（保留为历史参考）
- 未提交/推送

## 自测

- R3 短时正反例：5 个场景全部通过（正例 exit 0+终点、反例1 exit 1、反例2 exit 3+PROCESS_EXITED、反例3 exit 2+身份警告、反例4 exit 3+终点失败）
- CSV 字段结构：所有行 9 列，status 列正确填充，Import-Csv 验证通过
- 四轮补采：全部 exit 0 (COMPLETED)，DLL 匹配，终点样本捕获，首末区间满足 M-08 要求
- 报告更新：新四轮数据替换旧数据，旧数据保留为历史参考段

## 最终复核请求

请监理最终复核：
1. R3 限定修正三项是否达标（终点失败不标 COMPLETED + 异常行 9 列 + dynamic-r1 撤回补采）
2. R3 短时正反例是否充分（5 个场景覆盖完成/不存在/提前退出/身份不匹配/终点失败）
3. 四轮补采数据是否满足 M-08（空闲≥5min×2 + 动态≥2min×2，PID+DLL 绑定，终点样本，exit 0）
4. 基线报告是否完整准确（新数据+旧数据历史参考+限制说明）
