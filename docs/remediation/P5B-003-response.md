# P5B-003 整改记录

维护者：开发 AI。流程见 [审查协作规则](../REVIEW_PROCESS.md)。

- 对应审查报告：[P5B-003-review.md](../reviews/P5B-003-review.md)
- 更新时间/时区：2026-11-25 Asia/Shanghai
- 本轮代码版本：未提交/推送。R19 整改（仅驱动 `tools/Invoke-P5B003R9Live.ps1`）：`Get-SafeCursorPoint` 三处候选坐标算式分别加括号（修复 `System.Object[] ... op_Subtraction` 运行异常）、函数定义提前至 SelfTest 之前并新增无鼠标实际调用自测 #36/#37（坐标结构 + 避开宠物/目标区域）；产品 `src/**`、素材、探针 `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs` 零改动；R16/R18 及更早工作区改动仍在。
- 当前边界：**R19 已整改，自测通过（SelfTest 37/37），待监理核对**（非项目任务状态；TASKS 保持 in-progress，监理核对通过前不进后续阶段）；R18 实机轮 run-20261125-185433-fixture 为 3 PASS/2 FAIL（③左侧已接受，④ curious 占用驱动如实拒绝）；F-06 未关闭；`-OnlyChecks "4,7"` 右侧与 land 拖动窄补证待用户授权后由监理执行；F-07 关闭与美术临时接受结论不变。

## docs/reviews/P5B-003-review.md#F-01

- 开发处置：已整改，待复核
- 监理问题（原意）：默认 120×120 下侧边攀抓/上缘移动姿态不可辨认，原手臂仍垂放，新增圆环/色块误绘在耳廓、脸颊与额头，美术风格不一致；不得用"垂臂+叠加圆圈"冒充新肢体。
- 修改说明与文件位置：
  1. 先交付少量候选关键帧（`side_cand_0/1/2` + `edge_cand_0/1/2`，见 `.temp/p5b-003/r1fix-candidates/` 与聚合预览 `preview.html`），经用户确认造型后批量生成并覆盖 assets。
  2. 新方案（真实坐标，以 `idle_0` 为底局部重绘）：先做几何摸底（`diag_geom.py`/`diag_geom2.py`）确认双耳 y8-15、身体 y18-108、腰最窄 y68-72、脚 y104-110 两段 (39,57)+(62,80)；由 y56-72 体侧轮廓反推真实肩部起点（左 (25,61)/右 (95,61)）并从肩部绘制**连体毛绒爪臂**——轮廓深棕半透明描边（α~170-215）+ 毛身色 ~(207,142,84) + FUR_DARK 深爪 + CREAM 爪垫，新增像素簇全部与身体连通，无悬浮圆。
  3. 消除误绘：脸部/耳部/脚带 y104-110 全部保持 idle 原样零改动；新爪臂避开双耳区（y8-20 无新绘色块）；侧伸爪臂肘部避开脸颊轮廓（修正过一遍：side_1/2 右高臂肘部 x103→106，消除脸颊区 x98-99/y32-34 的深棕描边 4px）。
  4. 动作语义：`climb_side` = 左伸高抓 → 双爪承重 → 右伸高抓；`climb_edge` = 身体 y≤102 整体横移 ±3px + 低伸爪（探左 → 居中 → 探右），上缘移动靠整体横移表达，不再叠加几何形状。
  5. `tools/Export-P5B003-ClimbPreview.py` 重写为 R1 F-01 版（连体爪臂生成），批量导出到 `.temp/p5b-003/r1fix-final/` 并覆盖 assets；定稿帧与候选逐字节一致（第 2 次校正后 diffPx=0）；`climb_land_0/1` 与既有 assets 字节一致（identical，收脚回站立方向保留）。
- 自测环境：Windows 11，本机 1920×1080@96DPI；Python 3（PIL）+ 既有 .NET 探针驱动。
- 实际命令、退出码及关键结果：
  - 几何/候选/像素校验脚本（`.temp/p5b-003/diag_geom*.py`、`diag_arms_ref.py`、`r1fix_candidates.py`、`verify_candidates.py`、`diag_pixels8_new.py`、`diag_fix_detail.py`、`cmp_cand_final.py`、`paw_crop.py`）：素材层全部通过——6 帧新增像素簇全部与身体连通（无悬浮圆）；耳区 y8-20 无新绘色块；脚带 y104-110 与 idle 逐像素一致；帧间 diffPx side 1088-2179 / edge 6896-7130（`climb_edge` 三帧有 lean 横移）；素材层面部/耳区与基准（side=idle、edge=同 dx 平移后的 idle）零差异，输出 ALL OK。
  - 生成：`python tools\Export-P5B003-ClimbPreview.py .temp\p5b-003\r1fix-final` → 覆盖 assets 6 张，与 r1fix-final 定稿 SHA256 全等；land 与既有 byte-identical。
  - 驱动重跑（最终轮 `run-20261122-185508`）：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003ClimbPreview.ps1` → 探针 `probe-result.json` exitCode=0，34 帧（idle 1 + side 9 + edge 9 + land 15）齐全。
  - 渲染级断言：`python tools\Verify-P5B003RenderPreview.py` → **34 项 ALL PASS**（`render-summary.txt`）：idle_base 未引入位移（bbox±1、真实底行=110、内容量 6395→6553 在 ±5% 内）；side/edge 各 9 帧与 land 帧脚带 y104-110 中心≈60±1.5、不越出 idle±1、y110 落地；`render-foot-stability.json` allPass=true、failures=[]。
- 人工步骤、观察者和实际现象：候选关键帧预览（`.temp/p5b-003/r1fix-candidates/preview.html`，含新旧方案对照与 GIF）经用户查看并确认造型后批量；未在 120×120 真实窗口做人工观感确认（留给监理复核）。
- 未验证项及原因：默认 120×120 真实窗口/动态回放的人工观感尚未由监理或用户确认（正是 F-01 复核点）；镜像与路线接线未做（本轮边界明确排除）；多屏/混合 DPI 未测（延续任务既有限制）。
- 与监理反例关系：R1 F-01 的两条反例（垂臂+叠加圆圈冒充肢体、耳/脸/额误绘色块）已分别被连体爪臂方案与零改动五官校验所消解；渲染级断言沿用既有几何口径（脚点稳定/落位），未放宽。
- 证据路径：候选预览 `.temp/p5b-003/r1fix-candidates/preview.html`（+ `candidates_4x.png`）；定稿帧 `.temp/p5b-003/r1fix-final/`；最终渲染轮 `.temp/p5b-003/run-20261122-185508/`（`deliverables/` 含 `render-foot-stability.json`、`render-summary.txt`、`preview_side/edge/land.gif`、`climb_keyframes_4x.png`、`climb_zoom_compare_2x.png`）；素材像素校验 `.temp/p5b-003/diag_pixels8_new.py`（输出 ALL OK）。监理报告及 R1 原始证据见 `docs/reviews/evidence/P5B-003-R1/`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：核对 6 张新素材在默认 120×120 下侧边攀抓/上缘移动姿态是否可辨认、耳/脸/额是否仍有误绘、风格是否与既有小熊一致；核对素材层像素证据（脚带同 idle、面部/耳区零差异、无悬浮圆）与渲染轮 34 项 ALL PASS 输出。若通过可关闭 F-01，再由监理在报告中确认状态。

## docs/reviews/P5B-003-review.md#F-01（R2 限定）与 #F-02（R2 新增）

- 开发处置：已整改，自测通过，待监理复核（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意）：F-01 四臂残留——side/edge 仍以 idle 为底 `im.copy()` 保留原垂臂再叠加新臂，默认 120×120 可辨四臂，新臂细长硬边与短圆毛绒风格不符；限定为先交候选确认造型再批量、重绘替换原双臂、保证恰好两只前肢、脸/耳/世界脚点保护可保留但不得以"全图零差异"阻止必要肩臂重绘。F-02 旧素材渲染——最终 run-185508 渲染帧/放大图仍是 R1 旧版，与 run-182213 逐字节比较 33/34 相同；源 assets SHA 与定稿一致不能证明运行时加载了新图；限定为核对探针实际读取目录及 SHA、重建/同步运行资源、新增能发现"源图已换但运行旧图"的内容一致性检查、不以 exitCode=0 或 34 项脚点 PASS 替代内容检查、复验注明新 run。
- 修改说明与文件位置：
  1. F-01 v2 方案（用户已确认造型）：不再以 idle 为底叠加。真正收缩身体侧缘（左 `L_BAND=(10,44)`、右 `R_BAND=(76,110)`）消除 idle 垂臂，从真实肩部 (25,61)/(95,61) 连出恰好两条粗短圆润前肢（深棕半透明描边 + 毛身色 + FUR_DARK 深爪 + CREAM 爪垫），脸 y<44、脚带 y104-110 零改动；side_1/2 右臂肘部外移避脸颊。候选 6 帧（`.temp/p5b-003/r2-candidates/`）经用户浏览器确认 v2 造型后批量出帧。
  2. 批量导出：修正导出脚本两处与候选不一致（`L_BAND=(10,44)`、`R_BAND=(76,110)`；edge_0 臂坐标 (10,68)→(9,78)/(110,68)→(111,78)）后运行 `Export-P5B003-ClimbPreview.py .temp\p5b-003\r2-final`：全部断言过、land 与既有字节一致；逐字节校验 `climb_side_1 == side_cand_highgrab_v2`、`climb_edge_0 == edge_cand_lowmove_v2` 均 bytes_identical、diffPx=0；ASCII 目检双臂从肩连出、无垂臂、脸/脚无改动。
  3. F-02 根因与处置：探针 `FindAssetDirectory()` 首位＝探针 exe 输出目录 `tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\assets\pets\bear`，旧副本未随源 assets 更新致旧图。经 `sync_assets_to_bin.py` 将 6 张新素材覆盖同步到 bin 副本：src==final==bin SHA-256 前 16 位全一致（side_0=A4F15A45…、side_1=938086F7…、side_2=124862F9…、edge_0=7A5279C6…、edge_1=BAC250A5…、edge_2=D1AD86FF…）；character.json 源/bin 一致未动；land 2 张保持既有字节。
  4. Verify 增强（`tools/Verify-P5B003RenderPreview.py`）：①bin 6 张 SHA-256==源 assets 前置断言；②idle 基线 MAJOR%≈0（实测 0.59%）；③渲染帧 vs 源素材内容一致（MAJOR% 分类口径 A_MAX=3/C_MAX=24，素材映射以 events.jsonl `imagePath` 为权威＝运行探针实际加载的素材，非文件名取模）；④`--old-run` 新旧 run 字节对比（side/edge 18 帧必须不同，idle_base+land 素材未改允许相同）。反向验证：旧 run-185508 帧跑新 Verify → ③ 全 FAIL（MAJOR 14–28%）+ idle 基线 PASS，证明检查能识别"运行旧图"。
  5. 内容检查切换帧口径（诊断支撑，非放宽阈值）：新 run 首轮 Verify ③ 有 6 帧 FAIL（climb_side_02/05/08、climb_edge_02/05/08）。`diag_switch_frames.py` 输出 6×3 MAJOR% 矩阵证明全部为**动画切换瞬间采样时序伪影**——动画 300ms/帧、采样 100ms/帧，每阶段第 3 个采样点落到 frameIndex 切换处，实际渲染内容与相邻素材（MAJOR% 0.05–0.17%，与正常帧同量级）精确匹配，事件流声明比渲染内容错一拍；旧 R1 素材对该集合任一素材 MAJOR%≥14。据此将③口径修正为「与同阶段素材集合任一素材匹配（取最小并记录 best-match 证据）」，并在报告中保留 declared vs best 对照。反验证 `verify_reject_old_assets.py`：旧 run-185508 的 18 帧在新口径下仍 18/18 正确判 FAIL（14.19–21.64%），证明检查仍能发现"运行旧图"。
- 自测环境：Windows 11，本机 1920×1080@96DPI；Python 3（PIL）+ 既有 .NET 探针驱动。
- 实际命令、退出码及关键结果：
  - 候选/校验：`verify_r2_candidates.py` ALL OK → 用户确认 v2 → `Export-P5B003-ClimbPreview.py .temp\p5b-003\r2-final` 断言全过（land 与既有 byte-identical）。
  - 素材同步：`sync_assets_to_bin.py` → 6 张 src==final==bin SHA 前 16 位全一致。
  - 驱动重跑：`Invoke-P5B003ClimbPreview.ps1` → 新 run `run-20261122-194743`，探针 exitCode=0、34 帧齐全。
  - Verify：`python tools\Verify-P5B003RenderPreview.py .temp\p5b-003\run-20261122-194743\climb-preview\frames .temp\p5b-003\run-20261122-194743\deliverables --old-run .temp\p5b-003\run-20261122-185508\climb-preview\frames` → **ALL PASS**（idle 位移、① bin SHA、② idle 基线 0.59%、③ 18 帧内容一致 best-match 0.05–0.17%、脚带 34 项、④ side/edge 18/18 与旧 run 字节不同）；`render-foot-stability.json` allPass=true、failures=[]。
  - 反验证：`verify_reject_old_assets.py`（旧 run 18 帧，新口径）→ 18/18 正确 FAIL。
- 人工步骤、观察者和实际现象：v2 候选预览（高伸抓+低姿移动+其余帧聚合）经用户浏览器确认造型后批量；120×120 真实窗口人工观感留给监理复核。
- 未验证项及原因：默认 120×120 真实窗口/动态回放的人工观感尚未由监理或用户确认（正是 R2 F-01 复核点）；镜像与路线接线未做（本轮边界明确排除）；多屏/混合 DPI 未测（延续任务既有限制）。
- 与监理反例关系：F-01 四臂残留→v2 真正收缩侧缘消除垂臂、恰好两条粗短圆润前肢从肩连出，无 `im.copy()` 叠加残留；F-02 旧图渲染→bin 副本同步 + Verify ①SHA 断言 + ③内容一致性检查（含切换帧诊断口径）全链覆盖，复验注明新 run。
- 证据路径：候选 `.temp/p5b-003/r2-candidates/`（+ 用户确认的 v2 预览）；定稿帧 `.temp/p5b-003/r2-final/`（含 `cmp_cand_final` 逐字节校验输出）；同步脚本与 SHA 对照 `.temp/p5b-003/sync_assets_to_bin.py`；新 run `.temp/p5b-003/run-20261122-194743/`（frames 34 + events.jsonl + deliverables/`render-foot-stability.json` allPass=true、`preview_side/edge/land.gif`、`climb_keyframes_4x.png`、`climb_zoom_compare_2x.png`）；切换帧诊断 `.temp/p5b-003/diag_switch_frames.py`；旧图反验证 `.temp/p5b-003/verify_reject_old_assets.py`。监理报告及 R1/R2 原始证据见 `docs/reviews/evidence/P5B-003-R1/`、`P5B-003-R2/`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：核对新 run `run-20261122-194743` 的 GIF/4×/2× 交付与渲染帧，确认运行图确为新素材（双臂连肩、无垂臂四臂残留）、脚点稳定；核对整改记录证据（bin SHA 同步、内容一致性检查含切换帧诊断口径、旧图反验证）。若通过可关闭 F-01/F-02，再由监理在报告中确认状态。

## docs/reviews/P5B-003-review.md#F-03 / F-04 / F-05（R7 技术问题）

- 开发处置：已整改，自测通过，待监理复核（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意）：
  - F-03：到达后路线 `IsActive=false` 但 `climb_land` 仍在播；生产 `PauseAnimation→CancelForPause` 因路线已 inactive 直接返回，动作仍 land、镜像未清，暂停停表后可长期停攀爬帧。原 11 项取消测试均在路线 active 时直接调 `CancelFor*`，未覆盖交接后仍有视觉表现的窗口；缺失动作时 `PlayClimbAction` 先设 ScaleX=-1 再由播放器回退 idle，`EndClimbAction` 又因 CurrentAction 非 climb_* 直接返回，idle 可长期保持镜像。反例 `Reviewer_LandPlayback_PauseMustClearActionAndMirror`、`Reviewer_MissingAction_MustNotMirrorIdle` 失败。
  - F-04：`TryStart`/`DispatchStageAction` 先更新 `_lastClimbActionId/_lastClimbMirrorLeft` 再调用可能直接 return 的 `PlayClimbAction`；初始 curious 等表达导致播放被拒但缓存已标 climb_side，临时表达释放回 idle 后相同请求被当已完成，路线继续移动却不播专用动作。反例 `Reviewer_ExpressionEndsDuringClimb_RetryStagePlayback` 失败；另 hover 分支未排除攀爬动作。
  - F-05：`DispatchStageAction` 非顶部段用 `w1x > footX` 判方向，目标在右贴边后 footX 与 w1x 同为 700 条件翻转，沿墙垂直上爬时镜像从 -1 变 +1。反例 `Reviewer_ClimbingSameWall_MirrorMustNotFlipAtSideContact` 失败。
- 修改说明与文件位置：
  1. `src/DesktopPet.App/Platform/PetWindowClimb.cs`：
     - 新增 `SideWallEpsilonPx=0.5` 常量与 `IsPresentingClimb`（`IsActive || IsClimbActionId(_owner.CurrentAction)`），把"路线活跃"与"攀爬视觉/镜像所有权"解耦（F-03）。
     - `CancelForDrag/CancelForPause/CancelForRecover/CancelForExit` 先决条件由 `!IsActive` 放宽为 `!IsPresentingClimb`——land 仍在播（路线已结束）时生产中断同样清理动作与镜像（F-03）。
     - `TryStart` 起攀缓存只在 `_owner.PlayClimbAction("climb_side", mirrorLeft)` 返回 true 后记录；到达分支 `PlayClimbAction("climb_land")` 返回 true → `StopCore(keepLand:true)`，false → `StopCore(keepLand:false)` 立即回 idle 复位镜像（F-04）。
     - `DispatchStageAction`：缓存仅在实际成功播放后更新；非顶边镜像逻辑改为 `Math.Abs(w1x - footX) <= 0.5 ? _lastClimbMirrorLeft : w1x > footX`，脚点贴到侧边轴（数值相等误差内）保持上一镜像不翻转（F-05）；顶边上缘段仍按 `landingX > footX` 实际水平移动方向分派。
  2. `src/DesktopPet.App/MainWindow.xaml.cs`：
     - `PlayClimbAction` 返回 bool：当前非 idle/非 climb_* 守卫拒绝 → false 不改镜像；`_character.Actions.ContainsKey(actionId)` 缺失检测 → false（不改动作不改镜像，不再出现缺失回退把 idle 帧镜像显示）；播放后防御性复核 `IsClimbAction(CurrentActionId)`，失败复位 ScaleX=1 并返回 false（F-04）。
     - `EndClimbAction`：所有清理路径（keepLand=false）先无条件 `ClimbMirrorTransform.ScaleX=1`；仅当前动作确为 climb_* 时才 `PlayAction("idle")`，不误清更高优先级表达（F-03）。
     - hover 分支追加 `!(_petClimb?.IsActive ?? false)` 与 `!IsClimbAction(_animPlayer.CurrentActionId)` 守卫——攀爬路线进行中或 climb_* 动作呈现中（含到达后 land 播完前）都不触发好奇（F-04）。
  3. `tests/DesktopPet.App.Tests/ClimbActionTests.cs`：新增 8 项反例/正例（见下）。测试全部走生产路径（`PauseAnimation`、`OnUpdateTickTest`、`TestGetCursorPos`、`TestGetElapsedMs`、`climb.Tick`），不直接手工调 `CancelFor*` 冒充生产中断。
- 自测环境：Windows 11，本机 1920×1080@96DPI；.NET 10 SDK 10.0.402。
- 实际命令、退出码及关键结果：
  - `dotnet build tests\DesktopPet.App.Tests\DesktopPet.App.Tests.csproj -c Debug` → 0 警告 0 错误。
  - `dotnet test tests\DesktopPet.App.Tests\DesktopPet.App.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~ClimbActionTests"` → **18/18 通过**（原 10 项 + 新增 8 项）。
  - `--filter "FullyQualifiedName~ClimbEntryTests"` → **14/14 通过**（正式入口回归）。
  - `dotnet test DesktopPet.slnx -c Debug --no-build` → 复跑两次均 **1165/1165 通过**（Core 563 + App 602，0 失败 0 跳过）；首次运行出现一次 VSTest MSB4181（任务层无具体用例失败的偶发），两次复跑稳定全绿。
  - `dotnet build DesktopPet.slnx -c Debug -warnaserror --no-restore` → 0 警告 0 错误。
  - 资源一致性：bin 复制目录 9 张素材 + character.json SHA-256 与源全一致（mismatch=0）；本轮未改素材/character.json。
  - 复验补充（2026-11-23，本轮仅修正测试断言时序，产品代码未改）：`dotnet build src\DesktopPet.App\DesktopPet.App.csproj -c Debug -p:TreatWarningsAsErrors=true` → 0 警告 0 错误；`dotnet test tests\DesktopPet.App.Tests\... --filter "FullyQualifiedName~ClimbActionTests"`（重新编译测试项目后）→ **18/18 通过**；`--filter "FullyQualifiedName~ClimbEntryTests"` → **14/14 通过**；全量 `--no-build` → **1165/1165 通过**（Core 563 + App 602，0 失败 0 跳过），0 回归。上一轮 `--no-build` 复验 1 失败系测试 DLL 未重编译 + tick@600 单 tick 断言时序错误所致，均已在本轮消除。
