# -*- coding: utf-8 -*-
"""生成游戏图标（聚光灯 / 六边形棋盘主题）。

用法：
    python Tools/make_app_icon.py
输出：
    Assets/Game/Art/AppIcon/icon-1024.png ... icon-16.png
（输出是纯代码绘制的矢量式图形，任何尺寸都清晰，可随时重跑换色）
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       "..", "Assets", "Game", "Art", "AppIcon")
SIZES = [1024, 512, 256, 128, 64, 48, 32, 16]
MASTER = 1024

BG_TOP = (24, 36, 50)
BG_BOTTOM = (10, 16, 24)
GRID = (34, 48, 64)
HEX_FILL = (16, 36, 54)
HEX_EDGE = (70, 200, 255)
HEX_EDGE_DIM = (30, 110, 160)
BEAM = (190, 232, 255)
HAZARD = (242, 196, 32)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def hexagon(cx, cy, radius, flat_top=True):
    """返回六边形顶点（和项目地图一致：平顶朝向）"""
    pts = []
    for i in range(6):
        angle = math.radians(60 * i + (0 if flat_top else 30))
        pts.append((cx + radius * math.cos(angle), cy + radius * math.sin(angle)))
    return pts


def draw_background(img):
    draw = ImageDraw.Draw(img)
    for y in range(MASTER):
        draw.line([(0, y), (MASTER, y)], fill=lerp(BG_TOP, BG_BOTTOM, y / MASTER))

    # 淡淡的六边形网格（用项目地图的同一套几何：列间距 1.5R，行间距 sqrt(3)R）
    grid = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(grid)
    r = 118
    col = 1.5 * r
    row = math.sqrt(3) * r
    q = -6
    while q <= 6:
        rr = -8
        while rr <= 8:
            cx = MASTER / 2 + col * q
            cy = MASTER / 2 + row * (rr + q / 2)
            gd.polygon(hexagon(cx, cy, r * 0.98), outline=GRID + (255,))
            rr += 1
        q += 1
    img.alpha_composite(grid)
    return img


def draw_beam(img):
    """从上方打下来的聚光灯锥体"""
    beam = Image.new("L", img.size, 0)
    bd = ImageDraw.Draw(beam)
    bd.polygon([(MASTER / 2 - 70, -60), (MASTER / 2 + 70, -60),
                (MASTER / 2 + 330, 720), (MASTER / 2 - 330, 720)], fill=150)
    beam = beam.filter(ImageFilter.GaussianBlur(26))

    # 越往下越淡
    grad = Image.new("L", img.size)
    gd = ImageDraw.Draw(grad)
    for y in range(MASTER):
        gd.line([(0, y), (MASTER, y)], fill=int(max(0, 255 * (1 - y / 760))))
    beam = Image.eval(Image.merge("L", (beam,)), lambda v: v)
    beam = Image.composite(beam, Image.new("L", img.size, 0), grad)

    layer = Image.new("RGBA", img.size, BEAM + (0,))
    layer.putalpha(beam)
    img.alpha_composite(layer)
    return img


def draw_hex_mark(img):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    cx = cy = MASTER / 2
    radius = 300

    # 外发光
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.polygon(hexagon(cx, cy, radius), fill=HEX_EDGE + (90,))
    glow = glow.filter(ImageFilter.GaussianBlur(48))
    img.alpha_composite(glow)

    # 六边形本体
    ld.polygon(hexagon(cx, cy, radius), fill=HEX_FILL + (255,))
    ld.line(hexagon(cx, cy, radius) + [hexagon(cx, cy, radius)[0]],
            fill=HEX_EDGE + (255,), width=34, joint="curve")
    # 内侧一圈暗边，做出厚度
    ld.line(hexagon(cx, cy, radius - 46) + [hexagon(cx, cy, radius - 46)[0]],
            fill=HEX_EDGE_DIM + (255,), width=10, joint="curve")

    # 中心的准星（点出"被照亮的格子"）
    r2 = 96
    ld.polygon(hexagon(cx, cy, r2), outline=HEX_EDGE + (255,))
    ld.line([(cx - r2 - 60, cy), (cx - r2 + 10, cy)], fill=HEX_EDGE + (255,), width=18)
    ld.line([(cx + r2 - 10, cy), (cx + r2 + 60, cy)], fill=HEX_EDGE + (255,), width=18)
    ld.line([(cx, cy - r2 - 60), (cx, cy - r2 + 10)], fill=HEX_EDGE + (255,), width=18)
    ld.line([(cx, cy + r2 - 10), (cx, cy + r2 + 60)], fill=HEX_EDGE + (255,), width=18)
    ld.ellipse([cx - 26, cy - 26, cx + 26, cy + 26], fill=HEX_EDGE + (255,))

    img.alpha_composite(layer)
    return img


def draw_hazard(img):
    """底部一条黄黑警示带，呼应竞技场里的警戒线"""
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    top = MASTER - 96
    ld.rectangle([0, top, MASTER, MASTER], fill=HAZARD + (255,))
    step = 74
    for x in range(-MASTER, MASTER * 2, step * 2):
        ld.polygon([(x, MASTER), (x + step, MASTER), (x + step + 96, top), (x + 96, top)],
                   fill=(18, 22, 28, 255))
    ld.rectangle([0, top, MASTER, top + 10], fill=(18, 22, 28, 255))
    img.alpha_composite(layer)
    return img


def main():
    img = Image.new("RGBA", (MASTER, MASTER), BG_BOTTOM + (255,))
    img = draw_background(img)
    img = draw_beam(img)
    img = draw_hex_mark(img)
    img = draw_hazard(img)

    os.makedirs(OUT_DIR, exist_ok=True)
    for size in SIZES:
        icon = img if size == MASTER else img.resize((size, size), Image.LANCZOS)
        if size <= 64:
            # 小尺寸锐化一下，边缘更清楚
            icon = icon.filter(ImageFilter.UnsharpMask(radius=1.2, percent=120, threshold=2))
        path = os.path.join(OUT_DIR, f"icon-{size}.png")
        icon.save(path)
        print("saved", os.path.normpath(path))


if __name__ == "__main__":
    main()
