"""Writes the ASF modsuit sprites: Goob's Minerva (RD) modsuit, palette-swapped to the ASF colours, with a visor.

    python Tools/_WF/Asf/recolour_modsuit.py

The Minerva uses 16 flat colours, so the body is an exact swap. Pathfinder: pearl plates on a plum undersuit, lilac
strips, dark visor. Aegis (the shielded upgrade): dark violet armour with glowing hardlight strips and a lit visor,
so the two read apart at a glance. The Minerva has no visor of its own (its face is a grey plate), so the visor is
painted per frame: the connected face plate on front views and the helmet icon, a short strip on the front edge of
side views, nothing on the back.
"""
import json
import os
from pathlib import Path
from PIL import Image

T = Path(__file__).resolve().parents[3] / "Resources/Textures"
SLOTS = ("Back", "Head", "Hands", "OuterClothing", "Shoes")
UNDERSUIT, PLATE, PLATE_LIT = (30, 30, 50), (52, 52, 66), (84, 84, 97)
SHARED = {
    (13, 12, 25): (26, 16, 34),          # outline
    (34, 33, 47): (42, 30, 54),          # translucent seals
    (61, 56, 79): (78, 62, 98),
}
SUITS = {
    "modsuit": {
        "colours": {
            UNDERSUIT: (64, 46, 82), PLATE: (150, 144, 164), PLATE_LIT: (208, 204, 218),
            (80, 6, 119): (92, 52, 138), (122, 11, 183): (128, 78, 190), (157, 29, 232): (176, 122, 232),
            (164, 254, 244): (240, 222, 255), (121, 248, 230): (214, 180, 255), (60, 184, 165): (166, 107, 224),
            (35, 118, 143): (104, 66, 156), (79, 148, 173): (150, 118, 200),
        },
        "visor": ((34, 24, 50), (92, 74, 128)),            # dark glass, glint
    },
    "modsuit_aegis": {
        "colours": {
            UNDERSUIT: (30, 22, 42), PLATE: (68, 54, 94), PLATE_LIT: (108, 92, 138),
            (80, 6, 119): (176, 150, 236), (122, 11, 183): (222, 206, 255), (157, 29, 232): (250, 246, 255),
            (164, 254, 244): (250, 246, 255), (121, 248, 230): (222, 206, 255), (60, 184, 165): (176, 150, 236),
            (35, 118, 143): (120, 90, 190), (79, 148, 173): (176, 150, 236),
        },
        "visor": ((132, 92, 206), (236, 222, 255)),        # lit hardlight visor, bright glint
    },
}
DIRS = ((0, 0, "S"), (32, 0, "N"), (0, 32, "E"), (32, 32, "W"))   # equipped sheets: 2x2 frames, S N E W


def components(px, box, colour):
    """4-connected regions of `colour` inside box (x0, y0, x1, y1)."""
    x0, y0, x1, y1 = box
    seen, regions = set(), []
    for y in range(y0, y1):
        for x in range(x0, x1):
            if (x, y) in seen or px[x, y][:3] != colour or not px[x, y][3]:
                continue
            stack, region = [(x, y)], []
            seen.add((x, y))
            while stack:
                cx, cy = stack.pop()
                region.append((cx, cy))
                for n in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if x0 <= n[0] < x1 and y0 <= n[1] < y1 and n not in seen and px[n][3] and px[n][:3] == colour:
                        seen.add(n)
                        stack.append(n)
            regions.append(region)
    return regions


def face_plate(px, box):
    """The largest grey plate region whose centre sits in the middle third of the frame's helmet."""
    opaque = [(x, y) for y in range(box[1], box[3]) for x in range(box[0], box[2]) if px[x, y][3]]
    if not opaque:
        return []
    left, right = min(x for x, _ in opaque), max(x for x, _ in opaque)
    third = (right - left) / 3
    best = []
    for region in components(px, box, PLATE):
        cx = sum(x for x, _ in region) / len(region)
        if left + third <= cx <= right - third and len(region) > len(best):
            best = region
    return best


def side_strip(px, box, facing):
    """A 4x2 visor strip on the front edge of a side view, at eye level (4-5 rows below the helmet top)."""
    opaque = [(x, y) for y in range(box[1], box[3]) for x in range(box[0], box[2]) if px[x, y][3]]
    if not opaque:
        return []
    top = min(y for _, y in opaque)
    strip = []
    for y in (top + 4, top + 5):
        row = sorted(x for x, yy in opaque if yy == y)
        if not row:
            continue
        xs = row[-5:-1] if facing == "E" else row[1:5]        # skip the outline pixel
        strip += [(x, y) for x in xs]
    return strip


def visor_pixels(png_name, px, size):
    if png_name == "helmet.png":
        return face_plate(px, (0, 0, *size)), "S"
    if not png_name.startswith("equipped-HEAD"):
        return [], None
    out = []
    for x0, y0, d in DIRS:
        box = (x0, y0, x0 + 32, y0 + 32)
        if d == "S":
            out += face_plate(px, box)
        elif d in ("E", "W"):
            out += side_strip(px, box, d)
    return out, None


def paint_visor(px, pixels, glass, glint):
    if not pixels:
        return
    top = min(y for _, y in pixels)
    for x, y in pixels:
        px[x, y] = (*(glint if y == top else glass), px[x, y][3])
    first = min((p for p in pixels if p[1] == top + 1), default=None)
    if first:
        px[first] = (*glint, px[first][3])                          # catchlight in the upper left


for name, suit in SUITS.items():
    cmap = {**SHARED, **suit["colours"]}
    for slot in SLOTS:
        src = T / f"_Goobstation/Clothing/{slot}/Modsuits/researchdirector.rsi"
        out = T / f"_WF/Asf/Clothing/{slot}/{name}.rsi"
        os.makedirs(out, exist_ok=True)
        for png in sorted(src.glob("*.png")):
            im = Image.open(png).convert("RGBA")
            px = im.load()
            visor = visor_pixels(png.name, px, im.size)[0] if slot == "Head" else []
            for y in range(im.height):
                for x in range(im.width):
                    r, g, b, a = px[x, y]
                    if a and (r, g, b) in cmap:
                        px[x, y] = (*cmap[(r, g, b)], a)
            paint_visor(px, visor, *suit["visor"])
            im.save(out / png.name)
        meta = json.loads((src / "meta.json").read_text())
        meta["copyright"] += "; recoloured to the ASF palette for Wolfgate, visor added."
        with open(out / "meta.json", "w", newline="\n") as f:
            json.dump(meta, f, indent=2)
            f.write("\n")
        print("wrote", out)
