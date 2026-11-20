"""Generates modules.rsi, the drydock icons of the ship modules. jets: the maneuvering jets, a pod in the gyroscope's
housing greys under a thruster-metal nozzle, firing a cold-gas smoke ring upward.

Usage: python Tools/_WG/ShipModules/gen_module_icons.py [out.rsi]
"""
import json
import math
import os
import sys
from PIL import Image

OUT_DIR = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "Resources", "Textures", "_WG", "ShipModules", "modules.rsi")

W = H = 32
OUT = (44, 47, 47)
# housing greys, from gyroscope.rsi
HG = [(77, 83, 94), (118, 126, 143), (136, 145, 165), (147, 153, 167), (165, 171, 184), (196, 201, 211)]
# nozzle metal, from thruster.rsi
NZ = [(12, 12, 12), (21, 21, 21), (31, 31, 31), (45, 45, 45), (55, 55, 55), (72, 72, 72), (94, 93, 93), (131, 131, 131)]
# cold-gas accent
BL = [(52, 82, 130), (86, 128, 190), (159, 196, 255)]
# smoke ramp, from smoke_ring.rsi lights, made opaque
SM = [(118, 124, 136), (158, 164, 176), (192, 197, 207), (220, 224, 231), (240, 242, 246)]

px = {}


def put(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[(x, y)] = c


def sym(x, y, c):
    put(x, y, c)
    put(W - 1 - x, y, c)


# Housing: chamfered box, top face lit, front face mid, underside dark.
for y in range(19, 30):
    inset = {19: 3, 20: 1, 28: 1, 29: 3}.get(y, 0)
    for x in range(4 + inset, 16):
        if y <= 20:
            c = HG[5] if y == 19 else HG[4]
        elif y >= 28:
            c = HG[1] if y == 28 else HG[0]
        else:
            c = HG[3] if x > 6 else HG[2]
        sym(x, y, c)
        # the right half of the faces is a step darker
        if y > 19:
            put(W - 1 - x, y, HG[max(0, HG.index(c) - 1)])
# Seam under the top face, and the front panel's edges.
for x in range(6, 16):
    sym(x, 21, HG[1])
for y in range(22, 28):
    sym(6, y, HG[1])
# Gas gauges either side: a lit window over a dark well.
for y in range(23, 27):
    for x in range(8, 11):
        sym(x, y, BL[0] if y == 26 else (BL[2] if y == 23 else BL[1]))
# Rivets in the corners of the front panel.
for x, y in ((7, 22), (7, 27)):
    sym(x, y, HG[0])

# Nozzle bell: widens upward, ribbed, lit from the left.
for y in range(11, 20):
    half = 3 + (19 - y) * 4 // 8
    for x in range(16 - half, 16 + half):
        # 0 at the lit left edge, 1 at the shadowed right edge
        t = (x - (16 - half)) / max(1, 2 * half - 1)
        base = 7 if t < 0.12 else 6 if t < 0.3 else 5 if t < 0.55 else 4 if t < 0.8 else 3
        if (y - 11) % 3 == 2:
            base -= 2
        put(x, y, NZ[max(1, base)])
# Mouth: dark throat ring at the top.
for x in range(9, 23):
    put(x, 11, NZ[6] if x < 16 else NZ[5])
for x in range(10, 22):
    put(x, 12, NZ[0])
for x in range(12, 20):
    put(x, 13, NZ[1])

# Smoke ring: a torus seen from just above; the near side is fuller and brighter.
cx, cy = 15.5, 4.5
R, r = 7.0, 2.1
for y in range(0, 8):
    for x in range(0, W):
        dx = (x - cx)
        dy = (y - cy) * 2.2
        d = math.hypot(dx, dy)
        tube = abs(d - R)
        if tube > r:
            continue
        nz = math.sqrt(max(0.0, 1 - (tube / r) ** 2))
        near = dy > 0
        # the tube's own roundness, the near side toward the viewer, light from the top-left
        inner = d < R
        lum = 0.1 + 0.55 * nz + (0.2 if near else -0.1) - 0.12 * (dx / R) - (0.12 if inner and not near else 0)
        i = max(0, min(len(SM) - 1, int(lum * len(SM))))
        put(x, y, SM[i])

# Outline every shape on transparent neighbours, smoke in its own darker grey.
ring = {}
for (x, y), c in px.items():
    for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        n = (x + ddx, y + ddy)
        if n not in px and 0 <= n[0] < W and 0 <= n[1] < H:
            ring[n] = SM[0] if c in SM else OUT
px.update(ring)

im = Image.new('RGBA', (W, H))
for p, c in px.items():
    im.putpixel(p, c + (255,))
os.makedirs(OUT_DIR, exist_ok=True)
im.save(os.path.join(OUT_DIR, 'jets.png'))
meta = {
    "version": 1,
    "license": "CC-BY-SA-3.0",
    "copyright": "Created for Wolfgate (ShipModules), procedurally generated; palette from thruster.rsi and gyroscope.rsi (Baystation12).",
    "size": {"x": W, "y": H},
    "states": [{"name": "jets"}],
}
with open(os.path.join(OUT_DIR, 'meta.json'), 'w', newline='\n') as f:
    json.dump(meta, f, indent=4)
    f.write('\n')
