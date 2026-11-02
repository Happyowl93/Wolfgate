#!/usr/bin/env python3
"""Generates the harpoon turret's action icons. Run from the repo root:

    python Tools/_WF/Tether/harpoon_sprites.py
"""

import os

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
TEXTURES = os.path.join(ROOT, "Resources", "Textures", "_WF", "Tether")

STEEL_DARK = (58, 62, 70, 255)
STEEL_LIGHT = (163, 170, 180, 255)
OXIDE = (176, 84, 42, 255)
AMBER = (232, 176, 70, 255)
SHADOW = (34, 36, 42, 255)


def blank():
    return Image.new("RGBA", (32, 32), (0, 0, 0, 0))


def icon(kind):
    """Small control icons for the operator's actions."""
    img = blank()
    d = ImageDraw.Draw(img)
    d.ellipse((4, 4, 27, 27), fill=SHADOW)
    d.ellipse((6, 6, 25, 25), fill=STEEL_DARK)

    if kind == "reel-in":
        d.ellipse((11, 11, 20, 20), outline=AMBER)
        d.polygon(((16, 6), (12, 12), (20, 12)), fill=AMBER)
    elif kind == "pay-out":
        d.ellipse((11, 11, 20, 20), outline=STEEL_LIGHT)
        d.polygon(((16, 25), (12, 19), (20, 19)), fill=STEEL_LIGHT)
    else:
        d.line((9, 22, 22, 9), fill=OXIDE, width=2)
        d.line((9, 9, 22, 22), fill=OXIDE, width=2)

    return img


def main():
    folder = os.path.join(TEXTURES, "harpoon_turret.rsi")
    os.makedirs(folder, exist_ok=True)
    for kind in ("reel-in", "pay-out", "release"):
        icon(kind).save(os.path.join(folder, "icon-" + kind + ".png"))


if __name__ == "__main__":
    main()
