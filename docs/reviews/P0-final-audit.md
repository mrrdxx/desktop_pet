# 阶段 0 收尾后的全面核查

日期：2026-10-22（Asia/Shanghai）。执行者：项目监理。
用户范围：全面核查已收尾项目，核查后再讨论阶段 1；本轮不开始 P1-001，不改应用实现。

## 结论

**阶段 0 验收继续有效，未发现须重新打开 P0-001～P0-005 的已证实运行缺陷。** 本轮发现一个旧测试入口故障及若干文档偏差，已把当前运行文档指向实测可用的入口，并完成纯文档修正。
构建 0 警告/错误、正式单元测试 55/55、当前隔离十场景全部通过。用户要求的“等待启动阶段 1”仍有效，不能因为本报告通过就自动写 P1 应用代码。

本报告是阶段收尾一致性与回归核查，不冒充对本助手既往编写修复的新一轮独立签字；P0-004 仍沿用其他指定审查者的 R1，P0-005 沿用已生效的 R2，并补充本轮实际证据。

## 覆盖及版本

- 检查 AGENTS、README、产品、架构、决策、任务、状态、验证、新对话接续及审查流程；对照 P0-002～P0-005 最后有效复核、最新整改及验收汇总。
- 阅读当前生产 C#、XAML、项目/SDK/manifest、五份单元测试源码及集成探针/两个驱动，检查启动/退出、暂停/菜单/拖动、DPI/恢复、损坏/高版本保护与保存链。
- 检查文档本地链接、历史快照、代码摘要和长期证据落点；仓库无 Git，本轮不初始化或提交。
- [源码版本清单](evidence/P0-final-audit-source-sha256.txt) 覆盖 12 个生产 C# 文件、5 个单元测试文件及集成探针。本轮未修改这些源码，也未修改任何 P0 历史报告的原结论。
- App.xaml.cs、MainWindow.xaml.cs 与 P0-005 最终验收摘要一致。项目仍为两个生产项目、两个测试项目；没有角色 PNG/JSON 加载器、完整行为循环或阶段 1 应用实现。

## 本轮实际验证

1. `dotnet build DesktopPet.slnx -c Debug --no-restore -m:1 --nologo`：退出码 0，0 警告/错误；[原始输出](evidence/P0-final-audit-build.txt)。
2. `dotnet test DesktopPet.slnx -c Debug --no-build --no-restore -m:1 --nologo`：沙箱外退出码 0，App 33/33、Core 22/22，0 失败/跳过；[原始输出](evidence/P0-final-audit-tests.txt)。沙箱首次运行 testhost 连接 90 秒超时，随后对自己启动的测试命令发出取消，不据此判应用失败；[失败尝试](evidence/P0-final-audit-tests-sandbox.txt) 保留。
3. Windows PowerShell 5.1 实际解析两个集成入口；旧入口 5 个语法错误、新入口 0；[解析结果](evidence/P0-final-audit-script-parse.txt)。
4. `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Integration/Run-Lifecycle-UTF8.ps1`：退出码 0，十场景 ALL ISOLATED LIFECYCLE SCENARIOS PASS；[完整输出](evidence/P0-final-audit-lifecycle.txt)。本次单屏实际 HWND DPI=144，测试目录 `artifacts/p0-004-fix/run-df89ce04/`。
5. 十场景覆盖 normal、paused-start、offscreen-start、fast-final、recover-after-save、archive-cases、hold、restart-position、readonly-state、readonly-settings。正常实例退出码均为 0，重复启动的第二实例按预期为 1；原真实存档摘要前后相同，没有强杀用户进程。
6. 当前单屏 100%/150% 覆盖沿用 [P0-005 R2](P0-005-review.md) 的真实 HWND DPI 日志；本轮没有重新改变系统缩放、重测 Shell 实际点击或性能五分钟。Shell/动画用户六步反馈及性能校正仍按 R1 来源归属。

## 发现与处置（均不新增阶段阻断）

### N-01：旧集成入口不可执行；当前文档入口已修正

