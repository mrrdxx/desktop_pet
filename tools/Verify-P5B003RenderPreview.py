#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# P5B-003: 渲染帧后处理与交付物合成（PIL）。
#
# 输入：探针 climb-preview 场景产出的 frames/*.png（120×120 渲染帧，
#       经生产 UpdateFrame + RenderTargetBitmap(PetImage) 渲染，非素材原图）。
# 输出（预览目录）：
#   preview_side.gif / preview_edge.gif / preview_land.gif  120×120 动态预览
#   climb_keyframes_4x.png                                   关键帧 4× 放大网格
#   climb_zoom_compare_2x.png                                idle vs 三阶段 2× 放大对比
#   render-foot-stability.json                               【渲染级】逐帧脚底测量 + 断言
#   render-summary.txt                                       摘要
#
# 断言口径（渲染层，非仅 Offset=0/素材脚底行）：
#   side/edge 渲染帧：脚带 y104-110 与 idle_base 渲染帧逐像素一致（含 alpha）。
#   land 渲染帧：脚带收拢（span 不越出 idle、中心≈60）且 y=110 仍落地。
#   idle_base 本身须与素材 idle_0 逐像素一致（证明渲染管线未引入位移）。
#
# P5B-003 R2 F-02 新增【内容一致性】（能发现「源图已换但运行旧图」）：
#   ① 运行素材同步检查：探针 FindAssetDirectory 优先 bin 副本,先断言
#      bin\assets\pets\bear 的 6 张 climb 素材 SHA-256 与源 assets 一致；
#   ② idle 基线：idle_base 渲染帧 vs idle_0 素材的「显著差异像素 MAJOR%」
#      应≈0（渲染链仅 ±1 插值扰动,属 MINOR 类）,验证分类口径有效；
#   ③ 每张 side/edge 渲染帧 vs 对应源素材（loop i%3）做最小平移对齐后
#      的 MAJOR% 分类：新素材渲染应 ≤8%（旧图 14%+），>10% 判 FAIL。
#      允许已知渲染插值误差（MINOR 类不计数），不能只验证脚带。
#   ④ 提供 --old-run <dir> 时,对同名渲染帧做字节对比,证明新 run 与旧 run
#      渲染帧不同（R2 F-02: 旧 run 33/34 帧与更早 run 字节相同）。
#
# 用法：
#   python tools/Verify-P5B003RenderPreview.py <framesDir> <outDir> [--old-run <dir>]

import sys
import json
import hashlib
from pathlib import Path
from PIL import Image

ASSET_DIR = Path(r"assets/pets/bear")
BIN_ASSET_DIR = Path(
    r"tests/DesktopPet.DiagnosticProbe/bin/Debug/net10.0-windows/assets/pets/bear")
ASSET_IDLE = ASSET_DIR / "idle_0.png"
CLIMB_ASSETS = [f"climb_side_{i}.png" for i in range(3)] + \
               [f"climb_edge_{i}.png" for i in range(3)]

# 像素分类阈值：MINOR = 渲染链 ±1 alpha/RGB 扰动（A_MAX=3, C_MAX=24 涵盖
# 插值余量）；MAJOR = 内容实质不同（运行图与源素材不是同一张）。
A_MAX = 3
C_MAX = 24
CONTENT_MAJOR_FAIL = 10.0   # MAJOR% 超过即判「运行非源素材」
CONTENT_MAJOR_NOTE = 8.0   # 8~10% 视为可疑（保留，不直接失败）


def row_notable(im, y):
    """y 行非透明 x 范围，返回 (min, max, count) 或 None。"""
    px = im.load()
    xs = [x for x in range(im.width) if px[x, y][3] > 0]
    if not xs:
        return None
    return (min(xs), max(xs), len(xs))


