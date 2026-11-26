# P6-002 聚合审查

## R1：接收（2026-11-27，Asia/Shanghai）

**结论：P6-002 接收，可标记 done。本轮未发现阻断项，不新增 F 编号。** P6-001 R11 接收不变；本结论不表示已实现预测或展示，不自动启动 P6-003/004。

### 对象与方法

- 本监理对当前工作区进行代码与运行复核，开发自测数字不作为本轮独立运行结果。Git HEAD 为 `0aa994dd371ac7e804ca863836b71eeadf877788`；产品实现与多阶段历史修改尚未提交，HEAD 本身不包含全部验收对象。相关五份源码/测试的 SHA-256 见 [source-hashes.json](evidence/P6-002-R1/source-hashes.json)。
- 核对 PRODUCT R-09、ARCHITECTURE §7、TASKS 当前任务、REVIEW_PROCESS 及 [开发交付记录](../remediation/P6-002-response.md)，检查 Collector、Archive、Service、专项测试及实际工作区。
- 用独立 artifacts 目录重新编译并运行现有统计专项；另写监理隔离验证器，调用实际 Core/Service、真实独立文件目录，注入时间与采样数据。未改产品/正式测试、未使用真实统计目录、未操作屏幕/锁屏/睡眠。

### 本轮实际运行

- Core `UsageStatsCollectorTests`：**39/39**，失败/跳过 0；[TRX](evidence/P6-002-R1/core-usage.trx)。
- App `UsageStatsServiceTests`：**21/21**，失败/跳过 0；[TRX](evidence/P6-002-R1/app-usage.trx)。
- 监理补充验证：**6/6**、退出 0；[结果](evidence/P6-002-R1/checks-final-results.json)、[源码](evidence/P6-002-R1/checks/Program.cs)。包含：精确半小时边界不生成空桶及连续跨午夜只计一次；固定种子 6000 次采样逐桶比对预期时长/会话数；重复 drain→重启新增活动计新会话→清除再重启；锁定文件造成写失败后重试不叠加；v1 文件加载/新活动保存 v2；原始第 7/8 个统计日与聚合第 30/31 个统计日的保留边界（关闭状态）。
- 首次验证器 5/6：文件锁场景在只有基线样本、尚未产生 `stats.json` 时尝试打开文件，属于监理夹具前置错误。修正为先提交一个区间建立文件，再加锁制造替换失败；最终在新目录复跑 6/6。保留[首次结果](evidence/P6-002-R1/checks-first-results.json)，不作为产品缺陷。
- 本轮没有重跑无关全量；Core 602/App 631 及 DiagnosticProbe 构建 0/0 为开发侧已报告自测，不冒称本轮复跑。

可复现命令（仓库根目录 PowerShell；验证器输出目录必须全新，避免恢复上次数据）：

```powershell
dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj --artifacts-path artifacts/p6002-core-r1 --filter "FullyQualifiedName~UsageStatsCollectorTests" --logger "trx;LogFileName=core-usage.trx" --results-directory docs/reviews/evidence/P6-002-R1
dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj --artifacts-path artifacts/p6002-app-r1 --filter "FullyQualifiedName~UsageStatsServiceTests" --logger "trx;LogFileName=app-usage.trx" --results-directory docs/reviews/evidence/P6-002-R1
$checkOutput = Join-Path '.temp' ('p6-002-review-' + [guid]::NewGuid().ToString('N'))
dotnet run --project docs/reviews/evidence/P6-002-R1/checks/Review.csproj --artifacts-path artifacts/p6002-checks-r1 -- $checkOutput
```

复跑需另选结果目录/文件名，不覆盖本轮留存证据。

### 接收依据与边界

1. 每个已确认区间同时进入原始段与聚合桶；本地零点对齐的 30 分钟切分与午夜切分保持总时长。会话由连续已确认同应用区间定义，中断或应用切换结束；只在起始桶计次，不把采样数当频次。
2. 原始 7 天与聚合 30 天按各自被统计日期独立清理；整份已提交快照覆盖写，重复 drain、失败重试与重启均未重复叠加。恢复不重建运行中基线/会话，新活动计新会话。
3. 全局开关、默认关闭、清除代次与 P6-001 写失败隔离保持；清除包含聚合桶，隔离重启未复生。聚合仅入独立 `stats.json`，未扩展隐私采集字段或宠物存档格式。
4. **v1 兼容边界**：缺失 `buckets` 的旧文件保留原始记录、聚合为空；只聚合升级后的新确认区间，首次新保存升级到 v2。无法从旧原始段可靠还原会话边界，不补造历史频次。此行为已在架构/任务和开发记录明确，本轮实测符合，不承诺补齐过去 30 天聚合。
5. 未进行真实跨月等待、时区切换/夏令时专项或预测准确度验证；当前固定桶、双保留验收以受控时间和真实文件往返覆盖即可，不新增“必须实际运行一个月”的完成门槛。P6-001 已接收的菜单、前台、锁屏/睡眠证据不机械重跑。

下一步：开发 AI 只需在独立整改记录追加本 R1 接收引用，无需重测或重复实现。P6-003 保持 todo，另按用户安排展开样本门槛与预测范围；不提交/推送。
