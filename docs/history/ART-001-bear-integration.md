# ART-001 小熊形象与静态表情接入记录

后续范围变更（2026-10-25）：[D-009](../DECISIONS.md#d-009取消快速移鼠受惊功能) 已取消快速移鼠受惊，当前实现见 [INPUT-001交接](../handoffs/INPUT-001-remove-mouse-fright.md)。本记录仍描述移除前版本，旧快划待测不是当前门槛；当时的资源/证据不追改成新版本。

日期：2026-10-24～25（Asia/Shanghai）。本助手承担本轮美术与接入实现，以下为自测，不代签独立监理通过。
授权：用户认可小熊设定后要求“可以，把你的修改写入现有的项目，注意把文档也做相应的修改”。形象和静态表情先做，动作暂缓。

## 版本边界

Git HEAD为a2412f7。开始已有15个文件/目录的未提交P1-004撤回修改，包括PeekScheduler及其测试删除、App/Core存档退回和TASKS恢复todo；本轮未发起、撤销或覆盖这些改动。构建和测试对象为这个既有工作区加本轮小熊接入，不能把整个git diff都归因于本助手。
本轮直接改动：新增 assets/pets/bear 与 assets/art/bear-v1，MainWindow选择bear优先并把委屈映射到grievance；新增 BearResourceTests，调整旧脚点测试读取当前bear，表情生命周期断言使用grievance；将旧占位图固定透明坐标用例改为从实际PNG读取相邻Alpha边界。原default资源未删除。完整交接见 [接入交接](../handoffs/ART-001-bear-integration.md)。

## 美术与实际运行范围

- 用户已确认的 [初版设定](../design/images/bear-character-v1.png) 保留为设计参考板。
- 使用内置image_gen参考确认图，另生成 [透明全身表情母版](../../assets/art/bear-v1/expression-atlas.png)，1254×1254、3×3，无文字/背景。生成提示词及来源在 [母版说明](../../assets/art/bear-v1/README.md)。
- [导出脚本](../../tools/Export-BearAssets.ps1) 只做分格/缩放/Alpha整理/脚点对齐，不重绘角色。输出9张120×120、96DPI PNG，普通底部110、curious底部101+JSON offset9；世界脚点保持110。
- 当前默认角色bear-v1，原default作为原型回退。idle沿用800/200ms的普通脸/闭眼舒服切帧，curious/dodge/drag对应好奇/受惊/受惊临时脸，grievance使用独立委屈脸。
- happy/annoyed/sleepy/content/bashful已注册为静态资源。除content参与idle闭眼外，未新增自动触发状态，不宣称实现新的情绪行为。未制作背对/跑动/攀爬等身体动作。
- [实际120像素深浅背景预览](../design/images/bear-runtime-preview.png) 已目视检查。图集不是直接塞进旧JSON；独立PNG尺寸/DPI/透明与脚点有真实像素检查。

## 构建和回归

```powershell
dotnet build DesktopPet.slnx -c Debug --artifacts-path artifacts/bear-integration/build -m:1 --no-restore --nologo -v minimal
dotnet test DesktopPet.slnx -c Debug --artifacts-path artifacts/bear-integration/build --no-build --no-restore -m:1 --nologo -v minimal
```

最终构建零警告/错误；**Core208+App128=336/336通过，0失败/跳过**，均为本轮实际编译并运行，新增2项BearResourceTests。使用独立输出避免旧PowerShell加载DLL占用；首次新输出目录还原因沙箱不能访问NuGet漏洞元数据失败，获准联网后还原成功，未禁用检查或新增依赖。
首次回归335通过/1失败：旧透明区用例写死(70,12)，在新小熊上是可见像素。修正为从当前PNG寻找相邻不透明/透明像素，以小于2px的真实处理入口移动验证Alpha退出；未删除测试或放宽断言。脚点回归继续保留curious非零补偿和错误补偿负对照。
新表情改变了委屈动作的资源ID，因此仅将“表达期间应是curious”的断言改为grievance；表达结束后的悬停仍应触发curious，未混改该预期。

## 实机尝试：未完成，不计通过

先后运行两轮既有 Run-EmotionLive.ps1，窗口实际DPI144（150%），日志明确显示加载 assets/pets/bear。第一轮真实输入未能正确落在角色上，未获得+5，保留 [不完整日志](evidence/ART-001-live-incomplete.txt)。
第二轮增加实际指针和WindowFromPoint归属诊断：目标(615,480)，实际指针(756,588)，命中PID与自建窗口不同；只读查询确认命中为游戏进程，探针中止、恢复指针并正常退出。[窗口归属日志](evidence/ART-001-live-foreign-window.txt)。这不能证明小熊手势通过，也不单凭环境干扰判产品手势实现失败。未切换或结束游戏。
测试确认原用户存档摘要未变；以后所有左键按下前都检查是否命中本次测试进程，不符时不发送按下。加固后的探针已通过BuildOnly编译，但未再在游戏环境中重跑实机输入。第一次未完成尝试发生在加固之前，不能把后补保护描述成当时已生效。
本轮未重签或覆盖原P1-003 R9历史证据：那份证明的是旧资源版本。新小熊资源实机鼠标仍需空闲桌面复核；100%/多屏/锁屏未新增验证。

## 当前可运行与接续

本轮实际已构建程序：`artifacts/bear-integration/build/bin/DesktopPet.App/debug/DesktopPet.exe`。可以启动查看新小熊；本轮未为展示切换正在使用的游戏。常规 `dotnet run --project src/DesktopPet.App/DesktopPet.App.csproj` 在正常构建环境也会复制并优先读取bear。
ART-001保留awaiting-verification，等待可交互桌面下的运行观感/鼠标复核；当前实现和336项回归已完成。开发AI按交接核对既有撤回改动与本轮美术差异，原样保留用户确认设计和证据，不自动恢复P1-004或推动新阶段。未提交、未推送。
