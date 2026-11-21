# P5A-001 整改记录（监理 R1 → 开发整改）

2026-11-18（Asia/Shanghai）。对象：[监理报告 R1](../reviews/P5A-001-review.md) 的 F-01～F-04 阻断项。未修改监理报告；未提交/推送；任务状态保持 `awaiting-verification`。

## F-01 可见落位（初选和跟随都检查完整宠物矩形）

### 根因

初选/跟随只算上缘中点脚点，不检查完整宠物矩形：目标上沿容不下宠物时（如最大化），宠物被移出屏幕仍报 `landed/follow`。

### 修复

- `src/DesktopPet.Core/Windows/WindowPerchLandingPolicy.cs`：新增带宠物宽高与显示器边界的重载，完整宠物矩形（脚点居中、贴上缘）必须完整落在该脚点所在显示器内，容不下返回 `NoLanding`；原单点重载保留。
- `src/DesktopPet.App/Platform/WindowPerchInterop.cs`：新增 `TryGetMonitorBoundsForPoint`（`MonitorFromPoint(MONITOR_DEFAULTTONEAREST)` + `GetMonitorInfoW`，取完整 `rcMonitor`，物理像素），查询失败拒绝落位。
- `src/DesktopPet.App/Platform/PetWindowPerch.cs` `TryApplyLanding`：先按单点定位脚点，再按脚点查显示器边界，再按完整矩形重算落位；容不下返回 `NoLanding`。跟随分支对 `NoLanding` 走 `DetachInternal("no-landing")`（发 `detached` 事件，宠物停在原地保持完整可见）。
- 初选拒绝（`TrySelectExplicit` 失败）静默返回 false，不移动、不发事件。

### 单测（Core `ExternalWindowTests` 新增）

上沿容不下完整宠物时拒绝；恰好容下正例；水平溢出拒绝；右缘贴边正例；显示器查询失败拒绝；最大化目标拒绝。

### M-15 实机（新增断言）

最大化步骤改为：最大化→等 `detached(reason=no-landing)`→断言宠物**实际矩形**完整在其所在屏幕 `Screen.FromPoint` 动态边界内（不硬编码分辨率）且 `perchActive=false`→还原→重选→`landed`+脚点贴锚点上缘。四轮均 PASS（如 notepad `pet=[1860,670,2040,850] screen=[0,0,2560,1600]` 完整内含）。

## F-02 位置所有权（栖息接入现有位置更新边界）

### 根因

栖息写位置绕过 `IsPerchWriteAllowed` 守卫，与拖动/物理/暂停/社交等行为竞争位置所有权。

### 修复

- `src/DesktopPet.App/MainWindow.xaml.cs`：`IsPerchWriteAllowed` 扩展为 `!_isDragging && !_isPettingCandidate && !_isAppPaused && !_isPhysicsActive && !_isProbing && !_isSocialActive`；`ApplyPerchFoot` 返回 bool（守卫拒绝返回 false 且不动位置）。
- `src/DesktopPet.App/Platform/PetWindowPerch.cs` `TryApplyLanding`：`ApplyPerchFoot` 拒绝返回新枚举 `WriteRejected`——不报 `landed/follow`、不消费跟随基线、保持绑定，下轮轮询重试（`OnTick` 顶部 `!IsPerchWriteAllowed` 早退兜底）。`TrySelectExplicit` 开头同样守卫：占用期间拒绝初选、不报 `landed`。
- `src/DesktopPet.Core/Windows/PerchUpdateGate.cs`：新增 `PeekShouldReport` 只读预检，跟随分支 Peek 判断、成功提交才 `ShouldReport` 消费基线（拒绝时不消费，让出后仍能跟随到最新位置）。

### 单测

- Core：`PeekShouldReport` 不消费基线、无基线时 false 等 4 项。
- App（新建 `tests/DesktopPet.App.Tests/WindowPerchOwnershipTests.cs`，9 项）：暂停/物理占用时 `TrySelectExplicit` 拒绝初选（无 `landed` 事件、宠物不动）反例；`ApplyPerchFoot` 空闲 true/物理 false/暂停 false；守卫矩阵；占用期间跟随不写不报。

