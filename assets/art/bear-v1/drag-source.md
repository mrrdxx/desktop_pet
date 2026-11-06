# 温和拖动与松手素材

2026-10-25，ART-003/D-012。内置imagegen编辑既有idle_0，输出已复制为 [drag-source.png](drag-source.png)，两格分别为轻微意外、逐渐放松。第一次请求网络失败，原工具重试成功，未使用CLI/API回退。

运行图不直接替换整身：仍以原idle_0逐像素复制底图，仅替换左眼/眉 `[37,30,15,27]`、右眼/眉 `[68,30,15,27]` 区域内RGB，区域边缘渐回原像素。嘴、鼻、耳、身体、脚底、阴影以及全图Alpha均不变。输出 [drag_gentle.png](../../pets/bear/drag_gentle.png) 和 [release_settle.png](../../pets/bear/release_settle.png)，120×120、96DPI。

在仓库根目录重复导出：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Export-BearBlinkAssets.ps1 -BlinkSheet assets/art/bear-v1/drag-source.png -IncludeBrows -FirstName drag_gentle -SecondName release_settle
```

复用已有局部合成器，未新增图像库。默认不传IncludeBrows时仍导出旧眨眼帧；已输出到隔离目录并逐文件验证SHA256与现有两张眨眼PNG完全相同。运行映射与250ms时序见PRODUCT/角色JSON，不在此另维护行为规格。

## 成功请求提示词

Use case: precise-object-edit. Edit the attached existing tiny brown bear into a transparent TWO-CELL sprite sheet, two equal square cells side-by-side, full bear same scale, pose and baseline in both. Left cell: gentle mild surprise while picked up, only slightly raised symmetric eyebrows and slightly more attentive small bean eyes; no frightened huge eyes or white rings. Right cell: settling back to calm, eyebrows softly relaxed and eyes still open with softened upper lids, not sleepy or smiling closed eyes. Change ONLY eyes and eyebrows. Keep original closed neutral mouth, nose, muzzle, blush, head outline, ears, body, arms, feet, fur colors and soft 2D style exactly unchanged. No body motion, tilt, deformation, props, text, grid or extra characters. Generous transparent margins, genuinely transparent alpha background. This is a restrained desktop companion; subtle and legible at 120 pixels. Final eye/brow patches will be composited on the exact original body pixels, so do not redesign the bear.