`tests/Integration/README.md` 原来指向 `Run-Lifecycle.ps1`；它在 Windows PowerShell 5.1 因编码导致字符串/语法解析错误，不能复跑。新版 `Run-Lifecycle-UTF8.ps1` 的完整十场景已实测通过。
运行文档现指定新版，附多个场景的正确 PowerShell 数组调用和输出重定向说明。旧文件保留历史，不作为支持入口；本轮未重写驱动或覆盖旧证据。将来若要删除/合并旧驱动，作为单独维护改动验证即可，无需先修改当前已通过的应用。

### N-02：当前规格与未来约定混写；已明确边界

- PRODUCT/ARCHITECTURE 不再笼统标“尚未实现/拟采用”，而是说明已交付与后续约定并存，状态仍以 TASKS 为准。
- ARCHITECTURE 的工程结构反映已有 App.Tests/Platform/Persistence；测试区分 net10.0 与 net10.0-windows。
- 相对工作区位置/旧 DPI 是后续存档目标，v1 实际仍为显示器标识与 DIP 左上角；未增加字段或改变存档格式。
- 共享更新循环、Core 运动唯一写入者是阶段 1 起约定；阶段 0 的窗口直接拖动和 800ms 两帧计时器不被包装为完整行为框架。
- animationSpeed 已有 DTO/JSON 往返，但当前 App 未接到实际调速或设置界面，现明确记录。autoStart 仍为预留，不注册启动项。
- D-001 增补已采用及阶段 0 验收出处，保留起步理由与调整条件，没有重选技术栈。

### N-03：性能证据归属与动画描述偏差；已更正

- 原性能采样没有实测宠物 HWND DPI。后来的 DPI=144 启动实例不能倒填成性能采样当时的读数；汇总现保留“该次实际窗口 DPI 未记录”。这不否定已测 CPU/内存结果。
- 当前占位帧定时器是 800ms，原汇总“约 500ms”已更正；非零 CPU 间隔不作为动画证明，视觉依据仍为用户反馈。
- [原始性能数据留存](evidence/P0-final-audit-performance.txt) 已从被忽略的 artifacts 复制进审查证据目录。原日志错误汇总不被覆盖；采用 R1 校正的全段 315.469 秒、CPU 约 0.27% 单核、工作集均值 122.6MB。
- Release 未测，去掉“预期更低”的保证性推断；短时内存增长不解释成长期稳定或泄漏。

### N-04：历史快照链接失效；已修正

四份从 STATUS 迁入 history 的快照仍使用原 docs 目录的相对路径，共 39 处链接失效。本轮仅重定位链接并加归档说明，保留历史正文和当时结论。当前入口链路及修正后的全部本地 Markdown 链接重新检查。

## 测试解释与后续风险边界

- 55 项通过只证明已覆盖路径。`Clamp_WindowLargerThanWorkArea_ClampsToMinimum` 的实际输入为 120×140 窗口、200×200 工作区，窗口并不比工作区大；该测试名不能作为“超大窗口”覆盖证据。120×140 的测试夹具本身可验证非正方形，不应称为与当前 120×120 占位窗口相同。
- `ClampToWorkArea_NaNInput_ThrowsOrProducesValidOutput` 实际断言当前 NaN 会透传，是现状记录而非 Core 已拒绝非法数值。App 启动对非有限存档坐标已有防护；后续接入新的数值来源应保持调用边界验证。上述名称/注释偏差不证明当前单屏正常路径失败，也不据此重开已验收问题。
- 磁盘满、存档路径无权限/持续占用、异常断电、长期内存趋势、真实混合 DPI 多屏/热插拔、会话锁屏/休眠均未在本轮故障注入或实机验证。当前保存正常路径通过，不扩展为所有 IO 异常都能无损恢复的承诺。
- 无 Git 意味着目前主要依赖文件摘要和已有备份追溯，摘要不能替代完整版本历史；本轮不自动创建仓库、提交或推送。
- KL-001（Alt+Tab 拖动切走仍跟随）继续是用户接受的失败例外，不改写成通过。正式美术、安装包、自启仍未实现。

## 交接

本轮只修改文档、历史链接及新增审计证据；生产/测试源码与旧报告保留。阶段 0 done 状态保持，P1-001 仍 todo。
**等待用户启动阶段 1。** 可以先讨论需求与资源方案，但不能从“全面核查完成”推断已有 P1 实现授权。后续若修改代码，按影响范围验证，无需把本次核查变成重复全量审批。
