# P5A-003 整改记录（R1 F-01 / F-02）

2026-11-19（Asia/Shanghai）。对象为[监理报告 R1](../reviews/P5A-003-review.md)。状态：已整改、自测与实机补证通过，`awaiting-verification` 待监理复核。未提交/推送。

## F-01 暂停没有即时结束拾取（已整改）

- 修复位置：`src/DesktopPet.App/MainWindow.xaml.cs` 暂停入口（约 3286 行）：`EndPerchPick("cancelled")`。暂停会停止更新计时器，`UpdatePerchPick` 的暂停兜底不可达，故在暂停入口同步结束拾取；托盘与应用级暂停共用同一入口，恢复后不续接旧拾取。
- 自测证据：App 专项与全量回归通过（Core 548/548、App 546/546），含新增暂停路径用例（`PauseActionSwitchTests` 等）。监理反例 `StartPerchPick → PauseAnimation → IsPerchPickActive` 对应路径已随修复关闭，具体判定以监理复核为准。

## F-02 真实原生菜单端到端证据与文档行为不一致（已整改）

### 产品修复（用户已批准范围）

- 菜单打开期间挂起稳态遮挡判定：`src/DesktopPet.App/Platform/PetWindowPerch.cs` 稳态轮询在 `_owner.IsContextMenuOpen` 时返回（约 297-303 行）。菜单（`TrackPopupMenuEx` 弹层）是自家 UI，会短暂盖住落脚区；若按外来窗口处理，栖息中右键想选「Leave Perch」会被误判遮挡先行脱离（M-16 实机实测菜单打开瞬间 `fraction=0.44`）。菜单关闭后下一轮轮询自动恢复判定。
- 不采用鼠标注入选中菜单项；菜单选中走键盘注入（前台守卫 + DOWN×index + ENTER），Enter 之后零 UIA 跨进程调用。

### 实机补证（R1 要求的真实菜单场景）

- 补证轮：`.temp/p5a-003/entry/run-20261119-192641-notepad/`，12/12 ALL PASS，探针 exitCode=0，环境 1920×1080@96DPI 单屏。
- 场景链：真实右键打开宠物原生菜单 → 键盘选中「Perch on Window」→ 真实左键点击记事本目标 → landed（脚点=可见外框上缘中点）→ 目标移动 follow → 上缘遮挡 `occluded-detach` → 解除重选 → 关闭 `detached(WindowGone)` → 重开重选 → 真实鼠标拖动 `drag-preempted` → 拖走后不回拉 → 拖动后再选 landed → 真实菜单「Leave Perch」→ `detached(user-left)` 宠物原位解绑。
- 复核确认轮（2026-11-20 凌晨）：`.temp/p5a-003/entry/run-20261120-013233-notepad/`，同命令带 `-RealMenu` 重跑 12/12 ALL PASS，探针 exitCode=0，环境 1920×1080@96DPI 单屏。
- 前台归属诊断确认轮（2026-11-20 下午）：驱动补齐 `LASTINPUTINFO`/`GetLastInputInfo` 前台诊断（fg hwnd/pid/进程名/最后真实输入年龄）后重跑 `.temp/p5a-003/entry/run-20261120-132156-notepad/`，12/12 ALL PASS，探针 exitCode=0，环境 1920×1080@96DPI 单屏。每次真实菜单打开前诊断确认前台归属目标记事本（如 fg=0x16419C0 pid=56864 proc='Notepad'），「Perch on Window」「Leave Perch」均 attempt 1 键盘选中成功，「no menu」未复现。
- 复跑命令：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5A003EntryPreview.ps1 -TargetKind notepad -RealMenu -DurationSec 1200`。`-RealMenu` 驱动真实右键（`SetCursorPos` + `mouse_event`）、纯 `FindWindowEx('#32768')` 菜单出现/关闭探测、UIA 仅选中前项索引定位、Enter 后零 UIA。
- 过程失败轮按当时版本保留：run-20261119-180459/185949（右键 4×「no menu」，前台被非宠物窗口持有，生产 `ShowNativeContextMenu` 的 `SetForegroundWindow` 被系统拒绝后静默返回；驱动补齐前台归属诊断日志后复跑通过，产品代码两轮间未变）。无效的 AltTap 前台解锁方案已从驱动撤销（无效且会激活用户前台应用菜单）。

### 文档校正（无效点击结束拾取）

- [ARCHITECTURE](../ARCHITECTURE.md) §9 与 [TESTING](../TESTING.md) M-16 已改为当前实现：点中宠物自身 `cancelled-self`、点击不合格目标 `rejected-ineligible`、落位失败 `rejected-landing`，均结束拾取；15s 超时 `cancelled-timeout`。
- M-16 每轮 12 项口径已据实标注；最小化/恢复、无效点击与超时仅自动测试覆盖（`PerchEntryTests` 等），实机清单不含这些步骤。

## 验证汇总

- 全量构建 0 警告 0 错误；Core 548/548、App 546/546 通过。
- 实机：初两轮（测试分发入口）run-20261119-085140-notepad、run-20261119-085219-edge 各 12/12；R1 F-02 真实菜单补证轮 12/12。

## 限制与未测项

- 跨屏、混合 DPI 未测（当前 1920×1080@96DPI 单屏）；多屏布局变化下的拾取与脱离待有环境后补测。
- 真实菜单轮在记事本目标上验证；Edge 目标的真实菜单轮未单跑（初两轮 Edge 12/12 沿用测试分发入口证据）。
- 环境前台锁偶发（外部应用持有前台导致 `SetForegroundWindow` 被拒、菜单不开）非产品缺陷，驱动以前台诊断 + 重试处理。

## 复核请求

请监理复核 F-01、F-02 是否可关闭；本记录为开发自述，复核结论以监理为准。