- 新增测试逐项（R7 反例对应）：
  - `LandPlayback_PauseClearsActionAndMirror`（F-03 / Reviewer_LandPlayback_PauseMustClearActionAndMirror）：到达播 land → 路线结束 → 生产 `PauseAnimation()` → 断言 CurrentAction=idle、ScaleX=1。
  - `MissingAction_DoesNotMirrorIdle`（F-03/F-04 / Reviewer_MissingAction_MustNotMirrorIdle）：`PlayClimbAction("climb_missing", true)` 返回 false、动作保持 idle、ScaleX=1（缺失不镜像 idle）。
  - `ExpressionDuringClimb_ReplaysSideAfterRelease`（F-04 / Reviewer_ExpressionEndsDuringClimb_RetryStagePlayback）：先 play curious → 生产 `TryStart`（Started 但播放被拒）→ 表达释放回 idle → 下一 tick `DispatchStageAction` 经生产 `climb.Tick` 补播 climb_side 并置镜像。
  - `SameSideWall_ContactKeepsMirror_NoFlip`（F-05 / Reviewer_ClimbingSameWall_MirrorMustNotFlipAtSideContact）：目标在右起攀 -1 → Tick200 靠边保持 -1 → Tick400 贴边仍 -1 → Tick600 贴边向落位方向推进一格仍 side/-1 → Tick800 脚点 (730,420) 满足 onTopEdge 切 climb_edge、镜像按落位方向保持 -1（同墙垂直上爬不翻转）。复验补充（2026-11-23）：动作分派用推进前脚点判段，tick@600 起点 (700,480) y=480>420 仍属侧边段、tick@800 起点 (730,420) 才切 edge——预先单 tick 断言 tick@600 即切 edge 属测试时序错误，已按既有 `SideToEdge_SwitchesAction_KeepsMirrorTowardTarget` 时序修正断言，产品分派逻辑未改。
  - `LeftSideWall_KeepsNoFlip_ThenEdgeFollowsLanding`（F-05 双侧补充）：目标在左全程 ScaleX=1 不翻转，贴边后仍 1，贴边后仍 climb_side。
  - `HoverDwell_DoesNotOverrideClimbPresentation`（F-04 生产 hover 正例）：光标经 `PetImageGeometry.OpaqueBounds` 命中身体、dwell 1300ms≥1200ms，攀爬进行中动作仍 climb_side、镜像不受影响。
  - `HoverDwell_TriggersCurious_WhenNotClimbing`（F-04 hover 反例）：同样设置非攀爬时正常触发 curious，证明守卫不误伤。
  - `OriginalAssertionUpdate`：原 `MissingAction_FallsBackToIdle_NoCrash` 旧注释/断言按新语义修正为"缺失=拒绝播放+随后合法动作正常播放+方向正确"，未回退产品 bool 返回（保留历史通过记录）。
- 人工步骤、观察者和实际现象：反例先落盘后验证，全部经生产入口驱动；未占用真实屏幕做实机（R7 结论明确暂不整套实机，先修技术反例，接线视觉/输入验收后续安排）。
- 未验证项及原因：真实鼠标 hover/暂停/拖动的实机观感与输入链（TESTING 既有 M-17/M-18 实机入口可复用，本轮未跑）；多屏/混合 DPI 未测（延续任务既有限制）；临时美术豁免（F-01 后置）与 F-02 关闭结论保持不变，未重新打磨素材。
- 与监理反例关系：三条 R7 反例（land 播放中暂停残留、缺失动作镜像 idle、贴边同墙翻转）均被上述新测试经生产路径覆盖并转绿；hover 正反例补足 F-04 的运行期低优先级不覆盖问题；F-05 补左墙跨"靠近→贴边→上爬"序列（双侧）。
- 证据路径：测试源码 `tests/DesktopPet.App.Tests/ClimbActionTests.cs`；产品代码 `src/DesktopPet.App/Platform/PetWindowClimb.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`；构建/测试输出见本记录命令段。监理报告见 `docs/reviews/P5B-003-review.md`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：核对 F-03 land 播放中各生产中断（暂停/拖动/找回/退出）确实清理动作与镜像（`IsPresentingClimb` 判定与 `EndClimbAction` 无条件复位）；F-04 缓存只在真实成功播放后更新、表达释放后下一 tick 补播、hover 不覆盖攀爬（`PlayClimbAction` bool 返回 + hover 双守卫）；F-05 贴边沿墙不翻转（SideWallEpsilonPx 保持上一镜像）+ 上缘按实际方向分派；核对新增 8 项测试与前序 18/18、入口 14/14、全量 1165/1165、warnaserror 0/0、资源一致性。若通过可关闭 F-03～F-05，再由监理在报告中确认状态。

