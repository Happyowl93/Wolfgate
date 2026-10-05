"""ASF Lantern Post: room plan -> generated hull -> finished base ship (a mobile POI with its own engines).

    python Tools/_WF/Asf/build_lantern.py Resources/Maps/_WF/Asf/lantern.yml

Plan: rooms are rectangles of different sizes, offset from each other, so the hull that is generated around them
      (walls, chamfered outer corners, window runs) has an uneven outline. Bow north: a tapered bridge on a wide
      ready-room collar with a sensor boom off to starboard. A private north block (envoy office, shipyard hall,
      quarters, post with its armoury wall, wardrobe room, a commons that juts out to starboard) over a public middle (consular office,
      atrium, clinic) and the dock concourse, which runs out into a long port docking arm (fuel and flatpack vendors
      at its bay) and a short starboard annex with the cargo bay behind it (sell pallets, ASF bounties, grant kiosk). Aft: farm with a stores pod off its flank, gyro room, R&D lab, power room, atmos and the anomaly rooms.
      Outside the pressure hull: engine pylons of different lengths on lattice trusses, a central engine block,
      gun pads and trusses, hull plating filling the notches. Nothing is mirrored.
Zones: PUBLIC = concourse, docking arm and annex, cargo bay, atrium, clinic, consular office. Everything else needs ASF access.
       The bridge takes ASF bridge access (all but Colonists), the post ASF security, the envoy office ASF command.
Power: RTGs -> HV. A substation feeds the room APCs (one pooled LV net); the shield and the main engines have
      their own LAPCs on HV, on LV nets that never touch the rooms' (AsfLanternTest.LanternIsPowered).
Lights, vents, scrubbers, alarms and cables are placed per room by rule, so the plan can change freely.
"""
import math
import sys
sys.path.insert(0, r"C:/Users/alvar/.claude/skills/ss14-mapping")
from mapkit import REPO, new_grid_from, route, lay_cable, lay_pipes, add_device, dir_to_q, info, rot_dirs

out = sys.argv[1]
m = new_grid_from(REPO / "Resources/Maps/_NF/POI/courthouse.yml", name="ASF Lantern Post")
PURPLE = "#A66BE0FF"
LILAC = "#C79DD0FF"
V = {"N": (0, 1), "S": (0, -1), "E": (1, 0), "W": (-1, 0)}
nb = lambda t, d: (t[0] + V[d][0], t[1] + V[d][1])
OPP = {"N": "S", "S": "N", "E": "W", "W": "E"}

# ---- 1. plan ----------------------------------------------------------------------------------
ROOMS = {   # interior rectangles (x0, y0, x1, y1) and floor
    "ready": ((-5, 24, 5, 26), "FloorDarkDiagonal"),
    "hall": ((-2, 18, 2, 22), "FloorDarkHerringbone"),
    "envoy": ((-8, 18, -4, 21), "FloorWoodLarge"),
    "quarters": ((4, 18, 9, 21), "FloorWood"),
    "spine": ((-1, 13, 1, 16), "FloorDarkMono"),
    "post": ((-9, 13, -3, 15), "FloorSteelMono"),
    "wardrobe": ((3, 13, 6, 16), "FloorDarkPavement"),
    "commons": ((8, 12, 12, 16), "FloorWoodTile"),
    "consular": ((-9, 7, -4, 11), "FloorCarpetOffice"),
    "atrium": ((-2, 7, 2, 11), "FloorGrass"),
    "clinic": ((4, 7, 8, 10), "FloorWhiteMono"),
    "concourse": ((-9, 3, 9, 5), "FloorSteel"),
    "portarm": ((-19, 4, -11, 4), "FloorSteelPavement"),
    "portbay": ((-22, 3, -20, 5), "FloorSteelCheckerDark"),
    "annex": ((11, 3, 13, 5), "FloorSteelCheckerDark"),
    "cargo": ((11, -2, 15, 1), "FloorSteelMono"),
    "farm": ((-10, -4, -4, 1), "FloorHydro"),
    "stores": ((-16, -6, -12, -2), "FloorSteelDirty"),
    "gyro": ((-2, -2, 2, 1), "FloorTechMaint"),
    "lab": ((4, -3, 9, 1), "FloorWhite"),
    "power": ((-3, -8, 3, -4), "FloorReinforced"),
    "atmos": ((-10, -11, -5, -6), "FloorTechMaintDark"),
    "obs": ((5, -6, 9, -5), "FloorWhitePlastic"),
    "chamber": ((5, -9, 9, -8), "FloorReinforcedHardened"),
}
BRIDGE = [(28, -4, 4), (29, -4, 4), (30, -3, 3), (31, -2, 2), (32, -1, 1)]      # the nose: (y, x0, x1)
PUB, PRIV, ASF, CMD = "AirlockGlass", "WFAsfAirlock", "WFAsfAirlockGlass", "WFAsfAirlockCommand"
BRIDGE_DOOR, SEC = "WFAsfAirlockBridge", "WFAsfAirlockSecurity"     # bridge: all but Colonists; post: Enforcers
DOORS = {
    (0, 27): BRIDGE_DOOR, (0, 23): PRIV, (-3, 20): CMD, (3, 19): PRIV, (0, 17): PRIV,
    (-2, 14): SEC, (2, 14): PRIV, (7, 14): PRIV, (0, 12): ASF,
    (-3, 9): PUB, (3, 9): PUB, (0, 6): PUB, (-6, 6): PUB, (6, 6): PUB, (-10, 4): PUB, (10, 4): PUB,
    (0, 2): ASF, (-6, 2): ASF, (6, 2): ASF, (-11, -3): PRIV, (0, -3): PRIV, (6, -4): PRIV, (-4, -7): PRIV,
    (5, -7): PRIV, (12, 2): PUB,
}
DOCKS = {(-23, 4): "W", (-15, 5): "N", (-13, 3): "S", (14, 4): "E", (12, 6): "N"}
PLASMA = {(x, -7) for x in range(6, 10)}                                        # observation | test chamber
WINDOWED = {"bridge", "envoy", "quarters", "commons", "consular", "clinic", "farm", "lab", "portarm", "ready", "cargo"}

