"""Writes the ASF PDA and ID card sprites: Frontier PDAs and vanilla ID cards recoloured to ASF purple, with
per-role PDA trims and ID symbols that copy the role's job icon.

    python Tools/_WF/Asf/draw_ids.py
"""
import json
import os
from pathlib import Path
from PIL import Image

T = Path(__file__).resolve().parents[3] / "Resources/Textures"
PDA_SRC = T / "_NF/Objects/Devices/pda.rsi"
ID_SRC = T / "Objects/Misc/id_cards.rsi"
PDA_OUT = T / "_WF/Asf/Objects/Devices/pda.rsi"
ID_OUT = T / "_WF/Asf/Objects/Misc/id_cards.rsi"


def hx(s):
    return tuple(int(s[i:i + 2], 16) for i in (1, 3, 5)) + (255,)


OUTLINE, PURPLE, LILAC, PALE, WHITE = hx("#4a2f73"), hx("#7b52b8"), hx("#9a74d1"), hx("#c9bfe0"), hx("#f2eefb")
GOLD, GOLD_DARK = hx("#ebc913"), hx("#8a6a00")

# PDA frame greys (pda.png) to purple, per frame shade.
FRAME = {
    "plain": {0xaa: "#b49be0", 0x95: "#9a7dd0", 0x80: "#7b52b8", 0x60: "#5d3f96", 0x55: "#4a2f73"},
    "light": {0xaa: "#efe9fa", 0x95: "#d6c8ee", 0x80: "#b49be0", 0x60: "#9a7dd0", 0x55: "#6a4aa6"},
    "dark": {0xaa: "#9a74d1", 0x95: "#7b52b8", 0x80: "#5d3f96", 0x60: "#4a2f73", 0x55: "#33215a"},
}
# Screen glare greys get a faint violet cast.
SCREEN = {0x21: "#221c2c", 0x2e: "#2e2640", 0x38: "#3a3052"}


def recolour_pda(im, frame):
    lut = {v: hx(c) for v, c in {**FRAME[frame], **SCREEN}.items()}
    out = im.copy()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = im.getpixel((x, y))
            if a and r == g == b and r in lut:
                out.putpixel((x, y), lut[r])
    return out


def ramp(im, stops):
    """Maps every grey pixel by luminance onto a purple ramp; coloured pixels are kept."""
    out = im.copy()
    stops = [(v, hx(c)) for v, c in stops]
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = im.getpixel((x, y))
            if not a or max(r, g, b) - min(r, g, b) > 8:
                continue
            v = (r + g + b) // 3
            lo = max((s for s in stops if s[0] <= v), default=stops[0])
            hi = min((s for s in stops if s[0] >= v), default=stops[-1])
            t = 0 if hi[0] == lo[0] else (v - lo[0]) / (hi[0] - lo[0])
            out.putpixel((x, y), tuple(round(lo[1][i] + (hi[1][i] - lo[1][i]) * t) for i in range(3)) + (a,))
    return out


def put(im, pixels, colour):
    for p in pixels:
        im.putpixel(p, colour)


def stripe(im, light, dark):
    """Security-style two-tone stripe through the top and bottom bands."""
    for y in (4, 5, 28, 29, 30, 31):
        for x, c in ((10, light), (11, light), (12, dark), (13, dark)):
            if im.getpixel((x, y))[3]:
                im.putpixel((x, y), c)


def pda_states():
    base = Image.open(PDA_SRC / "pda.png").convert("RGBA")
    plain = recolour_pda(base, "plain")

    envoy = plain.copy()
    rails = [(y) for y in (20, 21, 22, 25, 26, 27)]
    put(envoy, [(6, y) for y in rails] + [(25, y) for y in rails], GOLD_DARK)
    put(envoy, [(7, y) for y in rails] + [(24, y) for y in rails], GOLD)
    put(envoy, [(18, 5), (19, 5), (20, 5)], GOLD)

    enforcer = recolour_pda(base, "dark")
    stripe(enforcer, WHITE, LILAC)

    researcher = recolour_pda(base, "light")
    stripe(researcher, PURPLE, OUTLINE)

    return {"pda": plain, "pda-envoy": envoy, "pda-enforcer": enforcer,
            "pda-researcher": researcher, "pda-colonist": plain}


