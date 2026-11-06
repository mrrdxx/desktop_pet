# 默认小熊运行资源

角色ID：bear-v1。完整设定/来源见 [设计文档](../../../docs/design/BEAR_CHARACTER.md)，生成母版和重复导出方式见 [母版说明](../../art/bear-v1/README.md)。

九种原始静态表情、两张眨眼过渡及两张温和拖动/松手PNG（共13张，原startled保留但不再注册拖动）位于本目录，均120×120透明、96DPI，脚点(60,110)。当前注册关系以character.json为准。MainWindow优先加载bear，旧default保留为原型回退。

- idle：固定idle_0中性待机图，不再切换content笑脸。
- blink：blink_half/closed/half/idle，40/70/70/40ms，单次220ms。App通过Core的IdleBlinkScheduler仅覆盖idle的渲染帧，不改变动作所有权。3～7秒间隔、低概率双眨及中断规则以PRODUCT D-011章节为准。
- blink_half/closed以idle_0为固定底图，仅替换眼部两块局部区域；每个像素的Alpha及眼部外RGBA不变，嘴形和身体完全保留。来源和可重复导出见 [眨眼素材说明](../../art/bear-v1/blink-source.md)。
- curious：好奇，offsetY=9，PNG本身上移9，世界脚点不变。
- 快速移鼠受惊已按D-009取消，不再注册dodge动作；原startled图片仅作旧表情储备。
- drag：温和意外脸drag_gentle，仅改眼/眉，不改变嘴或身体，没有被拎起的身体动作。
- release：drag_gentle90ms→release_settle90ms→idle70ms，仅在真实拖动结束后作为idle的面部覆盖播放；位置与捕获立即停止，不把250ms过渡当作松手延迟。新输入可取消，暂停冻结；规格见PRODUCT D-012说明，来源见 [拖动素材](../../art/bear-v1/drag-source.md)。
- grievance：独立委屈脸，用于既有10秒情绪表达，不再借用curious；背对动作仍暂缓。
- happy/annoyed/sleepy/content/bashful：已注册、可供播放器加载的静态表情；content只作情绪素材储备，不再用于普通眨眼。尚未增加这些表情的自动行为触发。

表情切换/输入回归和限制见 [本轮接入记录](../../../docs/history/ART-001-bear-integration.md)。不要删除旧原型或把未接入触发的静态资源写成新功能。
