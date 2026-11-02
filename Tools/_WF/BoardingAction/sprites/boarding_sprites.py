#!/usr/bin/env python3
"""Generates the boarding action sprites and the redrawn tow harpoon art.

Hand-laid pixel grids with the ramps of the _Mono ship weapons (L85, t35) and machines
(gravity_generator_mini). Light comes from the top left. Run from anywhere:

    python Tools/_WF/BoardingAction/sprites/boarding_sprites.py [--root DIR] [--layers DIR]

--root redirects output (default: the repo root); --layers also dumps each sprite's layers as PNGs,
which build_sources.sh turns into the layered .aseprite files in this folder.
"""

import argparse
import json
import math
import os

from PIL import Image

SIZE = 32
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))

# Slate ramp sampled from L85, teal accent for the hacking gear.
GUN = {
    "0": (14, 13, 32), "1": (29, 38, 64), "2": (67, 77, 99), "4": (115, 125, 141),
    "5": (155, 163, 172), "6": (191, 197, 213), "7": (233, 235, 241),
    "t": (8, 62, 70), "u": (14, 112, 118), "v": (40, 176, 172), "w": (132, 240, 226), "W": (230, 255, 250),
}
# Same family, one extra mid step and oxide paint for the tow gear.
TOW = {
    "0": (14, 13, 32), "1": (29, 38, 64), "2": (67, 77, 99), "3": (90, 100, 120), "4": (115, 125, 141),
    "5": (155, 163, 172), "6": (191, 197, 213), "7": (233, 235, 241),
    "o": (116, 52, 34), "p": (176, 84, 42), "q": (226, 136, 62),
}
# gravity_generator_mini ramp, plus dead glass and hazard paint.
MACHINE = {
    "k": (12, 13, 20), "0": (21, 24, 36), "1": (33, 35, 48), "2": (55, 58, 75), "3": (71, 74, 91),
    "4": (96, 99, 117), "5": (121, 125, 143), "6": (148, 151, 171), "7": (197, 199, 212),
    "8": (237, 239, 247),
    "g": (24, 46, 56), "j": (38, 74, 84), "J": (70, 122, 128),
    "h": (172, 120, 60), "r": (107, 35, 40),
}
LIGHT = {
    "dim": (42, 110, 118), "dim2": (70, 158, 160),
    "amber0": (150, 92, 30), "amber1": (226, 150, 48), "amber2": (255, 214, 120),
    "red0": (140, 32, 44), "red1": (228, 59, 68), "red2": (255, 150, 140),
    "cyan0": (30, 120, 136), "cyan1": (72, 214, 226), "cyan2": (200, 255, 250),
}
ARC = {"w": (240, 255, 255), "c": (120, 236, 244), "b": (44, 148, 214), "d": (28, 84, 150)}
DARK = {"7": "6", "6": "5", "5": "4", "4": "2", "3": "2", "2": "1", "1": "0", "v": "u", "w": "v", "u": "t",
        "q": "p", "p": "o"}
MACHINE_DARK = {"8": "7", "7": "6", "6": "5", "5": "4", "4": "3", "3": "2", "2": "1", "1": "0"}


def art(x0, y0, text):
    """A block of rows from (x0, y0); '.' is transparent."""
    px = {}
    rows = [r.strip() for r in text.strip("\n").split("\n")]
    for j, row in enumerate(rows):
        for i, ch in enumerate(row):
            if ch != ".":
                px[(x0 + i, y0 + j)] = ch
    return px


def sym(px, cx):
    """Reflects px across column cx (x.5 for even widths) and keeps both sides."""
    out = dict(px)
    for (x, y), ch in px.items():
        out[(int(2 * cx - x), y)] = ch
    return out


def relight(px, cx, keys=DARK):
    """Steps everything right of cx one shade darker, so a mirrored part keeps its top-left light."""
    return {(x, y): keys.get(c, c) if x > cx else c for (x, y), c in px.items()}


def over(*layers):
    out = {}
    for layer in layers:
        out.update(layer)
    return out