# 7x8 ID symbols, the job icons narrowed to the vanilla symbol frame. o outline, . field, # glyph.
SYMBOLS = {
    "envoy": (GOLD, [".ooooo.", "o#####o", "o##...o", "o####.o", "o##...o", "o##...o", "o#####o", ".ooooo."]),
    "enforcer": (WHITE, [".ooooo.", "o#####o", "o#####o", "o#####o", "o.###.o", "o.###.o", "o..#..o", ".ooooo."]),
    "researcher": (WHITE, [".ooooo.", "o####.o", "o#...#o", "o#...#o", "o####.o", "o#..#.o", "o#...#o", ".ooooo."]),
    "colonist": (WHITE, [".ooooo.", "o.####o", "o##...o", "o##...o", "o##...o", "o##...o", "o.####o", ".ooooo."]),
}


def id_states():
    card = ramp(Image.open(ID_SRC / "default.png").convert("RGBA"),
                [(0x31, "#24163a"), (0x43, "#4a2f73"), (0x7d, "#6a47a3"), (0xa1, "#9273c8"), (0xc3, "#c4afe8")])
    # The NanoTrasen mark becomes a small ASF star.
    body = card.getpixel((20, 13))
    put(card, [(x, y) for x in range(15, 24) for y in range(14, 20) if card.getpixel((x, y)) == hx("#c4afe8")], body)
    put(card, [(19, 14), (19, 18), (17, 16), (21, 16)], LILAC)
    put(card, [(19, 15), (19, 17), (18, 16), (20, 16)], PALE)
    put(card, [(19, 16)], WHITE)
    states = {"asfbase": card}
    for role, (glyph, rows) in SYMBOLS.items():
        im = Image.new("RGBA", (32, 32))
        for dy, row in enumerate(rows):
            for dx, ch in enumerate(row):
                if ch != " ":
                    im.putpixel((9 + dx, 10 + dy), {"o": OUTLINE, ".": PURPLE, "#": glyph}[ch])
        # Department bar, as on the vanilla department cards.
        bar = GOLD if role == "envoy" else PURPLE
        put(im, [(x, 20) for x in range(10, 22)], bar)
        put(im, [(9, 20), (22, 20)] + [(x, 21) for x in range(10, 22)], GOLD_DARK if role == "envoy" else OUTLINE)
        states[f"idasf{role}"] = im
    for side in ("left", "right"):
        src = Image.open(ID_SRC / f"default-inhand-{side}.png").convert("RGBA")
        states[f"default-inhand-{side}"] = ramp(src, [(0x48, "#4a2f73"), (0x7d, "#7b52b8"), (0xa7, "#b49be0")])
    return states


def pda_extras():
    """Overlays and the tiny worn and held sheets, unchanged."""
    names = ("light_overlay", "id_overlay", "insert_overlay", "equipped-BELT", "equipped-IDCARD", "inhand-left",
             "inhand-right")
    return {name: Image.open(PDA_SRC / f"{name}.png").convert("RGBA") for name in names}


def write(out, states, src_meta, copyright):
    src = {s["name"]: s for s in json.load(open(src_meta / "meta.json"))["states"]}
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        os.remove(out / f)
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": copyright, "size": {"x": 32, "y": 32},
            "states": []}
    for name, im in states.items():
        im.save(out / f"{name}.png")
        entry = {"name": name}
        ref = src.get(name) or src.get("default" if name.startswith("id") or name == "asfbase" else "pda")
        if ref and "directions" in ref:
            entry["directions"] = ref["directions"]
        meta["states"].append(entry)
    with open(out / "meta.json", "w", newline="\n") as fh:
        json.dump(meta, fh, indent=2)
        fh.write("\n")
    print("wrote", out)


write(PDA_OUT, {**pda_states(), **pda_extras()}, PDA_SRC,
      "aPDA retexture by ZweiHawke @ zweihawke.net (_NF/Objects/Devices/pda.rsi); recoloured to ASF purple with "
      "per-role trims for Wolfgate.")
write(ID_OUT, id_states(), ID_SRC,
      "Taken from tgstation at commit https://github.com/tgstation/tgstation/commit/"
      "d917f4c2a088419d5c3aec7656b7ff8cebd1822e (Objects/Misc/id_cards.rsi); recoloured to ASF purple, with job "
      "symbols following the ASF job icons, for Wolfgate.")