floor = {}
for name, ((x0, y0, x1, y1), tile) in ROOMS.items():
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            floor[(x, y)] = (name, tile)
for y, x0, x1 in BRIDGE:
    for x in range(x0, x1 + 1):
        floor[(x, y)] = ("bridge", "FloorDarkMono")
room_tiles = {}
for t, (name, _) in floor.items():
    room_tiles.setdefault(name, set()).add(t)

# ---- 2. generated hull --------------------------------------------------------------------------
walls = {(x + dx, y + dy) for (x, y) in floor for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - set(floor)
for t in DOORS:
    walls.discard(t)
    floor[t] = ("door", next(floor[nb(t, d)][1] for d in "NSEW" if nb(t, d) in floor and floor[nb(t, d)][0] != "door"))
for t in DOCKS:
    assert t in walls, t
    walls.discard(t)
    floor[t] = ("dock", "FloorSteelCheckerDark")
solid = lambda t: t in walls or t in floor
outside = lambda t: not solid(t)
OPEN_Q = {frozenset("NW"): 0, frozenset("NE"): 3, frozenset("SW"): 1, frozenset("SE"): 2}    # diagonal by open sides
diagonals = {}
for t in walls:
    sides = frozenset(d for d in "NSEW" if outside(nb(t, d)))
    if sides in OPEN_Q and all(solid(nb(t, d)) for d in "NSEW" if d not in sides):
        diagonals[t] = OPEN_Q[sides]
windows = set()
for t in walls - set(diagonals):
    if not any(outside(nb(t, d)) for d in "NSEW"):
        continue
    if not {floor[nb(t, d)][0] for d in "NSEW" if nb(t, d) in floor} & WINDOWED:
        continue
    horiz = nb(t, "E") in walls and nb(t, "W") in walls
    vert = nb(t, "N") in walls and nb(t, "S") in walls
    if (horiz and t[0] % 3) or (vert and t[1] % 3):          # runs of two panes between solid posts
        windows.add(t)

for t, (_, tile) in floor.items():
    m.set_tile(*t, tile)
for t in walls:
    m.set_tile(*t, "Plating")
    if t in diagonals:
        m.add("WallPlastitaniumDiagonalOutpost", *t, q=diagonals[t])
    elif t in PLASMA:
        m.add("Grille", *t); m.add("ReinforcedPlasmaWindow", *t)
    elif t in windows:
        m.add("Grille", *t); m.add("PlastitaniumWindowOutpost", *t)
    else:
        m.add("WallPlastitaniumOutpost", *t)
for t, proto in DOORS.items():
    m.add(proto, *t)
for t, side in DOCKS.items():
    q = dir_to_q("S", side)
    m.add("AirlockGlassShuttle", *t, q=q); m.add("AtmosDeviceFanDirectional", *t, q=q)

# ---- 3. exterior: pylons, trusses, pads, greebles, hull plating --------------------------------
EXT = {
    ".": ("Lattice", []), ":": ("Lattice", ["Catwalk"]), "h": ("FloorHullReinforced", []),
    ";": ("FloorHullReinforced", ["Catwalk"]), "k": ("Plating", ["Catwalk"]), "=": ("Lattice", ["Grille"]),
    "#": ("Plating", ["WallPlastitaniumOutpost"]), "a": ("Lattice", ["GreebleAntenna1"]),
    "b": ("Lattice", ["GreebleAntenna2"]), "c": ("Lattice", ["GreebleAntenna3"]),
    "L": ("FloorHullReinforced", ["PoweredlightExterior"]),
}
for ch, q in (("(", 0), (")", 3), ("{", 1), ("}", 2)):
    EXT[ch] = ("Plating", [("WallPlastitaniumDiagonalOutpost", q)])
ext_tiles = set()


def art(x0, y_top, rows):
    """Exterior ASCII overlay; never touches the generated hull."""
    for dy, row in enumerate(rows):
        for dx, ch in enumerate(row):
            t = (x0 + dx, y_top - dy)
            if ch == " " or solid(t) or t in ext_tiles:
                continue
            tile, ents = EXT[ch]
            m.set_tile(*t, tile)
            ext_tiles.add(t)
            for e in ents:
                proto, q = e if isinstance(e, tuple) else (e, 0)
                m.add(proto, *t, q=q)


art(3, 37, [":a  ", ":.c ", ":.==", ":.  ", ":   ", ":   "])          # sensor boom off the starboard bow
art(-9, 27, ["  .", "..k", ". ."])                                     # port bow gun pad on lattice
art(7, 26, [".k", "#}"])                                               # starboard bow gun on a pad beyond its stub
art(-16, 11, ["(kk...", "kkk:::", "{kk..."])                           # port gun truss off the consular office
art(13, 16, ["h", "h;", "hk", "h;", "h"])                              # plating and a gun pad on the commons
art(-18, -3, ["k.", " ."])                                             # Phalanx pad on the stores pod
art(10, 8, ["k"])                                                      # Phalanx pad by the clinic
art(-11, 12, ["h", "h", "L", "h"])                                     # plating along the consular wall
art(10, 22, ["h", "L", "h"])                                           # and along the quarters
# port engine pylon: long, two large thrusters at its foot, a lattice truss to atmos, a torpedo pad on its flank
art(-21, -7, ["",
              "  (##) ",
              "k(####)...",
              " #::::#...",
              "k#::::#...",
              " #::::#",
              " #::::#",
              " #::::#",
              " {::::}"])
# starboard engine pylon: shorter, one large thruster, off the test chamber
art(11, -6, [" k  ", "(##)", "#::#", "#::#k", "#::#", "{::}"])
art(-4, -10, ["(#######)", "{#######}"])                               # central engine block under the power room
# ---- 4. engines and guns -------------------------------------------------------------------------
THRUSTER_TILES = set()


def thruster(x, y, side, proto="WFAsfThruster"):
    m.set_tile(x, y, "Plating")
    m.add(proto, x, y, q=dir_to_q("N", side)); m.add("Catwalk", x, y); m.add("AtmosFixBlockerMarker", x, y)
    THRUSTER_TILES.add((x, y))


# main engines: 2x2 large thrusters exhausting south; at q=2 each covers its anchor and the tiles west and north
for ax, ay in ((-18, -17), (-16, -17), (-2, -13), (0, -13), (2, -13), (4, -13), (13, -13)):
    for x in (ax - 1, ax):
        for y in (ay, ay + 1):
            m.set_tile(x, y, "Plating")
            m.add("Catwalk", x, y); m.add("AtmosFixBlockerMarker", x, y)
            THRUSTER_TILES.add((x, y))
    m.add("WFAsfThrusterLarge", ax, ay, q=2)
for x, y, side in ((-7, 28, "N"), (6, 28, "N"), (-6, 29, "W"), (5, 30, "E"), (-21, -13, "W"), (14, 12, "E"),
                   (-7, -13, "S"), (7, -11, "S")):
    thruster(x, y, side)

GUNS = [
    ("WeaponLaserTurretApollo", "HardpointEnergyHeavy", [(-7, 26, "N"), (8, 26, "N")]),
    ("WeaponLaserTurretPrometheus", "HardpointEnergyMedium", [(-15, 10, "W"), (14, 14, "E"), (-21, -9, "W"), (12, -6, "N")]),
    ("WeaponTurretSerpentMissile", "HardpointMissileMedium", [(-21, -11, "W"), (15, -9, "E")]),
    ("WeaponLaserTurretL1Phalanx", "HardpointEnergyLight", [(-18, -3, "W"), (10, 8, "E")]),
]
for gun, hardpoint, spots in GUNS:
    for x, y, facing in spots:
        m.set_tile(x, y, "Plating")
        m.add("AtmosFixBlockerMarker", x, y)
        m.add(hardpoint, x, y, q=dir_to_q("S", facing))      # a turret's barrel points S at rotation 0
        m.add(gun, x, y, q=dir_to_q("S", facing))

# hull plating fills every notch and concave corner of the outline, so the hull reads as one mass; dock approaches,
# thruster exhaust and gun muzzles stay clear
keep_out = set()
for t, side in DOCKS.items():
    keep_out |= {(t[0] + V[side][0] * k, t[1] + V[side][1] * k) for k in (1, 2, 3)}
for e in m.on_grid():
    if info(e.proto).thruster:
        d = rot_dirs("N", e.q)
        keep_out |= {(e.tile[0] + V[x][0] * k, e.tile[1] + V[x][1] * k) for x in d for k in (1, 2, 3)}
for gun, _, spots in GUNS:
    for x, y, facing in spots:
        keep_out |= {(x + V[facing][0] * k, y + V[facing][1] * k) for k in (1, 2)}
for _ in range(2):
    for t in sorted({nb(w, d) for w in walls | ext_tiles for d in "NSEW"}):
        if solid(t) or t in ext_tiles or t in keep_out or t in THRUSTER_TILES or t in m.tiles:
            continue
        if sum(solid(nb(t, d)) or nb(t, d) in ext_tiles for d in "NSEW") >= 3:
            m.set_tile(*t, "FloorHullReinforced")
            ext_tiles.add(t)

# ---- 5. helpers for furnishing -----------------------------------------------------------------------
approach = {nb(t, d) for t in list(DOORS) + list(DOCKS) for d in "NSEW"} & set(floor)
used_walls = set()


def light(x, y, wall, proto="Poweredlight"):
    return m.add(proto, x, y, q=dir_to_q("N", wall))


def wm(proto, x, y, room_side):
    """Wallmount on a wall tile, facing the room on `room_side`."""
    used_walls.add((x, y))
    return m.add(proto, x, y, q=dir_to_q("S", room_side))


def chair(proto, x, y, facing):
    return m.add(proto, x, y, q=dir_to_q("S", facing))


def console(proto, x, y, back, comps=None):
    return m.add(proto, x, y, q=dir_to_q("N", back), comps=comps)


def on_table(table, x, y, *items):
    m.add(table, x, y)
    for item in items:
        m.add(item, x, y)


BLOCKING = {"object", "power", "wall", "window", "door", "diagwall", "atmos"}
busy = lambda t: any(info(e.proto).category() in BLOCKING for e in m.at(*t))


def wall_spot(room, prefer_inner=True, skip=()):
    """A plain wall tile beside `room` for a wallmount: (x, y, side facing the room)."""
    best = []
    for t in sorted(room_tiles[room]):
        if t in approach:
            continue
        for d in "NSEW":
            w = nb(t, d)
            if w not in walls or w in diagonals or w in windows or w in PLASMA or w in used_walls or w in skip:
                continue
            if any(nb(w, e) in DOORS for e in "NSEW"):
                continue
            inner = not any(outside(nb(w, e)) for e in "NSEW")
            best.append((not inner if prefer_inner else 0, t, w, OPP[d]))
    if not best:
        return None
    best.sort()
    _, _, w, side = best[0]
    return (*w, side)


def free_floor(room, n=1, avoid=()):
    """Free floor tiles in a room, spread out: not a door approach, nothing solid on them."""
    tiles = [t for t in sorted(room_tiles[room]) if t not in approach and t not in avoid and not busy(t)]
    picked = []
    while tiles and len(picked) < n:      # farthest-point spread
        t = max(tiles, key=lambda t: min([abs(t[0] - p[0]) + abs(t[1] - p[1]) for p in picked] or [0]))
        picked.append(t)
        tiles.remove(t)
    return picked


def place(proto, x, y, q=0):
    if (x, y) in approach or busy((x, y)):
        print(f"  skip {proto} at {(x, y)}")
        return None
    return m.add(proto, x, y, q=q)


# ---- 6. rooms -------------------------------------------------------------------------------------
# Bridge (y 28..32), door from the ready room at (0, 27)
console("ComputerRadar", -1, 32, "N"); console("ComputerShuttle", 0, 32, "N"); console("ComputerGunneryConsole", 1, 32, "N")
chair("ChairOfficeDark", -1, 31, "N"); chair("ChairPilotSeat", 0, 31, "N"); chair("ChairOfficeDark", 1, 31, "N")
console("ComputerCrewMonitoring", -3, 30, "W"); console("ComputerStationRecords", 3, 30, "E")
console("ComputerComms", -4, 29, "W"); console("ComputerIFF", 4, 29, "E")
chair("ChairOfficeDark", -3, 29, "W"); chair("ChairOfficeDark", 3, 29, "E")
m.add("WFAsfBanner", -4, 28); m.add("WFAsfMarineBanner", 4, 28)
on_table("TableReinforced", -3, 28, "PaperBin5"); on_table("TableReinforced", 3, 28, "Pen")
m.add("PottedPlantRandom", -2, 28); m.add("PottedPlantRandom", 2, 28)

# Ready room (x -5..5, y 24..26): fire control and EVA gear
m.add("GunneryServerStation", -5, 26)
on_table("TableReinforced", -4, 26, "WeaponCapacitorRecharger"); on_table("TableReinforced", -3, 26, "WeaponCapacitorRecharger")
m.add("ClosetFireFilled", -5, 24); m.add("ClosetEmergencyFilledRandom", -5, 25)
for x in (-3, -2, 2, 3):
    m.add("SteelBench", x, 24)
for x in (3, 4, 5):
    m.add("SuitStorageEVA", x, 26)
m.add("ClosetToolFilled", 5, 24); m.add("WFAsfBanner", -1, 26); m.add("WFAsfBanner", 1, 26)
m.add("ToolboxEmergencyFilled", -4, 26)

# Shipyard hall (x -2..2, y 18..22)
console("WFAsfComputerShipyard", -2, 22, "N"); console("ComputerBankATM", 2, 22, "N")
m.add("WFAsfBanner", -1, 22); m.add("WFAsfMarineBanner", 1, 22)
m.add("SteelBench", -2, 18); m.add("PottedPlantRandom", 2, 18); m.add("ClosetEmergencyFilledRandom", -1, 18)

# Envoy office (x -8..-4, y 18..21), command door from the hall at (-3, 20)
for x in (-7, -6, -5):
    m.add("TableWood", x, 20)
    for y in (19, 20):
        m.add("CarpetPurple", x, y)
m.add("PaperBin5", -7, 20); m.add("Pen", -5, 20); m.add("LampGold", -6, 20); m.add("DrinkMugBlue", -5, 20)
chair("ComfyChair", -6, 21, "S"); chair("Chair", -7, 19, "N"); chair("Chair", -5, 19, "N")
m.add("BookshelfFilled", -8, 21); m.add("BookshelfFilled", -8, 20); m.add("PottedPlantRandom", -4, 21)
m.add("WFAsfBanner", -8, 18); m.add("filingCabinetDrawerRandom", -4, 18)
m.add("WFAsfSpawnPointEnvoy", -7, 18)

# Quarters (x 4..9, y 18..21), door from the hall at (3, 19)
for x in (6, 8):
    m.add("Bed", x, 21); m.add("BedsheetPurple", x, 21)
m.add("Dresser", 7, 21); m.add("WardrobeMixedFilled", 9, 21); m.add("WardrobeMixedFilled", 9, 20)
m.add("MachineCryoSleepPod", 9, 18); m.add("DefaultStationBeaconCryosleep", 9, 18)
on_table("TableWood", 5, 21, "LampGold"); m.add("PottedPlantRandom", 4, 18); m.add("PlushieMoth", 8, 21)
for x in (6, 7, 8):
    m.add("CarpetPurple", x, 19)
m.add("WFAsfSpawnPointEnforcer", 6, 19); m.add("WFAsfSpawnPointFieldResearcher", 7, 19)
m.add("WFAsfSpawnPointColonist", 8, 19)

# Spine (x -1..1, y 13..16): the private corridor
m.add("PottedPlantRandom", 1, 16); m.add("WFAsfBanner", -1, 16)

# Post (x -9..-3, y 13..15): Enforcer desk, an armoury wall to the west (rifle rack, ammo table, sidearm rack) and
# three Pathfinder modsuits in suit storage along the north wall
m.add("WFAsfGunRackFilled", -9, 15)
on_table("TableReinforced", -9, 14, *["WFAsfBoxCoilSlug"] * 4, "WFAsfBoxMagazineRifleCoil", "WFAsfBoxMagazinePistolCoil")
m.add("WFAsfPistolRackFilled", -9, 13)
for x in (-8, -7, -6):
    m.add("WFAsfSuitStoragePathfinder", x, 15)
m.add("WFAsfLockerSecurity", -5, 15); m.add("WFAsfLockerSecurity", -7, 13)
on_table("TableReinforced", -4, 15, "WeaponCapacitorRecharger"); chair("ChairOfficeDark", -4, 14, "N")
console("ComputerCrewMonitoring", -6, 13, "S")

# Wardrobe room (x 3..6, y 13..16): the ASF drobe
console("WFAsfDrobe", 3, 16, "N"); console("WFAsfDrobe", 5, 16, "N")
m.add("WardrobeMixedFilled", 3, 13); m.add("WardrobeMixedFilled", 6, 13)
m.add("SteelBench", 4, 13); m.add("PottedPlantRandom", 6, 16)

# Commons (x 8..12, y 12..16), juts out to starboard
for x in (10, 11):
    m.add("TableWood", x, 14)
    chair("Chair", x, 15, "S"); chair("Chair", x, 13, "N")
m.add("FoodTinPeaches", 10, 14); m.add("DrinkMug", 11, 14)
on_table("TableWood", 12, 16, "KitchenMicrowave"); m.add("WaterCooler", 11, 16)
console("VendingMachineCoffee", 8, 16, "N"); m.add("PottedPlantRandom", 8, 12); m.add("PottedPlantRandom", 12, 12)
m.add("ComfyChair", 12, 14, q=dir_to_q("S", "W"))

# Consular office (x -9..-4, y 7..11): public reception
for x in (-8, -7, -6):
    m.add("TableReinforced", x, 10)
m.add("PaperBin5", -8, 10); m.add("Pen", -6, 10); m.add("LampGold", -7, 10); m.add("BoxFolderClipboard", -6, 10)
chair("ChairOfficeDark", -7, 11, "S"); chair("Chair", -8, 9, "N"); chair("Chair", -6, 9, "N")
m.add("BookshelfFilled", -9, 11); m.add("filingCabinetDrawerRandom", -5, 11); m.add("WFAsfBanner", -4, 11)
m.add("WaterCooler", -9, 8); m.add("PottedPlantRandom", -9, 7); m.add("PottedPlantRandom", -4, 7)
m.add("SteelBench", -8, 7)

# Atrium (x -2..2, y 7..11): a public garden around a boardwalk cross
for x, y in ((-2, 11), (-2, 10), (-1, 11)):
    m.add("FloorWaterEntity", x, y)
m.add("FloraTree", 2, 11, offset=(0.5, 0.2)); m.add("FloraTree", -2, 7, offset=(0.5, 0.2))
m.add("SteelBench", 1, 10); m.add("SteelBench", -1, 8); m.add("SteelBench", 2, 8)
m.add("PoweredLightPostSmall", 2, 10); m.add("PoweredLightPostSmall", -2, 8)

# Clinic (x 4..8, y 7..10)
for x in (5, 7):
    m.add("MedicalBed", x, 10); m.add("BedsheetMedical", x, 10)
m.add("StasisBed", 6, 10)
console("VendingMachineMedical", 8, 10, "E"); m.add("LockerMedicineFilled", 8, 9)
on_table("TableGlass", 8, 8, "MedkitFilled"); on_table("TableGlass", 8, 7, "HandheldHealthAnalyzer")
chair("Chair", 4, 10, "S"); m.add("SinkWide", 4, 7)

# Concourse (x -9..9, y 3..5) with the port arm and bay (3 docks) and the starboard annex (2 docks)
for x in (-4, -3, 3, 4):
    m.add("SteelBench", x, 5)
for x in (-3, 3):
    m.add("SteelBench", x, 3)
console("VendingMachineCoffee", -9, 5, "N"); console("VendingMachineCola", 9, 5, "N")
console("VendingMachineSnack", 9, 3, "S"); m.add("ClosetEmergencyFilledRandom", -9, 3)
m.add("MachineCryoSleepPod", -8, 3)                                       # public cryo for visitors
m.add("ComputerBankATM", -8, 5); m.add("VendingMachineDiscount", 8, 5)
for x, y in ((-2, 5), (2, 5), (-4, 3), (4, 3)):
    m.add("PottedPlantRandom", x, y)
m.add("WarpPoint", 0, 4, comps=[{"type": "WarpPoint", "location": "ASF Lantern Post"}])
m.add("DefaultStationBeaconArrivals", -1, 4)
m.add("ClosetEmergencyFilledRandom", -22, 5); m.add("SteelBench", -22, 3); m.add("SteelBench", 13, 3)
console("VendingMachineFuelVend", -21, 5, "N"); console("VendingMachineFlatpackVend", -21, 3, "S")   # by the docks

# Cargo bay (x 11..15, y -2..1), public, behind the starboard annex at (12, 2): sell pallets, the ASF bounty console
# (crystals and artifacts for ARG) and the grant kiosk that redeems ARG (ASF crew only)
for x, y in ((14, -2), (15, -2), (14, -1), (15, -1)):
    m.add("CargoPalletSell", x, y)
console("ComputerCargoBounty", 14, 1, "N"); console("ComputerPalletConsoleNFNormalMarket", 15, 1, "N")
console("WFAsfGrantKiosk", 11, 1, "N")
m.add("CrateArtifactContainer", 11, -2); m.add("CrateArtifactContainer", 12, -2)
on_table("TableReinforced", 11, -1, "HandLabeler", "Paper")

# Farm (x -10..-4, y -4..1): trays either side of a path at x -6
for x in (-9, -8, -4):
    for y in (0, -2):
        m.add("hydroponicsTray", x, y)
m.add("LockerBotanistFilled", -10, 1); console("VendingMachineSeeds", -4, 1, "N")
m.add("WaterTankFull", -10, -4); m.add("SeedExtractor", -9, -4); m.add("Biogenerator", -4, -4)
on_table("TableWood", -8, -4, "HydroponicsToolSpade", "HydroponicsToolMiniHoe", "HydroponicsToolClippers")
m.add("PottedPlantRandom", -10, -1); m.add("Bucket", -8, -4)

# Stores pod (x -16..-12, y -6..-2), off the farm at (-11, -3)
for x, y in ((-16, -6), (-15, -6), (-16, -5), (-12, -6), (-13, -6)):
    m.add("CrateGenericSteel", x, y)
m.add("ClosetMaintenanceFilledRandom", -16, -2); m.add("ClosetMaintenanceFilledRandom", -12, -2)
m.add("CrateEngineeringCableBulk", -12, -5)
on_table("Rack", -15, -2, "SheetSteel"); on_table("Rack", -14, -2, "SheetGlass")

# Gyro room (x -2..2, y -2..1)
for x, y in ((1, 1), (2, 1), (2, 0)):
    m.add("Gyroscope", x, y)
m.add("GravityGeneratorMini", 2, -2); m.add("StationAnchorOff", -2, 1); m.add("ClosetToolFilled", -2, -2)

# R&D lab (x 4..9, y -3..1)
console("ComputerResearchAndDevelopment", 4, 1, "N"); chair("ChairOfficeLight", 4, 0, "N")
silo = m.add("MachineMaterialSilo", 7, 1)                                        # every lathe draws from it
on_silo = lambda: [{"type": "OreSiloClient", "silo": silo.uid}]
lathes = [console("Protolathe", 8, 1, "N", comps=on_silo()),
          console("WFAsfLathe", 9, 1, "N", comps=on_silo()),                   # researched ASF vouchers
          console("CircuitImprinter", 9, -1, "E", comps=on_silo())]
silo.set_comp("OreSilo", clients=[e.uid for e in lathes])
m.add("WFAsfResearchServer", 9, -3)
m.add("DatafarmResearchFaction", 9, -2)                                          # the server earns points only from clients
on_table("TableReinforced", 4, -1, "AnomalyScanner", "Beaker"); on_table("TableReinforced", 4, -2, "HandheldHealthAnalyzer")
m.add("LockerScienceFilled", 4, -3); m.add("ShelfMetal", 9, 0)

# Power room (x -3..3, y -8..-4): RTGs, the substation, the shield and five LAPCs. A 15 kW APC can't carry the
# shield (80 kW idle, up to 150 kW under fire) or the main engines (about 80 kW), so they draw from HV through LAPCs.
gens = [m.add("GeneratorRTG", x, -8) for x in (-3, -2, -1, 1, 2, 3)] + [m.add("GeneratorRTG", x, -7) for x in (2, 3)]
sub = m.add("SubstationBasic", -3, -4)
engine_lapcs = [m.add("LargeAPC", x, -4) for x in (-2, -1)]
shield_lapcs = [m.add("LargeAPC", x, y) for x, y in ((1, -4), (2, -4), (3, -5))]
shield = m.add("WFAsfShieldGenerator", 3, -4)
m.add("ClosetFireFilled", -3, -5); m.add("ClosetRadiationSuitFilled", -3, -6)

# Atmos (x -10..-5, y -11..-6): air canister on a port feeding the distro pump
for x in (-10, -9, -8):
    m.add("AirCanister", x, -11)
m.add("NitrogenCanister", -6, -11); m.add("OxygenCanister", -5, -11); m.add("StorageCanister", -7, -11)
m.add("AirCanister", -10, -9)
m.add("ClosetFireFilled", -10, -6); m.add("ClosetEmergencyFilledRandom", -9, -6)
m.add("PortableScrubber", -5, -10); m.add("WeldingFuelTankFull", -5, -6)

# Anomaly testing: the observation room looks through plasma glass into the test chamber
analyzer = m.add("MachineArtifactAnalyzer", 8, -9)
console("ComputerAnalysisConsole", 8, -5, "N", comps=[
    {"type": "DeviceLinkSource", "linkedPorts": {analyzer.uid: [["ArtifactAnalyzerSender", "ArtifactAnalyzerReceiver"]]}}])
chair("ChairOfficeLight", 8, -6, "N"); m.add("MachineAnomalyVessel", 9, -5)
on_table("TableGlass", 7, -5, "AnomalyScanner", "ResearchDisk"); m.add("LockerScienceFilled", 9, -6)
m.add("MachineAPE", 6, -9, q=dir_to_q("N", "E")); m.add("FloorDrain", 7, -8)

# ---- 7. lights: per room, on walls, spread out --------------------------------------------------
NO_LIGHT = {"door", "dock"}
for name, tiles in room_tiles.items():
    if name in NO_LIGHT:
        continue
    want = max(1, math.ceil(len(tiles) / 16))
    spots = []
    for t in sorted(tiles):
        if t in approach or busy(t):
            continue
        for d in "NSEW":
            w = nb(t, d)
            if w in walls and w not in diagonals and w not in windows:
                spots.append((t, d))
                break
    chosen = []
    for t, d in sorted(spots, key=lambda s: (s[1] != "N", s[0])):
        if len(chosen) == want:
            break
        if all(abs(t[0] - c[0][0]) + abs(t[1] - c[0][1]) >= 4 for c in chosen):
            chosen.append((t, d))
    for t, d in chosen:
        light(*t, d)
    if len(tiles) >= 20 and len(spots) > len(chosen):
        t, d = next(s for s in spots if s not in chosen)
        light(*t, d, "EmergencyLight")

# ---- 8. power: RTGs -> HV -> substation -> MV -> APCs -> LV --------------------------------------------
lay_cable(m, "HV", [(x, -8) for x in range(-3, 4)] + [(2, -7), (3, -7), (3, -6), (3, -5), (2, -5), (2, -4), (1, -4),
                                                        (-3, -7), (-3, -6), (-3, -5), (-3, -4), (-2, -4), (-1, -4)])
apcs = []
# room APCs stay off the power room's walls, where the shield and engine LAPC nets run
power_walls = {w for w in walls if any(nb(w, d) in room_tiles["power"] for d in "NSEW")}
for room in ("ready", "hall", "spine", "consular", "concourse", "commons", "farm", "lab", "atmos", "obs", "portarm",
             "gyro", "cargo"):
    spot = wall_spot(room, skip=power_walls)
    if spot:
        apcs.append(wm("APCBasic", *spot))


def tree(roots, targets, avoid=lambda t: False):
    net = {tuple(r) for r in roots}

    def spots(t):
        """The target itself, or for one on an avoided tile the free tiles within reception range, nearest first."""
        if not avoid(t):
            return [t]
        near = ((t[0] + dx, t[1] + dy) for dx in range(-2, 3) for dy in range(-2, 3))
        return sorted((c for c in near if c in m.tiles and not avoid(c)), key=lambda c: abs(c[0] - t[0]) + abs(c[1] - t[1]))

    dist = lambda t: min(abs(t[0] - n[0]) + abs(t[1] - n[1]) for n in net)
    todo = [tuple(t) for t in targets if tuple(t) not in net]
    while todo:
        t = min(todo, key=dist)
        todo.remove(t)
        for c in spots(t):
            if c in net:
                break
            near = min(net, key=lambda n: abs(c[0] - n[0]) + abs(c[1] - n[1]))
            try:
                net |= set(route(m, near, c, floor_only=False, avoid=avoid))
                break
            except ValueError:
                continue
        else:
            print(f"  no route to {t}")
    return net


lay_cable(m, "MV", tree([sub.tile], [a.tile for a in apcs]))

# ---- 9. atmos: a vent and a scrubber per room, one distro and one waste tree ----------------------
NO_AIR = {"door", "dock"}
vents, scrubs = {}, {}
dev_tiles = {(-10, -9), (-9, -9), (-8, -9)}                               # air supply in atmos


def device(proto, t, layer, color):
    # face a floor tile of the room when there is one, so the pipe can always reach it
    order = sorted("NSEW", key=lambda d: (nb(t, d) not in floor or floor[nb(t, d)][0] in ("door", "dock"),
                                          nb(t, d) not in m.tiles))
    face = next(d for d in order if nb(t, d) not in dev_tiles and nb(t, d) not in DOCKS)
    dev_tiles.add(t)
    return add_device(m, proto, *t, face=face, layer=layer, color=color)


for name, tiles in room_tiles.items():
    if name in NO_AIR:
        continue
    n = 2 if len(tiles) >= 30 else 1
    picks = free_floor(name, 2 * n, avoid=dev_tiles)
    if len(picks) < 2:
        picks += [t for t in sorted(tiles) if t not in approach and t not in picks and t not in dev_tiles][:2 - len(picks)]
    vents[name] = [device("GasVentPump", t, None, "distro") for t in picks[0::2]]
    scrubs[name] = [device("GasVentScrubber", t, 1, "waste") for t in picks[1::2]]

facing = lambda e: nb(e.tile, next(iter(rot_dirs(info(e.proto).pipe_nodes[0][0], e.q))))
port = add_device(m, "GasPort", -10, -9, face="E", color="distro")
pump = add_device(m, "GasPressurePumpOn", -9, -9, face="W", color="distro")
all_vents = [v for vs in vents.values() for v in vs]
all_scrubs = [s for ss in scrubs.values() for s in ss]
pipe_avoid = lambda t: t in dev_tiles or t in THRUSTER_TILES or t in DOCKS
lay_pipes(m, tree([(-8, -9)], [facing(v) for v in all_vents], avoid=pipe_avoid), layer=0, color="distro")
# waste vents to space from the port engine truss
outlet = (-14, -10)
add_device(m, "GasPassiveVent", *outlet, face="E", layer=1, color="waste")
dev_tiles.add(outlet)
lay_pipes(m, tree([(-13, -10)], [facing(s) for s in all_scrubs], avoid=pipe_avoid), layer=1, color="waste")

for name in vents:
    spot = wall_spot(name)
    if spot and len(room_tiles[name]) >= 6:
        m.link_alarm(wm("AirAlarm", *spot), vents[name] + scrubs[name])
for room in ("ready", "hall", "spine", "concourse", "farm", "power", "lab", "atmos", "stores", "cargo"):
    spot = wall_spot(room)
    if spot:
        wm("FireAlarm", *spot)
for room in ("ready", "envoy", "post", "consular", "concourse", "clinic", "farm", "power", "lab", "stores", "commons", "cargo"):
    spot = wall_spot(room)
    if spot:
        wm("ExtinguisherCabinetFilled", *spot)
for t in ((0, 12), (0, 6), (-6, 6), (6, 6), (0, 2), (-6, 2), (6, 2), (-10, 4), (10, 4), (12, 2)):
    m.add("FirelockGlass", *t)
for proto, room in (("SignCryo", "quarters"), ("DefibrillatorCabinetFilled", "clinic"), ("SignMedical", "clinic"), ("StationMap", "concourse"),
                    ("SignShipDock", "portarm"), ("SignShipDock", "annex"), ("SignEngineering", "gyro"),
                    ("SignScience", "lab"), ("SignHydro1", "farm"), ("SignAnomaly2", "obs"),
                    ("WallmountTelevision", "commons"), ("RandomPainting", "envoy"), ("RandomPainting", "hall"),
                    ("RandomPainting", "consular"), ("RandomPainting", "quarters"), ("Mirror", "wardrobe"),
                    ("LockerWallMedicalFilled", "clinic"), ("ClosetWallEmergencyFilledRandom", "portarm"),
                    ("ClosetWallFireFilledRandom", "lab"), ("RandomPainting", "concourse"), ("SignCargo", "cargo")):
    spot = wall_spot(room)
    if spot:
        wm(proto, *spot)

# LV last, so every receiver placed above is wired. Three separate nets: the main engines (large thrusters and
# the small aft ones) and the shield each on their LAPCs, everything else on the room APCs. Nets never touch.
around = lambda tiles: {(x + dx, y + dy) for x, y in tiles for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1))}
engines = [e for e in m.on_grid() if info(e.proto).thruster and e.tile[1] <= -10]
shield_net = {e.tile for e in shield_lapcs} | {shield.tile}
room_roots = {a.tile for a in apcs}
keep_clear = around(shield_net | room_roots)
engine_net = tree([e.tile for e in engine_lapcs], [e.tile for e in engines], avoid=lambda t: t in keep_clear)
fenced = around(engine_net | shield_net)
skip = {e.uid for e in engines} | {shield.uid}
receivers = [e for e in m.on_grid() if info(e.proto).receiver and e.uid not in skip]
room_net = tree([apcs[0].tile], [a.tile for a in apcs[1:]], avoid=lambda t: t in fenced)
room_net = tree(room_net, [r.tile for r in receivers], avoid=lambda t: t in fenced)
lay_cable(m, "LV", engine_net | shield_net | room_net)

