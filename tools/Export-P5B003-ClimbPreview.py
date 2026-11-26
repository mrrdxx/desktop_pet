#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# P5B-003: 三阶段攀爬动作素材程序化派生（PIL，以 idle_0 为底局部重绘）。
# 手法参照 P4-002 R5 F-04（stretch：以 idle 为底改局部，不触 JSON/代码）
# 与 P1 眨眼派生（局部区域合成，逐像素不破坏周边）。
#
# R2 F-01 整改（2026-11-22，替换 R1 版 im.copy() 叠加为 v2 重建方案）：
#   1. 不再保留 idle 原垂臂再叠加新臂（R2 反例：四臂/细长硬边）。改为
#      rebuild_side_edges() 真正收缩 y44-70 躯干侧缘：把超出"新无臂轮廓"
#      的旧像素删除（非叠加颜色），再用毛色+羽化重建平滑收窄侧缘，
#      从源头消除 idle 垂臂感；
#   2. 画恰好两条粗短圆润前肢（side 高伸抓 w16；edge 低姿侧探 w13），
#      臂主体位于身体轮廓外侧空气层（x<21 / x>99），仅根部从真实肩部
#      （左 (25,61)/右 (95,61)，由 idle y56-72 体侧轮廓反推）连接，
#      完全避开脸颊/耳/额（y<44 的 solid 像素零改动）；
#   3. 爪型：深爪椭圆 + 奶油爪垫 + 亮侧高光 + 下缘阴影，粗短圆润，
#      光影与现有小熊风格一致；不保留任何旧垂臂/叠加圆环。
#   4. side 三帧循环：0=左伸高抓/右低握，1=双爪上伸承重，2=右伸高抓/左低握
#      edge 三帧循环：0=探左(dx-3)，1=居中(dx0)，2=探右(dx+3)，低伸爪
#      从肩部连出，身体 y<=102 整体横移表达上缘挪步，脚带不动。
#   5. 脸部/耳部（y<44 idle solid 像素）与脚带（y104-110）零改动；
#      land（收脚回站立方向）管线保持原样，输出与既有 assets 逐字节一致。
# 本脚本只生成帧到输出目录；运行脚本负责拷入 assets/pets/bear/。
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

BASE = Path(r"assets/pets/bear/idle_0.png")
OUT = Path(sys.argv[1] if len(sys.argv) > 1 else r".temp/p5b-003/r2-final")

# idle_0 实测色板（本脚本由实际像素提取，非记忆）
BODY = (213, 149, 91)        # 身体主棕（取色板众数）
BODY_DARK = (188, 128, 74)   # 棕阴影（轮廓过渡，近似）
OUTLINE = (62, 23, 9)        # 深可可为轮廓/眼
CREAM = (247, 208, 167)      # 奶油口鼻/肚皮
MOUTH_DARK = (128, 72, 68)   # 口腔内（P4-002 stretch 先例用色）

# 新爪臂专用色（idle 毛绒过渡采样：轮廓深棕半透明 α~170-215，毛身 ≈(207,142,84)）
FUR = (207, 142, 84, 255)       # 毛身主色
FUR_SOFT = (228, 165, 104, 255) # 亮侧过渡
FUR_DARK = (150, 90, 48, 255)   # 爪/外缘阴影
SHELL = (56, 19, 6, 150)        # 手臂描边（细半透明深棕，粗短不用硬边）
L_SHOULDER = (25, 61)
R_SHOULDER = (95, 61)

# 侧缘重构带（覆盖原臂凸起 y44-70，避开腿 y71+；与 R2 候选 v2 完全一致）
L_BAND = (10, 44)    # 左带 x10..x44（候选真值）
R_BAND = (76, 110)   # 右带 x76..x109（候选真值）
Y0, Y1 = 44, 70

# 新无臂躯干侧缘（由肩下收窄到腰，与 idle y70 腰宽衔接）
def new_left_edge(y):
    pts = [(44, 27), (48, 28), (52, 29), (56, 30), (60, 32), (64, 34), (68, 36), (70, 38)]
    for i in range(len(pts) - 1):
        y0, x0 = pts[i]
        y1, x1 = pts[i + 1]
        if y0 <= y <= y1:
            return x0 + (x1 - x0) * (y - y0) / (y1 - y0)
    return 38

def new_right_edge(y):
    return 120 - new_left_edge(y)


def load_idle():
    return Image.open(BASE).convert("RGBA")


def save(im, name):
    OUT.mkdir(parents=True, exist_ok=True)
    im.save(OUT / name, dpi=(96, 96))


