# P0-003 交付审查

日期：2026-10-21（Asia/Shanghai）。审查者：用户指定的监理助手。
首次结论：退回整改，F-01～F-04 阻断 P0-003 完成及依赖它的 P0-004 启动。**当前以文末 R4 为准：F-01～F-04 全部关闭，P0-003 验收通过，解除 P0-004 的依赖阻断**。历史问题与中间轮次保留，不回写成首次即通过。
开发整改入口：`docs/remediation/P0-003-response.md`（由开发方建立并维护）。

## 对象、方法与限制

- 用户转交的“P0-003 已完成”报告、当前 STATUS/TASKS、ARCHITECTURE §4、TESTING M-03，以及 App/Core/测试代码。
- 实际执行：读取代码、搜索调用点、MSBuild 属性/Item 求值、只读提取现有 EXE 的内嵌 manifest；原始结果见 [清单证据](evidence/P0-003-manifest-check.txt)。
- 本轮未重跑构建/17 项测试，未启动应用、未操作用户桌面。100% 用户通过是开发方转述；既往 150% 回归仅适用于 [修复交接](../handoffs/P0-003-recover-fix.md) 所记录的版本（当时 PMv2=false），不能作为 PMv2 生效后的新证据。
- 监理此前是 MonitorInterop DLL 修复作者。本轮核对其改动被保留，不把这部分作者自测重新包装为独立复核；本报告主要检查开发方新增配置及本任务其余交付。
- 无 Git。源码 SHA256：
  - `src/DesktopPet.App/DesktopPet.App.csproj`：`FD0F91C41A730B2A5437998FB9BBF5E4ACEE0B42C1F4B6D1D709EFE5F0E896AA`
  - `src/DesktopPet.App/app.manifest`：`EE5F88357E6D758F180978828F577F443BA09506D1F7CF5647572CA63E936174`
  - `src/DesktopPet.App/MainWindow.xaml.cs`：`6B71FCDBA0BC918EF96697EBDD6F743560F52F5606BD486B5EDA1E6FBCA0CF44`
  - `src/DesktopPet.App/Platform/MonitorInterop.cs`：`1F07FE81D2392676607C5FF37270BA81E8157E55307611124635DEA5C12954D3`
  - `src/DesktopPet.Core/Geometry/MonitorLocator.cs`：`820F0168279961161325287A9F7D30EFB773C442B207D6883DEEACC898582D1C`
  - `tests/DesktopPet.Core.Tests/MonitorLocatorTests.cs`：`232B07E762CDD66B8071909F7AC95746FA1783F4849D35D252CEA757C79BCC9E`

## F-01：PMv2 声明未进入生成程序（P1，阻断）

