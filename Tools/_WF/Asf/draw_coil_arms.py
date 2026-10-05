"""Writes the ASF coil pistol item sprite (base, mag-0, icon) and both coil magazine sprites (base, mag-1).

    python Tools/_WF/Asf/draw_coil_arms.py

Part grids with an auto outline. The pistol: white slide with serrations and capacitor vents, a dark coil housing with
lilac coil windows (as on the rifle), a light frame with dust cover and trigger guard, and a raked plum grip with a
stippled panel. Magazines are full size like Mono's, with white feed lips, a slug window and a lilac band; mag-1 shows
the slugs. The pistol's inhand and equipped states are not touched.
"""
from pathlib import Path
from PIL import Image

GUNS = Path(__file__).resolve().parents[3] / "Resources/Textures/_WF/Asf/Objects/Weapons/Guns"
W, H = 32, 32
PAL = {
    # magazine greys
    's': (116, 116, 130),
    # white frame ramp (from the coil rifle)
    'A': (240, 242, 244), 'B': (226, 228, 230), 'C': (208, 210, 212), 'D': (190, 192, 196), 'E': (164, 166, 172),
    'F': (134, 136, 144), 'G': (104, 106, 116), 'O': (72, 74, 79),
    # dark metal (coil housing, rail)
    'k': (18, 18, 20), 'm': (34, 34, 40), 'n': (50, 50, 58), 'q': (70, 70, 80), 'r': (92, 92, 104),
    # plum polymer grip
    '0': (28, 22, 36), '1': (42, 34, 54), '2': (56, 46, 72), '3': (72, 60, 92), '4': (90, 78, 112),
    # lilac coils / glow
    'u': (74, 40, 112), 'v': (110, 64, 168), 'w': (166, 107, 224), 'x': (206, 166, 248), 'y': (240, 226, 255),
}


def grid(rows):
    px = {}
    for y, x0, s in rows:
        for i, ch in enumerate(s):
            if ch != '.':
                px[(x0 + i, y)] = ch
    return px


def grip_px():
    """Raked grip: white backstrap, stippled plum panel lit from the top, darker front strap."""
    rows = [(16, 7, 6), (17, 7, 6), (18, 6, 6), (19, 6, 6), (20, 6, 5), (21, 5, 5), (22, 5, 5), (23, 4, 5), (24, 4, 5)]
    px = {}
    for y, x0, w in rows:
        for i in range(w):
            x = x0 + i
            if i == 0:
                ch = 'C' if y < 19 else 'D' if y < 22 else 'E'
            elif i == w - 1:
                ch = '1'
            elif (x + y) % 2:
                ch = '1'
            else:
                ch = '4' if y < 18 else '3' if y < 21 else '2'
            px[(x, y)] = ch
    return px


PARTS = {
    'grip': ('0', grip_px()),
    'guard': (None, grid([
        (17, 18, "O"), (18, 18, "O"), (19, 13, "OOOOO"),                         # trigger guard loop
        (17, 15, "q"), (18, 15, "k"),                                             # trigger
    ])),
    'frame': ('O', grid([
        (15, 6, "DDDDDDDDDDDDDDDDDDE"),
        (16, 13, "FFFFFFFFFFG"),
    ])),
    'slide': ('O', grid([
        (10, 5, "AAAAAAAAAAAAA"),
        (11, 5, "BBBBBBBBBBBBB"),
        (12, 5, "CCCCCCCCCCCCC"),
        (13, 5, "DDDDDDDDDDDDD"),
        (14, 5, "EEEEEEEEEEEEE"),
    ])),
    'coil': ('k', grid([
        (10, 18, "rrqqqqqqq"),
        (11, 18, "qnnnnnnnn"),
        (12, 18, "mwmwmwmwm"),
        (13, 18, "mvmvmvmvm"),
        (14, 18, "mmmmmmmmm"),
    ])),
    'muzzle': ('k', grid([(11, 27, "n"), (12, 27, "xy"), (13, 27, "wx"), (14, 27, "m")])),
    'details': (None, grid([
        (9, 6, "kk"), (9, 25, "k"),                              # rear and front sights
        (11, 5, "F.F"), (12, 5, "F.F"), (13, 5, "G.G"),          # rear serrations
        (12, 10, "E.E.E"), (13, 10, "F.F.F"),                    # capacitor vents
        (11, 15, "y"), (12, 15, "w"),                            # charge light
        (15, 9, "F"), (15, 16, "F"), (15, 22, "F"),              # frame pins
        (16, 21, "OOO"),                                         # dust-cover rail slot
    ])),
    'mag': ('k', grid([(25, 3, "rqqqqq"), (26, 4, "vwv")])),
}
BODY = ['grip', 'frame', 'guard', 'slide', 'coil', 'muzzle', 'details']
STATES = {'base': BODY, 'mag-0': ['mag'], 'icon': BODY + ['mag']}