def body_color(im, y):
    """取躯干主体毛色（优先 x40/x80，带内已透明则回退）。"""
    px = im.load()
    for sx in (40, 30, 50, 80, 70, 90):
        c = px[sx, min(115, y + 2)]
        if c[3] > 200:
            return c
    return FUR


def rebuild_side_edges(im):
    """删除 y44-76 内超出新无臂轮廓的旧像素，重建毛色渐变侧缘（真正收窄躯干）。
    与 R2 候选 v2 的 rebuild_side_edges 逐一致，消除 idle 垂臂感。"""
    im = im.copy()
    px = im.load()
    # 1) 删除超边旧像素（从外向内扫描到新边缘）
    for y in range(Y0, Y1 + 1):
        lx = int(round(new_left_edge(y)))
        rx = int(round(new_right_edge(y)))
        for x in range(0, 120):
            if x <= lx or x >= rx:
                if L_BAND[0] <= x <= L_BAND[1] or R_BAND[0] <= x <= R_BAND[1]:
                    if px[x, y][3] > 0:
                        px[x, y] = (0, 0, 0, 0)
    # 2) 在带内重建渐变色（新边缘向内 3px 实色，向外 10px 渐变到透明）
    band = Image.new("RGBA", im.size, (0, 0, 0, 0))
    bp = band.load()
    for y in range(Y0, Y1 + 1):
        lx = int(round(new_left_edge(y)))
        rx = int(round(new_right_edge(y)))
        c = body_color(im, y)
        for x in range(lx - 8, lx + 4):
            if not (L_BAND[0] <= x <= L_BAND[1]):
                continue
            t = (x - (lx - 8)) / 12.0          # 0(外)..1(内)
            a = int(255 * min(1.0, max(0.0, t * 1.6)))
            bp[x, y] = (c[0], c[1], c[2], a)
        for x in range(rx - 4, rx + 9):
            if not (R_BAND[0] <= x <= R_BAND[1]):
                continue
            t = ((rx + 8) - x) / 12.0          # 0(外)..1(内)
            a = int(255 * min(1.0, max(0.0, t * 1.6)))
            bp[x, y] = (c[0], c[1], c[2], a)
    band = band.filter(ImageFilter.GaussianBlur(1.0))
    bp2 = band.load()
    for y in range(120):
        if y < Y0 or y > Y1:
            for x in range(120):
                bp2[x, y] = (0, 0, 0, 0)
    im.alpha_composite(band)
    return im


def capsule(d, p1, p2, w, fill):
    """圆角粗线段（壳/毛分两层调用）。"""
    d.line([p1, p2], fill=fill, width=w)
    r = w // 2
    d.ellipse((p1[0] - r, p1[1] - r, p1[0] + r, p1[1] + r), fill=fill)
    d.ellipse((p2[0] - r, p2[1] - r, p2[0] + r, p2[1] + r), fill=fill)