## F-03 关闭清理（关闭或移除宠物时停止栖息轮询并解除绑定）

### 修复

- `src/DesktopPet.App/MainWindow.xaml.cs` `OnClosed`：`_petPerch?.Detach("closed")`——停轮询定时器、解绑、清跟踪器。
- 退出恢复：`PetWindowPerch` 在提交落位前记录栖息前位置（`_prePerchLeft/_prePerchTop`，成功提交才保留）；`RestoreProbePositionForExit` 先调 `_petPerch?.RestorePrePerchPositionForExit()` 恢复栖息前位置再执行原退出恢复（退出保存坐标语义见下）。

### 退出保存坐标语义

存在栖息绑定时退出：宠物窗口先恢复到**栖息前位置**，退出存档记录该恢复位置；不把栖息脚点写进存档。重启后宠物从存档位置出现，不自动续接旧目标（绑定只在内存中）。已关闭宠物：`OnClosed` 已停轮询、解除绑定，外部目标存亡变化不会再更新该宠物。

### 单测

App `WindowPerchOwnershipTests`：`OnClosed` 停计时器且 `detached("closed")`；`RestorePrePerchPositionForExit` 恢复栖息前位置并停用；未激活时不动。

## F-04 遮挡顺序（提交位置前检查实际落脚处）

### 根因

遮挡采样用旧"上缘可见性"口径（可遮上缘远端即脱离），且提交与检查顺序未定义。

### 修复

- `src/DesktopPet.App/Platform/PetWindowPerch.cs` `TryApplyLanding` 改为枚举结果 `PerchLandingResult { Landed, NoLanding, Occluded, WriteRejected }`，**先校验后写入**：可见区校验→实际落脚区遮挡采样→`ApplyPerchFoot` 提交，全部通过才发事件。
- 遮挡采样改为**实际落脚区**：`SampleLandingOcclusion`（替换并删除旧 `SampleTopEdgeOcclusion`）采样 `[footX−petWidthPx/2, footX+petWidthPx/2]`（y=外框上缘+2px），`WindowFromPoint` 根窗口≠目标即遮挡；只遮上缘落脚区之外的远端不产生遮挡。稳态每 tick 以 `_lastFootX/_lastPetWidthPx` 复查落脚区，被盖→`occluded-detach`（先发事件后脱离，宠物停在原地）。
- 提交被遮挡/出屏位置不再可能发生：`Occluded` 分支在写入前返回。

### 单测与实机

- Core：落脚区之外遮挡不影响落位正例；落脚区被盖拒绝。
- M-15 新增断言"远端遮挡：落脚区外上缘被盖不脱离"：WinForms TopMost cover 盖落脚区右侧之外（cover 左缘=footX+petW/2+10），等 7s 断言无 `occluded-detach`、`perchActive=true`、脚点不变。四轮均 PASS（cover=[1950,795,300,120]，foot=1850，petW=180）。
- 原步骤 8"落脚处被盖 occluded-detach 且宠物停在原地"保持不变，四轮 PASS。

## 探针（测试工具）修复，不影响产品代码

`tests/DesktopPet.DiagnosticProbe/WindowPerchLiveScenario.cs` `WriteState`：驱动并发读取 `perch-live-state.json` 与探针 `File.Move(overwrite)` 竞争抛 `UnauthorizedAccessException`，未处理导致探针崩溃（`run-20261118-162830-notepad`，探针 exit=-532462766）。修复为 `IOException/UnauthorizedAccessException` 重试 3 次（间隔 20ms），仍失败跳过本心跳继续运行。产品代码未改。

## 驱动诊断增强

`tools/Invoke-P5A001Preview.ps1` 新增 `Get-OccludersAtPoint`：远端遮挡断言失败时枚举覆盖脚点的可见顶层窗口标题写入检查明细，用于区分真实遮挡者。`run-20261118-162718-notepad` 的一次 FAIL 由该诊断确认为**用户自己的 Edge 全屏窗口**（闲鱼聊天页，运行期间用户操作电脑盖住落脚区），非产品缺陷；本轮按当时版本保留。

## 实机复跑证据（单屏 144 DPI/150%，驱动线程 PMv2，每轮 15 项）