# ---- 9b. dressing: clutter and floor variety ----------------------------------------------------------
for proto, x, y in (("Rack", 2, 24), ("PottedPlantRandom", -1, 24), ("SteelBench", 1, 18), ("ShelfWood", 4, 21),
                    ("Rack", -15, -6), ("ShelfMetal", -16, -3), ("SpaceHeaterAnchored", -6, -7), ("ChairFolding", -4, 13),
                    ("PottedPlantRandom", -9, 1), ("ChairWood", 5, 18),
                    ("Stool", 9, 13), ("PottedPlantRandom", 13, 5), ("PottedPlantRandom", -20, 3),
                    ("TableReinforced", 7, -6)):
    place(proto, x, y)
for item, x, y in (("ToolboxMechanicalFilled", -2, -2), ("BoxLightMixed", -9, 3), ("CableApcStack", -12, -5),
                   ("Wrench", -3, -5), ("LargeBeaker", 4, -1), ("ResearchDisk", 7, -6), ("Paper", 2, 22),
                   ("DrinkMug", 5, 21), ("BoxFolderClipboard", -4, 15), ("BoxFolderBlue", -4, 15), ("FoodTinPeaches", -12, -6),
                   ("Bucket", -9, -4), ("HydroponicsToolScythe", -8, -4), ("CrowbarRed", -16, -6),
                   ("FlashlightLantern", 7, -6), ("BoxCardboard", 2, 24)):
    m.add(item, x, y)
