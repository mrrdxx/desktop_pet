# P5A-002 整改记录（监理 R1 → 开发整改）

2026-11-18（Asia/Shanghai）。对象：监理对 P5A-002 窗口栖息晃动反馈的 R1 收尾三项。未修改监理报告；未提交/推送；任务状态保持 `awaiting-verification`。P5A-001 已验收的落位、跟随和脱离逻辑保持原样（本轮未改 `PetWindowPerch` 落位/跟随/脱离判定，仅晃动视觉与驱动断言）。

## R1-1 固定素材脚点

### 问题

旋转中心为画面底部中点 (60,120)，不是素材脚点。素材脚点（`character.json` `footAnchorX=60,footAnchorY=110`）经帧偏移补偿后的实际位置才是脚底贴住目标上沿的点。

### 修复

- `src/DesktopPet.App/MainWindow.xaml`：`WobbleTransform` 初值中心改为 (60,110)，注释改为"帧偏移补偿后的素材脚点"；XAML 值仅占位。
- `src/DesktopPet.App/MainWindow.xaml.cs` `UpdatePerchWobble`：每 tick 设置 `WobbleTransform.CenterX/Y = _character.FootAnchorX/Y + FrameOffsetTransform.X/Y`（帧偏移随动作切换变化，动态更新）。

### 验证

- 单测 `PerchWobble_NonZeroAngle_MaterialFootAndWindowStayFixed`（新增）：非零角度下断言 (a) 旋转中心==素材脚点；(b) `PetImage.RenderTransform`（TransformGroup）作用于素材脚点保持该点不动（实际素材脚点位置不变）；(c) 窗口 Left/Top 不变。
- 原 `PerchWobbleTests` 中心断言 120→110 同步修正。

## R1-2 连续移动时左右晃动

### 问题

`NotifyPerchFollowLanded` 每次成功跟随都重置 `_perchWobbleStartMs`，晃动相位每次从正弦零点重启，连续跟随时小熊反复只向一侧摆。

### 修复

`NotifyPerchFollowLanded` 改为：正在晃动（`_perchWobbleStartMs >= 0`）时直接返回，保留相位与衰减包络，不重置起始时刻；未在晃动时才启动。连续跟随期间按原相位继续正弦振荡（角度有正有负），停止跟随后包络内自动回稳归零。

### 验证

单测 `PerchWobble_ContinuousFollow_KeepsPhase_SwingsBothWays_ThenSettles`（新增）：follow #1 启动晃动（四分之一周期角度为正）→ 目标继续移动 follow #2 → 断言起始时刻未重置（相位保留）→ 四分之三周期角度为负（持续移动有正有负）→ 停下 3s 后 `wobbling=false` 且角度归零。

## R1-3 补实机打断（正在晃时抓住拖动→立即归零）

### 修复（驱动断言）

`tools/Invoke-P5A001Preview.ps1` 步骤 12 改为：拖动注入前先小位移目标（+60,+40，最多重试 3 次）触发 follow，在晃动衰减包络（约 0.7s）内 60ms 间隔轮询 `perch-live-state.json`，捕获 `wobbling=true` 且 |angle|>0.2 后立即执行原抓取/光标校验/注入流程。新增检查项：

- 「晃动反馈：拖动时小熊正在晃（角度非零）」——补证打断发生时晃动确实在进行；
- 原「晃动反馈：拖动接管打断后无残留」（`wobbling=false` 且角度=0）不变。

### 实机证据

`.temp/p5a-001/m15/run-20261118-190108-notepad/`：19 条 PASS（单屏 144 DPI；清单条数按文件记录，此前误记 22）。关键新证据：
- 「晃动反馈：拖动时小熊正在晃（角度非零）」PASS `wobbling=True angle=4.509`；
- 「拖动接管：真实鼠标拖动宠物 drag-preempted」PASS（dragCancelled=目标句柄、moved=True）；
- 「晃动反馈：拖动接管打断后无残留」PASS `wobbling=False angle=0`。

既有「移动时晃」（maxAngle=7.523°）与「停下回稳归零」断言同轮 PASS，沿用为移动和回稳证据。

## 自测汇总

- 构建 0 警告 0 错误（`DesktopPet.slnx`）。
- 全量：Core 548/548、App 534/534（合计 1082/1082）。`PerchWobbleTests` 6 项（原 4 + R1 新增 2）全部通过。
- M-15 实机（R1 补证轮，notepad）：19 条 PASS（清单条数按文件记录校正，此前误记 22 项）。
- 未测边界不变：暂停打断实机路径仍仅单测覆盖；跨屏、混合 DPI、真实用户窗口动态遮挡未测。