def draw_limb_v2(im, pts, w):
    """粗短圆润前肢：细壳 + 主毛，末端圆爪 + 奶油爪垫 + 高光阴影。
    与 R2 候选 v2 完全一致（用户已确认造型）。"""
    d = ImageDraw.Draw(im)
    for i in range(len(pts) - 1):
        capsule(d, pts[i], pts[i + 1], w + 2, SHELL)
    for i in range(len(pts) - 1):
        capsule(d, pts[i], pts[i + 1], w, FUR)
    for p in pts[1:-1]:
        rr = w // 2
        d.ellipse((p[0] - rr, p[1] - rr, p[0] + rr, p[1] + rr), fill=FUR)
    dx, dy = pts[-1]
    for rx, ry in [(w // 2 + 1, w // 3), (w // 2 - 1, w // 3 - 1)]:
        d.ellipse((dx - rx, dy - ry, dx + rx, dy + ry), fill=FUR_DARK)
    d.ellipse((dx - 3, dy - 1, dx + 3, dy + 3), fill=CREAM)
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        d.line([(a[0] - 1, a[1] - 1), (b[0] - 1, b[1] - 1)], fill=FUR_SOFT, width=1)
        d.line([(a[0] + 1, a[1] + 2), (b[0] + 1, b[1] + 2)], fill=(120, 66, 32, 70), width=1)


def shift_torso(im, dx):
    top = im.crop((0, 0, 120, 103))
    im2 = Image.new("RGBA", (120, 120), (0, 0, 0, 0))
    im2.alpha_composite(top, (dx, 0))
    im2.paste(im.crop((0, 103, 120, 120)), (0, 103))
    return im2


def side_climb(im, variant):
    """侧边攀抓（高伸扒边 3 帧循环）：0=左伸高/右低握，1=双爪上伸承重，2=右伸高/左低握。
    双臂从肩部连出，臂主体在空气层（x<21 / x>99），避开头/脸/耳。"""
    f = rebuild_side_edges(im)
    if variant == 0:
        draw_limb_v2(f, [L_SHOULDER, (8, 46), (8, 24)], w=16)   # 左伸高抓
        draw_limb_v2(f, [R_SHOULDER, (105, 72), (108, 82)], w=13)  # 右低握（略低）
    elif variant == 1:
        draw_limb_v2(f, [L_SHOULDER, (8, 46), (8, 24)], w=16)
        draw_limb_v2(f, [R_SHOULDER, (112, 46), (112, 24)], w=16)  # 双爪上伸承重
    else:
        draw_limb_v2(f, [L_SHOULDER, (15, 72), (12, 82)], w=13)  # 左低握（略低）
        draw_limb_v2(f, [R_SHOULDER, (112, 46), (112, 24)], w=16)  # 右伸高抓
    return f


def edge_crawl(im, variant):
    """上缘低姿挪步 3 帧循环：0=探左(dx-3)，1=居中(dx0)，2=探右(dx+3)。
    低伸爪从肩部连出，身体 y<=102 整体横移表达上缘挪步，脚带不动。"""
    f = rebuild_side_edges(im)
    if variant == 0:
        draw_limb_v2(f, [L_SHOULDER, (10, 68), (9, 78)], w=13)   # 左短臂探下（候选 v2 真值）
        draw_limb_v2(f, [R_SHOULDER, (110, 68), (111, 78)], w=13)
        f = shift_torso(f, -3)
    elif variant == 1:
        draw_limb_v2(f, [L_SHOULDER, (12, 68), (12, 78)], w=13)
        draw_limb_v2(f, [R_SHOULDER, (108, 68), (108, 78)], w=13)
    else:
        draw_limb_v2(f, [L_SHOULDER, (9, 68), (8, 78)], w=13)
        draw_limb_v2(f, [R_SHOULDER, (111, 68), (112, 78)], w=13)
        f = shift_torso(f, 3)
    return f


def face_forward_focused(im):
    """专注盯边脸（仅 land 收脚帧沿用，保持既有 assets 逐字节一致）。"""
    d = ImageDraw.Draw(im)
    d.ellipse((26, 23, 34, 29), fill=OUTLINE)
    d.ellipse((86, 23, 94, 29), fill=OUTLINE)
    d.line((24, 21, 31, 19), fill=OUTLINE, width=2)
    d.line((96, 21, 89, 19), fill=OUTLINE, width=2)
    d.ellipse((56, 31, 64, 37), fill=MOUTH_DARK)
    return im


def land_collect_feet(im, amount=2):
    """顶部落位收脚帧：双足以中轴 x=60 对称向内收拢 amount 像素（沿用既有实现）。"""
    band = im.crop((35, 103, 85, 111))
    left = band.crop((0, 0, 25, 8))
    right = band.crop((25, 0, 50, 8))
    im2 = im.copy()
    d = ImageDraw.Draw(im2)
    d.rectangle((35, 103, 85, 110), fill=(0, 0, 0, 0))
    im2.paste(left, (35 + amount, 103), left)
    im2.paste(right, (60 - amount, 103), right)
    return im2


def assert_collected_feet(name, im, idle_px):
    """收脚帧专用断言：y104..110 每行不透明区间不越出 idle 对应行，
    区间中心≈60（世界脚点稳定），y=110 仍落地。输出每行实测证据。"""
    px = im.load()
    rows = []
    for y in range(104, 111):
        xs = [x for x in range(120) if px[x, y][3] > 0]
        idle_xs = [x for x in range(120) if idle_px[x, y][3] > 0]
        assert xs, f"{name} foot row {y} empty"
        assert min(xs) >= min(idle_xs), f"{name} row {y} left out {min(xs)}<{min(idle_xs)}"
        assert max(xs) <= max(idle_xs), f"{name} row {y} right out {max(xs)}>{max(idle_xs)}"
        center = (min(xs) + max(xs)) / 2
        rows.append((y, min(xs), max(xs), round(center, 1)))
        assert abs(center - 60) <= 1.5, f"{name} row {y} center {center} drifts"
    assert any(px[x, 110][3] > 0 for x in range(120)), f"{name} y=110 empty (not grounded)"
    print(f"{name}: collected-feet rows(y,min,max,center)={rows} centers≈60 ok")


def print_ascii(im, label, path):
    """轮廓字符画目检：120 宽减半为 60 字符，alpha 分级。返回文本而非写文件。"""
    px = im.load()
    lines = [f"== {label} =="]
    for y in range(0, 120, 2):
        row = []
        for x in range(0, 120, 2):
            a = max(px[x, y][3], px[x + 1, y][3], px[x, y + 1][3], px[x + 1, y + 1][3])
            row.append("#" if a > 200 else ("o" if a > 90 else ("." if a > 0 else " ")))
        lines.append("".join(row))
    lines.append("")
    return "\n".join(lines)


def diff_px(a, b):
    pa, pb = a.load(), b.load()
    return sum(1 for y in range(120) for x in range(120) if pa[x, y] != pb[x, y])


def main():
    print("base:", BASE, "exists:", BASE.exists())
    idle = load_idle()

    # ---- climb_side（侧边爬升）：连体爪臂高伸扒边，3 帧 ----
    for v in range(3):
        save(side_climb(idle.copy(), v), f"climb_side_{v}.png")

    # ---- climb_edge（上缘移动）：低姿横移+低伸爪，3 帧 ----
    for v in range(3):
        save(edge_crawl(idle.copy(), v), f"climb_edge_{v}.png")

    # ---- climb_land（顶部落位）：收脚（两帧渐收）+专注脸沿用既有实现；末帧引用 idle ----
    save(land_collect_feet(face_forward_focused(idle.copy()), amount=2),
         "climb_land_0.png")
    save(land_collect_feet(face_forward_focused(idle.copy()), amount=4),
         "climb_land_1.png")

    # ---- 像素断言 ----
    all_ok = True
    for name in ["climb_side_0", "climb_side_1", "climb_side_2",
                 "climb_edge_0", "climb_edge_1", "climb_edge_2"]:
        im = Image.open(OUT / f"{name}.png").convert("RGBA")
        px = im.load()
        assert im.size == (120, 120), f"{name} size {im.size}"
        idle_px = idle.load()
        for y in range(104, 111):
            for x in range(120):
                if px[x, y] != idle_px[x, y]:
                    all_ok = False
                    print(f"{name}: foot row diff {x},{y}: {px[x,y]} vs {idle_px[x,y]}")
        print(f"{name}: 120x120, foot rows y104-110 identical to idle")

    for grp in [("climb_side_0", "climb_side_1", "climb_side_2"),
                ("climb_edge_0", "climb_edge_1", "climb_edge_2")]:
        for i in range(3):
            for j in range(i + 1, 3):
                d = diff_px(Image.open(OUT / f"{grp[i]}.png").convert("RGBA"),
                            Image.open(OUT / f"{grp[j]}.png").convert("RGBA"))
                print(f"{grp[i]} vs {grp[j]}: diffPx={d}")
                if d == 0:
                    all_ok = False
                    print(f"{grp[i]} vs {grp[j]}: IDENTICAL (cycle frame not distinguishable)")

    # land 收脚帧需与既有 assets 逐字节一致（保留 land，不扩大整改）
    from pathlib import Path as _P
    asset_land = _P("assets/pets/bear")
    for i in range(2):
        newf = Image.open(OUT / f"climb_land_{i}.png").convert("RGBA")
        oldf = Image.open(asset_land / f"climb_land_{i}.png").convert("RGBA")
        same = newf.tobytes() == oldf.tobytes()
        d = diff_px(newf, oldf)
        print(f"climb_land_{i} vs assets 既有: identical={same} diffPx={d}")
        if not same:
            all_ok = False
            print(f"climb_land_{i}: LAND 与既有 assets 不一致！")

    for name in ["climb_land_0", "climb_land_1"]:
        im = Image.open(OUT / f"{name}.png").convert("RGBA")
        assert im.size == (120, 120), f"{name} size {im.size}"
        try:
            assert_collected_feet(name, im, idle.load())
        except AssertionError as exc:
            all_ok = False
            print(f"{name}: FAILED {exc}")

    if not all_ok:
        print("ASSERT FAILED, see diffs above.")
        sys.exit(2)
    print("ASSERT: all climb frames OK (side/edge feet identical to idle, land kept byte-identical)")

    # ---- ASCII 目检 ----
    ascii_path = OUT / "ascii_review.txt"
    ascii_parts = []
    for n in ["idle_0", "climb_side_0", "climb_side_1", "climb_side_2",
              "climb_edge_0", "climb_edge_1", "climb_edge_2",
              "climb_land_0", "climb_land_1"]:
        im = idle if n == "idle_0" else Image.open(OUT / f"{n}.png").convert("RGBA")
        ascii_parts.append(print_ascii(im, n, ascii_path))
    ascii_path.write_text("\n".join(ascii_parts))
    print("ascii review saved:", ascii_path)

    print("done. out dir:", OUT)


if __name__ == "__main__":
    main()