# 中性眨眼素材与重复导出

2026-10-25，ART-002/D-011。使用内置imagegen编辑既有 `assets/pets/bear/idle_0.png`，未走CLI/API路径；生成的双格透明母版已复制为本目录 [blink-source.png](blink-source.png)，不依赖Codex外部生成目录。

左格半闭、右格闭眼。生成图只提供眼部内容，不能整张重新缩放替换运行角色；实际底图仍为原idle_0。重复导出：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Export-BearBlinkAssets.ps1
```

脚本按两格Alpha边界归一化到原底图边界，仅在左眼 `[37,39,15,18]`、右眼 `[68,39,15,18]`（x/y/width/height）内合成，并在区域边缘保留原像素；其他RGBA逐像素复制。Alpha全图不变，嘴/鼻/耳/身体/脚底/阴影未重画。输出 [blink_half.png](../../pets/bear/blink_half.png) 和 [blink_closed.png](../../pets/bear/blink_closed.png)，均120×120、96DPI。

复制使用原始RGBA读取/写入，不用Bitmap(Image)重绘；后者曾使半透明轮廓RGB产生1级取整差，已由实际像素测试检出并修正。新输出已通过眼部外像素和全图Alpha的严格相等断言。

效果预览见 [睁眼/半闭/闭眼对照](../../../docs/design/images/bear-natural-blink-preview.png)。舒服笑脸content_0保留为独立情绪储备，不再作为眨眼。

## 原始提示词

Use case: precise-object-edit. Asset type: two-state blink sprite sheet for an existing Windows desktop companion. Input image is both the exact edit target and identity reference: ONE existing warm brown little bear, front view, neutral tiny closed mouth. Create ONE transparent PNG sprite sheet of exactly two equal square cells, one horizontal row, with generous transparent gutters and no text. Left cell: the reference bear with both eyes HALF CLOSED during a calm neutral blink. Right cell: the same bear with both eyes FULLY CLOSED into very small neutral relaxed eyelid curves, NOT smiling crescents and NOT squeezed shut. Do not use the happy/content expression. In both cells change ONLY the small eye areas; keep the original neutral mouth, eyebrows, nose, cheek blush, head outline, ear positions, body, feet, ground shadow, proportions, pose, colors and fur texture exactly unchanged. Same frontal view, no head tilt, no motion, no props, no effects. Both bears at precisely equal size, orientation and baseline with no cropping; entire body and original small ground shadow visible. The neutral open reference is deliberately tiny; retain its simple soft hand-painted 2D character at desktop sprite scale. Do not reinterpret or improve the character, do not repaint the whole body. The output will be cropped to per-eye patches and composited onto the exact original pixels to enforce identity. Use a genuinely transparent background, no checkerboard. No text, no borders, no extra sprites.
