# 小熊美术母版 v1

2026-10-25 ART-002增加了独立 [中性眨眼母版与导出说明](blink-source.md)。原九格表情母版保留，新增half/closed以原idle为固定底图，不能从content笑脸重新裁切。完整重复导出先运行下文基础导出，再运行tools/Export-BearBlinkAssets.ps1，二者都不覆盖JSON。

用户于2026-10-24确认本项目 [小熊设定图](../../../docs/design/images/bear-character-v1.png)，并要求写入现有项目。expression-atlas.png 使用内置 image_gen 编辑工具参照该图生成，原始输出1254×1254、真实透明背景、3×3全身静态表情；不是旧评审板的裁切。原生成输出保留在 Codex generated_images，本目录保存项目副本。

行优先顺序：待机、开心、好奇、受惊、委屈、小生气、困倦、舒服、害羞。九格均正面站立，暂未制作身体动作。

运行资源在 assets/pets/bear/，通过 [导出脚本](../../../tools/Export-BearAssets.ps1) 进行分格、缩放、透明边缘整理和脚点对齐，未通过代码重绘角色。输出120×120、96DPI、RGBA PNG。普通帧不透明底部在y=110；好奇帧上移9像素至y=101，JSON补偿offsetY=9，保持原非零脚点补偿回归。

## 重复导出

在仓库根目录运行 `./tools/Export-BearAssets.ps1`。此命令只导出PNG，不覆盖角色JSON。若调整母版，须同时核对PNG实际尺寸、透明区、头部命中与世界脚点。默认原占位角色未删除。

## 图像生成提示词（原文）

```text
Use case: identity-preserve. Asset type: ONE production sprite atlas of static facial variants for the Windows desktop bear in the attached approved concept sheet. Use the reference only for character identity, not its page layout. Preserve exactly this cute warm biscuit brown bear, round ears, cream muzzle and belly, dark cocoa outlines, plush soft 2D illustration, same big round head and short body. Produce a SQUARE 3 by 3 sprite atlas, nine equal square cells, on a genuinely transparent alpha background. In EVERY cell show ONE complete full-body FRONT-view bear, standing in the EXACT SAME neutral resting pose, same head and body size, ear positions, cream belly patch, arms down, feet flat at identical baseline. NO head-only portraits. Every bear must fit wholly in its cell with generous transparent margins, no touching adjacent cells. The ONLY variation between cells is facial expression; no body actions, no motion, no rotation. Row-major cell order: 1 neutral gentle smile and open eyes, 2 joyful crescent eyes and open smile, 3 curious slightly uneven eyebrows and focused eyes; 4 startled wide eyes and tiny O mouth, 5 aggrieved watery eyes and downturned mouth, 6 gently annoyed puffed cheeks and mild angry eyebrows; 7 sleepy heavy eyelids and tiny open mouth, 8 contented closed smiling eyes and blush, 9 bashful glancing eyes and blush. Match the nine faces of the reference while keeping full-body silhouette consistent. Center each full body identically in its cell, keep paws/feet and ears perfectly within bounds. Simplify very fine fur texture so it reads beautifully at small desktop size. This is a usable game resource sheet, no title, no labels, no letters, no numbers, no grid lines, no palette chips, no floor, absolutely NO cast shadow or ground oval, no background or checkerboard drawn into pixels, no clothing, no accessories, no watermark. Clean transparent space between every complete bear.
```