for x in range(-8, 9):
    m.set_tile(x, 4, "FloorSteelPavement")
for y in range(13, 17):
    m.set_tile(0, y, "FloorShuttlePurple")
for y in range(-8, -4):
    m.set_tile(0, y, "FloorTechMaint2")
for x in range(5, 10):
    m.set_tile(x, -1, "FloorWhiteDiagonal")
for x in range(-4, 5):
    m.set_tile(x, 25, "FloorDarkMono")
for x, y in ((-15, -4), (-13, -3), (-14, -5)):
    m.set_tile(x, y, "Plating")

# ---- 10. decals ------------------------------------------------------------------------------------
TRIM = {"envoy": PURPLE, "hall": PURPLE, "quarters": PURPLE, "post": PURPLE, "wardrobe": PURPLE, "commons": PURPLE,
        "consular": PURPLE, "clinic": PURPLE, "lab": PURPLE, "obs": PURPLE, "concourse": LILAC, "ready": LILAC,
        "spine": LILAC, "portarm": LILAC, "cargo": LILAC, "gyro": "#FFFFFFFF", "power": "#FFFFFFFF", "atmos": "#52B4E9FF",
        "stores": "#FFFFFFFF", "chamber": "#DE3A3AFF"}
for name, color in TRIM.items():
    (x0, y0, x1, y1), _ = ROOMS[name]
    base = "BrickTileWhite" if color in (PURPLE, LILAC) else "BrickTileDark"
    for x in range(x0 + 1, x1):
        m.add_decal(base + "LineN", x, y1, color=color); m.add_decal(base + "LineS", x, y0, color=color)
    for y in range(y0 + 1, y1):
        m.add_decal(base + "LineW", x0, y, color=color); m.add_decal(base + "LineE", x1, y, color=color)
    if x1 > x0 and y1 > y0:
        for x, y, c in ((x0, y0, "Sw"), (x1, y0, "Se"), (x0, y1, "Nw"), (x1, y1, "Ne")):
            m.add_decal(f"{base}Corner{c}", x, y, color=color)