- notepad 第一轮：`.temp/p5a-001/m15/run-20261118-161637-notepad/` 15/15 ALL PASS
- notepad 第二轮：`.temp/p5a-001/m15/run-20261118-163221-notepad/` 15/15 ALL PASS
- edge 第一轮：`.temp/p5a-001/m15/run-20261118-163324-edge/` 15/15 ALL PASS
- edge 第二轮：`.temp/p5a-001/m15/run-20261118-163417-edge/` 15/15 ALL PASS

新增 3 项断言（最大化 no-landing 脱离且宠物完整可见、最大化恢复后重选落位、远端遮挡不脱离），原 12 项全部保持 PASS；每轮探针 exitCode=0，含真实鼠标拖动 `drag-preempted`（dragCancelled=目标句柄）与松手不回拉。过程失败轮（`run-20261118-161933`、`run-20261118-162718`、`run-20261118-162830`，均为用户操作/探针写状态竞争，按当时版本保留）。

## 全量验证

- 构建（`DesktopPet.slnx`）0 警告 0 错误；`DesktopPet.DiagnosticProbe` 单独构建通过。
- Core 548/548、App 520/520 全部通过（含本轮新增 Core 8 项 + App 9 项反例）。

## 未测项（不变）

跨屏、混合 DPI、真实用户窗口动态遮挡（遮挡由自建覆盖窗模拟）；晃动、主动靠近不在本轮，攀爬在 R-08。

## 复核请求

请监理复核 F-01～F-04 是否满足 R1 整改条件，并对四轮实机证据（上列 run 目录）按需独立复跑。任务状态保持 `awaiting-verification`。

---

# P5A-001 R2 剩余项整改（监理 R2 → 开发整改）

2026-11-18（Asia/Shanghai）。对象：[监理 R2](../reviews/P5A-001-review.md)（L29-L39）F-02～F-04 的剩余要求。未修改监理报告；未提交/推送；四轮 M-15 实机证据沿用（R2 明确"无需重跑四轮全部步骤"），不重复执行。

## F-02 剩余：找回解绑与重选失败保留旧绑定

### 关于"接入已有位置更新边界"

栖息位置提交已接入受守卫的单一写入边界：初选与跟随的实际位置写入统一走 `MainWindow.ApplyPerchFoot`（L2200），其入口受 `IsPerchWriteAllowed` 守卫（拖动/抚摸/暂停/物理/探头/社交六旗互斥），拒绝返回 `WriteRejected` 且不移动、不报事件、不消费跟随基线。独立的 `DispatcherTimer`（120ms）只承载低频只读观测与跟随触发，不构成第二个位置写入通道——R1 整改后的四轮 M-15 证据（拖动抢占、暂停拒绝、物理拒绝反例）均基于该边界。本段沿用 R1 实现，不再重复整改。

### 修复 1：找回宠物解除栖息绑定

- `src/DesktopPet.App/MainWindow.xaml.cs` `RecoverPet`（L2878）：入口新增 `_petPerch?.Detach("recovered")`。找回即取回位置所有权：停轮询、解绑、发 `detached(recovered)` 事件（留观测痕迹），随后目标再移动不会把已找回的小熊拉走。

### 修复 2：已有绑定时重选失败保留旧绑定（反例）

- 缺陷（监理静态复核确认）：`PetWindowPerch.TrySelectExplicit` 原先无条件 `_tracker.Reset()` 后再 `Select`；已有绑定时重选一个"合格但不可落位"的目标会把旧跟踪器清掉，落位失败后 `IsActive`/计时器仍保留，下一轮 `OnTick` 对空目标 `_tracker.Target!` 解引用。
- 修复（`src/DesktopPet.App/Platform/PetWindowPerch.cs` `TrySelectExplicit`）：先保存旧绑定快照（`_tracker.Target`）与栖息前位置；新目标资格校验、落位、遮挡校验任一失败时，原样恢复旧绑定（跟踪器、`IsActive`、计时器、跟随基线、栖息前位置全部一致），下一轮轮询读到的仍是原目标；旧绑定恢复失败（理论不可达）则完整停用，不留激活残态。重选成功才替换目标并重置 `PerchUpdateGate` 基线；重选成功时栖息前位置沿用原休息位（仅首个绑定记录一次）。

