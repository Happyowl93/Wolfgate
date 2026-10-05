"""Writes the ASF grant kiosk and the ARG ticket: the MMC requisition kiosk and MIC ticket recoloured to ASF purple.

    python Tools/_WF/Asf/recolour_grants.py

Orange, amber and olive shift to the ASF purple, keeping their shading; greys, blues and whites stay as they are.
"""
import colorsys
import json
import os
from pathlib import Path
from PIL import Image

T = str(Path(__file__).resolve().parents[3] / "Resources/Textures")
HUE = 274 / 360
KIOSK = "_Mono/Structures/Machines/VendingMachines/usspvend.rsi"
JOBS = [    # (source rsi, {source state: new state}, destination rsi)
    (KIOSK, {"mmc": "asf", "mmc-unshaded": "asf-unshaded", "broken": "broken", "off": "off", "panel": "panel"},
     "_WF/Asf/Structures/grant_kiosk.rsi"),
    ("_Mono/Objects/Specific/MMC/mmc_ticket.rsi", {"ticket": "ticket"}, "_WF/Asf/Objects/grant_ticket.rsi"),
]


def recolour(px):
    r, g, b, a = px
    if a == 0:
        return px
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if s > 0.2 and 0.02 < h < 0.2:                    # orange, amber, olive
        nr, ng, nb = colorsys.hsv_to_rgb(HUE, min(1.0, s * 0.8), min(1.0, v * 1.05))
        return (round(nr * 255), round(ng * 255), round(nb * 255), a)
    return px


for src, states, dst in JOBS:
    src, dst = f"{T}/{src}", f"{T}/{dst}"
    meta = json.load(open(f"{src}/meta.json"))
    os.makedirs(dst, exist_ok=True)
    for f in os.listdir(dst):
        os.remove(f"{dst}/{f}")
    for old, new in states.items():
        im = Image.open(f"{src}/{old}.png").convert("RGBA")
        im.putdata([recolour(p) for p in im.getdata()])
        im.save(f"{dst}/{new}.png")
    meta["states"] = [{**st, "name": states[st["name"]]} for st in meta["states"] if st["name"] in states]
    meta["copyright"] = meta["copyright"].rstrip(".") + ". Recoloured to the ASF palette for Wolfgate."
    with open(f"{dst}/meta.json", "w", newline="\n") as fh:
        json.dump(meta, fh, indent=2)
        fh.write("\n")
    print("wrote", dst)