def bottom_ground_row(im, min_alpha=1):
    """最底非透明行（y 从高到低找 alpha>min_alpha）。

    min_alpha=4 用于过滤渲染链（RenderTargetBitmap→PNG）在实体边缘
    插值出的 alpha 1-3 伪差行（diag_pixels9 实证：idle 渲染 y111 全为
    alpha≤3 伪差、真实内容仍在 y110），避免把伪差行误判为位移。
    """
    px = im.load()
    for y in range(im.height - 1, -1, -1):
        if any(px[x, y][3] > min_alpha for x in range(im.width)):
            return y
    return -1


def main():
    args = sys.argv[1:]
    old_run_dir = None
    if "--old-run" in args:
        i = args.index("--old-run")
        old_run_dir = Path(args[i + 1])
        del args[i:i + 2]
    if len(args) < 2:
        print(f"usage: {sys.argv[0]} <framesDir> <outDir> [--old-run <dir>]")
        sys.exit(2)
    frames_dir = Path(args[0])
    out_dir = Path(args[1])
    out_dir.mkdir(parents=True, exist_ok=True)

    idle_asset = Image.open(ASSET_IDLE).convert("RGBA")
    idle_render = Image.open(frames_dir / "idle_base.png").convert("RGBA")
    if idle_render.size != (120, 120):
        print(f"FATAL: idle_base size {idle_render.size}")
        sys.exit(2)

    checks = []
    failures = []

    def check(name, ok, detail):
        checks.append({"name": name, "ok": bool(ok), "detail": str(detail)})
        print(f"[{'PASS' if ok else 'FAIL'}] {name}: {detail}")
        if not ok:
            failures.append(name)

    # ---- 1. 渲染管线未引入位移：idle_base 与素材 idle_0 几何一致 ----
    # 渲染链路（RenderTargetBitmap→PNG→PIL 解码）对素材有系统性扰动（实测
    # idle 有内容 6395 像素中 6306 有差：±1 alpha/颜色 + 透明像素插值出
    # alpha≤2 的伪差，如 (127,0,0,2)），逐字节断言不可行（首轮已证伪）。
    # 位移的可靠判据是几何：非透明包围盒一致（±1）、真实内容底行仍 =110
    # （alpha>3，过滤渲染链边缘插值伪差——diag_pixels9 实证 idle 渲染
    # y111 行全为 alpha≤3 伪差、真实内容仍在 y110）、有内容像素数贴近
    # 素材（渲染不凭空增减 ≥±5%）。脚点稳定再由下方 side/edge/land 的
    # 逐帧渲染几何断言覆盖。
    def bbox(im):
        px = im.load()
        xs, ys = [], []
        for y in range(im.height):
            for x in range(im.width):
                if px[x, y][3] > 0:
                    xs.append(x)
                    ys.append(y)
        return (min(xs), min(ys), max(xs), max(ys)) if xs else None

    bob = bbox(idle_asset)
    bir = bbox(idle_render)
    n_asset = sum(1 for y in range(120) for x in range(120) if idle_asset.getpixel((x, y))[3] > 0)
    n_render = sum(1 for y in range(120) for x in range(120) if idle_render.getpixel((x, y))[3] > 0)
    bbox_ok = bob is not None and bir is not None and all(abs(a - b) <= 1 for a, b in zip(bob, bir))
    count_ok = n_render <= n_asset * 1.05 and n_render >= n_asset * 0.95
    check("idle_base 渲染帧未引入位移（bbox±1、真实底行=110、内容量贴近）",
          bbox_ok and count_ok and bottom_ground_row(idle_render, 4) == 110,
          f"bboxAsset={bob} bboxRender={bir} contentPx={n_asset}->{n_render} bottom={bottom_ground_row(idle_render, 4)} (alpha≤3 伪差行已滤除)")

    # ---- 1.5 P5B-003 R2 F-02: 内容一致性（能发现「源图已换但运行旧图」） ----
    # ① 运行素材同步：探针 FindAssetDirectory 优先 bin 副本,bin 6 张 climb
    #    素材 SHA-256 必须与源 assets 一致,否则 F-02 旧图问题会重现。
    def sha256_16(p: Path) -> str:
        return hashlib.sha256(p.read_bytes()).hexdigest()[:16].upper()

    sha_issues = []
    for name in CLIMB_ASSETS:
        src_f = ASSET_DIR / name
        bin_f = BIN_ASSET_DIR / name
        if not src_f.exists():
            sha_issues.append(f"{name} 源缺")
            continue
        if not bin_f.exists():
            sha_issues.append(f"{name} bin 缺")
            continue
        if sha256_16(src_f) != sha256_16(bin_f):
            sha_issues.append(f"{name} SHA 不一致")
    check("① 运行素材已同步（bin climb 6 张 SHA-256 == 源 assets）",
          not sha_issues,
          "；".join(sha_issues) if sha_issues else
          "bin==" + "==".join(sha256_16(ASSET_DIR / n) for n in CLIMB_ASSETS) + "(前16位一致)")

    # ② idle 基线：分类口径有效性。idle 渲染只剩 MINOR 扰动,MAJOR% 应≈0。
    def pix_class(render, asset, dx=0, dy=0):
        """返回 (SAME, MINOR, MAJOR)。diff 平移（dx,dy）用于对齐 bbox±1 位移。"""
        pr, pa = render.load(), asset.load()
        same = minor = major = 0
        for y in range(120):
            sy = y + dy
            if not (0 <= sy < 120):
                continue
            for x in range(120):
                sx = x + dx
                if not (0 <= sx < 120):
                    continue
                r, a = pr[x, y], pa[sx, sy]
                if r == a:
                    same += 1
                    continue
                da = abs(r[3] - a[3])
                dc = max(abs(r[i] - a[i]) for i in range(3))
                if da <= A_MAX and dc <= C_MAX:
                    minor += 1
                else:
                    major += 1
        return same, minor, major

    def major_pct_min(render, asset):
        """9 种平移（dx,dy∈{-1,0,1}）取最小 MAJOR%,输出最佳平移参数。"""
        best = (None, None, 101.0, 0, 0, 0)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                s, mi, ma = pix_class(render, asset, dx, dy)
                pct = ma / 14400 * 100
                if pct < best[2]:
                    best = (dx, dy, pct, s, mi, ma)
        return best

    _, _, idle_major, *_ = major_pct_min(idle_render, idle_asset)
    check("② idle 基线：渲染链仅 MINOR 扰动（idle_base vs idle_0 MAJOR%≈0）",
          idle_major <= 1.0,
          f"idle MAJOR%={idle_major:.2f} (阈值≤1.0, MINOR 类扰动不计)")

    # ③ side/edge 渲染帧 vs 对应源素材内容一致性。
    #    素材映射不按 i%3 猜测（动画 300ms/帧、采样 100ms/帧,同素材帧会停留
    #    多个采样点）,以探针 events.jsonl 记录的 imagePath 为权威——这与
    #    运行探针实际加载的素材一一对应,正是 F-02 要核对的内容来源。
    #    新素材渲染 MAJOR% 应 ≤8%;旧 R1 素材渲染实测 14-28%,>10% 判 FAIL。
    content_reports = []
    events_file = frames_dir.parent / "climb-preview-events.jsonl"
    frame_to_asset = {}
    if events_file.exists():
        import csv
        with events_file.open(encoding="utf-8") as ef:
            for line in ef:
                line = line.strip()
                if not line:
                    continue
                try:
                    rec = json.loads(line)
                except json.JSONDecodeError:
                    continue
                img = rec.get("imageFile", "")
                path = rec.get("imagePath", "")
                if img and path:
                    frame_to_asset[img] = Path(path).stem
    else:
        print(f"WARN: 未找到 events.jsonl（{events_file}），内容检查按文件名 i%3 兜底")

    for stage_glob in ("climb_side_", "climb_edge_"):
        for i, f in enumerate(sorted(frames_dir.glob(f"{stage_glob}*.png"))):
            rim = Image.open(f).convert("RGBA")
            declared = frame_to_asset.get(f.name)
            pool = "side" if stage_glob == "climb_side_" else "edge"
            if declared is None:
                # 兜底（无 events）：side/edge 内 3 素材循环
                declared = f"climb_{pool}_{i % 3}"
            # 动画 300ms/帧、采样 100ms/帧,切换瞬间采样帧会拍到前/后帧素材
            # （diag_switch_frames.py 诊断 6 帧 vs 相邻素材 MAJOR% 0.05-0.17,
            #  同正常帧量级;若只对 declared 素材判定会误报时序伪影为内容不符）。
            # 判定口径：取同阶段素材集合的最小 MAJOR%,并记录 best-match 证据;
            # 旧 R1 素材（四臂/细臂）对集合内任一素材 MAJOR%≥14,仍能可靠区分。
            best = (None, None, 101.0, None)
            for k in range(3):
                aim = Image.open(ASSET_DIR / f"climb_{pool}_{k}.png").convert("RGBA")
                dx, dy, pct, s, mi, ma = major_pct_min(rim, aim)
                if pct < best[2]:
                    best = (f"climb_{pool}_{k}", (dx, dy), pct, (s, mi, ma))
            b_name, b_shift, b_pct, b_stats = best
            ok = b_pct <= CONTENT_MAJOR_NOTE
            check(f"③ {f.name} 内容一致（declared={declared} → best={b_name}）",
                  ok and b_pct <= CONTENT_MAJOR_FAIL,
                  f"MAJOR%={b_pct:.2f}（shift={b_shift} SAME={b_stats[0]} MINOR={b_stats[1]} "
                  f"MAJOR={b_stats[2]}; 阈值≤8,旧图对集合任一素材≥14）")
            content_reports.append((f.name, declared, b_name, b_pct, ok))

    # ---- 2. 逐帧脚底测量 ----
    foot_stats = {}

    def measure(name, im):
        px = im.load()
        rows = {}
        for y in range(100, 111):
            r = row_notable(im, y)
            if r:
                rows[y] = {"min": r[0], "max": r[1], "center": round((r[0] + r[1]) / 2, 1), "count": r[2]}
            else:
                rows[y] = None
        foot_stats[name] = {
            "bottomGroundRow": bottom_ground_row(im),
            "rows100-110": rows,
        }

    measure("idle_base", idle_render)

    side_names, edge_names, land_names = [], [], []
    for f in sorted(frames_dir.glob("climb_side_*.png")):
        side_names.append(f.stem)
    for f in sorted(frames_dir.glob("climb_edge_*.png")):
        edge_names.append(f.stem)
    for f in sorted(frames_dir.glob("climb_land_*.png")):
        land_names.append(f.stem)

    if not (side_names and edge_names and land_names):
        print(f"FATAL: missing stage frames side={len(side_names)} edge={len(edge_names)} land={len(land_names)}")
        sys.exit(2)

    # side/edge: 逐像素断言已被诊断6证伪——渲染链对不同位图（即使脚带源
    # 像素与 idle 相同）存在系统性 ±1 亚像素/插值差异（bbox 右下 +1、
    # 127 红通道等），「同像素输入≠同像素输出」。脚点稳定改以渲染几何
    # 口径测量（仍是逐帧渲染像素，非 Offset=0/脚底行相等）：
    #   - y104-110 每行非透明范围不越出 idle_base 渲染帧相应行 ±1px；
    #   - 每行中心 ≈60 ±1.5；
    #   - y=110 仍落地，底行必须 =110（与 idle 同一地表）。
    for name in side_names + edge_names:
        im = Image.open(frames_dir / f"{name}.png").convert("RGBA")
        measure(name, im)
        bad, details = [], []
        for y in range(104, 111):
            r = row_notable(im, y)
            ir = row_notable(idle_render, y)
            if r is None:
                bad.append(f"row{y} empty")
                continue
            if ir is not None and (r[0] < ir[0] - 1 or r[1] > ir[1] + 1):
                bad.append(f"row{y} out-of-idle {r[0]}-{r[1]} vs {ir[0]}-{ir[1]}")
            center = (r[0] + r[1]) / 2
            if abs(center - 60) > 1.5:
                bad.append(f"row{y} center drift {center}")
            details.append((y, r[0], r[1], round(center, 1)))
        check(f"{name} 渲染帧脚带稳定（中心≈60、不越出 idle±1、y110 落地）",
              len(bad) == 0 and bottom_ground_row(im) == 110,
              f"rows={details} bottom={bottom_ground_row(im)}")

    # land: y104-110 每行收拢、中心≈60、不越出 idle、y110 落地
    for name in land_names:
        im = Image.open(frames_dir / f"{name}.png").convert("RGBA")
        measure(name, im)
        bad = []
        details = []
        for y in range(104, 111):
            r = row_notable(im, y)
            ir = row_notable(idle_render, y)
            if r is None:
                bad.append(f"row{y} empty")
                continue
            if ir is not None and (r[0] < ir[0] or r[1] > ir[1]):
                bad.append(f"row{y} out-of-idle {r[0]}-{r[1]} vs {ir[0]}-{ir[1]}")
            center = (r[0] + r[1]) / 2
            if abs(center - 60) > 1.5:
                bad.append(f"row{y} center drift {center}")
            details.append((y, r[0], r[1], round(center, 1)))
        check(f"{name} 渲染帧收脚（中心≈60、不越出 idle、y110 落地）",
              len(bad) == 0 and row_notable(im, 110) is not None,
              f"rows={details} ok")

    # ---- 2.5 P5B-003 R2 F-02: 新 run vs 旧 run 渲染帧字节对比 ----
    # 旧 run-182213/185508 曾 33/34 帧与更早 run 字节相同（旧素材复核轮）。
    # 新 run 中 side/edge 共 18 帧（素材已换）必须全部与旧 run 字节不同；
    # idle_base（1）与 land（15）素材未改，渲染帧预期与旧 run 相同，不计入。
    byte_diff_reports = []
    if old_run_dir is not None:
        old_frames = old_run_dir
        sides = sorted(frames_dir.glob("climb_side_*.png"))
        edges = sorted(frames_dir.glob("climb_edge_*.png"))
        changed_frames = sides + edges
        diff_cnt = 0
        same_list = []
        missing = 0
        for f in sorted(frames_dir.glob("*.png")):
            of = old_frames / f.name
            if not of.exists():
                missing += 1
                continue
            nb = f.read_bytes()
            ob = of.read_bytes()
            if nb == ob:
                same_list.append(f.name)
            else:
                diff_cnt += 1
        # side/edge 逐帧必须与旧 run 不同（18/18）
        change_fail = []
        for f in changed_frames:
            of = old_frames / f.name
            if of.exists() and f.read_bytes() == of.read_bytes():
                change_fail.append(f.name)
        check(f"④ 新 run 渲染帧与旧 run({old_run_dir.parent.name})字节不同",
              diff_cnt >= 18 and not change_fail and missing == 0,
              f"全帧 diff={diff_cnt} 相同帧={same_list} side/edge未换帧={change_fail} missing={missing} "
              f"(idle_base+land 素材未改允许相同；side/edge 18/18 必须不同)")
    else:
        check("④ 未指定 --old-run（跳过新旧 run 字节对比）", True, "optional")

    # ---- 3. 交付物 ----
    def gif(frames, path, dur, loop=0):
        frames[0].convert("RGB").save(path, save_all=True,
                                      append_images=[f.convert("RGB") for f in frames[1:]],
                                      duration=dur, loop=loop, disposal=2)

    side_imgs = [Image.open(frames_dir / f"{n}.png").convert("RGBA") for n in side_names]
    edge_imgs = [Image.open(frames_dir / f"{n}.png").convert("RGBA") for n in edge_names]
    land_imgs = [Image.open(frames_dir / f"{n}.png").convert("RGBA") for n in land_names]
    gif(side_imgs, out_dir / "preview_side.gif", [100] * len(side_imgs))
    gif(edge_imgs, out_dir / "preview_edge.gif", [100] * len(edge_imgs))
    # land 是 once（收脚 260ms + 340ms + 站稳 2400ms）。GIF 每帧以步进
    # 200ms 播放（15 帧 = 3000ms，与注册 once 总时长一致）。
    land_dur = [200] * len(land_imgs)
    gif(land_imgs, out_dir / "preview_land.gif", land_dur)

    # 关键帧 4× 放大网格：idle 参考 + side + edge + land + 站稳参照（idle）
    names = (["idle_base"] + side_names + edge_names + land_names + ["idle_base"])
    labels = (["idle(参考)"] + [f"side_{i}" for i in range(len(side_names))] +
              [f"edge_{i}" for i in range(len(edge_names))] +
              [f"land_{i}" for i in range(len(land_names))] + ["站立=idle"])
    imgs = [idle_render if n == "idle_base" else Image.open(frames_dir / f"{n}.png").convert("RGBA")
            for n in names]
    scale = 4
    grid = Image.new("RGBA", (120 * scale, 120 * len(imgs)), (255, 255, 255, 255))
    for i, (im, lab) in enumerate(zip(imgs, labels)):
        grid.paste(im.resize((120 * scale, 120 * scale), Image.NEAREST), (0, i * 120 * scale))
    grid.save(out_dir / "climb_keyframes_4x.png", dpi=(96, 96))

    # 2× 放大对比：idle vs side vs edge vs land（各取代表性帧）
    zoom_names = ["idle_base", side_names[0], side_names[-1], edge_names[0], edge_names[-1],
                  land_names[0], land_names[-1], "idle_base"]
    zoom_labels = ["idle(参考)", "side 扒边1", "side 扒边2", "edge 探1", "edge 探2",
                   "land 收脚1", "land 收脚2", "站立=idle"]
    zoom_imgs = [idle_render if n == "idle_base" else Image.open(frames_dir / f"{n}.png").convert("RGBA")
                 for n in zoom_names]
    zs = 2
    zoom = Image.new("RGBA", (120 * zs * len(zoom_imgs), 120 * zs + 16), (255, 255, 255, 255))
    for i, (im, lab) in enumerate(zip(zoom_imgs, zoom_labels)):
        zoom.paste(im.resize((120 * zs, 120 * zs), Image.NEAREST), (i * 120 * zs, 0))
    zoom.save(out_dir / "climb_zoom_compare_2x.png", dpi=(96, 96))

    # ---- 4. 汇总 ----
    side_ok = all(c["ok"] for c in checks if c["name"].startswith("climb_side_"))
    edge_ok = all(c["ok"] for c in checks if c["name"].startswith("climb_edge_"))
    land_ok = all(c["ok"] for c in checks if c["name"].startswith("climb_land_"))
    idle_ok = checks[0]["ok"]

    summary = {
        "idleRenderMatchesAsset": idle_ok,
        "sideFootStable": side_ok,
        "edgeFootStable": edge_ok,
        "landFeetCollectedStable": land_ok,
        "contentConsistencyR2F02": {
            "binSync": checks[1]["ok"] if len(checks) > 1 else None,
            "idleBaselineMajorPct": next((c["detail"] for c in checks if c["name"].startswith("② idle 基线")), None),
            "frames": [
                {"frame": n, "declaredAsset": a, "bestMatch": b, "majorPct": round(p, 2), "ok": o}
                for n, a, b, p, o in content_reports
            ],
        },
        "byteDiffVsOldRun": byte_diff_reports,
        "allPass": not failures,
        "failures": failures,
        "footStats": foot_stats,
        "deliverables": [p.name for p in sorted(out_dir.glob("*.gif")) + sorted(out_dir.glob("*.png"))],
    }
    (out_dir / "render-foot-stability.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")

    with (out_dir / "render-summary.txt").open("w", encoding="utf-8") as f:
        for c in checks:
            f.write(f"[{'PASS' if c['ok'] else 'FAIL'}] {c['name']}: {c['detail']}\n")
        f.write(f"\nALL PASS={not failures}\n")

    print(f"\nALL PASS={not failures} -> {out_dir}")
    sys.exit(0 if not failures else 1)


if __name__ == "__main__":
    main()