## 复核请求

请监理复核 R1 三项收尾：R1-1 素材脚点固定（单测）、R1-2 相位保留与正负晃动（单测）、R1-3 实机打断补证（M-15 新断言）。落位、跟随、脱离行为未变（全量回归 1082/1082 + M-15 原有断言同轮 PASS）。

# R2 整改回应（2026-11-18）

对象：监理 R2 复核的两项遗留——F-01 帧切换时旋转中心与帧偏移同 tick 同步、F-03 接管完成时的角度记录。未修改监理报告；未提交/推送；状态保持 `awaiting-verification`。

## F-01 帧切换：旋转中心与帧偏移同一次 `UpdateFrame` 更新

### 修复

- [MainWindow.xaml.cs](../src/DesktopPet.App/MainWindow.xaml.cs) `UpdateFrame`：在应用新帧偏移（`FrameOffsetTransform.X/Y = frame.OffsetX/Y`）的同一入口、同一行序紧随其后设置 `WobbleTransform.CenterX/Y = _character.FootAnchorX/Y + offsetX/offsetY`。消除同一 tick 内"中心=旧帧偏移、偏移=新帧"的失配窗口（帧切换时素材脚点横移约 1.17 DIP 的根因）。
- `UpdatePerchWobble`：移除旋转中心设置，只更新角度；`NotifyPerchFollowLanded` 保留相位、`EndPerchWobble` 归零不变（R1 已收）。

### 验证

- 单测 `PerchWobble_FrameSwitchDuringWobble_MaterialFootStaysAtFrameOffsetOnlyPosition`（新增）：非零角度晃动中 `PlayAction("curious")`（非零 `offsetY` 帧）后反射调用生产 `UpdateFrame`，断言 (a) 旋转中心==footAnchor+新帧偏移；(b) 角度非零；(c) **原始素材脚点经完整 `TransformGroup`（帧偏移+旋转）后的位置，与只应用帧偏移平移时一致**；(d) 窗口 Left/Top 不变。curious 帧偏移从运行时 `TestCharacter` 动态读取，不硬编码。`PerchWobbleTests` 7/7（原 6 + 新 1）。
- 全量回归：Core 548/548 + App 535/535 = 1083/1083 全部通过（含 R2 改动）。

## F-03 接管完成时的角度记录

### 修复

- [Invoke-P5A001Preview.ps1](../tools/Invoke-P5A001Preview.ps1)：`drag-preempted` 事件后取 **`tMs > dragEv.tMs` 的第一条心跳**（轮询 40ms、上限 3s）作为接管完成时刻读数，断言 `wobbling=false` 且 `|angle|<0.001`，记录 `zeroAtPreempt/wobAtPreempt/angleAtPreempt`。不再使用"松手后额外等待 1000ms 才读零"的旧断言（旧读数已越过自然衰减期限，不能证明打断归零）。

### 验证（M-15 R2 补证轮，`run-20261118-212632-notepad`，18/18 ALL PASS）

关键证据链：拖动前晃动中读数 `wobbling=True angle=6.876`（晃动中抓取前置成立）→ 真实鼠标拖动注入 → `drag-preempted`（`dragCancelled=0x912DA target=0x912DA moved=True`）→ 接管后第一条心跳 `wobbling=False angle=0`、`zeroAtPreempt=True wobAtPreempt=False angleAtPreempt=0`。归零读数来自接管时刻（心跳粒度 200ms 内），非衰减后补读。其余沿 R2 前已验证断言：移动时晃 `maxAngle=5.573`、跟随贴新上缘 `dx=0 dy=0`、回稳归零、松手不回拉。清单与事件流：`window-perch-live/perch-live-events.jsonl`、`m15-checklist.txt`。

## 本轮环境异常与驱动修复（仅测试工具，非产品逻辑）