- 位置：`src/DesktopPet.App/DesktopPet.App.csproj:17`，`app.manifest`。
- 配置把 `ApplicationManifest` 写成 ItemGroup 内的 Item。实际 MSBuild 的同名 Property 为空；现有 EXE 资源只有默认 UAC 清单，没有 PMv2 声明。
- 影响：文件存在不代表运行模式已切换，“PMv2 声明未引入回归”的现有验收无法证明预期模式。依据：[ApplicationManifest 项目属性](https://learn.microsoft.com/en-us/visualstudio/msbuild/common-msbuild-project-properties)。
- 整改目标：使用构建真正消费的 ApplicationManifest 属性，核对生成 EXE 的清单，并验证实际 HWND 的 DPI awareness context。不能只检查 XML 文件或编译成功。
- 同时复核启用后的 DPI 查询边界：当前仍在 UI 线程使用 `GetDpiForMonitor`；[微软文档](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor) 明确提示每显示器感知线程不要使用此 API。针对窗口及显示器元数据选择合适方案，不机械地把 HWND API 当作接受 HMONITOR 的替换。
- 验收：提供有效 Property、最终 EXE 清单及实际窗口 PMv2=true 证据；在真正启用后的版本补做必要的 100%/150% 拖动、Recover 和输入焦点回归。缺少条件的部分明确待验证。

## F-02：Core 约定未完整接入 App，缺少工作区约束和布局变化恢复（P1，阻断）

- 位置：`MainWindow.xaml.cs:136`（WndProc）、`:383`（拖动赋值）、`:422`（RecoverPet）。
- 拖动直接写 Left/Top，无工作区约束；脚底物理坐标目前只在手动 Recover 内临时创建，不是应用统一的位置状态。
- App 没有显示器布局/工作区变化的订阅或消息处理路径。Core 的 `RecoverPosition` 没有 App 调用点。布局变化导致原位置不可见时，当前没有自动恢复路径。
- 影响：手动 Recover 可用不能代替 TASKS 已列出的“统一坐标与脚点、工作区约束、显示器布局改变后的恢复”。无第二屏允许缺少双屏实测，不等于这些功能可不实现。
- 整改目标：补齐当前阶段的坐标状态与 Platform 边界，使拖动、手动找回、布局/工作区变化使用一致约定；布局改变后重新枚举并验证/恢复当前位置，包含事件或消息清理。不要提前搭建后期物理/行为框架。
- 验收：提供实际调用路径；单屏边缘拖动符合 TESTING M-03 的可操作要求；通过可注入显示器布局快照验证布局缩减后的恢复与无效数据路径。真实混合 DPI/断屏操作仍可因设备不足记未验证，不要求购买第二屏，也不把伪造系统消息当成真实热插拔验证。

## F-03：RecoverPosition 的显示器选择不符合已有契约（P2，阻断）

- 位置：`src/DesktopPet.Core/Geometry/MonitorLocator.cs:125–134`。
- 存在 savedDeviceName 时，代码查到 savedMonitor 却仍对整个列表按旧坐标选显示器；两个分支最终调用完全相同的 ClampToWorkArea。标识缺失时也按最近屏幕处理，与 ARCHITECTURE 的“标识失效映射当前主屏”不同。
- 具体反例：主屏 x=0..1920，副屏 x=1920..3840。保存标识是副屏但旧脚点 x=500 时，当前代码仍放到主屏；保存标识已失效而 x=2500 落在另一块新副屏时，当前代码留在副屏而不是回主屏。
- 整改目标：明确主屏信息，按已记录契约选择目标屏幕并在其工作区约束位置；保留同意过的产品规则。P0-004 的 JSON 读取尚无需实现，本项仅要求可供其复用的几何恢复逻辑正确。
- 验收：增加“标识存在但旧坐标已落在另一块屏”“标识失效且仍有多块屏”等有区分度的逻辑测试；断开场景不能只用唯一剩余主屏掩盖目标选择错误。若认为契约应改变，在整改记录提出理由，不自行把现有实现改写成正确需求。

## F-04：空洞测试使用恒真断言，未证明声称的覆盖（P2，阻断）

- 位置：`tests/DesktopPet.Core.Tests/MonitorLocatorTests.cs:121`。
- `x >= 60 || x <= 3880` 对任意有限 x 都成立。例如原样返回空洞中的 x=1970 也能通过。
- 影响：17 项全绿不能证明此项已防止宠物留在屏间空洞；缺少第二屏时尤其依赖正确逻辑测试。
- 整改目标：断言整个宠物窗口包含在某个合法工作区内，或者断言明确预期位置；同时拒绝 NaN/Infinity 等无效结果。
- 验收：提供反例证明原空洞坐标会被新断言拒绝，修复后的有效位置会被接受，再运行相关测试。无需为文档措辞编写测试。

## 文档与接续

- 先按当前报告整改；TASKS 恢复为 in-progress，完成自测后依 REVIEW_PROCESS 提交 awaiting-verification，不自行修改本报告或关闭问题。
- STATUS 链接本报告和开发方整改记录；修正“必需项全部完成”和旧 150% 证据覆盖新 PMv2 模式的表述。
- 本轮未修改应用代码、测试代码、既往审查结论或用户 KL-001 豁免。开发方可继续同任务整改，暂不开始 P0-004。

## R1：整改复核（2026-10-21，Asia/Shanghai）

范围：读取 [开发整改记录](../remediation/P0-003-response.md)、真实代码及生成的 WPF 入口；独立构建、运行 22 项 Core 测试，并启动本轮测试桌宠读取真实 HWND 状态。
本轮只修改审查/状态/任务记录，没有修改开发实现或开发方整改记录。

### 版本与原始证据

- App.csproj SHA256：`BA192BBCBCC2EF99AB9E6DD576628F4DC0AC12928A2B7C46EF3C1704F51BFAB5`
- app.manifest：`E2A8BC943924CE32187EFC0D19A9364415A45986F7D0E0DE59D71B4B710CFACC`
- MainWindow.xaml.cs：`2555DA741D59ECAC288047847B3669286E32C612FCE9D29C647DFF992F660937`
- MonitorInterop.cs：`F2C68BD39144ADBC5CC863B419F77B57A1F8BEA611C21433C2C20CC1CEB8E5B9`
- MonitorLocator.cs：`E81D9A3886A139D9D95F42922988522BD444DEDC8824A41B3D3B5070BFEA9B9E`
- MonitorLocatorTests.cs：`7207F78AE0E4708DC94CF9B9CA0A6CD927136E58C83E4387CF970DFF39787530`
- 本轮生成 EXE：`0DBD1CE32A40305BF1F3A3DB2F7E1D4CEDB3F5C369D7CBB03E8CD16A96454614`
- [构建原始输出](evidence/P0-003-R1-build.txt)：`dotnet build DesktopPet.slnx -c Debug --no-restore -m:1`，进程退出码 0，0 警告/错误。
- [测试原始输出](evidence/P0-003-R1-tests.txt)：`dotnet test tests/DesktopPet.Core.Tests/DesktopPet.Core.Tests.csproj -c Debug --no-build --no-restore -m:1`，进程退出码 0，22/22 通过。
- [窗口原始输出](evidence/P0-003-R1-window-test.txt)：运行既有 `artifacts/recover-fix/Test-Recover.ps1`，Windows 10.0.26200.0，DPI=144，**PMv2=False**，测试 PID=129336。
- 窗口脚本整体退出码 1：启动点及连续 3 次 Recover 通过后，在强制移动到部分屏外的场景中等待菜单打开超时；错误由终端记录，stdout 文件仅包含此前通过步骤。本轮未重测到后续拖动、菜单关闭和 Exit，不能写成整套回归通过。该中止场景原因未定，不单凭此发布新的确定缺陷；测试实例已由 finally 的 WM_CLOSE 清理，未强杀，也没有把清理记成 Exit 验收。

### F-01：未通过，保持阻断

- ApplicationManifest 已从 Item 改为 Property，这是正确修复；但 PMv2 并未实际启用，真实窗口检查直接返回 false。
- 生成入口 `obj/Debug/net10.0-windows/App.g.cs` 的 Main 仅创建 WPF App、InitializeComponent、Run，没有调用 WinForms `ApplicationConfiguration.Initialize()`；`App.xaml.cs` 也没有 DPI 初始化。
- `ApplicationHighDpiMode` 并不自动在此 WPF 启动链执行。EXE 中找到字符串不能证明执行了设置；当前 manifest 中的 XML 注释本身就包含 `PerMonitorV2` 字样，因此会产生这种误判。
- 官方 [WFO0003 说明](https://github.com/dotnet/docs-desktop/blob/main/dotnet-desktop-guide/winforms/compiler-messages/wfo0003.md) 描述的是 WinForms 配置初始化流程，不应据此把 WPF 清单声明删除后就认为运行模式等效。可采用适用于 WPF 的 manifest 方案并对混合项目分析器作有依据的处理，或采用足够早、能验证成功的显式初始化；不要求机械加入整个 WinForms 启动流程。
- `EnumerateMonitors()` 仍在当前 UI 线程调用 GetDpiForMonitor；给其他方法新增 GetDpiForWindow 并未自动解决枚举路径的线程 DPI 适配，需一并核实并记录方案。
- 关闭条件保持原要求：先由开发方工具检测真实 HWND 为 PMv2=true，保存可复现输出，再在该版本验证 100%/150% 交互。不要让用户靠“看起来变大了”判断技术模式。
- 同步修正 ARCHITECTURE、manifest 注释和整改记录中的“属性在任何托管代码前生效/等效清单”等未经本项目验证的陈述。

### F-02：部分通过，尚未关闭

- 已确认 EndDrag → ClampWindowToWorkArea、WM_DISPLAYCHANGE → OnDisplayLayoutChanged → Core.RecoverPosition 的实际调用路径存在，接受这部分进展。
- 尚缺工作区单独变化的通知：当前只处理 WM_DISPLAYCHANGE，没有 WM_SETTINGCHANGE/SPI_SETWORKAREA 或等价路径。显示分辨率不变而工作区缩小时，静止宠物不会重新约束。两类通知分别见 [WM_DISPLAYCHANGE](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange)、[WM_SETTINGCHANGE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange) 和 [SPI_SETWORKAREA](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)。
- 坐标适配尚需闭环：Recover/布局恢复在移动前读取原窗口 DPI，但 Core 对目标屏幕使用目标 MonitorInfo 的 DPI 约束，回写时仍使用原窗口 DPI。源屏与目标屏比例不同，两个边界采用的尺度不一致；不能把 GetDpiForWindow 的调用本身当作跨屏位置转换正确的证据。
- 原 F-02 已要求通过可注入显示器布局快照验证应用适配路径。新增的 3 项测试只调用 Core.RecoverPosition，尚未覆盖 App 的窗口读写、源/目标 DPI 变化和布局通知路径。可以抽出一个小型可测适配边界或提供隔离集成测试，无需提前构建泛化框架。
- 剩余关闭条件：处理工作区变化；统一实际窗口物理位置、目标屏幕尺寸和回写之间的坐标边界；补覆盖源/目标比例不同及布局缩减的适配测试；真正启用 PMv2 后补单屏边缘拖动/找回/焦点回归。无第二屏仍可保留真实热插拔、双屏实测未验证，不要求用户更改系统设置来代替逻辑修复。

### F-03：通过，关闭

已检查存在标识时直接调用 ClampToMonitorWorkArea、标识失效时选择主屏的分支，3 项新增反例测试具有区分度，且本轮 22 项测试通过。接受本问题修复。

### F-04：通过，关闭

空洞测试已检查完整窗口落入工作区、有限输出并验证原空洞位置会被拒绝，覆盖了原问题。本轮测试通过，关闭。
另一个新增 NaN 测试名字声称有效输出，实际断言 NaN 透传，名称/说明应纠正；这是非阻断清晰度建议，不把原 F-04 扩大为新的 Core 全面输入校验需求。

### 当前结论与接续

保持 P0-003 awaiting-verification，不进入 P0-004。开发方继续 F-01/F-02，实际修改时将任务切回 in-progress，沿用原整改记录追加新版本、自测和反证，不改本报告。
当前无需用户重做整套验收，先修正已确认的技术问题；开发工具可以检测的 HWND 模式由开发方先提供证据。

## R2：整改复核（2026-10-21，Asia/Shanghai）

结论：PMv2 启用缺陷已修复，工作区通知及可测适配边界已有实现；**F-01/F-02 部分通过，暂不关闭**。剩余重点是可靠的交互验收证据和 100% 实测，不要求重做已关闭的 F-03/F-04，也不因无第二屏阻断可选实机覆盖。

### 版本、方法与验证

- 阅读开发整改记录 R2、真实实现及 App 测试、开发方 PMv2 脚本和日志；独立构建及运行所有测试，启动自建桌宠实例做 HWND、原生菜单和输入验证；本轮未改应用/开发测试代码。
- SHA256：App.csproj `CE73BB733923718A9EBDF48B3DB31206166F441E8A2190C476D9E4C45BF93D8A`；app.manifest `53A86F48C3E188A7B54FFB102EA29E50901CC69C8947849E117681B224E01602`；MainWindow.xaml.cs `6483AA902029B1B265342804EA77D86E911A534A303B5A074E9D9083FD0661D4`；WindowPositionAdapter.cs `841AA7532E6B07C1DB6EF40DF6F2FB24A5EDAE40B46FB9B6E96C0F06CFDA469F`；WindowPositionAdapterTests.cs `544DB43092AF76D8E528F8D6FB3771350A19F8D5DB348BD6FC52A9782403A86F`；EXE `228CC7B8F944C63FB4573C12B6E27AC2211EC5E67883D7F52E90AA0F04271F73`。
- [独立构建](evidence/P0-003-R2-build.txt)：`dotnet build DesktopPet.slnx -c Debug --no-restore -m:1`，退出码 0、0 警告/错误，4 个项目。
- [独立测试](evidence/P0-003-R2-tests.txt)：`dotnet test DesktopPet.slnx -c Debug --no-build --no-restore -m:1`，退出码 0，Core 22/22、App 16/16，共 38/38。
- [开发方 150% 自测日志副本](evidence/P0-003-R2-developer-150pct.txt)：保留原始 ALL PASS 声明，但其证明范围受下述脚本缺陷限制。
- 本轮实际 HWND 多次读取均 DPI=144、PMv2=true。早先一轮中心位置严格测试在终端完整通过：鼠标目标位移 `(100,60)`，窗口实际位移 `(100,60)`，松手停止、前台保持、外点关闭菜单/Esc/实际 Exit 退出码 0。该轮原 stdout 后被后续诊断输出覆盖，故这里只作为审查者实际观察记录，不伪造独立原始文件。
- [最终同步通知与严格拖动检查](evidence/P0-003-R2-final-window-test.txt)，PID=141920，退出码 1：PMv2=true；连续 Recover 通过；将自己的测试 HWND 移到屏外后，同步发送 WM_SETTINGCHANGE/SPI_SETWORKAREA、WM_DISPLAYCHANGE 均恢复到 `(0,0)-(180,180)`；之后 Recover 回到主屏可见位置。最后拖动期望 `(100,60)`，实际 `(33,-1)`，严格检查失败，因此该轮没有验证到后续焦点/菜单/Exit。
- [复现脚本](evidence/P0-003-R2-Test-Window.ps1)：仅操作自己启动的桌宠与测试窗口，不改系统缩放、工作区或分辨率，不广播消息。合成消息通过只证明接线与当前布局下恢复，不冒充真实工作区调整或热插拔。
- 通知初测及重试分别保留于 [初测](evidence/P0-003-R2-window-test.txt)、[诊断重试](evidence/P0-003-R2-notification-retry.txt)：当时错误地异步投递 WM_SETTINGCHANGE；改用同步发送后同一通知路径通过。这属于监理测试方法修正，不能归咎应用。参见 [PostMessage 的指针消息限制](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagea)。
- 失败测试的实例均在 finally 发送 WM_CLOSE 清理；没有强杀，也没有把清理当作 Exit 通过。

### F-01：PMv2 启用与 150% 模式检查通过，100% 验收待补

manifest 已恢复真实 DPI 元素，项目正确指定 ApplicationManifest，并针对 WPF 主入口的 WinForms 互操作说明 WFO0003 屏蔽理由。真实窗口 PMv2=true，接受启用修复，不再要求重改这条启动路径。
原要求的 100% 回归尚缺；150% 交互证据还应按 F-02 排除脚本问题后完整留存。暂保持部分通过，不能以旧 PMv2=false 的 100%/150% 测试补齐。
ARCHITECTURE 曾仍写“ApplicationHighDpiMode 启用、不在 manifest 声明”，本轮监理已按实际实现更正该技术文字，未修改开发方历史整改记录。

### F-02：通知接线和适配测试通过，交互证据尚不足

接受 WM_SETTINGCHANGE/SPI_SETWORKAREA 新增处理、App 实际调用 WindowPositionAdapter，以及注入布局的适配测试；38 项测试确实通过。单屏隔离消息路径也已实际通过。混合 DPI 真实跨屏仍未测，不扩大声称。

必须先修正开发方 `artifacts/recover-fix/Test-PMv2-Verify.ps1` 的验收判定：

1. 只检查“窗口移动了”，放弃抓取点位移一致性。其原日志明确记期望鼠标 `(100,60)`，实际窗口 `(91,-246)` 仍给 PASS；这是证据不足，不是证明 WPF 应当允许这种位移。
2. 焦点改变时只打印 WARNING，甚至恢复前台后继续给 ALL PASS；焦点保持要求没有因此被满足。应在失焦时失败，不能以测试主动修复替代观察。
3. Assert-Recovered 的条件脚本会把诊断字符串写入成功输出流，Wait-Probe 使用 `if (& $Condition)` 时可能把“字符串＋false”的数组当作 true。应让条件只返回一个布尔值，诊断走其他流；另外当前产品约定脚点在工作区中央，不能误把窗口中心当作同一验收点。
4. 目前只口头列出“松手停止”，没有在松手后移开鼠标并再次比较窗口位置，需补真实断言。

独立严格检查已有一轮位移符合、另一轮不符合，**暂不裁定应用必然有拖动 bug**。下一轮先记录 GetCursorPos 的真实起点/终点、SetCursorPos 返回值、捕获状态与窗口矩形；等待真实事件状态而非只依赖固定延迟。确认输入确已按目标送达后再判断应用，若有人工鼠标干扰或自动化失败应明确标“测试无效/需重试”，不能放宽断言。
监理自己的严格脚本同样尚未记录实际光标终点，因此失败仅作为需解释的差异，不作为确定的实现根因。它供参考，不要求原样复制或当作完整权威测试。

### 下一步与边界

开发方在原整改记录追加：脚本修正、差异排查、可靠的 150% 回归；然后再安排用户做 100% 体验/运行修正后的脚本。用户现在无需调整任务栏或重复未可信的 ALL PASS 脚本。
P0-003 保持 awaiting-verification；修复期间按规则切换 in-progress，待闭环后提交复核。P0-004 暂不开始。F-03/F-04 维持已关闭，不新增其他阶段要求。

## R3：整改复核（2026-10-22，Asia/Shanghai）

结论：**F-01 通过关闭；F-03/F-04 维持关闭；F-02 部分通过，仅剩位移一致性验收未闭环**。P0-003 暂保持 awaiting-verification，不进入 P0-004。

### 本轮依据及已接受内容

- 阅读开发整改记录 R3、完整 150% 日志及当前脚本关键判定；100% 结果为开发方转述的用户 ALL PASS，未保存原输出，按这一来源记录，不冒充监理实测。
- MainWindow.xaml.cs、WindowPositionAdapter.cs、App.csproj 的 SHA256 与 R2 相同；当前窗口模式配置和应用适配实现没有变化。本轮未启动桌宠或重跑构建/38 项测试，沿用 R2 的对应版本验证，不无故要求重复全部检查。
- [150% 原日志副本](evidence/P0-003-R3-developer-150pct.txt)显示 PMv2=true；光标、窗口位移都为 `(100,60)`，捕获/释放、焦点、松手停止、Esc、Exit 均有记录。这一次具体位移观察是合格证据，予以接受。
- [本轮开发脚本快照](evidence/P0-003-R3-developer-script.ps1)，原路径 `artifacts/recover-fix/Test-PMv2-Verify.ps1`，SHA256 `5D1CE5872AC30052ADF7328D103D23A6821AE3480EFFD360D1082B629AC21D6B`。
- Wait-Probe 只接受单个布尔真值、Recover 按脚点判断、失焦抛错、松手移开光标后比较窗口位置，均已核对，接受这些修正。
- 综合真实 PMv2 检查、R3 150% 完整观察及开发方转述的 100% 模式/焦点通过反馈，关闭 F-01。100% 抓取点的证明不足保留在 F-02，不把同一缺口重复挂到两个问题。

### F-02 剩余项：位移断言仍被明确删除

当前脚本约第 349–369 行明确写“Only check that the window actually moved”“Do NOT compare”，运行判定仅为：

```powershell
if ($deltaX -eq 0 -and $deltaY -eq 0) { throw ... }
```

这仍会允许光标移动 `(100,60)`、窗口移动 `(91,-246)` 时通过。新增 cursorDelivered 只证明鼠标大致到达，不能替代窗口与光标位移一致性的验收。原 150% 日志这次恰好一致，不会让缺失断言自动成立；100% 只有 ALL PASS 转述，无法从中判断这一项。
脚本注释还把放弃比较写成“per reviewer R2 directive”，与 R2 原文相反，需更正此归因。开发方若有反证，应在整改记录提出，不能把需求放宽写成监理要求。

### 最小剩余整改与关闭条件

1. 在当前不触及屏幕边缘的拖动测试中，确认真实光标起终点、捕获状态和窗口位置稳定后，比较 `$deltaX/$deltaY` 与 `$cursorDeltaX/$cursorDeltaY`；允许约 2 个物理像素的取整误差，超出即失败。例如：

```powershell
if ([Math]::Abs($deltaX - $cursorDeltaX) -gt 2 -or
    [Math]::Abs($deltaY - $cursorDeltaY) -gt 2) {
    throw "Drag displacement mismatch: window=($deltaX,$deltaY), cursor=($cursorDeltaX,$cursorDeltaY)"
}
```

2. 自动化未把输入送达或有用户鼠标干扰时标测试无效并排查；不得改回“只要动了就通过”。如果主张两组物理坐标不能比较，提交具体 API/实测反证供裁定。
3. 明确验证反例：cursorDelta=`(100,60)`、windowDelta=`(91,-246)` 必须失败；两者都 `(100,60)` 必须通过。这里仅检查判定，无需再加应用测试工程。
4. 提供修正后脚本下的 150%/100% 抓取点结果和脚本版本。已有 150% 原始位移观察、其余焦点/Recover/退出和 38 项测试保持有效；无需重做全部 P0-002 验收。100% 可保存脚本输出，或由用户明确反馈“抓取点无跳变/漂移”这一实际体验；仅同一弱脚本的 ALL PASS 不补足该项。

本轮仅维护监理报告、状态和任务入口；未修改应用、开发脚本及开发方整改记录。下一轮只围绕上述剩余项复核，不重开 F-01/F-03/F-04。

## R4：最终复核通过（2026-10-22，Asia/Shanghai）

**F-02 通过关闭。F-01/F-03/F-04 维持已关闭；P0-003 必需验收闭环，可以标 done，允许按 TASKS 进入 P0-004。**

### 本轮检查与版本

- 仅复核 R3 剩余的位移一致性断言、错误归因修正、正反例及 100%/150% 原始日志，不重复已接受的全部验收。
- 当前开发脚本 SHA256：`719DA57A284B30E33D66E7467F9B63DCA60D367DA0DBDAB91B16BC1FAE322A34`；[脚本留存副本](evidence/P0-003-R4-developer-script.ps1)。
- MainWindow.xaml.cs、WindowPositionAdapter.cs、App.csproj 哈希与 R2 一致，关键应用实现未变；沿用既有独立构建及 38 项测试结果。本轮未重新构建、未运行完整 GUI，也未修改应用或开发方脚本/整改记录。
- 独立解析实际脚本 AST，提取唯一的位移 if 语句执行正反例：窗口 `(91,-246)`/光标 `(100,60)` 确实抛错，二者 `(100,60)` 不抛错。脚本语法无错误；[独立检查输出](evidence/P0-003-R4-assertion-check.txt)。

### 实机证据与结论

- [150% 开发自测日志](evidence/P0-003-R4-developer-150pct.txt)：DPI=144、PMv2=true；窗口 `(1190,584)`→`(1290,644)`，实际光标位移 `(100,60)`，2px 一致性检查 PASS。捕获/释放、焦点保持、松手停止、Recover、Esc、实际 Exit 退出码 0 均记录通过。
- [100% 用户执行日志](evidence/P0-003-R4-user-100pct.txt)：DPI=96、PMv2=true；窗口 `(1220,656)`→`(1320,716)`，实际光标位移 `(100,60)`，一致性检查 PASS；其余同项亦有通过记录。结果来源为开发方提交的原始日志及用户反馈，不冒充本监理亲自操作。
- 100% 日志中退出前菜单第一次打开失败、第二次成功，保留原始重试记录，不描述为“无重试”。不据此扩大已经完成的本次剩余验收范围。
- 日志里的 `WPF DIP` 一行仍是用固定 1.5 推算的诊断值，不是真正读取的 WPF 属性，100% 时不适用；本轮只采用 GetCursorPos/GetWindowRect 的物理像素与实际断言作为位移依据，因此不影响此次通过，不新增阻断。
- 原错误注释已更正，脚本现明确保留比较；未再把删除断言归因于监理。

### 接续与保留边界

P0-003 无剩余审查阻断，TASKS 标 done，STATUS 转为 P0-004 的接手入口；本轮没有开始 P0-004 的实现，也不要求用户重复验收。
混合 DPI 双屏/真实断开显示器仍未实测，按原 TASKS 为可选覆盖；不能宣称已实机支持所有显示器组合。KL-001 的 Alt+Tab 豁免保持原范围。P0-004 和 P0-005 仍按各自范围与验收推进。
