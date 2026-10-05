"""Writes the ASF thruster sprites: plan-view vanilla and Mono large thrusters recoloured to the ASF palette.

    python Tools/_WF/Asf/recolour_thrusters.py

Greys go darker with a faint violet cast; the blue plume and glow shift to ASF purple, keeping their shading.
"""
import colorsys
import json
import os
from pathlib import Path
from PIL import Image

T = str(Path(__file__).resolve().parents[3] / "Resources/Textures")
HUE = 272 / 360
JOBS = [
    ("Structures/Shuttles/thruster.rsi", "_WF/Asf/Structures/thruster.rsi",
     "Taken from https://github.com/Baystation12/Baystation12/tree/7274e5654c3cf82d4104dbf818ef00fdc0082aa8 "
     "(Structures/Shuttles/thruster.rsi); recoloured to the ASF palette for Wolfgate."),
    ("_Mono/Structures/Shuttles/thruster_large.rsi", "_WF/Asf/Structures/thruster_large.rsi",
     None),
]


def recolour(px):
    r, g, b, a = px
    if a == 0:
        return px
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if s > 0.15 and 0.45 < h < 0.75:                  # plume, glow and blue trim
        nr, ng, nb = colorsys.hsv_to_rgb(HUE, min(1.0, s * 1.05), v)
    else:                                              # hull greys
        nr, ng, nb = colorsys.hsv_to_rgb(HUE, min(0.10, s + 0.06), v * 0.88)
    return (round(nr * 255), round(ng * 255), round(nb * 255), a)


for src, dst, copyright in JOBS:
    src, dst = f"{T}/{src}", f"{T}/{dst}"
    meta = json.load(open(f"{src}/meta.json"))
    os.makedirs(dst, exist_ok=True)
    for f in os.listdir(dst):
        os.remove(f"{dst}/{f}")
    for st in meta["states"]:
        im = Image.open(f"{src}/{st['name']}.png").convert("RGBA")
        im.putdata([recolour(p) for p in im.getdata()])
        im.save(f"{dst}/{st['name']}.png")
    meta["copyright"] = copyright or meta["copyright"].rstrip(".") + ". Recoloured to the ASF palette for Wolfgate."
    with open(f"{dst}/meta.json", "w", newline="\n") as fh:
        json.dump(meta, fh, indent=2)
        fh.write("\n")
    print("wrote", dst)