### 补证（App 单测，`WindowPerchOwnershipTests` 新增）

- `TrySelectExplicit_ReselectFails_PreservesOldBinding_NextPollConsistent`：旧绑定 A 激活后重选"落脚处被占"的目标 B（伪造句柄：落脚区 `WindowFromPoint` 永远命中其他根窗口，构造确定性遮挡）→ 重选失败、零事件、零位移、旧绑定保留（`TargetHwndValue` 仍为 A）；失败后继续推进下一轮轮询——读到旧绑定 A（非空目标），句柄已销毁 → 一致 `detached(WindowGone)`，无空引用；再推进一轮未激活早退。这正是监理要求的"失败后继续推进的反例"。
- `RecoverPet_ReleasesPerchBinding_NoRewriteAfterwards`：栖息激活 → `RecoverPet()` → `detached(recovered)`、绑定解除、轮询停止；随后驱动一轮轮询零写入（目标再移动不写位置）。
- `TrySelectExplicit_LandingZoneCovered_RejectedWithZeroDisplacement`（见 F-04a）。

## F-03 剩余：隔离存档往返

- 修复内容：无产品代码变更。退出链路（`ShutdownMainWindow` → `FlushFinalState` → 每窗口 `RestoreProbePositionForExit` → `_petPerch.RestorePrePerchPositionForExit()` 恢复栖息前位置 → `MarkStateDirty` → `SaveCoordinator` 落盘）R1 已实现，本轮补齐真实保存/重载/新窗口恢复的隔离证据。

### 补证

- `Perch_ExitArchiveRoundTrip_SavesPrePerchPosition_NewWindowNoOldBinding`（App 级，真实链路）：隔离存档目录 + `TestSetupIsolatedSave` + `CreatePetWindow`；反射构造栖息激活（伪造目标——真实落位需真实外部目标，绑定与栖息前位置字段与真实落位语义一致）；窗口移到临时栖息位（555,66）→ 正常退出（`ShutdownMainWindowForTest` 走真实退出保存链路）→ `ArchiveService.LoadState()` 重载：落盘 `WindowLeft/Top` = 栖息前坐标（222,333），非临时栖息位 → 从重载状态创建新窗口：`Perch.IsActive=false`、无目标句柄、轮询未启动，驱动一轮轮询零写入。绑定只在内存，重启后不存在。
- 既有 `OnClosed_StopsPerchPollingAndDetaches` 等关闭用例保持不变（R2 已确认通过）。

## F-04 剩余：两个小场景的 App/隔离证据

- 无产品代码变更（R2 已确认先校验后写入代码正确），补两个小步的隔离证据：
- `TrySelectExplicit_LandingZoneCovered_RejectedWithZeroDisplacement`（初选）：落脚处已被遮挡时走真实初选路径 → 拒绝（`Occluded` 在 `ApplyPerchFoot` 之前返回）、零位移、不发 `landed`、不激活。
- `FollowTowardOccludedLanding_RejectsBeforeWrite`（跟随）：目标移向被遮挡落脚处时跟随提交 → 遮挡校验先于写入拒绝（`PerchLandingResult.Occluded`），宠物不被移过去（零写入）、不发 `follow`。
- 两例与 M-15 实机"落脚区被盖 occluded-detach、远端遮挡不脱离"互补，共同覆盖初选/跟随两侧的先校验后写入。

## 验证（R2 剩余项整改后）

- 构建（`DesktopPet.slnx`）0 警告 0 错误。
- Core 548/548、App 525/525（`WindowPerchOwnershipTests` 9→14 项，新增 5 项补证）全部通过。
- 四轮 M-15 实机证据沿用（R2 已核对），本轮不重跑。

## 未测项（不变）

跨屏、混合 DPI、真实用户窗口动态遮挡（遮挡由自建覆盖窗/伪造句柄确定性构造）。

## R2 复核请求

请监理复核 R2 F-02～F-04 剩余项：找回解绑与重选失败反例、隔离存档往返、F-04 两个小场景的零写入证据，确认是否关闭。任务状态保持 `awaiting-verification`，未提交/推送。

---