## 后续轮次与复核引用

按日期追加。收到监理结论后只链接原报告的复核节，不在本文件代写或改写监理结论。

## docs/reviews/P5B-003-review.md#F-03 / F-04（R8 限定补边）

- 开发处置：已整改，自测通过，待监理复核（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意）：
  - F-03：栖息实际脱离（目标失效/最小化/遮挡，或主动脱离/拖动/暂停/找回——生产 `Perch.OnTick` 轮询 Cleared/no-landing/occluded 与 `Detach/DetachForPause/CancelForDrag` 均收敛到 `DetachInternal`）时，到达交接后仍由攀爬持有的落位表现（`climb_land` 在播、路线已结束）未释放——land 动作连同镜像悬挂在宠物体上。
  - F-04：阶段缓存相还只说明"上一 tick 实际成功播过该组合"；已成功播过后被更高优先级表达（curious 等）覆盖、表达释放回 idle 后，缓存相等但真实播放已不是请求阶段——`DispatchStageAction` 仍返回不重试，路线继续移动却不播专用动作。
- 修改说明与文件位置：
  1. `src/DesktopPet.App/Platform/PetWindowClimb.cs`：新增 `CancelForPerchDetached()`——`IsPresentingClimb` 守卫 + `StopCore()` + Emit(`climb-cancelled`,`perch-detached`)；与 `CancelForDrag` 同语义不移动、不恢复位置（栖息脱离=宠物留在当前位置），不与 Pause/Recover/Exit 既有清理重复（已清理后 IsPresentingClimb 为 false → no-op）（F-03）。
  2. `src/DesktopPet.App/MainWindow.xaml.cs`：新增 internal `ReleaseClimbForPerchDetach()` => `_petClimb?.CancelForPerchDetached()`——经 `_petClimb?` 避免纯栖息场景懒创建攀爬实例（F-03 接线）。
  3. `src/DesktopPet.App/Platform/PetWindowPerch.cs`：`DetachInternal(reason)` 尾部（`_hasPrePerchPosition=false` 之后、Emit 之前）调用 `_owner.ReleaseClimbForPerchDetach()`——全部脱离路径（OnTick Cleared/no-landing/occluded、Detach/DetachForPause/CancelForDrag）统一收口释放攀爬落位表现（F-03）。
  4. `src/DesktopPet.App/Platform/PetWindowClimb.cs` `DispatchStageAction`：缓存相等提前 return 改为——先核对 `_owner.CurrentAction == actionId && (_owner.ClimbMirrorTransform.ScaleX < 0) == mirrorLeft`（ScaleX 仅 -1/1 两态）；一致才 return（正常同阶段不重启），不一致继续 `PlayClimbAction` 重试（守卫通过则补播并更新缓存）（F-04）。
- 接线兼容性核对（阅读代码确认，未实测）：`PauseAnimation`/`RecoverPet`/`StartDragging` 均先调 `_petPerch?.DetachForPause/Detach("recovered")/CancelForDrag()`（Perch active 时才触发 DetachInternal→新清理）再调 `_petClimb?.CancelFor*()`——新清理执行后 `IsPresentingClimb` 已 false，后续调用 no-op，无重复事件；Perch 非 active 时新接线不触发，行为不变。`CancelForExit` 走 `RestorePrePerchPositionForExit`（独立退出路径，不经 DetachInternal），Exit 自身清理仍在攀爬侧，不重复。
- 自测环境：Windows 11，本机 1920×1080@96DPI；.NET 10 SDK 10.0.402。
- 实际命令、退出码及关键结果：
  - `dotnet build tests\DesktopPet.App.Tests\DesktopPet.App.Tests.csproj -c Debug` → 0 警告 0 错误。
  - `dotnet test tests\DesktopPet.App.Tests\DesktopPet.App.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~ClimbActionTests"` → **22/22 通过**（R7 18 项 + 新增 4 项）。
  - `--filter "FullyQualifiedName~ClimbEntryTests"` → **14/14 通过**（正式入口回归）。
  - `dotnet test DesktopPet.slnx -c Debug --no-build` → **1169/1169 通过**（0 失败 0 跳过）。
  - `dotnet build DesktopPet.slnx -c Debug -warnaserror --no-restore` → 0 警告 0 错误。
  - 资源一致性：`assets\pets` 范围内 35 个文件双向 SHA-256 校验 bin 复制目录与源全一致（mismatch=0、missing=0）；本轮未改素材/character.json。`assets\art\bear-v1\` 的 6 个设计源文件不在 csproj 复制范围（`DesktopPet.App.csproj` 仅 `assets\pets\**`），非资源缺失。
- 新增测试逐项（R8 反例对应）：
  - `LandPlayback_TargetGoneViaPerchOnTick_ReleasesClimbPresentation`（F-03 反例 / Reviewer_LandTargetGone_MustReleasePresentation）：到达播 land → 生产 `Perch.OnTick` 轮询（反射驱动，仅替换 `TestSnapshotSource` 为 null，Observe 状态机/脱离收口原样走生产路径）→ 目标失效 Cleared → DetachInternal → 断言 Perch 脱离 + `AssertCleanIdle`（动作回 idle、镜像复位）。
  - `LandPlayback_TargetValidViaPerchOnTick_KeepsClimbPresentation`（F-03 正例）：到达播 land → 生产 `Perch.OnTick` 轮询，目标有效（外框变化，同句柄同身份）→ follow 分支（测试缝遮挡采样判定不遮挡）→ Landed 跟随 → 断言 Perch 保持 active、`climb_land` 不被误清、镜像保持——修复不得在目标有效时误清。
  - `StagePlayedThenOverridden_ReleaseReplaysStage`（F-04 反例 / Reviewer_AlreadyPlayingThenOverridden_MustRetryStage）：起攀成功（climb_side 已实际播出并缓存）→ 临时表达 curious 覆盖 → 释放回 idle → `climb.Tick(50)` 生产推进 → 缓存相等但真实播放非请求阶段 → 断言补播 climb_side 并置镜像（修复前缓存相等提前 return 保持 idle 失败）。
  - `SameStageTick_DoesNotRestartPlayback`（F-04 正例）：起攀成功 → 同阶段 `climb.Tick(200)`、`climb.Tick(400)` 多 tick → 反射读 `AnimationPlayer._actionStartMs` 采样 A/B/C（只做相对比较，不依赖真实时钟绝对值）→ 断言 B==A、C==B（PlayAction 未被重复调用，正常同阶段不重启）且每次 CurrentAction==climb_side。
- 人工步骤、观察者和实际现象：反例按监源码逐行转写为先导规格，全部经生产入口驱动（F-03 走 `Perch.OnTick` 生产轮询反射、F-04 走 `climb.Tick` 生产推进），不直接调 `Detach`/`CancelFor*` 冒充生产中断；未占用真实屏幕做实机（技术闭环前不整套实机，接线视觉/输入验收后续安排）。
- 未验证项及原因：真实窗口目标失效/遮挡/拖动的实机观感与输入链（TESTING 既有实机入口可复用，本轮未跑）；多屏/混合 DPI 未测（延续任务既有限制）；F-05、F-01 临时美术豁免及 F-02 关闭结论保持不变，未重做已通过项。
- 与监理反例关系：R8 三条反例（`Reviewer_LandTargetGone_MustReleasePresentation`、`Reviewer_AlreadyPlayingThenOverridden_MustRetryStage` 及 R8 复核的缓存相等仍须核对真实播放语义）均被上述新增测试经生产路径覆盖并转绿；补 F-03 正例（目标有效不误清）与 F-04 正例（同阶段不重启）防止修复过度。
- 证据路径：测试源码 `tests/DesktopPet.App.Tests/ClimbActionTests.cs`（22 项）；产品代码 `src/DesktopPet.App/Platform/PetWindowClimb.cs`、`src/DesktopPet.App/Platform/PetWindowPerch.cs`、`src/DesktopPet.App/MainWindow.xaml.cs`；监理 R8 反例副本 `docs/reviews/evidence/P5B-003-R8/ClimbActionTests.reviewer-copy.cs.txt`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：核对 F-03 栖息实际脱离（生产 Perch.OnTick 目标失效反例）确实释放攀爬落位表现与镜像、目标有效正例不误清；F-04 阶段缓存相等仍核对真实播放（已播放→覆盖→释放补播反例）、同阶段正常播放不反复重启（正例）；核对新增 4 项测试与前序 22/22、入口 14/14、全量 1169/1169、warnaserror 0/0、资源一致性（assets\pets 35 文件 SHA 一致）。若通过可关闭 R8 限定项，再由监理在报告中确认状态。

## docs/reviews/P5B-003-review.md#R9 最后验收边界（R9 观测与驱动准备，2026-11-23）

- 开发处置：观测补强与针对性驱动已就绪、构建过、无鼠标冒烟通过；真实输入回合待用户授权后由监理执行。TASKS 保持 in-progress，整体仍 awaiting-verification。
- 监理 R9 边界（原报告 201-207 行）：F-03/F-04 关闭、整体尚不标 done；检查现有探针与 M-17/M-18 驱动无 CurrentAction/ClimbMirrorTransform.ScaleX 观测，旧清单不能证明本轮新动作/镜像清理；下一步只补隔离探针观测与针对性驱动，不修改产品行为：记录阶段动作/镜像、路线与栖息状态、必要渲染帧/默认尺寸截图；验证左右侧接近→贴边→上缘→land 正常回 idle，以及 hover 不抢占、land 期间暂停/拖动/找回/目标失效清理；已有自动测试覆盖的罕见故障注入不伪装成真实鼠标现象，证据层次逐项标明；不机械重跑 M-17/M-18 全部历史检查、不为临时美术再迭代资源；准备就绪后用户允许时再占鼠标；不提交/推送、不进入后续功能。
- 本轮文件改动（全部在探针与驱动，产品代码零改动——`src/DesktopPet.App/**`、XAML、assets 均未触碰）：
  1. `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs`：`WriteState` 心跳新增 `currentAction`（`mainWindow.CurrentAction` public 属性直访）与 `climbMirrorScaleX`（`ClimbMirrorTransform` 是 XAML 生成 internal 字段，App 的 `InternalsVisibleTo` 仅对 `DesktopPet.App.Tests`，探针按 ClimbPreviewScenario 先例反射读 `ScaleTransform.ScaleX`，失败置 null 供驱动区分，不静默默认值）；命令流新增 `capture <name>`（反射 PetImage + RenderTargetBitmap 120×120 → `frames/<name>.png` + `frame-captured` 事件）与 `recover`（直调 public `mainWindow.RecoverPet()`，走生产找回清理路径）。
  2. `tools/Invoke-P5B003R9Live.ps1`（新建，1064 行，PARSE_OK）：针对性驱动，复用 M-18 助手（目标归属/诱饵/PInvoke/真实右键菜单/UIA/真实左键/拖动三重校验），新增左右侧放置与阶段采样；检查清单 ⑩ 项见下。
- R9 驱动检查清单 ⑩ 项：①归属确认（目标窗口与诱饵）②心跳含新字段 ③左侧链（贴边镜像=1→上缘→land→回 idle）④右侧链（镜像=-1→回 idle）⑤hover 不抢占（真实悬停 2.5s 采样保持 climb_*）⑥land 期间 pause→resume→回 idle（脚冻结判定）⑦land 期间拖动→`climb-drag-preempted`→回 idle 镜像复位不回拉 ⑧land 期间 `recover`→清理 ⑨land 期间关闭归属目标（WM_CLOSE）→清理 ⑩exitCode=0 + 诱饵存活。每项 detail 标注证据来源（心跳/事件/渲染帧/自动测试已覆盖），渲染帧落 `frames/left-climb.png` 等。主流程共用 `Wait-Land`（先等 `climb-completed`，再等当前动作 climb_* 或已回 idle 的终态）。
- 构建：`dotnet build tests\DesktopPet.DiagnosticProbe\DesktopPet.DiagnosticProbe.csproj -c Debug -p:TreatWarningsAsErrors=true` → 0 警告 0 错误。
- 无鼠标冒烟（`.temp/p5b-003/smoke-r9/`，不占真实鼠标，目标标题 R9SmokeNoSuchWindow-DOESNOTEXIST 保证无真实占用）：心跳含 `currentAction=idle` + `climbMirrorScaleX=1`；`capture smoke-frame` 产生 `frames/smoke-frame.png`（120×120，15101B）+ `frame-captured` 事件；`recover` 安全消费；`quit` 后 result.json exitCode=0。教训已吸收：起探针前清理 state/result 残留文件，且必须先读到本轮新心跳再发命令（探针启动会清空命令文件）。
- 证据层次说明：⑧recover 与 land 期间暂停/拖动的生产清理路径已有自动测试覆盖（R8 新增 4 项 + 既有 22/22 攀爬专项），驱动实机轮是补真实输入与渲染观察，不作为伪造的罕见故障注入：真实鼠标轮只呈现可复现的普通交互现象（接近→贴边→上缘→land 回 idle、hover、暂停恢复、拖动打断、recover、目标关闭），与自动测试的分工在每项 detail 中标注。
- 人工步骤、观察者和实际现象：本轮未占用真实屏幕/鼠标；探针只观测、命令只触发生产入口，无产品行为改动。
- 未验证项及原因：真实输入回合未跑（需用户授权占鼠标后由监理执行，授权后注入前需 SetCursorPos 回读±2 + WindowFromPoint 根窗口==目标前置校验，脚本已内置）；多屏/混合 DPI 未测（延续任务既有限制）；不重跑 M-17/M-18 全套与全量 1169。
- 证据路径：探针 `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs`；驱动 `tools/Invoke-P5B003R9Live.ps1`；冒烟产物 `.temp/p5b-003/smoke-r9/`；监理 R9 报告 `docs/reviews/P5B-003-review.md`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：待用户授权占鼠标后运行 `pwsh -File tools\Invoke-P5B003R9Live.ps1 -DurationSec <值>`，按 ⑩ 项核对左右阶段切换、land 回 idle、hover 不抢占与 land 期间暂停/拖动/找回/目标失效清理，产出 r9-summary.json/frames/transcript 后判定是否满足 R9 最后验收边界；本整改记录不代写监理结论。

## docs/reviews/P5B-003-review.md#R10 F-06（整改，2026-11-23）

- 开发处置：已整改，自测通过——探针构建 0 警告 0 错误、驱动脚本 PARSE_OK、无鼠标 SelfTest 11 项正反例全 PASS（exit 0）；真实输入回合待用户授权后由监理执行。TASKS 保持 in-progress，整体仍 awaiting-verification。
- 监理 R10 F-06 边界（原报告 214-230 行，5 条限定）：
  1. 中断前必须确认本次新鲜状态确为 `climb_land`（completed 后 + 栖息曾活跃）；land 已自然结束则失败/补测，不得算覆盖。
  2. 暂停后、恢复前立即检查动作清理与镜像复位；字段缺失或旧心跳不得通过。
  3. 截图记录实际动作、镜像、采集时间（sidecar）。
  4. 10 项唯一完整、全绿、无异常、探针成功才退出 0；失败返非零；安全清理本轮自建对象（lure 内容/标题/位置核对后才清理，限定精确进程）。
  5. 无鼠标受控负例覆盖：land 过期 / 字段缺失 / 暂停仍 land / 不完整清单。
- 本轮文件改动（全部在探针与驱动，产品代码零改动——`src/DesktopPet.App/**`、XAML、assets 均未触碰）：
  1. `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs`：提取 `ReadClimbMirrorScaleX()`（`WriteState` 与 `capture` 共用，反射读 `ScaleTransform.ScaleX`，失败置 null 供驱动区分）；`capture` 命令新增 sidecar `frames/<name>.json`（记录采集瞬间真实值：tMs/action/mirror/climbActive/perchActive/name）。
  2. `tools/Invoke-P5B003R9Live.ps1`：新增纯判定函数与 SelfTest（见下），⑥⑦⑧⑨ 全部改造为 `-RequireInLand` 前置 + sidecar 证明 + 中断后 fresh 心跳 `Test-NonClimbIdle`；⑩/finally 收尾重写；脚本末尾按 `$script:driverExit` 显式退出。
- R10 驱动改造要点（按 5 条限定逐条对应）：
  - 判定函数（真实驱动与 SelfTest 共用同一实现，防"受控检查/实机判定"两套逻辑）：`Test-FreshState`/`Wait-FreshState`（tMs 必须严格大于已知最新，防旧心跳）、`Test-NonClimbIdle`（null 字段/非 climb_*/镜像 1/双 inactive 才过）、`Get-LandVerdict`（in-land/land-finished/fields-missing/not-climbing/other-climb 五态）、`Test-CompleteChecks`（清单恰 10 项、名称唯一、全 PASS 才过）。
  - `Wait-Land -RequireInLand`：先等 `climb-completed`，其后连续 fresh 心跳采样，必须亲眼采到 `climb_land` 且栖息曾 active（`perchSeen`）才 Ok；`land-finished`（land 已自然结束）/`fields-missing`/超时统一返回 Ok=false（对应限定 1/2）。
  - ⑥暂停：`-RequireInLand` → sidecar 证明 → `pause` → 恢复前立即 fresh 心跳 `Test-NonClimbIdle`（暂停当下必须非 climb_* 且镜像 1）→ `resume` 后 2.5s 观察不续播 → 最终 clean idle（限定 2）。
  - ⑦拖动/⑧recover/⑨目标失效：同前置模式；⑨ 用 WM_CLOSE 关归属目标后连续 fresh 心跳等双 inactive，最终 `Test-NonClimbIdle`（限定 1）。
  - ⑩：诱惑文件内容（非仅存在）、窗口标题/位置与 `$script:lureSnapshot` 比对、windowless 进程存活核对；`Test-CompleteChecks` 对 10 项盖章；`exitCode` 非零或清单不完整时 `$script:driverExit=1`；finally 中 lure 核对通过后仅对精确进程/窗口/表单清理（`windowlessLure.Kill()` + `lureWindow.Dispose()`，不越界），探针先 `SendMessage(WM_CLOSE)` 再 `WaitForExit` 未退才 `Kill()`（限定 4）。
  - `-SelfTest` 11 项正反例（限定 5）：in-land（正）、land 过期→land-finished（反）、字段缺失（反）、暂停仍 land→`Test-NonClimbIdle` FAIL（反）、未清理镜像/climbActive（反）、干净 idle（正）、旧心跳被拒（反）、新心跳通过（正）、9/10 不完整（反）、10 项含 FAIL（反）、10 唯一全 PASS（正）；SelfTest 在 lure/窗口/进程创建前 exit，零鼠标/窗口/文件副作用。
