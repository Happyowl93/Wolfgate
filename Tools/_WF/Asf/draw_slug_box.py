"""Writes the ASF coil slug box sprite: Mono's caseless ammo box in plum with a lilac lid band and a "4mm" label.

    python Tools/_WF/Asf/draw_slug_box.py
"""
import json
import os
from pathlib import Path
from PIL import Image

T = Path(__file__).resolve().parents[3] / "Resources/Textures"
SRC = T / "_Mono/Objects/Weapons/Guns/Ammunition/Boxes/68x52mm_caseless.rsi/base.png"
OUT = T / "_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_slug_box.rsi"
RECOLOUR = {
    (35, 41, 39): (30, 20, 40),       # outline
    (51, 62, 60): (52, 36, 70),       # lid rim
    (66, 79, 75): (74, 52, 98),       # lid edge, label ground
    (81, 96, 94): (96, 70, 126),      # lid face
}
BAND = [(166, 107, 224), (128, 78, 190)]    # lid band, top row lit
INK, INK_SHADE, GROUND = (236, 226, 248), (168, 150, 190), (74, 52, 98)
LABEL = [                                    # rows 19..23, from x 9
    "x.x..............",
    "x.x..............",
    "xxx.xxxx..xxxx...",
    "..x.x.x.x.x.x.x..",
    "..x.x.x.x.x.x.x..",
]

im = Image.open(SRC).convert("RGBA")
px = im.load()
for y in range(im.height):
    for x in range(im.width):
        r, g, b, a = px[x, y]
        if a and (r, g, b) in RECOLOUR:
            px[x, y] = (*RECOLOUR[(r, g, b)], a)
for x in range(8, 24):                       # lid band across the face
    for i, y in enumerate((12, 13)):
        px[x, y] = (*BAND[i], 255)
for y in range(19, 24):                      # clear the old calibre text
    for x in range(7, 25):
        px[x, y] = (*GROUND, 255)
for dy, row in enumerate(LABEL):
    for dx, ch in enumerate(row):
        if ch == "x":
            px[9 + dx, 19 + dy] = (*(INK if dy < 4 else INK_SHADE), 255)
os.makedirs(OUT, exist_ok=True)
im.save(OUT / "base.png")
meta = {"version": 1, "license": "CC-BY-SA-3.0",
        "copyright": "Taken from SS13 Shiptest, edited by _starch_ (discord) (Mono 68x52mm_caseless box); "
                     "recoloured and relabelled for the ASF coil slug by Wolfgate.",
        "size": {"x": 32, "y": 32}, "states": [{"name": "base"}]}
with open(OUT / "meta.json", "w", newline="\n") as f:
    json.dump(meta, f, indent=2)
    f.write("\n")
print("wrote", OUT)