# P5A-001 R3 收尾（监理 R3 两项 → 开发整改）

2026-11-18（Asia/Shanghai）。对象：[监理 R3](../reviews/P5A-001-review.md) 的两项收尾：F-02 栖息/社交共用位置所有权判定、F-04 生产轮询跟随遮挡补证。未修改监理报告；未提交/推送；四轮 M-15 实机证据沿用，不重跑。

## F-02 收尾：栖息与社交共用位置所有权判定

### 修复

- `src/DesktopPet.App/MainWindow.xaml.cs`：所有权判定收拢重构——新增互斥核心 `HasCorePositionConflict`（`_isDragging || _isPettingCandidate || _isAppPaused || _isPhysicsActive || _isProbing` 五旗）；`IsPerchOwned`（`_petPerch?.IsActive`）；`IsPerchWriteAllowed => !HasCorePositionConflict && !_isSocialActive`；新增 `IsSocialPositionWriteAllowed => !HasCorePositionConflict && !IsPerchOwned`。
- seek 写入点（同文件 ~L1355）：原逐旗守卫替换为 `seekerWindow.IsSocialPositionWriteAllowed`——栖息绑定持有时社交步进不写位置。
- chase 写入点（同文件 ~L1384）：leader/follower 两侧原逐旗守卫替换为各自的 `IsSocialPositionWriteAllowed`，**paired-commit 语义保留**（任一不可写则双方跳过，下轮由协调器 `CheckInterrupt` 取消）；`TestBlockChaseCommit` 测试钩子（P3-004 R4 F-01）语义不变（置 `_isPhysicsActive=true` 经共享判定仍然阻断）。
- 语义：栖息与社交经同一互斥核心互斥；先到者持有位置，另一方让出（社交持有 `_isSocialActive` 时栖息经 `IsPerchWriteAllowed` 让出；栖息持有 `IsPerchOwned` 时社交经 `IsSocialPositionWriteAllowed` 让出）。

### 补证（App 单测，`WindowPerchOwnershipTests` 新增）

- `PerchAndSocial_SharedOwnership_OnlyApprovedPathWrites`：同一只宠物同时收到两种位移请求时只有获准路径写入——栖息持有时 `IsSocialPositionWriteAllowed=false` 且 `ApplyPerchFoot` 提交成功（栖息是唯一获准写入）；栖息让出、社交持有（`_isSocialActive=true`）时社交步进获准（`IsSocialPositionWriteAllowed=true`）且栖息位移被拒（`ApplyPerchFoot=false`）；核心冲突（拖动等五旗）时双方都拒。

## F-04 收尾：生产轮询跟随遮挡补证

### 测试缝（仅替换快照采集，生产轮询路径不变）

- `src/DesktopPet.App/Platform/PetWindowPerch.cs`：新增 `internal Func<long, ExternalWindowSnapshot?>? TestSnapshotSource`（返回 null 等价句柄销毁）；`OnTick` 的快照采集改为 `TestSnapshotSource is not null ? TestSnapshotSource(target.HwndValue) : SnapshotWindow(...)`。其余 `OnTick` 路径（IsActive→暂停→拖动→守卫→Observe→gate→`TryApplyLanding("follow")`→分支处理）零改动。

### 补证（App 单测，`WindowPerchOwnershipTests` 新增）

- `OnTick_TargetMovesToOccludedSpot_PetStaysPut_Detaches`：目标 A 已绑定（跟随基线=旧外框）→ 目标移动到落脚区被占的新位置（身份不变仅 bounds 变化；伪造句柄：新落脚区 `WindowFromPoint` 永远命中其他根窗口，构造确定性遮挡）→ 反射驱动一轮**生产轮询** `OnTick`：无 `follow` 事件、恰一条 `occluded-detach`、`IsActive=false`、目标绑定解除（`TargetHwndValue=null`）、轮询计时器停止、小熊保持原位（零写入）。与既有提交方法级证据（`TryApplyLanding("follow")` 先校验后写入）互补，完整覆盖监理要求的"再走一次生产轮询"路径。

## 验证