for y, x0, x1 in BRIDGE:                                                 # bridge edge lines and runner
    m.add_decal("BrickTileDarkLineW", x0, y, color=PURPLE); m.add_decal("BrickTileDarkLineE", x1, y, color=PURPLE)
    m.set_tile(0, y, "FloorShuttlePurple")
for y in range(18, 23):
    m.set_tile(0, y, "FloorShuttlePurple")
for x in range(-9, 10):
    m.add_decal("MonoOverlay", x, 4, color="#C79DD030")
for x in range(-21, -10):
    m.add_decal("MonoOverlay", x, 4, color="#C79DD030")
for y in range(7, 12):
    m.set_tile(0, y, "FloorWood")
for x in range(-2, 3):
    m.set_tile(x, 9, "FloorWood")
flowers = ["Flowerspv1", "Flowerspv2", "Flowerspv3", "Flowersy1", "Flowersy2", "Flowersy3", "Flowersy4",
           "Flowersbr1", "Flowersbr3", "Bushb1", "Grassa1", "Grassb2"]
for i, t in enumerate(t for t in sorted(room_tiles["atrium"]) if t[0] and t[1] != 9 and not m.at(*t)):
    m.add_decal(flowers[i % len(flowers)], *t)
for y in range(-4, 2):
    m.set_tile(-6, y, "FloorSteelDiagonal")