def outline(px):
    ring = set()
    for (x, y) in px:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (x + dx, y + dy)
            if n not in px and 0 <= n[0] < W and 0 <= n[1] < H:
                ring.add(n)
    return ring


def render(oc, px):
    im = Image.new('RGBA', (W, H))
    if oc:
        for p in outline(px):
            im.putpixel(p, PAL[oc] + (255,))
    for p, ch in px.items():
        im.putpixel(p, PAL[ch] + (255,))
    return im




def draw_pistol():
    layers = {name: render(*part) for name, part in PARTS.items()}
    for state, names in STATES.items():
        im = Image.new('RGBA', (W, H))
        for n in names:
            im.alpha_composite(layers[n])
        im.save(GUNS / f"coil_pistol.rsi/{state}.png")


def mag(width, top, height, step, window_cols):
    """Straight box magazine leaning 1 px right every `step` rows (0: upright). Returns (base, loaded) pixel dicts."""
    base, loaded = {}, {}
    x0 = 16 - width // 2
    bottom = top + height - 1
    for y in range(top, bottom + 1):
        off = (y - top) // step if step else 0
        row_x = x0 + off
        if y == top:                                   # feed lips: white shoulders, slug gap in the middle
            for i in range(width):
                if i in (0, width - 1):
                    continue
                base[(row_x + i, y)] = 'A' if i in (1, width - 2) else 'k'
                if i not in (1, width - 2):
                    loaded[(row_x + i, y)] = 'y' if i == width // 2 else 'x'
            continue
        plate = y >= bottom - 2
        w = width + 2 if plate else width
        rx = row_x - 1 if plate else row_x
        for i in range(w):
            x = rx + i
            if plate:
                ch = 's' if y == bottom - 2 else 'r' if y == bottom - 1 else 'q'
                if y == bottom - 1 and i == w // 2:
                    ch = 'w'
            elif y == top + 1:
                ch = 'C' if i in (0, width - 1) else 'E'
            elif i == 0:
                ch = 'r'
            elif i == width - 1:
                ch = 'm'
            else:
                ch = 'n' if i < width // 2 else 'm'
                if (y - top) % 5 == 0:                        # rib seams
                    ch = 'q' if i < width // 2 else 'n'
                if y in (bottom - 5, bottom - 4):            # lilac Federation band
                    ch = 'w' if y == bottom - 5 else 'v'
            base[(x, y)] = ch
            if not plate and window_cols and i in window_cols and top + 3 <= y <= bottom - 7:
                base[(x, y)] = 'k'
                loaded[(x, y)] = 'w' if (y - top) % 2 else 'v'   # stacked slugs behind the window
    return base, loaded


def draw_mags():
    for name, args in (("coil_pistol_mag", (7, 4, 24, 0, (3,))), ("coil_rifle_mag", (9, 3, 26, 5, (4,)))):
        base, loaded = mag(*args)
        render('k', base).save(GUNS / f"Ammunition/{name}.rsi/base.png")
        render(None, loaded).save(GUNS / f"Ammunition/{name}.rsi/mag-1.png")


draw_pistol()
draw_mags()
print("wrote coil pistol and magazines")
