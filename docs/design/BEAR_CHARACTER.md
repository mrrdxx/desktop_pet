# 小熊形象与静态表情设计

日期：2026-10-24。设计版本：v1 已获用户认可。用户明确角色为“可爱小熊”，要求先完善形象、表情，动作暂缓；随后授权“可以，把你的修改写入现有的项目，注意把文档也做相应的修改”。本轮运行资源已接入，自测与实机边界见 [接入记录](../history/ART-001-bear-integration.md)。

## 当前交付与范围

2026-10-25用户批准D-011自然观感首版：安静、柔软、有一点好奇，反应及时。新增两张局部眼部帧（半闭/闭眼），普通眨眼不改变嘴形/身体；眨眼节奏和短暂好奇规格见PRODUCT。身体动作和其余行为表情调整仍暂缓；本次实现/证据入口见 [ART-002交接](../handoffs/ART-002-natural-expressions.md)。

- [形象与表情设定图 v1](images/bear-character-v1.png)：一个全身正面主形象、正/侧/背静态参考、九种脸部表情。
- 上述旧图仍是设计参考板，不能直接作为运行图集。现另生成 [透明表情母版](../../assets/art/bear-v1/expression-atlas.png)，导出 [bear运行资源](../../assets/pets/bear/character.json)，MainWindow优先加载bear；原default保留。查看 [实际120像素素材预览](images/bear-runtime-preview.png)。
- 原v1先确定脸型、身形、毛色、画风与表情差异；后续已接入并按D-011增加眨眼眼部帧。身体动作和音效以后单独安排。角色名未定，不默认附加服装或饰品。

## 造型提案

温暖、柔软、亲近，带一点容易委屈的小脾气。大圆头、小圆耳、短四肢、略鼓的肚子、站稳的小脚；头约占全身高度一半，避免身体小到看不见。
暖棕毛色、奶油色口鼻与肚皮，深可可轮廓/眼鼻，少量淡粉腮红。奶油口鼻和小鼻子形成稳定的识别点，九种表情保持耳位、眼距和口鼻比例一致。
画风采用圆润二维插画，清晰深浅关系和轻微柔软质感。实际小尺寸资源应简化细毛、细高光和纹理，保证在现有120×120画布上看得清眼睛、嘴和轮廓；不以放大设定图好看代替实际小尺寸验收。

## 九种静态表情提案

1. 待机：自然豆豆眼、小微笑，适合作为日常基础表情。
2. 开心：弯眼、开口笑，比待机明显更高兴。
3. 好奇：眉毛一高一低、眼神集中，避免与委屈混淆。
4. 受惊：睁大眼睛、小圆嘴；用短暂惊讶表达，避免死亡/X眼。
5. 委屈：内侧眉抬高、嘴角向下、眼中含泪；不使用夸张哭泣遮住脸。
6. 小生气：轻皱眉、鼓脸，仍然可爱，不用凶狠攻击表情。
7. 困倦：半闭眼和小张嘴；本轮仅静态表情，不制作打哈欠动作。
8. 舒服：闭眼浅笑、放松腮红，适合未来抚摸反馈。
9. 害羞：侧向眼神和淡腮红，作为可选表现储备，不因此新增情绪状态变量。

九种静态表情已生成并注册，不等于九种自动行为或九套身体动作。已接待机/独立眨眼、好奇、拖动临时受惊脸和委屈；快速移鼠受惊已按D-009取消，其图片仅供拖动使用。其他表情保留供后续映射，具体以运行资源README为准。背面静态参考供未来背对动作使用，本轮未制作该动作。

## 运行资源约束与后续优化

- 已依据确认图另行生成统一全身透明母版并导出独立PNG；旧设定板只作参考，不直接裁成运行图。
- 沿用当前角色格式约定：现有运行画布120×120、脚点(60,110)。如用更大绘制母版，应在接入前导出符合声明尺寸的资源；不能直接把高分辨率图片塞入原 JSON。
- 表情切换只改变脸部表达，头身比例、身体外轮廓与脚点应稳定。静态表情不凭多张素材数量声称动画丝滑。
- 已检查120×120素材的深浅背景预览，并运行真实像素/脚点回归。较早150% [运行检查](../history/ART-001-animation-review.md)曾发现眨眼身体轮廓有差异；ART-002现改用固定底图眼部帧，严格像素检查通过，版本边界见本页当前交付说明。100%/多屏仍未补；旧快划触发按D-009取消。不将设定图目视检查当作完整运行验收。
- 待机、独立中性眨眼、好奇、拖动临时受惊脸、委屈已接入；舒服素材不再作眨眼，保留供未来情绪反馈。身体动画及更多触发映射待后续任务安排，不因素材存在就宣称对应行为已完成。

## 版本与生成方式

使用内置 image_gen 工具生成，无 CLI/API key 路径。生成原图保留在 Codex generated_images，项目副本位于本目录 images/bear-character-v1.png，SHA256：D8504CE87F7F93D6049323DE172A5A4A6BC218C93520F9F28796B7B72941F490。
用户已认可本版设计方向；实际运行观感仍可按反馈改进。透明母版的生成提示词/导出过程另存 assets/art/bear-v1/README.md；后续修改另存版本，不覆盖已被引用的设计图。

### 本次生成提示词（原文）

```text
Use case: stylized-concept. Asset type: polished character design and static expression reference sheet for an original Windows desktop companion. Create ONE cohesive landscape character design board, roughly 3:2, on a warm ivory plain background with generous clean whitespace. Subject: ONE original irresistibly cute little brown bear, with a soft chubby silhouette, large rounded head about half the full-body height, two small round ears, warm biscuit-brown fur, cream muzzle and oval belly patch, dark cocoa small bean eyes and tiny oval nose, short rounded arms and little grounded feet. Charming and emotionally readable at a desktop display size of 120 pixels. Cute but not sugary or cluttered. Soft clean 2D hand-drawn illustration, smooth clear cocoa outlines, restrained two-tone shading, minimal plush texture; no realistic hairs, no 3D render, no pixel art. No clothing, bow, hats, held objects or decorative props. Keep color, muzzle size, eye spacing, ear placement, head shape and silhouette consistent in every drawing. The character must look like a bear, no tail like a cat, no rabbit ears. Composition: left third shows one LARGE full-body front neutral standing character as the hero, with a small front/side/back static model reference row below (same neutral resting stance, no action sequence). Right two thirds shows a neat 3-by-3 grid of large expressive HEAD portraits of exactly the same bear. Nine expressions in row order: relaxed neutral, joyful smile with crescent eyes, curious with uneven eyebrows; startled with round wide eyes and tiny o mouth, aggrieved with downturned mouth and watery eyes but no waterfall tears, gently annoyed with puffed cheeks and mild brows; sleepy half-closed eyelids, contented enjoying head pats with closed smiling eyes and rosy cheeks but no hand shown, bashful looking aside. Express through eyes, brows, muzzle and cheek shapes; no head rotations that hide facial features. These are STATIC expression alternatives, not animation frames or action poses. Add small tasteful readable Chinese labels under the nine heads, exactly: '待机', '开心', '好奇', '受惊', '委屈', '小生气', '困倦', '舒服', '害羞'. A small simple title at top exactly '小熊 · 形象与表情初稿'. Avoid other text, no watermarks, no logos, no busy panels. This is an art direction sheet for review, not a ready-to-import sprite atlas. Prioritize excellent silhouette, warmth and strong facial differences, especially curious vs aggrieved vs annoyed.
```