- 自测环境：Windows 11，本机 1920×1080@96DPI；.NET 10 SDK 10.0.402；pwsh 7。
- 实际命令、退出码及关键结果：
  - 探针构建：`dotnet build tests\DesktopPet.DiagnosticProbe\DesktopPet.DiagnosticProbe.csproj -c Debug -p:TreatWarningsAsErrors=true` → 0 警告 0 错误。
  - 驱动语法：`[Parser]::ParseFile(tools\Invoke-P5B003R9Live.ps1)` → PARSE_OK（0 语法错误）。
  - SelfTest：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003R9Live.ps1 -SelfTest` → **11/11 PASS，RESULT=PASS，exit 0**；零副作用核实：`.temp\p5b-003\r9` 目录未创建，无 lure/probe/窗口残留。
  - 过程中定位并修复一处 SelfTest 测试数据构造缺陷（判定函数本身正确）：清单项 `"第$_项"` 中 `$_项` 被 PowerShell 当变量名整体解析为空、10 项全变同值，`Test-CompleteChecks` 正确报"item names not unique"；改为 `"第$($_)项"` 后 11/11 转绿。
- 人工步骤、观察者和实际现象：本轮未占用真实屏幕/鼠标；探针只观测、命令只触发生产入口，无产品行为改动。
- 未验证项及原因：真实输入回合未跑（需用户授权占鼠标后由监理执行，脚本已内置 SetCursorPos 回读±2 + WindowFromPoint 根窗口前置校验）；多屏/混合 DPI 未测（延续任务既有限制）；不重跑 M-17/M-18 全套与全量 1169。
- 与监理反例关系：R10 反例 `wait-land-counterexample.json`（completed 存在 + 动作 idle + 旧 Wait-Land Ok=true）被 `Wait-Land -RequireInLand` 的 fresh 心跳 land 判定与 `land-finished` 失败路径直接拒绝；字段缺失/暂停仍 land/不完整清单等负例由 SelfTest 5-11 项受控覆盖。
- 证据路径：探针 `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs`；驱动 `tools/Invoke-P5B003R9Live.ps1`；SelfTest 命令输出见本记录命令段；监理 R10 报告 `docs/reviews/P5B-003-review.md`（214-230 行，只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：预检 R10 5 条限定对应的驱动判定（RequireInLand 中断前置、暂停即时清理、sidecar 采集事实、10 项完整+退出码、诱饵安全清理）与 SelfTest 负例输出；真实输入回合待用户授权后另跑，本整改记录不代写监理结论。

## docs/reviews/P5B-003-review.md#R11 F-06（整改，2026-11-23）

- 开发处置：已整改，自测通过——驱动脚本 PARSE_OK、无鼠标 SelfTest 14 项正反例全 PASS（exit 0）、零副作用核实通过；真实输入回合待用户授权后由监理执行。TASKS 保持 in-progress，整体仍 awaiting-verification。
- 监理 R11 F-06 边界：区分「正常落位收尾」与「中断清理」。正常收尾应为 idle、镜像 1、路线结束（climbActive=false）、栖息仍活跃（perchActive=true）；不得为测试通过解除合法栖息。正常链（③④）及 hover（⑤）收尾需有界等待 land 真正播完。SelfTest 补「正常已栖息通过」「仍在 land 拒绝」「正常栖息丢失拒绝」三项，保留原负例。不改产品逻辑、动画时长。
- 本轮文件改动（仅驱动脚本，产品代码/探针零改动——`src/DesktopPet.App/**`、XAML、assets、`WindowClimbLiveScenario.cs` 均未触碰）：
  1. `tools/Invoke-P5B003R9Live.ps1` 新增 `Test-NormalLandingIdle`（缺字段即 FAIL；climbActive/climb_*/perchActive=false/mirror≠1 → FAIL）与 `Wait-NormalLandingIdle $AfterMs $LandSeenHint`（两阶段有界等待：阶段 1 先见 `climb_land`，阶段 2 再等非 climb_* 且 -not climbActive 且 perchActive=true 且 mirror=1；阶段 1 未先见 land 就回非 climb_* idle → FAIL；栖息丢失/镜像未复位 → 立即 FAIL；一直 land → 超时 FAIL）。
  2. ③ 左链（原 1086-1090）与 ④ 右链（原 1128-1132）收尾：`Wait-FreshState`+`Test-NonClimbIdle` 替换为 `Wait-NormalLandingIdle $sinceL ($null -ne $landAt)`（采样循环是否采到 land 作 hint）。
  3. ⑤ hover 收尾（原 1160-1193）：采样循环新增 `$hoverSeenLand` 标记（采到 climb_land 置 true）；原 `Wait-Land`+`Wait-FreshState`+`Test-NonClimbIdle` 组合替换为 `Wait-NormalLandingIdle $hoverStartMs $hoverSeenLand`；`capture hover-samples` 命令先发、sidecar 在 land 等待期间异步落盘，`Wait-CaptureSidecar` 移后取回。
  4. ⑥⑦⑧⑨ 中断清理保持 R10 `-RequireInLand` 前置 + fresh 心跳 `Test-NonClimbIdle`（perchActive=false 要求不变）模板，已核实未动。
  5. SelfTest（原 509-517）追加三项：正常已栖息（idle/mirror=1/climbActive=false/perchActive=true）→ PASS；仍在 land（climb_land/climbActive=true）→ FAIL；栖息丢失（idle/mirror=1/climbActive=false/perchActive=false）→ FAIL；原 11 项未动。