def image(px, pal):
    im = Image.new("RGBA", (SIZE, SIZE))
    for (x, y), ch in px.items():
        c = pal[ch]
        im.putpixel((x, y), tuple(c) + (255,))
    return im


# hack_harpoon_gun

def spool(cx, cy):
    """A cable drum with its axis across the gun: flanges, helical wraps, rounded back housing."""
    px = {}
    px.update(sym(art(cx - 6, cy - 8, """
        ....000
        ..00667
        .015566
        0124555
        0124444
    """), cx))
    rows = ["0", "v", "w", "v", "v", "u", "u", "u", "t", "t", "0"]
    dark = {"v": "u", "w": "v", "u": "t", "t": "0", "0": "0"}
    for j, key in enumerate(rows):
        y = cy - 5 + j
        for x in range(cx - 5, cx + 6):
            helix = (x + (j + 1) // 3) % 2
            px[(x, y)] = dark[key] if helix else key
    flange = art(cx - 7, cy - 6, """
        .00
        065
        065
        055
        054
        054
        054
        054
        024
        024
        024
        014
        .00
    """)
    px.update(flange)
    px.update({(int(2 * cx - x), y): {"6": "5", "5": "4", "4": "2", "2": "1"}.get(c, c) for (x, y), c in flange.items()})
    return px


def hack_gun():
    pods = sym(art(6, 9, """
        .111.
        14551
        14651
        14551
        14451
        12221
        14441
        14441
        12441
        12221
        .1220
        ..000
    """), 16)
    frame = sym(art(10, 15, """
        1200000
        1245500
        1256665
        0245554
        0124222
        .045100
        .056100
        .056100
        .045100
        0244211
        .056100
        .056100
        .057100
        02670..
        0460...
        000....
    """), 16)
    cable = art(15, 16, """
        0v0
        0v0
        0u0
        0v0
        0u0
        0v0
    """)
    dart = art(14, 22, """
        .464.
        .tvt.
        .565.
        t767t
        .565.
        ..6..
        ..7..
    """)
    lamps = over(art(7, 16, "v"), art(25, 16, "v"), art(7, 17, "u"), art(25, 17, "u"))
    return [("pods", pods, GUN), ("frame", frame, GUN), ("cable", cable, GUN), ("dart", dart, GUN),
            ("reel", spool(16, 10), GUN), ("lamps", lamps, GUN)]


# hack_dart

def hack_dart_body():
    return relight(sym(art(11, 5, """
        ...000
        ..0vvw
        ..0v0.
        ..0v0.
        ..0uuv
        ...014
        ..0245
        0.0256
        40.251
        240251
        024251
        .02256
        ..0tuv
        ...045
        ..0256
        0..056
        40.056
        240457
        024567
        .02467
        ..0467
        ...057
        ...067
        ....07
        ....06
    """), 16), 16)


DART_LENS = (16, 14)


def dart_light(level):
    """The status lamp and the data band below it: 0 is dead, 3 is full flare."""
    x, y = DART_LENS
    if level == 0:
        return {(x, y): "t"}
    if level == 2:
        return {(x, y): "w", (x, y - 1): "v", (x, y + 1): "v", (x, y + 3): "w", (x - 1, y + 3): "v"}
    return {(x, y): "W", (x, y - 1): "w", (x, y + 1): "w", (x - 1, y): "w", (x + 1, y): "w",
            (x, y + 3): "W", (x - 1, y + 3): "w", (x + 1, y + 3): "v"}


# Harpoon and turret

def tow_harpoon():
    return relight(sym(art(11, 2, """
        ...000
        ..0qqq
        .0qp00
        .0qp..
        .0po..
        .0po00
        ..0ppp
        ..0oop
        ..0255
        ..0266
        ...045
        ...056
        ...056
        ...026
        ...056
        ...026
        ...056
        0..056
        40.056
        240056
        024056
        .02456
        .02567
        ..0467
        ..0467
        ...057
        ...057
        ....06
        ....07
    """), 16), 16)


def tow_stand():
    """The bolted deck ring the launcher turns on."""
    px = {}
    cx, cy = 15.5, 16.5
    for y in range(SIZE):
        for x in range(SIZE):
            dx, dy = x - cx, y - cy
            r = math.hypot(dx, dy)
            if r > 11.2:
                continue
            lit = (-dx * 0.6 - dy * 0.8) / max(r, 1)
            if r > 10.4:
                px[(x, y)] = "0"
            elif r > 8.2:
                px[(x, y)] = "6" if lit > 0.6 else "5" if lit > 0.1 else "4" if lit > -0.4 else "3" if lit > -0.8 else "2"
            elif r > 7.4:
                px[(x, y)] = "0"
            else:
                px[(x, y)] = "1"
    for x, y in ((8, 9), (23, 9), (5, 17), (26, 17), (8, 24), (23, 24)):
        px[(x, y)] = "0"
    return px


def tow_drum():
    px = {}
    rows = ["0", "6", "7", "6", "5", "4", "3", "2", "0"]
    dark = {"7": "5", "6": "4", "5": "3", "4": "2", "3": "1", "2": "1", "0": "0"}
    for j, key in enumerate(rows):
        for x in range(10, 22):
            px[(x, 7 + j)] = dark[key] if (x + (j + 1) // 3) % 2 else key
    flange = art(7, 6, """
        .00
        0qp
        0qp
        0qp
        0pp
        0pp
        0po
        0po
        0oo
        0oo
        .00
    """)
    px.update(flange)
    px.update({(31 - x, y): {"q": "p", "p": "o"}.get(c, c) for (x, y), c in flange.items()})
    return px


def tow_grips():
    return relight(sym(art(9, 2, """
        .00....
        0650...
        05500..
        04506qq
        0340pqq
    """), 15.5), 15.5)


def tow_launcher():
    return relight(sym(art(10, 15, """
        000000
        024500
        025655
        024544
        012100
        .05601
        .05601
        .04501
        024421
        .05601
        0qqp01
        0qpo01
        000001
    """), 15.5), 15.5)


def tow_loaded():
    return art(13, 20, """
        ..65..
        ..65..
        ..65..
        ..54..
        ..65..
        ..65..
        ..65..
        .0660.
        047650
        .0760.
        ..76..
        ..00..
    """)


def tow_turret():
    return [("stand", tow_stand(), TOW), ("drum", tow_drum(), TOW), ("grips", tow_grips(), TOW),
            ("launcher", tow_launcher(), TOW), ("harpoon", tow_loaded(), TOW)]


# ftl_disruptor

def ring(cy, w):
    """One coil of the spindle, half-width w around x 15.5."""
    left = 15 - w + 1
    px = {}
    for x in range(left, 16):
        d = x - left
        if d >= 2:
            px[(x, cy - 2)] = "0"
        if d == 1:
            px[(x, cy - 1)] = "0"
        elif d > 1:
            px[(x, cy - 1)] = "7" if x < 12 else "6"
        px[(x, cy)] = "0" if d == 0 else "5" if d == 1 else "6"
        px[(x, cy + 1)] = "0" if d == 0 else "3" if d == 1 else "g"
        px[(x, cy + 2)] = "0" if d == 0 else "2"
        if d >= 1:
            px[(x, cy + 3)] = "0"
    return relight(sym(px, 15.5), 15.5, MACHINE_DARK)


RINGS = [(18, 10), (14, 9), (10, 8), (6, 6)]


def disruptor():
    """Returns (pixels, ring index per lit pixel). Coil stack, capacitor banks, instrument plinth."""
    plinth = sym(art(2, 19, """
        00000000000000
        05666666666666
        04555555555555
        03444444444444
        01111111111111
        02333333333333
        02333333333333
        02333333333333
        02333333333333
        02333333333333
        01111111111111
        .0000000000000
    """), 15.5)
    banks = sym(art(2, 6, """
        .00000
        0877760
        0777760
        0666650
        0322220
        03g2g20
        03g2g20
        03g2g20
        03g2g20
        03g2g20
        03g2g20
        03g2g20
        0322220
        0222220
    """), 15.5)
    px = over(plinth, banks)
    owner = {}
    for i, (cy, w) in enumerate(RINGS):
        r = ring(cy, w)
        px.update(r)
        for p, c in r.items():
            owner[p] = i if c == "g" else None
    px.update(sym(art(13, 0, """
        ..0
        .0J
        0Jj
        0jg
        .0g
    """), 15.5))
    px.update(art(4, 25, """
        0000000
        0r0g0g0
        0000000
        0h2h2h0
        0000000
    """))
    px.update(art(12, 26, """
        k2k2k2k2
        k2k2k2k2
        k2k2k2k2
    """))
    px.update(art(21, 25, """
        0000000
        0hhkkh0
        0hkkhh0
        0kkhhk0
        0000000
    """))
    return px, owner


LAMPS = [(5, 26), (7, 26), (9, 26)]


def disruptor_lit():
    """The glass the overlays paint: ring bands bottom to top, bank slots, the emitter prism."""
    px, owner = disruptor()
    rings = [[p for p, i in owner.items() if i == n and px.get(p) == "g"] for n in range(len(RINGS))]
    banks = [p for p, c in px.items() if c == "g" and owner.get(p) is None and 5 < p[1] < 19]
    banks = [p for p in banks if p[0] < 9 or p[0] > 22]
    prism = [p for p, c in px.items() if c in "Jj" or (c == "g" and p[1] < 5)]
    return rings, banks, prism


def paint(points, key):
    return {p: key for p in points}


def disruptor_idle():
    rings, banks, prism = disruptor_lit()
    return over(paint(prism, "dim"), paint([LAMPS[1]], "dim2"),
                paint([p for p in rings[3] if 14 <= p[0] <= 17], "dim"))


def disruptor_locking(f):
    """Amber sweep climbing the coil stack, banks charging behind it."""
    rings, banks, prism = disruptor_lit()
    top, bottom = min(p[1] for p in banks), max(p[1] for p in banks)
    fill = bottom - (bottom - top + 1) * (f + 1) // 4
    out = over(paint(banks, "amber0"), paint([p for p in banks if p[1] > fill], "amber1"))
    out.update(paint(rings[(f - 2) % 4], "amber0"))
    out.update(paint(rings[(f - 1) % 4], "amber1"))
    out.update(paint(rings[f], "amber2"))
    out.update(paint(prism, "amber2" if f == 3 else "amber1" if f == 2 else "amber0"))
    out.update(paint([LAMPS[0]], "amber2" if f % 2 else "amber0"))
    out.update(paint([LAMPS[1]], "amber1"))
    return out


def disruptor_locked(f):
    """Hard lock: the coils swap red and cyan, the prism flares with whichever is hot."""
    rings, banks, prism = disruptor_lit()
    hot, dim = ("red", "cyan") if f < 2 else ("cyan", "red")
    level = "1" if f % 2 == 0 else "0"
    out = paint(banks, hot + level)
    for n in range(4):
        out.update(paint(rings[n], (hot if n % 2 == 0 else dim) + level))
    out.update(paint(prism, hot + ("2" if f % 2 == 0 else "1")))
    out.update(paint(LAMPS, "red1" if f % 2 == 0 else "red0"))
    return out


def disruptor_layers():
    return [("body", disruptor()[0], MACHINE)]


# hack_arc

def line(points, key):
    """8-connected polyline through points."""
    px = {}
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        steps = max(abs(x1 - x0), abs(y1 - y0), 1)
        for i in range(steps + 1):
            px[(round(x0 + (x1 - x0) * i / steps), round(y0 + (y1 - y0) * i / steps))] = key
    return px


# Each frame: (points, key) bolts, bottom-to-top.
ARC_FRAMES = [
    [([(3, 13), (6, 12), (7, 15)], "c"), ([(26, 19), (24, 18)], "b")],
    [([(3, 13), (6, 11), (8, 14), (12, 13), (13, 16)], "c"), ([(8, 14), (12, 13)], "w"), ([(8, 14), (7, 18)], "b"),
     ([(28, 20), (25, 18), (24, 20)], "c")],
    [([(3, 14), (6, 11), (8, 14), (12, 13), (14, 17), (18, 15), (20, 11), (24, 13), (28, 18)], "c"),
     ([(8, 14), (12, 13), (14, 17), (18, 15)], "w"), ([(14, 17), (13, 22), (16, 24)], "b"),
     ([(20, 11), (21, 7)], "b"), ([(24, 13), (26, 10)], "d")],
    [([(5, 22), (9, 18), (10, 20), (15, 17), (18, 20), (20, 17), (25, 18), (28, 13)], "c"),
     ([(10, 20), (15, 17), (18, 20), (20, 17)], "w"), ([(15, 17), (14, 11), (17, 8)], "b"),
     ([(25, 18), (26, 23)], "b"), ([(9, 18), (7, 14)], "d")],
    [([(6, 21), (9, 18)], "b"), ([(14, 12), (16, 8)], "c"), ([(24, 19), (27, 14)], "b"), ([(18, 20), (20, 21)], "d")],
    [([(17, 8), (17, 8)], "d"), ([(27, 14), (27, 14)], "d"), ([(8, 19), (8, 19)], "d")],
]


def arc_frame(bolts):
    px = {}
    for points, key in bolts:
        px.update(line(points, key))
    return px


# Cables

def tiled(rows):
    """{(x, y): (grey, alpha)} -> 32x8 RGBA."""
    im = Image.new("RGBA", (32, 8))
    for (x, y), (g, a) in rows.items():
        im.putpixel((x, y), (g, g, g, a))
    return im


def step(g, ramp):
    """Snaps a grey to the nearest ramp step."""
    return min(ramp, key=lambda v: abs(v - g))


CABLE_RAMP = (255, 228, 200, 172, 144, 116, 92)


def tow_cable():
    """Three-lay twisted steel: strands climb 1px in x per row, period 8, grooves between them."""
    profile = [255, 238, 214, 190, 166, 142, 112, 214]
    body = {1: 0, 2: 0, 3: 0, 4: 1, 5: 2, 6: 3}
    px = {}
    for y in range(8):
        for x in range(32):
            t = (x + y) % 8
            if y in (0, 7):
                px[(x, y)] = (step(profile[t] - (30 if y == 7 else 0), CABLE_RAMP[2:5]), 96 if t < 6 else 40)
            else:
                px[(x, y)] = (step(profile[t] - body[y] * 18, CABLE_RAMP), 255)
    return tiled(px)


def hack_cable():
    """Smooth jacket, with a ferrule band every 16px and a printed tick midway."""
    jacket = {0: 172, 1: 228, 2: 255, 3: 255, 4: 228, 5: 200, 6: 172, 7: 144}
    px = {}
    for y in range(8):
        for x in range(32):
            g = jacket[y]
            t = x % 16
            if t in (0, 1, 2):
                g = {0: g - 56, 1: g, 2: g - 84}[t]
            elif t == 9:
                g -= 28
            px[(x, y)] = (step(g, CABLE_RAMP), 104 if y in (0, 7) else 255)
    return tiled(px)


def compose(layers):
    im = Image.new("RGBA", (SIZE, SIZE))
    for _, px, pal in layers:
        im.alpha_composite(image(px, pal))
    return im


def sheet(frames):
    """Lays frames row-major in a ceil(sqrt(n)) grid, as RSIs do."""
    cols = math.ceil(math.sqrt(len(frames)))
    rows = math.ceil(len(frames) / cols)
    im = Image.new("RGBA", (cols * SIZE, rows * SIZE))
    for i, frame in enumerate(frames):
        im.paste(frame, ((i % cols) * SIZE, (i // cols) * SIZE))
    return im


def write_rsi(folder, states):
    """states: name -> (frames, delay list or None)."""
    os.makedirs(folder, exist_ok=True)
    entries = []
    for name, (frames, delays) in states.items():
        sheet(frames).save(os.path.join(folder, name + ".png"))
        entries.append({"name": name, "delays": [delays]} if delays else {"name": name})
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": "Wolfgate",
            "size": {"x": SIZE, "y": SIZE}, "states": entries}
    with open(os.path.join(folder, "meta.json"), "w", encoding="utf-8", newline="\n") as file:
        json.dump(meta, file, indent=4)
        file.write("\n")


def write_layers(root, name, layers):
    """layers: name -> list of frame images. Dumps PNGs for build_sources.sh."""
    folder = os.path.join(root, name)
    os.makedirs(folder, exist_ok=True)
    for layer, frames in layers.items():
        for i, frame in enumerate(frames):
            frame.save(os.path.join(folder, f"{layer}_{i + 1}.png"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=REPO)
    parser.add_argument("--layers")
    args = parser.parse_args()
    boarding = os.path.join(args.root, "Resources", "Textures", "_WF", "BoardingAction")
    tether = os.path.join(args.root, "Resources", "Textures", "_WF", "Tether")

    gun = hack_gun()
    write_rsi(os.path.join(boarding, "hack_harpoon_gun.rsi"), {"icon": ([compose(gun)], None)})

    body = hack_dart_body()
    blink = [0, 2, 3, 2]
    dart_frames = [image(over(body, dart_light(level)), GUN) for level in blink]
    write_rsi(os.path.join(boarding, "hack_dart.rsi"), {
        "dart": ([image(over(body, dart_light(0)), GUN)], None),
        "latched": (dart_frames, [0.4, 0.1, 0.12, 0.1]),
    })

    machine = image(disruptor()[0], MACHINE)
    write_rsi(os.path.join(boarding, "ftl_disruptor.rsi"), {
        "base": ([machine], None),
        "idle": ([image(disruptor_idle(), LIGHT)], None),
        "locking": ([image(disruptor_locking(f), LIGHT) for f in range(4)], [0.2] * 4),
        "locked": ([image(disruptor_locked(f), LIGHT) for f in range(4)], [0.2] * 4),
    })

    arc = [image(arc_frame(bolts), ARC) for bolts in ARC_FRAMES]
    write_rsi(os.path.join(boarding, "hack_arc.rsi"), {"arc": (arc, [0.09] * len(arc))})

    turret = tow_turret()
    harpoon = tow_harpoon()
    for path, im in ((os.path.join(tether, "harpoon_turret.rsi", "base.png"), compose(turret)),
                     (os.path.join(tether, "harpoon_projectile.rsi", "harpoon.png"), image(harpoon, TOW))):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        im.save(path)

    for path, im in ((os.path.join(tether, "tow_cable.png"), tow_cable()),
                     (os.path.join(boarding, "hack_cable.png"), hack_cable())):
        im.save(path)
        with open(path + ".yml", "w", encoding="utf-8", newline="\n") as file:
            file.write("sample:\n  filter: false\n  wrap: repeat\n")

    if args.layers:
        n = len(dart_frames)
        write_layers(args.layers, "hack_harpoon_gun", {name: [image(px, pal)] for name, px, pal in gun})
        write_layers(args.layers, "hack_dart", {
            "body": [image(body, GUN)] * n,
            "lamp": [image(dart_light(level), GUN) for level in blink],
        })
        write_layers(args.layers, "ftl_disruptor", {
            "base": [machine] * 4,
            "idle": [image(disruptor_idle(), LIGHT)] * 4,
            "locking": [image(disruptor_locking(f), LIGHT) for f in range(4)],
            "locked": [image(disruptor_locked(f), LIGHT) for f in range(4)],
        })
        write_layers(args.layers, "hack_arc", {"arc": arc})
        write_layers(args.layers, "harpoon_turret", {name: [image(px, pal)] for name, px, pal in turret})
        write_layers(args.layers, "harpoon", {"harpoon": [image(harpoon, TOW)]})


if __name__ == "__main__":
    main()