for x in range(-3, 4):
    m.add_decal("WarnLineN", x, -8)
for x, y in ((-22, 4), (13, 4), (-15, 4), (-13, 4), (12, 5)):
    m.add_decal("StandClear", x, y)
for x in range(-19, -15):
    m.add_decal("ArrowsGreyscale", x, 4, color=LILAC, q=dir_to_q("N", "W"))
for x, y in ((-16, -6), (-15, -6), (-16, -5), (-12, -6), (-13, -6)):
    m.add_decal("Box", x, y)
for x, y in ((14, -2), (15, -2), (14, -1), (15, -1), (11, -2), (12, -2)):
    m.add_decal("WarnBox", x, y)
for t in ((13, 0), (12, -1), (13, -2)):
    m.add_decal("DirtLight", *t)
for t in ((-4, 26), (-3, 26), (3, 26), (4, 26), (5, 26), (-5, 26), (3, -5), (1, -4), (2, -4), (-2, -4), (-1, -4)):
    m.add_decal("Bot", *t)
for t in ((-10, -11), (-9, -11), (-8, -11), (-7, -11), (-6, -11), (-5, -11), (-10, -9)):
    m.add_decal("WarnBox", *t)
for x in range(5, 10):
    m.add_decal("WarnLineN", x, -9)