- 双判定关系：正常收尾只允许 `Test-NormalLandingIdle`（栖息仍活跃才过），中断清理沿用 `Test-NonClimbIdle`（栖息脱离后的清理状态），不混用、不把 ⑥⑦⑧⑨ 判据改成允许 perchActive=true；`Ensure-IdleForClimb`（起攀前解除既有栖息）与「正常落位后栖息仍活跃」不冲突。
- 自测环境：Windows 11，本机 1920×1080@96DPI；pwsh 7。
- 实际命令、退出码及关键结果：
  - 驱动语法：`[Parser]::ParseFile(tools\Invoke-P5B003R9Live.ps1)` → PARSE_OK（0 语法错误）。
  - SelfTest：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003R9Live.ps1 -SelfTest` → **14/14 PASS（total=14 failed=0），RESULT=PASS，exit 0**；新增三项输出 `positive normal-landing idle (perch active)`/`negative still-in-land rejected`/`negative perch-lost rejected` 均 PASS，第 13 项 Reason=`still climbActive (climb_land)`、第 14 项 Reason=`perch lost (正常落位后栖息应仍活跃)`。
  - 零副作用核实：`.temp\p5b-003\r9` 目录不存在（r9 dir absent），无 run 产物；SelfTest 在 lure/窗口/进程创建前 exit，无鼠标/窗口/文件副作用。
- 人工步骤、观察者和实际现象：本轮未占用真实屏幕/鼠标；仅编辑驱动脚本并运行无鼠标 SelfTest，探针只观测、命令只触发生产入口，无产品行为改动。
- 未验证项及原因：真实输入回合未跑（需用户授权占鼠标后由监理执行，脚本已内置 SetCursorPos 回读±2 + WindowFromPoint 根窗口前置校验）；多屏/混合 DPI 未测（延续任务既有限制）；不重跑 M-17/M-18 全套与全量测试、不重做 R10 已补部分。
- 与监理反例关系：R11 反例 `normal-perched-state.json`（合法 idle+镜像 1+route inactive+perch active）现可被 `Test-NormalLandingIdle` 直接接受（PASS），不再拒绝正常栖息成功状态；「仍在 land」「栖息丢失」「land 未播完提前终态」等负例由 SelfTest 12-14 项及 `Wait-NormalLandingIdle` 两阶段等待受控覆盖；不用 `Wait-Land`（非 Require）普通模式做正常链终态（其「见非 climb_* 就 break」会在 land 未播完时提前终态）。
- 证据路径：驱动 `tools/Invoke-P5B003R9Live.ps1`（新增判定函数与收尾改造见上）；SelfTest 命令输出见本记录命令段；监理 R11 报告 `docs/reviews/P5B-003-review.md`（只读，未改动）及反例 `docs/reviews/evidence/P5B-003-R11/normal-perched-state.json`。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：预检 R11 双判定区分（正常落位收尾 `Test-NormalLandingIdle` vs 中断清理 `Test-NonClimbIdle`）、`Wait-NormalLandingIdle` 两阶段有界等待（含 hover 的 `$hoverSeenLand` 标记）、SelfTest 三项新正反例输出与 14/14 结果；真实输入回合待用户授权后由监理执行，本整改记录不代写监理结论。

## R11 真实输入轮后续（开发侧执行，2026-11-24；仅改驱动脚本，产品代码仍冻结）

- 背景：用户已授权占鼠标执行完整十项真实输入轮。运行 `tools/Invoke-P5B003R9Live.ps1 -DurationSec 960`，全程真实 SetCursorPos/mouse_event 注入 + 目标锁定纪律（本轮独有标题标记 + 精确 PID + StartTime 归属 + SetCursorPos 回读±2 + WindowFromPoint 前置校验 + 诱饵存活证明清理）。
- 第 4 轮（run-20261123-215721，21:57 非夜间）：检查①② PASS、③ FAIL 中止。两点修复（均驱动侧，`Place-TargetNearFoot` 852-899 行与探针 exe）：
  1. `Place-TargetNearFoot` 翻转条件 bug：旧写法 `($tx - 800) < sb.Left` 恒真，左侧目标被翻到右导致镜像与采样错位，已修为「实际放不下才翻转」；
  2. ③ FAIL 根因＝探针 exe 是 21:57 编译、早于带 `mirror` 字段的源码（22:03）——事件流无 mirror，驱动采样解析失败；22:03:26 重编译新 exe 生效。第 4 轮不视为产品缺陷。
- 第 5 轮（run-20261124-002634，00:26 夜间）：检查①② PASS（目标标记/归属、心跳含 currentAction/climbMirrorScaleX 均为新 exe 字段）；事件流完整（climb-started mir=1 → 30 个 climb-step → climb-arrived → climb-completed，foot [240,330]→[218,180]→[510,180]，perchActive 短暂 true 交接成功后即回落）；但动作全程未播：sidecar `left-sidelimb: action=sleep mirror=1`，edge/land 心跳全缺 → ③ FAIL → 中止。④-⑩ 未执行。
- 第 5 轮根因（产品交互缺口，只读代码核实，产品代码未改）：
  - 夜间（23:30–06:00）空闲底态为 `sleep`（OnUpdateTick 1337-1350 行夜间接线 + ARCHITECTURE 317-324 行优先级表）；
  - 菜单「Climb on Window」全链路 `HandleContextMenuCommand(999-1001) → StartClimbPick(2491) → StartPick(2493) → UpdatePerchPick(2530,点击目标) → Climb.TryStart` 无 sleep→idle 打断（对照拖动/抚摸入口 `BeginCapturedPointerInteraction` 1951-1954 行有打断，菜单路径缺失）；
  - `PlayClimbAction` 守卫（MainWindow.xaml.cs 2689-2708 行）`current!="idle" && !IsClimbAction(current)` → sleep 底态拒播全部攀爬动作（不改动作不改镜像）；`TryStart`/`DispatchStageAction` 拒播但路线照常推进；因 sleep 是夜间底态不会被「释放」，`DispatchStageAction` 的"临时表达释放后补播"对 sleep 永不触发；到达分支 `climb_land` 同样拒播 → `StopCore(keepLand:false)` 回 idle 后下一 tick sleep 重入；
  - 与 TASKS.md 663 行承诺"优先级让位（好奇/委屈/睡眠不覆盖攀爬）"意图不完全一致：睡眠确实不会覆盖已开始的攀爬，但「sleep 已在播时经菜单起攀」路径未被入口打断，导致攀爬动作全程不播（表现＝睡眠动画的窗口沿路线移动到目标顶部）。
- 结论：此非驱动缺陷（驱动注入、目标锁定、事件流、位置路线全部验证正确），系产品入口语义缺口；驱动侧无干净绕法（拖动/抚摸打断会引入交互状态污染且 sleep 33ms tick 即重入）。按产品代码冻结纪律不改 `src/**`，真实输入轮进入阻塞态，需用户决策：
  - 选项 A：解冻一小点产品修复——`StartPick`/`Climb.TryStart` 入口按拖动/抚摸同语义打断 sleep→idle（一两行 + 补测），恢复「整天可用」的攀爬行为后继续夜间实机轮；
  - 选项 B：保持产品冻结，待 06:00–23:30 非夜间窗口重跑第 6 轮验证非夜间场景十项；夜间菜单攀爬缺口作为已知边界记录，验收后另行安排；
  - 选项 C：暂缓实机轮，先将缺口交监理评审定夺。
- 用户决策（2026-11-24）：选 C——暂缓实机轮，先交监理评审。开发侧不再进入下一轮、不改产品代码。
- 请求监理评审的范围（提交评审发现，不代写监理结论）：夜间快点路径「右键菜单 Climb on Window → 拾取点击目标 → Climb.TryStart」未像拖动/抚摸入口（`BeginCapturedPointerInteraction` 1951-1954 行）那样打断夜间 `sleep`，`PlayClimbAction` 守卫（2689-2708 行，仅接受 idle/climb_*）全程拒播三阶段动作，表现为睡眠动画的窗口沿既有路线移动到目标顶部、无任何攀爬动作帧；与 TASKS 663 行"优先级让位（好奇/委屈/睡眠不覆盖攀爬）"承诺的语义存在缺口。可选修复方向供评审评估（不改动路线/位置/交接语义）：A1 于 `StartPick`（攀爬意图）或 `Climb.TryStart` 入口按拖动/抚摸同语义先打断 sleep→idle 再起攀；A2 将 sleep 纳入「可被攀爬接管」集合（改守卫与夜间接线，影响面大于 A1）。评审结论用于决定后续实机轮方案（A 解冻小改继续夜间 / B 白天窗口验证非夜间场景并把夜间缺口头为边界 / 或按评审意见调整）。
- 未验证项及原因：④-⑩ 因③中止未执行；夜间攀爬动作呈现（=产品缺口）需决策后处理；多屏/混合 DPI 未测（延续任务既有限制）；未提交/推送，未改 `docs/reviews` 及既有证据。

## docs/reviews/P5B-003-review.md#F-07（R12 限定产品修复，开发侧执行，2026-11-24）

- 开发处置：已整改，自测通过，待监理复核（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意，R12 报告 250-279 行）：显式夜间攀爬未接管 `sleep` 底态，移动全程无攀爬动作。裁决为产品小范围修复：成功起攀取得位置所有权后让 `sleep` 交出呈现、真实播放 side/edge/land；不得粗暴取消 `PlayClimbAction` 对拖动/暂停/委屈等保护；不建议只在 StartPick 打开拾取时唤醒；拒绝路径不得先唤醒或改镜像；正常取消/完成后按当前本地时间恢复既有空闲底态；防夜间重判在有效路线/呈现期间重新覆盖。
- 修改说明与文件位置（仅 `src/DesktopPet.App/MainWindow.xaml.cs`，未动探针/驱动/资产）：
  1. `PlayClimbAction` 守卫（2700 行）：`current != "idle" && current != "sleep" && !IsClimbAction(current)` 才拒播——`sleep` 作为低优先级空闲底态放行给成功起攀后的三阶段；拖动/暂停/委屈/好奇等其他表达仍被拒播（保护未放开）；2702-2715 行 F-04 防御复核（动作缺失不改镜像、播放后非 climb_* 复位镜像）保持。
  2. 夜间 sleep 接线（1341-1355 行）：1337-1340 行新增注释说明，`1350 行 if 条件新增 && !IsClimbOwned` 显式排除攀爬路线活跃期——即使被更高优先级表达覆盖释放回 idle 而路线仍活跃的瞬时窗口，夜间重判也不重入 sleep，下一 tick 由攀爬控制器补播阶段动作；原有 `CurrentActionId == "idle"` 与 `!_isDragging` 等保护不变。
- 测试补证（新增 4 项，均用实例 `TestGetLocalNow` 固定夜间 00:30 + 实例 `_clockMs` 假时钟，经正式菜单入口 `TestOpenContextMenuLifecycle(8)` → 拾取 → `TryStart` 与生产 `OnUpdateTickTest()`，未改产品缝）：
  1. `ClimbEntryTests.NightClimb_SleepYieldsToStartedClimb_PlaysStages_ReturnsToSleepAfterLand`：sleep 底态 → 正式入口起攀 → 实际播放 climb_side（多 tick）→ climb_edge → climb_land → 真实等 land 播完（Thread.Sleep 3100 + 两次 tick + PumpDispatcher，沿既有 Arrival 测试模式）→ 夜间回 sleep。阶段推进严格断言，无放宽。
  2. `ClimbEntryTests.NightClimb_RejectedTarget_KeepsSleep_NoWakeNoMirror`：超距拒绝路径保持 sleep、不唤醒、不改镜像（mirror 保持 1）。
  3. `ClimbEntryTests.NightClimb_CancelAfterStart_ReturnsToSleepOnNextProductionTick`：取消后下个生产 tick 回 sleep（正常取消按本地时间恢复底态）。
  4. `ClimbActionTests.NightTick_ActiveRoute_IdleRelease_IsNotReplacedBySleep`：防重判核心反例——路线活跃 + 呈现被覆盖释放回 idle + 生产 OnUpdateTick → 不重入 sleep、补播 climb_side。
- 自测环境：Windows 11，本机 1920×1080@96DPI；.NET 10 Debug。
- 实际命令、退出码及关键结果：
  - `dotnet build DesktopPet.slnx -c Debug --no-restore` → 0 警告、0 错误。
  - 专项：`dotnet test tests/DesktopPet.App.Tests/DesktopPet.App.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~ClimbActionTests|FullyQualifiedName~ClimbEntryTests"` → **通过 40、失败 0、跳过 0**（含新增 4 项夜间测试）。
  - 全量回归：`dotnet test DesktopPet.slnx -c Debug --no-build` → Core 563/563 + App 610/610 = **1173/1173 通过**，0 失败 0 跳过。
- 人工步骤、观察者和实际现象：本轮无真实鼠标/屏幕占用，纯代码 + 自动化测试；未运行应用。
- 未验证项及原因：真实输入实机未跑（按 R12 277 行与用户指示，须监理代码复核通过后再安排针对性实机）；多屏/混合 DPI 未测（延续任务既有限制）；未提交/推送，未改 `docs/reviews` 及既有证据、未改驱动 `tools/Invoke-P5B003R9Live.ps1`。
- 驱动口径确认（无需改驱动，`Test-NormalLandingIdle` 364-411 行已符合 R12 271-273 行补充口径）：正常收尾两阶段等待——先见 `climb_land`，再等非 climb_* 且 -not climbActive 且 perchActive=true 且 mirror=1；攀爬过程中不能一直 sleep，正常完成后夜间回 sleep 合法（白天回 idle）。三阶段心跳观测保持严格，未放宽断言掩盖拒播。
- 与监理反例关系：R12 F-07 原始失败（路线成功但动作全程 sleep、sidecar `action=sleep mirror=1`）已由守卫放行 sleep + 三阶段实际播放覆盖，正例见测试 1；「拒绝路径唤醒/改镜像」「夜间重判覆盖有效路线」「进行中 sleep 冒充正常收尾」分别由测试 2/3/4 与 `Test-NormalLandingIdle` 两阶段要求受控覆盖。
- 证据路径：产品修复 `src/DesktopPet.App/MainWindow.xaml.cs`（2700、1337-1355 行）；测试 `tests/DesktopPet.App.Tests/ClimbEntryTests.cs`（607/620/628/715/722/763/771 行附近，含 `ApplyNightly` 布景辅助）与 `ClimbActionTests.cs`（816 行附近）；监理 R12 报告 `docs/reviews/P5B-003-review.md`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理复核的范围：核对两处产品改动的语义（守卫仅放行 sleep 底态、夜间接线排除路线活跃期）、4 项夜间测试经正式入口/生产 Tick 覆盖成功/拒绝/取消/防重判、驱动未作放宽；通过后关闭 F-07，再由监理安排/确认针对性实机（十项全 PASS + exit 0）。

## docs/reviews/P5B-003-review.md#R13 驱动正常底态断言收紧（开发侧执行，2026-11-24）

- 开发处置：已整改，自测通过（SelfTest 18/18），待监理核对后安排最终实机（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意，R13 报告 290-296 行）：开发称 `Test-NormalLandingIdle` 已符合 R12 口径不成立——该函数只排除 `climb_*`，未限定 idle/sleep，也未核对本地时段；受控反例 `normal-baseline-counterexample.json` 显示 currentAction=curious 或 drag（mirror=1、路线 inactive、栖息 active）时返回 Ok=true。限定：正常收尾只接受 idle 或实际本地夜间 sleep；拒绝 curious/drag 等其他动作；增加 curious/drag 拒绝、白天 sleep 拒绝、夜间 sleep 接受的 SelfTest 正反例；时段依据与实例/生产本地钟一致、边界按实际采样时间判断、不改系统时钟；保留 perch 活跃、mirror=1、路线 inactive 及 side/edge/land 观测要求；现有 14 项受控验证保留；不需改产品、素材或重跑全量。
- 修改说明与文件位置（仅 `tools/Invoke-P5B003R9Live.ps1`，未改产品/素材）：
  1. 新增 `Test-IsNightAt`：产品 `NightSchedule` 口径（23:30 含 → 06:00 不含，按 `TimeOnly` 判断），驱动与生产共用系统本地钟；
  2. `Test-NormalLandingIdle` 收紧：新增可选 `$LocalNow`（未显式提供时按采样判定时刻 `Get-Date`；SelfTest 传固定时刻）；idle → 接受；sleep → 仅 `Test-IsNightAt` 判定夜间时接受、白天拒绝；curious/drag 等其他动作一律拒绝；`climb_*`/`climbActive`/mirror!=1/perchActive=false/字段缺失断言全部保留；
  3. `Wait-NormalLandingIdle` 注释同步阶段 2 底态口径（idle 或实际本地夜间的 sleep），行为不变；
  4. SelfTest 追加 4 项（#15 curious 白天拒绝、#16 drag 白天拒绝、#17 白天 sleep 拒绝、#18 夜间 00:30 sleep 接受），原有 14 项未动。
- 实际命令、退出码及关键结果：
  - 语法：PowerShell Parser → PARSE_OK（0 错误）。
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003R9Live.ps1 -SelfTest` → **18/18 PASS（total=18 failed=0），RESULT=PASS，exit 0**；新增 4 项输出 `negative curious rejected (day, perch active)` / `negative drag rejected (day, perch active)` / `negative daytime-sleep rejected`（localNow=12:00）/ `positive night-sleep accepted (perch active)` 均 PASS。
  - 零副作用核实：`.temp\p5b-003\r9` 下无新增 run-* 目录（最新仍为 run-20261124-002634-fixture），SelfTest 在窗口/进程/鼠标创建前 exit。
- 人工步骤、观察者和实际现象：本轮无真实鼠标/屏幕占用；仅编辑驱动并运行无鼠标 SelfTest，无产品行为改动。
- 未验证项及原因：最终真实输入实机未跑（监理核对本收紧后再安排）；按监理明确不需要未重跑全量测试；多屏/混合 DPI 未测（延续任务既有限制）；未提交/推送，未改 `docs/reviews` 及既有证据。
- 更正说明：本记录 R12 段「驱动口径确认无需修改（`Test-NormalLandingIdle` 已符合 R12 补充口径）」表述已被监理 R13 更正——R12 时该函数只排除 `climb_*`、未限定底态与时段；本轮按 R13 收紧后，正常底态断言与 R12/R13 口径一致。
- 与监理反例关系：R13 反例 `normal-baseline-counterexample.json` 中 curious/drag 在 mirror=1+路线 inactive+栖息 active 下旧实现误判 Ok=true → 新实现拒绝（SelfTest #15/#16）；sleep 白天误接受 → 拒绝（#17）、夜间接受（#18）；idle 全天接受、climb_* 拒绝与 perch 丢失拒绝（原 #12/#13/#14）保留。
- 证据路径：驱动 `tools/Invoke-P5B003R9Live.ps1`（`Test-IsNightAt`、`Test-NormalLandingIdle`、`Wait-NormalLandingIdle` 注释、`Invoke-SelfTest` #15-#18）；监理 R13 报告 `docs/reviews/P5B-003-review.md` 282-296 行及反例 `docs/reviews/evidence/P5B-003-R13/normal-baseline-counterexample.json`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理核对的范围：核对 `Test-NormalLandingIdle` 收紧实现（idle 全天接受 / sleep 仅实际本地夜间接受 / curious、drag、白天 sleep 拒绝）、`Test-IsNightAt` 与产品 `NightSchedule`（23:30–06:00）口径一致、SelfTest 18/18 输出与零副作用；通过后安排最终针对性实机（十项全 PASS + exit 0 + 目标/诱饵安全清理）。

## docs/reviews/P5B-003-review.md#R15 事件流 action 与侧段实际动作取证、land 拖动 perch-detached 判据（开发侧执行，2026-11-24）

- 开发处置：已整改，自测通过（SelfTest 23/23 + 窄轮解析/判定受控断言 23/23），待监理核对后安排窄范围实机补证（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意，R15 报告 320-324 行）：①事件流未同步记录实际 CurrentAction，③④以 climb-started 的 mirror 替代 side 心跳，左侧 side 无直接动作证据（事件 mirror 1 本身不能证明 side 播放，初值也可能 1）；②⑦ land 拖动判据硬要求 `climb-drag-preempted`，而生产正确取消路径（StartDragging→Perch.CancelForDrag→DetachInternal→ReleaseClimbForPerchDetached）先清理 land 再走 Climb.CancelForDrag，`IsPresentingClimb` 已为 false 不再发抢占事件——run-043037 sidecar t=71021 确证 land 前置、78ms 内 perch-detached 快速清理。限定只改现有探针/驱动、不改产品/美术、不重跑全量。
- 修改说明与文件位置（仅 `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs` 与 `tools/Invoke-P5B003R9Live.ps1`，产品/素材零改动）：
  1. 探针事件流（114-135 行）：事件 JSON 新增 `action = mainWindow.CurrentAction`（与 sidecar/心跳同源的 public 属性；`Climb.TryStart` 中 `PlayClimbAction("climb_side", mirrorLeft)` 先于 `Emit("climb-started")`，事件触发时 CurrentAction 已为 climb_side，handler 同步读取有效）；127-130 行旧注释修正为「mirror 不独立承担 side 证据」。
  2. 驱动 ③④（约 1198-1310 行）：起攀前记录 `$chainBaseL/R` 基准，`$startedEvL/R` 限定本链——`tMs -gt $chainBase` 且 `targetHwnd -eq (HwndHex $script:ownedTargetHwnd)`；side 取证三要素＝本链事件 `action -eq 'climb_side'`（事件流实际动作）+ mirror 符向（左=1/右=-1）+ 限定本次时间/目标；没有 side 实际动作则 FAIL，不凭 mirror 独立推断；edge 镜像沿用方向随路线变化规则，不重新限定为常数；detail 输出 `sideAction=... mirSide=...`。
  3. 驱动 ⑦ land 拖动判据：新增 `Test-LegalDragCancel($events, [long]$sinceMs, [scriptblock]$targetWhere)`——接受 `climb-drag-preempted`，或 `tMs -gt $sinceMs`（本次拖动窗口内，以 land 确认心跳为界）且 reason 为 `perch-detached` 的 `climb-cancelled`；旧事件（Kind='stale'）、缺失（Kind='none'）、错 reason（recovered/paused 等不放宽）一律拒绝。⑦ 主流程保留 land 前置 capD、真实位移+前置校验 dragRes、镜像复位/绑定解除 idleJudgeD、松手不回拉 stableD，`$dragOk` 以 `$cancelD.Ok` 为准，轮询 `Test-LegalDragCancel (Read-Events) $inLandMsD {目标过滤}` 8s。
  4. SelfTest 追加 #19-23 + `MkEv` helper：合法窗口内 perch-detached 接受、合法 preempted 接受、旧取消拒绝、缺事件拒绝、错 reason 拒绝。
  5. `-OnlyChecks` 窄复验入口（param `[string]$OnlyChecks`；SelfTest 早退后解析 `'3,4,7'`→窄轮集合，空格/去重/排序，空白=全量，非法编号输出错误并 `exit 2`）：`Get-CheckNumber` 必须 `[System.StringComparison]::Ordinal`——ICU 文化比较把 ①(U+2460) 与 ⑩(U+2469) 视为等价圈号数字，默认 StartsWith 会把「⑩ 探针」误匹配为 ①（已修复并留注释）；`Test-CheckEnabled` 对 ①②⑩ 恒 true（③-⑨ 受集合约束）；完整性判定窄轮走 `Test-NarrowChecks`（仅要求被选编号 + ①②⑩ 存在且全 PASS）、全量走 `Test-CompleteChecks`；通过/失败输出带 `modeLabel`（如 `R15-NARROW[3,4,7]`），finally JSON 新增 mode/narrowChecks 字段——窄轮不伪装原生全量。
- 实际命令、退出码及关键结果：
  - 探针构建：`dotnet build tests\DesktopPet.DiagnosticProbe\DesktopPet.DiagnosticProbe.csproj -c Debug --no-restore -warnaserror` → 0 警告、0 错误。
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003R9Live.ps1 -SelfTest` → **23/23 PASS（total=23 failed=0），RESULT=PASS，EXITCODE=0**（#19-23 输出合法 perch-detached/preempted 接受与旧/缺/错 reason 拒绝均 PASS）。
  - 窄轮解析/判定受控断言（从驱动提取真实函数与解析段运行，临时脚本已清理）：判定 17/17 PASS、解析 5+1 PASS（含非法编号 → exit 2）；`Test-NarrowChecks`/`Test-CheckEnabled`/`Get-CheckNumber(Ordinal)` 均按被选编号 [3,4,7] 生效，①②⑩ 恒启用。
- 人工步骤、观察者和实际现象：本会话无真实鼠标/屏幕占用；仅编辑探针与驱动并运行无鼠标 SelfTest 与受控解析断言，探针仅观测、命令只触发生产入口，无产品行为改动。
- 未验证项及原因：`-OnlyChecks "3,4,7"` 端到端窄实机轮未跑（须用户授权占鼠标后由监理执行，开发侧不跑）；多屏/混合 DPI 未测（延续任务既有限制）；未提交/推送，未改 `docs/reviews` 及既有证据；不重跑全量测试。
- 与监理反例关系：run-20261124-043037-fixture（监理引用证据轮，只读）sidecar t=71021 `action=climb_land mirror=1 ... currentAction=sleep mirror=1 climbActive=False perchActive=False cleared=clean idle stable=True`——旧判据漏掉的合法 perch-detached 顺序现可被 `Test-LegalDragCancel` 接受（land 前置、即时清理、实际位移、镜像复位、松手不回拉断言全部保留）；旧/缺/错 reason 由 SelfTest #20/#21/#22/#23 受控拒绝；「要求正确路径必发 preempted」的观念已更正（生产正确取消路径不发该事件）；不把本轮 9/10 改写成原生 10/10。
- 证据路径：探针 `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs`（114-135 行）；驱动 `tools/Invoke-P5B003R9Live.ps1`（`Test-LegalDragCancel`、③④ side 取证、⑦ 轮询、`MkEv`/SelfTest #19-23、`-OnlyChecks` 全链）；监理 R15 报告 `docs/reviews/P5B-003-review.md`（320-324 行，只读，未改动）及证据副本 `docs/reviews/evidence/P5B-003-R15/`、实机原件 `.temp/p5b-003/r9/run-20261124-043037-fixture/`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理核对的范围：核对事件流 `action` 字段与 side 三要素口径（本链实际 climb_side 动作 + 镜像符向 + 限定时间/目标，不凭 mirror 独立推断）、`Test-LegalDragCancel` 判据（接受窗口内 perch-detached 与 preempted，拒绝旧/缺/错 reason，land 前置/即时清理/位移/回位/不回拉断言保留）、SelfTest 23/23 与零副作用、`-OnlyChecks` 窄轮入口语义（Ordinal 编号、modeLabel 标注、非法 exit 2、窄轮不伪装全量）；通过后由监理在用户授权时段执行 `-OnlyChecks "3,4,7"` 窄实机补证，以窄轮+R15 通过证据组合核对全部验收覆盖。

## docs/reviews/P5B-003-review.md#R16 ⑦ land 拖动空目标取消的链上下文关联（开发侧执行，2026-11-24）

- 开发处置：已整改，自测通过（SelfTest 27/27 + R15 原始事件回放 5/5 + OnlyChecks 严格校验隔离断言），待监理核对后安排 `-OnlyChecks "3,4,7"` 窄实机补证（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意，R16 报告 328-340 行）：⑦ 两个生产调用均给 `Test-LegalDragCancel` 传入 `targetHwnd` 必须等于 ownedTargetHwnd 的过滤器，但 R15 真实合法事件 t=71099 的 `targetHwnd=null`——land 交接时 tracker 已释放，`CancelForPerchDetached` 只拿到空目标；不是产品缺陷，不应为取证修改产品生命周期。唯一阻断收尾（336 行）：让驱动/探针通过本次起攀→land→真实拖动的上下文关联合法空目标取消（如探针维护只用于观测的本次链标识与目标关联，或驱动用本次实际 land/拖动时间窗关联）；必须保留已知非空错目标拒绝，不能全局接纳所有空句柄取消；把原 R15 t71021→71099 真实空目标样本用于受控回放，并以生产调用相同参数覆盖正例、历史取消/其他链/错目标负例，再交监理；既有侧段取证及正常底态规则不重做。附带核对（338 行）：`-OnlyChecks` 解析会静默丢弃混合参数中的非法 token（如 `3,garbage`），"非法编号均 exit 2" 表述过满，建议随本次小改严格校验所有 token；此项不额外阻断既定 `-OnlyChecks "3,4,7"`，也不扩建框架。
- 修改说明与文件位置（仅 `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs` 与 `tools/Invoke-P5B003R9Live.ps1`，产品/素材零改动）：
  1. 探针（114-115、124-132 行）：新增只用于观测的 `chainId`——`climb-started` 时递增，链内事件（step/cancelled 等）携带同一 chainId；不依赖 `ev.TargetHwnd`。
  2. 驱动 ⑦：`Test-LegalDragCancel` 新增第 4 参 `chainContext`，经 `Test-DragCancelContextMatch`（416-428 行）分流——非空 `targetHwnd` 仍须过既有 `$targetWhere`（记名 owned）；空 `targetHwnd` 只接受 `chainId -eq` 本次链（`Get-LastStartedChainId`：目标==owned 的最近 `climb-started` 的 chainId），chainId 属性缺失（旧探针事件）拒绝；移除遗留的二次 `$targetWhere` 过滤（避免误排空目标合法取消）。⑦ 主流程（1467-1488 行）起攀后取 `$chainIdD`，轮询/兜底过滤器传第 4 参；land 前置 capD、真实位移+前置校验 dragRes、镜像复位/绑定解除 idleJudgeD、松手不回拉 stableD 断言全部保留。
  3. SelfTest：`MkEv` 加可选第 5 参 chainId；新增 #24 R15 真实样本正例（t=71021/71099 参数化、chainId=5、接受）、#25 其他链空取消拒绝、#26 非空错目标拒绝、#27 无 chainId 拒绝；原有 #19-23 语义保留。
  4. `-OnlyChecks` 解析（645-660 行）：改逐 token 严格校验——任一非法/空 token（如 `3,garbage`、`3,,4`、`bad`、`0`，空 token 计入报错）即报错 exit 2，不再静默丢弃；合法集合为空仍报错；`3,4,7` 合法组合不受影响，不扩建框架，`Test-CompleteChecks`/`Test-NarrowChecks` 结构不变。
- 实际命令、退出码及关键结果：
  - 探针构建：`dotnet build tests\DesktopPet.DiagnosticProbe\DesktopPet.DiagnosticProbe.csproj -c Debug --no-restore -warnaserror` → 0 警告、0 错误。
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003R9Live.ps1 -SelfTest` → **27/27 PASS（total=27 failed=0），RESULT=PASS，EXITCODE=0**（R15 23 项保持 + #24-27；#24 输出 R15 真实样本正例接受、#25/#26/#27 各自拒绝均 PASS）。
  - OnlyChecks 严格校验：子进程 `-OnlyChecks "3,garbage"` → 报错、EXITCODE=2；另用隔离解析块（临时脚本）验证 `3,4,7` 通过、`3,garbage`/`3,,4`（空 token）/`bad`/`0` 各拒绝 exit 2。教训（勿重犯）：不能用整脚本子进程方式验证 OnlyChecks 合法组合——曾误触发真实夹具运行并创建无效 run 目录（run-20261124-122648/123005/123006/123007/123008-fixture 全部删除，无进程/夹具残留）；后续 OnlyChecks 验证只能隔离执行解析块。
  - R15 原始事件回放（临时脚本 `C:\Users\mrrdxx\AppData\Local\Temp\codefree-agent\p5b003-r16-replay\replay-r16.ps1`：从生产脚本 AST 提取真实 `Test-LegalDragCancel`/`Test-DragCancelContextMatch`，加载只读 `docs/reviews/evidence/P5B-003-R15/climb-live-events.jsonl` 259 行，按生产 ⑦ 参数：本链=land 前置 t=71021 前最后一条 climb-started（chainId=5、起攀 tMs=69592）、sinceMs=71021、owned=0x31444）→ **5 项断言全 PASS，EXITCODE=0**：① 正例 Ok=True Kind=perch-detached @t=71099；② 其他链 chainId=4 拒绝（Kind=stale tMs=63900 ≤ since=71021）；③ 旧链 chainId=3 拒绝（stale）；④ 混入非空错目标 0xBAD 时仍只接受本链空取消 @t=71099、错目标拒绝；⑤ 无 chainId 原始事件拒绝（stale）。
- 人工步骤、观察者和实际现象：本会话无真实鼠标/屏幕占用；仅编辑探针与驱动并运行构建、无鼠标 SelfTest 与只读事件流回放；探针仅观测、命令只触发生产入口，无产品行为改动。未以整脚本运行 `-OnlyChecks` 合法组合（避免误触实机）。
- 未验证项及原因：`-OnlyChecks "3,4,7"` 端到端窄实机轮未跑（须用户授权占鼠标后由监理执行，开发侧不跑）；多屏/混合 DPI 未测（延续任务既有限制）；未提交/推送，未改 `docs/reviews`（含 R16 报告及 R15/R16 证据副本，均只读）及既有证据；不重跑全量测试与已过构建/SelfTest（代码未变，重跑属重复验证）。
- 与监理反例关系：R16 反例 `actual-drag-cancel-counterexample.json`（唯一候选 t=63900 stale、实际合法事件 t=71099 空目标）在生产同款过滤器下旧实现误判 Ok=false → 新实现经链上下文关联对 R15 真实空目标取消接受（SelfTest #24 / 回放①），同时历史取消/其他链/无 chainId 空取消与非空错目标仍拒绝（SelfTest #25/#26/#27 / 回放②③④⑤）；`targetHwnd` 非空路径的 `$targetWhere` 记名 owned 校验保留，未全局接纳所有空句柄取消。
- 证据路径：探针 `tests/DesktopPet.DiagnosticProbe/WindowClimbLiveScenario.cs`（114-115、124-132 行）；驱动 `tools/Invoke-P5B003R9Live.ps1`（`Test-LegalDragCancel`、`Test-DragCancelContextMatch`、`Get-LastStartedChainId`、⑦ 1467-1488 行、`MkEv`/SelfTest #24-27、`-OnlyChecks` 逐 token 校验）；监理 R16 报告 `docs/reviews/P5B-003-review.md`（328-340 行，只读，未改动）；反例 `docs/reviews/evidence/P5B-003-R16/actual-drag-cancel-counterexample.json`（只读）与监理 R15 证据副本 `docs/reviews/evidence/P5B-003-R15/`（只读）；实机原件 `.temp/p5b-003/r9/run-20261124-043037-fixture/`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理核对的范围：核对探针 chainId 只观测语义（climb-started 递增、链内共用、不依赖 TargetHwnd）、`Test-DragCancelContextMatch` 分流（非空仍过 `$targetWhere`；空仅接受本次链 chainId、无 chainId 拒绝）、`Get-LastStartedChainId` 与 ⑦ 传参、SelfTest 27/27 与零副作用、R15 原始事件回放 5/5（正/负例）、`-OnlyChecks` 逐 token 严格校验（合法 `3,4,7` 不阻断）；通过后由监理在用户授权时段执行 `-OnlyChecks "3,4,7"` 窄实机补证，以窄轮+R15 通过证据组合核对全部验收覆盖。

## docs/reviews/P5B-003-review.md#R18 驱动空闲前置：安全鼠位 + 新鲜空闲确认 + 起攀时序取证（开发侧执行，2026-11-25）

- 开发处置：已整改，自测通过（SelfTest 35/35），待监理核对后由监理在授权时段执行 `-OnlyChecks "4,7"` 右侧与 land 拖动窄实机补证（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意，R18 报告 352-364 行）：④ chainId=2 实测 t14537 起攀 action=curious/mirror1、t16330 仍 curious；`Ensure-IdleForClimb` 旧实现无绑定直接返回 idle、拖动解绑后等不到 idle 仍按「non-idle」返回 Ok=true，是本轮中止根源；`PlayClimbAction` 守卫不覆盖 curious（与 R7/R12 保留临时表达优先级一致，不新增产品缺陷）。限定：只修现有驱动空闲前置——先把测试鼠标移到自有安全位置，等待新鲜真实 idle 或实际夜间 sleep 且无路线/绑定后才允许继续；超时如实返回未具备布景/失败，不再 Ok=true 带 non-idle；若真实菜单/拾取再次触发 curious 等表达，记录起攀前后实际动作并如实说明入口时序，不通过改产品守卫、清表情内部标志或强制播放 idle 凑通过；增加 curious/drag 等不能被称 idle 的受控正反例；保留已正确的阶段/链关联判据。
- 修改说明与文件位置（仅 `tools/Invoke-P5B003R9Live.ps1`，未改产品/素材/探针）：
  1. 安全鼠位：新增 `Get-SafeCursorPoint`/`Move-CursorToSafeSpot`——候选点取虚拟屏右上/左上/右下，避开宠物矩形与目标 DWM 帧，回读实际落点仅记录、不作通过门槛；`Ensure-IdleForClimb` 入口先移开鼠标再等待，避免测试光标停留在宠物/目标上诱发表达。
  2. 新鲜空闲确认（`Ensure-IdleForClimb` 重写）：用 `Wait-FreshState`（既有 `Test-FreshState`）循环等 tMs 前进的新鲜心跳，`Test-ClimbPrereqIdle` 纯判定只接受 idle 或合法夜间 sleep（`Test-IsNightAt` 23:30 含–06:00 不含）+ climbActive=false + perchActive=false + 字段齐全；curious/drag/白天 sleep/路线激活/栖息激活/字段缺失一律拒绝；栖息在但不在攀爬 → `Invoke-RealDrag 26 4 10 700` 解绑一次并回到安全位继续等；删除旧实现「无绑定直接返回 idle」与「non-idle 仍 Ok=true」两处错误路径；总体超时 25s 如实返回 Ok=false，Detail 含 safe 落点、detach、最后状态。
  3. 起攀时序取证（`Start-ClimbActual`）：新增 `beforeMenu`（真实菜单打开前 Get-State）→ `afterPick`（Enter-ClimbPick 后）→ `afterStart`（点击起攀后）三拍记录，成功/失败 Detail 均携带；若菜单或拾取触发 curious，如实保留时序证据并失败，不降级产品守卫。
  4. SelfTest 追加 #28-35（`Mk` helper 共用）：#28 正例 idle 接受；#29 正例夜间 sleep（00:30）接受；#30 curious 拒绝；#31 drag 拒绝；#32 白天 sleep 拒绝；#33 climbActive=true 路线激活拒绝；#34 perchActive=true 拒绝；#35 旧心跳（tMs=100 ≤ 200）不能冒充空闲（fresh 检查拒绝，单条判定本身可过说明新鲜性须由上层把关）；原有 #19-27 语义保留。
- 实际命令、退出码及关键结果：
  - 语法：PowerShell Parser → PARSE_OK（0 错误）。
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003R9Live.ps1 -SelfTest` → **35/35 PASS（total=35 failed=0），RESULT=PASS，EXITCODE=0**；新增 8 项输出（positive prereq idle / positive prereq night-sleep / negative curious / negative drag / negative daytime-sleep / negative route-active / negative perch-active / negative stale heartbeat cannot masquerade idle）均 PASS。
  - 零副作用核实：SelfTest 在窗口/进程/鼠标创建前早退（输出无 `[R9] target start` 等实机行为），无新增 run-* 目录、无鼠标注入；本轮未构建探针（探针未改动）。
- 人工步骤、观察者和实际现象：本轮无真实鼠标/屏幕占用；R18 实机轮 run-20261125-185433-fixture（监理执行，3 PASS/2 FAIL）证据保留在 `docs/reviews/evidence/P5B-003-R18/`（只读，未改动）。
- 未验证项及原因：`-OnlyChecks "4,7"` 右侧与 land 拖动端到端窄实机补证未跑（须用户授权占鼠标后由监理执行，开发侧不跑）；⑦ 未验证、F-06 未关闭；多屏/混合 DPI 未测（延续任务既有限制）；未提交/推送，未改 `docs/reviews`（含 R15/R16/R17/R18 报告与证据副本，均只读）及既有证据；不重跑全量测试与已过 SelfTest（驱动代码未变，重跑属重复验证）。
- 与监理反例关系：R18 实机 ④ chainId=2 起攀实际 curious/mirror1（run-20261125-185433-fixture）——旧实现「等不到 idle 仍 Ok=true」误放行的路径已删除；新实现按 SelfTest #30/#31/#35 受控拒绝 curious/drag/旧心跳，`Test-ClimbPrereqIdle` 对 R18 布景（curious 占用、期间无 side/edge/land 播放）必须拒绝；③左侧补证（chainId=1、t3185 实际 climb_side/mirror1）为监理接受的既有证据，本轮不重做。
- 证据路径：驱动 `tools/Invoke-P5B003R9Live.ps1`（`Get-SafeCursorPoint`/`Move-CursorToSafeSpot`、`Ensure-IdleForClimb`、`Test-ClimbPrereqIdle`、`Start-ClimbActual` 时序取证、SelfTest #28-35）；监理 R18 报告 `docs/reviews/P5B-003-review.md`（352-366 行，只读，未改动）及其证据副本 `docs/reviews/evidence/P5B-003-R18/`（只读）；实机原件 `.temp/p5b-003/r9/run-20261125-185433-fixture/`（只读，未改动）。
- 异议/需求疑问及反证：无。
- 请求监理核对的范围：核对 `Ensure-IdleForClimb` 新前置（安全鼠位回读留证、新鲜心跳确认、只接受 idle/夜间 sleep + 路线/栖息未激活、超时如实失败）、`Test-ClimbPrereqIdle` 判定口径、`Start-ClimbActual` 时序取证（菜单前/拾取后/起攀后三拍、curious 如实失败不降级守卫）、SelfTest 35/35 与零副作用；通过后由监理在用户授权时段执行 `-OnlyChecks "4,7"` 右侧与 land 拖动窄实机补证，③左侧及 R15 其余通过证据沿用，不重复左侧或全量。

## docs/reviews/P5B-003-review.md#R19 Get-SafeCursorPoint 坐标表达式修正与无鼠标实际调用自测（开发侧执行，2026-11-25）

- 开发处置：已整改，自测通过（SelfTest 37/37），待监理核对后由监理在授权时段执行 `-OnlyChecks "4,7"` 右侧与 land 拖动窄实机补证（非项目任务状态；TASKS 保持 in-progress）
- 监理问题（原意，R19 报告 368-376 行）：监理读取 R18 整改并实际无鼠标 SelfTest 35/35 PASS，接受空闲底态拒绝 curious/drag/非法 sleep、上层 fresh 心跳、删除 non-idle 成功返回的方向；但 F-06 仍余一处确定运行阻断——`Get-SafeCursorPoint` 构造 `$cands` 时 `@($vs.X + $vs.Width - 8, $vs.Y + 8)` 等坐标表达式未分别加括号，逗号与算术组合被 PowerShell 解释为数组参与减法，仅调用坐标计算（不调用 SetCursorPos）即抛 `System.Object[] does not contain ... op_Subtraction`；现有 35 项虽全绿，实机进入 `Ensure-IdleForClimb` 会先异常，不能放行。限定：只修坐标表达式——每个坐标算式各自加括号，确保返回恰好两个标量数值（如 `@(($vs.X + $vs.Width - 8), ($vs.Y + 8))`）；其余候选同修；补一个无鼠标实际调用此函数的自测，检查二维数值点及避开已知宠物/目标区域，不仅 Parser 语法或纯底态判定；其余已接受空闲前置/chain 关联/正常终态不重做；无需改产品、探针、素材或跑全量。
- 修改说明与文件位置（仅 `tools/Invoke-P5B003R9Live.ps1`，未改产品/素材/探针）：
  1. 坐标表达式修正（`Get-SafeCursorPoint`）：三处候选点均改为每个坐标算式各自加括号——`@(($vs.X + $vs.Width - 8), ($vs.Y + 8))`、`@(($vs.X + 8), ($vs.Y + 8))`、`@(($vs.X + $vs.Width - 8), ($vs.Y + $vs.Height - 8))`，返回恰好两个标量数值；候选顺序与避开宠物/目标判定逻辑不变。
  2. 函数定义顺序调整：`Get-SafeCursorPoint` 定义从原 997 行（SelfTest 之后）移到 `Test-PointOnScreen` 之后（SelfTest 之前），仅挪位置、函数体与语义零改动，目的是让新增无鼠标自测能实际调用该函数（PowerShell 按定义顺序加载）。
  3. SelfTest 追加 #36/#37：`#36` 无宠物/目标遮挡（SelfTest 无 stateFile、ownedTargetHwnd=null）实际调用 `Get-SafeCursorPoint` → 返回右上候选 `[vs.X+vs.Width-8, vs.Y+8]`，断言恰好 2 个 `[int]` 标量并落在虚拟屏内（直接复现并覆盖监理的坐标结构要求）；`#37` 用临时 stub `Get-State`（petRectPx 覆盖右上候选）与 `Get-FrameBounds`（目标帧覆盖左上候选）+ 临时 `ownedTargetHwnd` 非零，实际调用后必须避开宠物矩形与目标帧、返回右下候选 `[vs.X+vs.Width-8, vs.Y+vs.Height-8]`，验证后删除 stub 并恢复 `ownedTargetHwnd`，不残留副作用。
- 实际命令、退出码及关键结果：
  - 语法：PowerShell Parser → PARSE_OK（0 错误）。
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003R9Live.ps1 -SelfTest` → **37/37 PASS（total=37 failed=0），RESULT=PASS，EXITCODE=0**；新增 2 项输出 `positive safe-point returns 2 numeric scalars top-right (no occluder)`（p=[2552,8]）与 `positive safe-point avoids known pet rect and target frame (bottom-right)`（p=[2552,1592]）均 PASS（p 值随本机虚拟屏 2560×1600@96DPI 自适应）。
  - 零副作用核实：SelfTest 在窗口/进程/鼠标创建前早退（输出无 `[R9] target start` 等实机行为），无新增 run-* 目录（最新仍为 run-20261125-185433-fixture）、无鼠标注入；本轮未构建探针（探针未改动）。
- 人工步骤、观察者和实际现象：本轮无真实鼠标/屏幕占用；仅编辑驱动并运行无鼠标 SelfTest；R18 实机轮 run-20261125-185433-fixture（监理执行，3 PASS/2 FAIL）证据保留在 `docs/reviews/evidence/P5B-003-R18/`（只读，未改动）。
- 未验证项及原因：`-OnlyChecks "4,7"` 右侧与 land 拖动端到端窄实机补证未跑（须用户授权占鼠标后由监理执行，开发侧不跑）；⑦ 未验证、F-06 未关闭；多屏/混合 DPI 未测（延续任务既有限制）；未提交/推送，未改 `docs/reviews`（含 R15-R19 报告与证据副本，均只读）及既有证据；不重跑全量测试与已过 SelfTest（驱动坐标改动后 SelfTest 37/37 已实际重跑，为最新证据）。
- 与监理反例关系：R19 阻断是运行异常而非语义——监理提取真实函数在旧表达式中仅坐标计算即抛 `op_Subtraction`；新实现每个候选均为两个标量数值（#36 直接实际调用通过），stub 遮挡场景（#37）证明避开宠物/目标区域后才选点，R18 已接受的空闲前置/chain 关联/正常终态逻辑未触碰。
- 证据路径：驱动 `tools/Invoke-P5B003R9Live.ps1`（`Get-SafeCursorPoint` 坐标修正与位置调整、SelfTest #36/#37）；监理 R19 报告 `docs/reviews/P5B-003-review.md`（368-376 行，只读，未改动）及实际输出 `docs/reviews/evidence/P5B-003-R19/safe-point.txt` 与 SelfTest 原始输出（只读）。
- 异议/需求疑问及反证：无。
- 请求监理核对的范围：核对三处候选坐标算式均独立加括号（返回两个 `[int]` 标量，无数组参与减法）、`Get-SafeCursorPoint` 定义位置调整未改语义、SelfTest #36/#37 输出（右上数值点 + 避开宠物/目标后右下点）与零副作用、37/37 全 PASS；通过后由监理在用户授权时段执行 `-OnlyChecks "4,7"` 右侧与 land 拖动窄实机补证，③左侧及 R15 其余通过证据沿用，不重复左侧或全量。

## docs/reviews/P5B-003-review.md#R21 右侧/land 拖动补证接收引用（2026-11-25）

- 处置：仅追加接收引用，未重测、未改监理报告及证据副本、未提交/推送。P5B-003 已由监理接收，任务状态以 [TASKS](../TASKS.md) 的 done 状态行及 R21 报告为准（代码未变，不重复实机/自测，属重复验证）。
- 接收依据（R21 报告 386-406 行，只读）：用户授权占鼠标后监理仅执行 `-OnlyChecks "4,7"`；④ chainId1 实际 climb_side/edge/land/mirror-1 及正常收尾，⑦ chainId2 land 真实拖动 26 步、(-104,+260)、hold700ms、t17480 同链 climb-cancelled(perch-detached) 84ms 内清理、松手无回拉；两张必需渲染帧与 sidecar 完整、探针 exit0、诱饵保护及精确隔离进程清理、鼠标归还。
- 原始脚本 4/5、exit1 保留：⑩ 仅 FAIL 项是固定要求 frames>=4/frameEvents>=4 未按窄轮调整，系数量门槛假阴性；监理已按约定窄轮范围纠正验收解释并接受实际安全/证据覆盖，不修改原 FAIL/退出码、不称原生脚本全绿、不因凑四张无关截图再占鼠标。
- F-06 复核通过并关闭，P5B-003 接收、TASKS 可标 done；F-07 及 F-02/03/04/05 既有关闭不变，F-01 按 R6 用户临时美术豁免，不冒称最终美术通过；多屏/混合 DPI 与未来美术提升仍不在已测范围；未进入后续功能。
- 证据路径：监理 R21 报告 `docs/reviews/P5B-003-review.md`（386-406 行，只读，未改动）及证据副本 `docs/reviews/evidence/P5B-003-R21/`（只读，未改动）；实机原件 `.temp/p5b-003/r9/run-20261125-200852-fixture/`（只读，未改动）。