- 构建（`DesktopPet.slnx`）0 警告 0 错误。
- Core 548/548、App 527/527（`WindowPerchOwnershipTests` 14→16 项）全部通过。
- 四轮 M-15 实机证据沿用（监理 R2 已核对），本轮不重跑。

## R3 复核请求

请监理复核 R3 两项收尾：F-02 栖息/社交共用所有权判定（含"同一只宠物同时收到两种位移请求时只有获准路径写入"反例）与 F-04 生产轮询遮挡脱离补证，确认是否关闭。任务状态保持 `awaiting-verification`，未提交/推送。

---

# P5A-001 R4 收尾（F-02 跨窗口时序 → 开发整改）

2026-11-18（Asia/Shanghai）。对象：R4 反例——A 的生产追逐更新实际移动 B 后，即使 B 尚未 tick，B 也必须拒绝栖息位移；追逐结束后应恢复栖息权限。未修改监理报告；未提交/推送；四轮 M-15 实机证据沿用，不重跑。

## 根因

栖息所有权只读本窗口旗（`_isSocialActive` 等）：窗口 A 的 tick 驱动追逐并经配对提交实际移动窗口 B 时，B 的本地 `_isSocialActive` 要等 B 自己的 `EvaluateSocialTick` 才置位——间隙内 B 的栖息初选（`TrySelectExplicit`）、跟随轮询（`OnTick`）与落位（`ApplyPerchFoot`）都会放行，形成追逐位移与栖息位移双写入者。

## 修复（`src/DesktopPet.App/MainWindow.xaml.cs`）

- 新增 `IsInSharedSocialSession`：经 `Resolve(TestSocialCoordinator, StateContext?.SocialCoordinator, a => a.SocialCoordinator)` 读共享协调器权威会话（`CurrentSession` 活跃且 `InitiatorId/TargetId` 为本宠时为 true），及时生效，不依赖本地旗；与 `CanStartRestReminder` 既有模式一致。
- `IsPerchWriteAllowed => !HasCorePositionConflict && !_isSocialActive && !IsInSharedSocialSession`——栖息初选（`TrySelectExplicit` 入口）与跟随（`OnTick` 守卫早退）共用该判定，跨窗口即时拒绝。
- `ApplyPerchFoot` 守卫追加 `|| IsInSharedSocialSession`（提交层兜底）。
- 会话结束（自然完成/取消）后 `IsInSharedSocialSession` 立即为 false，栖息权限恢复，无额外状态需要复位。追逐位移写入（`IsSocialPositionWriteAllowed`）不读共享会话——追逐本身即社交持有方，语义不变。

## 补证（App 单测，`SocialIntegrationTests` 新增 `Perch_R4F02_ChaseMovesB_BeforeBTick_BRejectsPerchWrites_RestoresAfter`）

- 生产追逐链路（`SetupTwoPets` + `InjectChaseSocial`，只 tick A：invite→Chasing）：A 的下一轮生产 tick 实际移动 B（断言位移非零），此时 `wB.IsSocialActive=false`（B 全程未 tick）且共享会话活跃。
- B 的栖息三路径全部拒绝：`IsPerchWriteAllowed=false`；`ApplyPerchFoot` 返回 false 且零位移；`TrySelectExplicit` 拒绝初选、不激活。
- 跟随路径：反射构造 B 已有栖息绑定 + `TestSnapshotSource` 快照缝（目标移到被遮挡位置）驱动一轮生产轮询 `OnTick`——守卫早退，零写入、保持绑定（WriteRejected 语义，让出后仍能跟随）。
- 追逐自然结束（duration 到期）后：`IsPerchWriteAllowed=true`、`ApplyPerchFoot` 成功——栖息权限恢复，B 仍未 tick。

## 验证

- 构建（`DesktopPet.slnx`）0 警告 0 错误。
- Core 548/548、App 528/528（新增 R4 反例 1 项）全部通过。
- 四轮 M-15 实机证据沿用，本轮不重跑。

## R4 复核请求

请监理复核 F-02 跨窗口时序整改：栖息初选与跟随均读取共享协调器即时会话状态，R4 反例（生产追逐移动 B、B 未 tick 仍拒绝栖息位移、追逐结束恢复）已补证，确认是否关闭。任务状态保持 `awaiting-verification`，未提交/推送。