m.add_decal("WarnBox", 8, -9)
for x, y in ((1, 1), (2, 1), (2, 0), (2, -2), (-2, 1)):
    m.add_decal("WarnFull", x, y)
for x, y in ((-7, -1), (-9, 1), (-5, -3), (-1, -6), (1, -7), (-7, -8), (-8, -7), (-14, -4), (-13, -3), (6, -2)):
    m.add_decal("DirtLight", x, y)
for x, y in ((-15, -4), (-7, -10)):
    m.add_decal("DirtMedium", x, y)
for x in (-7, -6, -5):
    m.add_decal("WoodTrimThinLineN", x, 21); m.add_decal("WoodTrimThinLineN", x, 11)
for x in range(4, 9):                                                 # clinic tiling
    for y in range(7, 11):
        m.add_decal("MiniTileCheckerAOverlay", x, y, color="#FFFFFF20")
for x in range(-3, 4):
    m.add_decal("MonoOverlay", x, 25, color="#A66BE030")
for y in range(13, 17):
    m.add_decal("MonoOverlay", 0, y, color="#A66BE040")
for t in ((-3, -7), (2, -7), (3, -7), (-1, -8), (1, -8), (-9, -10), (-6, -8), (-14, -3), (-12, -4), (5, -2),
          (8, 0), (-2, 0), (1, -1), (-5, -1), (-8, 1), (12, 4), (-21, 4), (-18, 4)):
    m.add_decal("DirtLight", *t)
for t in ((-2, -1), (0, -7), (-8, -8), (-16, -4)):
    m.add_decal("DirtMedium", *t)
for x in range(-2, 3):
    m.add_decal("WarnLineS", x, -2)
for t in ((-10, -6), (-9, -6), (7, 1), (8, 1), (9, 1), (9, -1), (9, -3), (-3, -5), (-3, -6)):
    m.add_decal("Bot", *t)


m.save(out)
print("saved", out, len(m.ents), "entities")