- **首轮 R2 实跑 `run-20261118-205750-notepad` 8 项 FAIL，根因为环境变化，非 R2 回归**：实机显示环境已从 R1 证据轮的 2560×1440@144DPI（150%）变为 1920×1080@96DPI（100%）。清空点 (1450,800) + 800×500 目标在 1080p 下本身超出右/下屏缘；步骤 4 移动 +100/+50 后脚点 1950、宠物矩形右缘 2010 > 屏宽 1920，`TryComputeLanding` 按 P5A-001 R1 F-01 出屏保护语义正确拒绝 → `detached(no-landing)`，其后 `select` 在同一出屏位置重选持续失败，7 项连锁 FAIL 均由此单一环境事实产生。P5A-001 落位/跟随/脱离判定逻辑未改动且行为正确。
- **驱动修复**：`Find-ClearSpot` 新增 `Test-SpotFitsScreen`——候选点原始及移动后（+100/+50）的 800×500 矩形须完整落入当前主屏，上缘留 ≥200px 宠物高度余量。使 M-15 分辨率/DPI 自适应（本轮在 1920×1080@100% 下自动改选 (500,300)）。测试工具改动，不触及产品代码。
- 上一轮 R1 证据（19 条 PASS）在 150% 环境采集，其结论不受影响；本轮 R2 补证在 100% 环境采集，行为与 DPI 无关（全程物理像素计算）。

## R3 补充取证：F-03 接管前后角度记录（2026-11-18）

按用户指示只补 F-03 这一小段：隔离探针在接管处理前后各写一条带时间戳的角度记录，验证非零角度在接管完成时归零。产品代码零改动。

### 修复

- [WindowPerchLiveScenario.cs](../tests/DesktopPet.DiagnosticProbe/WindowPerchLiveScenario.cs)：`drag-preempted` 事件处理器内（`CancelForDrag` 同步回调中，此刻 `EndPerchWobble` 尚未执行）写 `preempt-angle-pre` 记录（tMs + `wobbling`/`wobbleAngle`）；随后经 `Dispatcher.BeginInvoke(Send)` 在 `CancelForDrag` 完整返回后写 `preempt-angle-post` 记录。两条记录写入 `perch-live-events.jsonl`。
- [Invoke-P5A001Preview.ps1](../tools/Invoke-P5A001Preview.ps1)：步骤 12 断言改为直接消费这两条记录——`pre` 必须 `wobbling=true` 且 `|angle|>0.001`（接管前非零、正在晃），`post` 必须 `wobbling=false` 且 `|angle|<0.001`（接管完成时刻归零）。

### 验证（M-15 取证轮，`run-20261118-214848-notepad`）

事件流原文（同一接管 tick 内，`drag-preempted` t=15702 → `pre` t=15703 → `post` t=15707，间隔 5ms，远早于约 0.7s 的自然衰减包络）：

```
{"tMs":15702,"type":"drag-preempted",...,"targetHwnd":"0x271314",...}
{"tMs":15703,"type":"preempt-angle-pre","wobbling":true,"wobbleAngle":-4.19}
{"tMs":15707,"type":"preempt-angle-post","wobbling":false,"wobbleAngle":0}
```

清单第 20 条 PASS：`preNonZero=True(wob=True angle=-4.19) zeroAtPost=True wobbling=False angle=0`。前置「拖动时小熊正在晃」PASS `wobbling=True angle=1.211`；`drag-preempted` PASS（moved=True、dragCancelled=目标）。其余 16 条同轮 PASS；唯一 FAIL 为「远端遮挡」——`occBy` 留痕显示遮挡者是用户浏览器窗口（近全屏 [-8,-8,1928,1040]）在运行期间盖住落脚区，产品按 R1 F-04 规格正确 occluded-detach，属环境干扰；该项沿用 `run-20261118-212632-notepad` 轮 PASS（occ=False footSame=True active=True）证据。清单条数更正：R1 补证轮 `run-20261118-190108` 实际 19 条 PASS（此前各文档误记 22）。

## 未验证边界（不变）

暂停打断实机路径仍仅单测覆盖；跨屏、混合 DPI、真实用户窗口动态遮挡未测。

## 复核请求

请监理复核 R2 两项收尾与 R3 补充取证：F-01 帧切换同 tick 同步（`UpdateFrame` 单点设置 + 素材脚点完整变换单测 7/7）、F-03 接管时刻角度记录（R3 轮探针 `preempt-angle-pre` -4.19° → `preempt-angle-post` 0，同 tick 5ms 内；R2 轮心跳取证同结论）。全量回归 1083/1083；R2 轮 18/18 ALL PASS，R3 取证轮关键断言 PASS（唯一 FAIL 为用户浏览器盖住落脚区的环境干扰，该项沿用 R2 轮 PASS）。落位、跟随、脱离行为未变。
