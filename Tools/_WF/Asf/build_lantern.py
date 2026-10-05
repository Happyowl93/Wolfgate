"""ASF Lantern Post: blank grid -> finished base ship (a mobile POI with its own engines).

    python Tools/_WF/Asf/build_lantern.py Resources/Maps/_WF/Asf/lantern.yml

Hull: a slim 21-tile-wide ship, bow north. A pointed bow holds the bridge, with a fire-control ready room behind
      it. Outside the pressure hull: swept delta wings that continue the bow's taper, a clear waist for docking,
      two long engine nacelles and a central engine block carrying eight large thrusters. Walls are station-grade plastitanium
      (WallPlastitaniumOutpost and its diagonal/window variants, as on Hokkaido, Camelot and the Halcyon).
      10 energy guns and torpedo launchers sit on hull sponsons; a purple FS-421 station shield covers the hull.
Decks, bow to stern:
      bridge | ready room | envoy office, shipyard hall, quarters | post, wardrobe room, commons
      consular office, atrium, clinic (PUBLIC) | dock concourse (PUBLIC)
      farm, gyro room + power room, R&D lab | stores, atmos, anomaly observation + test chamber
Zones: PUBLIC = concourse, atrium, clinic, consular office. Everything else needs ASF access.
Style targets, from the peer stations: >= 6 objects and >= 9 decals per 10 tiles, rooms of 10-25 tiles,
      15+ floor types, purple trim in every finished room.
"""
import sys
sys.path.insert(0, r"C:/Users/alvar/.claude/skills/ss14-mapping")
from mapkit import REPO, new_grid_from, paint, route, lay_cable, lay_pipes, add_device, dir_to_q, info

out = sys.argv[1]
m = new_grid_from(REPO / "Resources/Maps/_NF/POI/courthouse.yml", name="ASF Lantern Post")
PURPLE = "#A66BE0FF"
LILAC = "#C79DD0FF"

# ---- 1. shell -------------------------------------------------------------------------------
X0, Y_TOP, Y_BOT, W = -16, 33, -15, 33          # canvas covers x -16..16
canvas = {y: [" "] * W for y in range(Y_BOT, Y_TOP + 1)}


def put(x, y, ch):
    canvas[y][x - X0] = ch


def fill(x0, y0, x1, y1, ch):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(x, y, ch)


# bow: diagonal hull at |x| = 33 - y, a wall ring inside it, bridge then ready room
for y in range(23, 33):
    e = 33 - y
    put(-e, y, "("), put(e, y, ")")
    if e > 1:
        fill(-e + 1, y, e - 1, y, "#")
put(0, 32, "#")
fill(-1, 31, 1, 31, "O")                          # canopy
for y in range(26, 31):                           # bridge, y 26..30
    e = 33 - y
    fill(-e + 2, y, e - 2, y, "b")
put(-4, 28, "O"), put(4, 28, "O")                 # bridge side windows
put(0, 25, "a")                                   # bridge door
for y in (23, 24):                                # ready room
    e = 33 - y
    fill(-e + 2, y, e - 2, y, "y")

# body: hull walls at x = +-10 from y 22 down to y -10
fill(-10, -10, 10, 22, "#")
rooms = {   # name: (x0, y0, x1, y1, floor char) - interiors
    "envoy": (-9, 18, -5, 21, "w"), "hall": (-3, 18, 3, 21, "h"), "quarters": (5, 18, 9, 21, "q"),
    "post": (-9, 14, -5, 16, "p"), "wardrobe": (-3, 14, 3, 16, "r"), "commons": (5, 14, 9, 16, "c"),
    "consular": (-9, 8, -5, 12, "o"), "atrium": (-3, 8, 3, 12, "g"), "clinic": (5, 8, 9, 12, "m"),
    "concourse": (-9, 4, 9, 6, "s"),
    "farm": (-9, -3, -5, 2, "f"), "gyro": (-3, 1, 3, 2, "t"), "power": (-3, -3, 3, -1, "T"), "lab": (5, -3, 9, 2, "l"),
    "stores": (-9, -9, -5, -5, "u"), "atmos": (-3, -9, 3, -5, "x"), "obs": (5, -6, 9, -5, "n"), "chamber": (5, -9, 9, -8, "z"),
}
for x0, y0, x1, y1, ch in rooms.values():
    fill(x0, y0, x1, y1, ch)
doors = {
    (0, 22): "a", (-4, 20): "C", (4, 20): "d", (0, 17): "a", (7, 17): "d", (-4, 15): "d", (4, 15): "d",
    (0, 13): "A", (-5, 13): "A", (-4, 10): "D", (4, 10): "D", (0, 7): "D", (-7, 7): "D", (7, 7): "D",
    (-7, 3): "a", (0, 3): "a", (7, 3): "a", (0, 0): "a", (-7, -4): "a", (0, -4): "a", (7, -4): "a", (5, -7): "a",
    (-10, 5): "W", (10, 5): "E",
}
for (x, y), ch in doors.items():
    put(x, y, ch)
# exterior: solid plastitanium outside the pressure hull, drawn for the east side and mirrored west
MIRROR = {")": "(", "}": "{", ">": "<"}


def both(x, y, ch):
    put(x, y, ch), put(-x, y, MIRROR.get(ch, ch))


def span(y, x1, edge=None):
    """Hull mass from x 11 out to x1 on row y, with an optional diagonal one tile further out."""
    for x in range(11, x1 + 1):
        both(x, y, "#")
    if edge:
        both(x1 + 1, y, edge)


# delta wings: the leading edge continues the bow taper, the trailing edge sweeps back in
for y, x1, edge in ((22, 10, ")"), (21, 11, ")"), (20, 12, ")"), (19, 13, ")"), (18, 14, ")"), (17, 15, None),
                    (16, 14, "}"), (15, 13, "}"), (14, 12, "}"), (13, 11, "}"), (12, 10, "}")):
    span(y, x1, edge)
# engine nacelles, clear of the docks at y 5
for y, x1, edge in ((0, 10, ")"), (-1, 11, ")"), (-2, 12, ")"), (-3, 13, ")")):
    span(y, x1, edge)
for y in range(-4, -13, -1):
    span(y, 14)
both(10, -10, "#")                                 # the stern corner now joins the nacelle
# central engine block under the stern
fill(-6, -11, 6, -11, "#")
fill(-5, -12, 5, -12, "#")
put(-6, -12, "{"), put(6, -12, "}")

for y in range(-10, 23):                           # hull windows wherever open space lies outside
    for side in (-1, 1):
        if canvas[y][10 * side - X0] == "#" and canvas[y][11 * side - X0] == " " and y not in (-10, 2, 3, 7, 8):
            if canvas[y - 1][10 * side - X0] in "#O" and canvas[y + 1][10 * side - X0] in "#O" and abs(y - 5) > 1:
                put(10 * side, y, "O")
for x in (6, 7, 8, 9):                            # plasma glass between observation and test chamber
    put(x, -7, "P")
# manoeuvring thrusters: forward ones on the wing leading edges, side ones on the wingtips and nacelles,
# small stern ones in the notches between the nacelles and the engine block
for x, y in ((12, 22), (13, 21)):
    both(x, y, "^")
for x, y in ((16, 16), (15, -10)):
    both(x, y, ">")
for x in (8, 9):
    both(x, -11, "v")

WALL = {"tile": "Plating", "ents": ["WallPlastitaniumOutpost"]}
WIN = {"tile": "Plating", "ents": ["Grille", "PlastitaniumWindowOutpost"]}


def dock(side):
    q = dir_to_q("S", side)
    return {"tile": "FloorSteelCheckerDark", "ents": [("AirlockGlassShuttle", q), ("AtmosDeviceFanDirectional", q)]}


legend = {
    "#": WALL, "O": WIN,
    "P": {"tile": "Plating", "ents": ["Grille", "ReinforcedPlasmaWindow"]},
    "b": {"tile": "FloorDarkMono"}, "y": {"tile": "FloorDarkDiagonal"}, "w": {"tile": "FloorWoodLarge"},
    "h": {"tile": "FloorDarkHerringbone"}, "q": {"tile": "FloorWood"}, "p": {"tile": "FloorSteelMono"},
    "r": {"tile": "FloorDarkPavement"}, "c": {"tile": "FloorWoodTile"}, "o": {"tile": "FloorCarpetOffice"},
    "g": {"tile": "FloorGrass"}, "m": {"tile": "FloorWhiteMono"}, "s": {"tile": "FloorSteel"},
    "f": {"tile": "FloorHydro"}, "t": {"tile": "FloorTechMaint"}, "T": {"tile": "FloorReinforced"},
    "l": {"tile": "FloorWhite"}, "u": {"tile": "FloorSteelDirty"}, "x": {"tile": "FloorTechMaintDark"},
    "n": {"tile": "FloorWhitePlastic"}, "z": {"tile": "FloorReinforcedHardened"},
    "D": {"tile": "FloorSteel", "ents": ["AirlockGlass"]},
    "A": {"tile": "FloorDark", "ents": ["WFAsfAirlockGlass"]},      # public -> private
    "a": {"tile": "FloorDark", "ents": ["WFAsfAirlock"]},
    "d": {"tile": "FloorWood", "ents": ["AirlockGlass"]},           # private -> private
    "C": {"tile": "FloorWood", "ents": ["WFAsfAirlockCommand"]},
    "W": dock("W"), "E": dock("E"),
}
for code, q in (("(", 0), (")", 3), ("{", 1), ("}", 2)):       # named by the open corner; solid half SE at q=0
    legend[code] = {"tile": "Plating", "ents": [("WallPlastitaniumDiagonalOutpost", q)]}
for code, side in (("^", "N"), ("v", "S"), ("<", "W"), (">", "E")):   # thrusters exhaust N at q=0
    legend[code] = {"tile": "Plating", "ents": [("WFAsfThruster", dir_to_q("N", side)), "Catwalk", "AtmosFixBlockerMarker"]}

rows = ["".join(canvas[y]) for y in range(Y_TOP, Y_BOT - 1, -1)]
paint(m, X0, Y_TOP, rows, legend)

# main engines: 2x2 large thrusters exhausting south; at q=2 each covers its anchor and the tiles west and north
for ax in (-13, -11, -4, -1, 2, 5, 12, 14):
    for x in (ax - 1, ax):
        for y in (-14, -13):
            m.set_tile(x, y, "Plating")
            m.add("Catwalk", x, y); m.add("AtmosFixBlockerMarker", x, y)
    m.add("WFAsfThrusterLarge", ax, -14, q=2)

# floor accents: a purple runner down the bridge and hall, wood boardwalk in the atrium, dock vestibules
for y in range(26, 31):
    m.set_tile(0, y, "FloorShuttlePurple")
for y in range(8, 13):
    m.set_tile(0, y, "FloorWood")
for x in range(-3, 4):
    m.set_tile(x, 10, "FloorWood")
for x in (-3, 3):
    for y in (8, 12):
        m.set_tile(x, y, "FloorAstroGrass")
for x in range(-9, 10):
    m.set_tile(x, 5, "FloorSteelPavement")
for x in (-9, 9):
    for y in (4, 6):
        m.set_tile(x, y, "FloorSteelCheckerDark")
for x in range(-3, 4):
    m.set_tile(x, 19, "FloorShuttlePurple")
m.set_tile(0, 15, "FloorShuttlePurple")
for x, y in ((-7, -2), (-7, 0)):
    m.set_tile(x, y, "FloorSteelDiagonal")
for x in range(5, 10):
    m.set_tile(x, -1, "FloorWhiteDiagonal")

# ---- helpers --------------------------------------------------------------------------------
DIR = {"N": (0, 1), "S": (0, -1), "E": (1, 0), "W": (-1, 0)}


def light(x, y, wall, proto="Poweredlight"):
    return m.add(proto, x, y, q=dir_to_q("N", wall))


def wm(proto, x, y, room_side):
    """Wallmount on a wall tile, facing the room on `room_side`."""
    return m.add(proto, x, y, q=dir_to_q("S", room_side))


def chair(proto, x, y, facing):
    return m.add(proto, x, y, q=dir_to_q("S", facing))


def console(proto, x, y, back, comps=None):
    return m.add(proto, x, y, q=dir_to_q("N", back), comps=comps)


def on_table(table, x, y, *items):
    m.add(table, x, y)
    for item in items:
        m.add(item, x, y)


def trim(name, color=PURPLE, base="BrickTileWhite"):
    """Edge trim around the inside of a room, with matching corners."""
    x0, y0, x1, y1 = rooms[name][:4] if isinstance(name, str) else name
    for x in range(x0 + 1, x1):
        m.add_decal(base + "LineN", x, y1, color=color)
        m.add_decal(base + "LineS", x, y0, color=color)
    for y in range(y0 + 1, y1):
        m.add_decal(base + "LineW", x0, y, color=color)
        m.add_decal(base + "LineE", x1, y, color=color)
    for x, y, c in ((x0, y0, "Sw"), (x1, y0, "Se"), (x0, y1, "Nw"), (x1, y1, "Ne")):
        m.add_decal(f"{base}Corner{c}", x, y, color=color)


# ---- 2. rooms -------------------------------------------------------------------------------
# Bridge (nose, y 26..30), door a from the ready room at (0, 25)
console("ComputerRadar", -1, 30, "N"); console("ComputerShuttle", 0, 30, "N"); console("ComputerGunneryConsole", 1, 30, "N")
chair("ChairOfficeDark", -1, 29, "N"); chair("ChairPilotSeat", 0, 29, "N"); chair("ChairOfficeDark", 1, 29, "N")
console("ComputerCrewMonitoring", -3, 28, "W"); console("ComputerStationRecords", 3, 28, "E")
console("ComputerComms", -4, 27, "W"); console("ComputerIFF", 4, 27, "E")
chair("ChairOfficeDark", -3, 27, "W"); chair("ChairOfficeDark", 3, 27, "E")
m.add("WFAsfBanner", -5, 26); m.add("WFAsfMarineBanner", 5, 26)
m.add("PottedPlantRandom", -2, 26); m.add("PottedPlantRandom", 2, 26)
on_table("TableReinforced", -4, 26, "PaperBin5"); on_table("TableReinforced", 4, 26, "Pen")

# Ready room (y 23..24): fire control for the hull guns and EVA gear, door a to the shipyard hall at (0, 22)
m.add("GunneryServerStation", -8, 23)
on_table("TableReinforced", -6, 24, "WeaponCapacitorRecharger"); on_table("TableReinforced", -5, 24, "WeaponCapacitorRecharger")
m.add("ClosetFireFilled", -7, 24); m.add("ClosetEmergencyFilledRandom", -7, 23)
for x in (-4, -3, 3, 4):
    m.add("SteelBench", x, 23)
for x in (5, 6, 7):
    m.add("SuitStorageEVA", x, 24)
m.add("ClosetToolFilled", 8, 23); m.add("WFAsfBanner", -1, 24); m.add("WFAsfBanner", 1, 24)
m.add("PottedPlantRandom", -6, 23)

# Envoy office (x -9..-5, y 18..21), door C to the hall at (-4, 20)
for x in (-8, -7, -6):
    m.add("TableWood", x, 20)
    for y in (19, 20):
        m.add("CarpetPurple", x, y)
m.add("PaperBin5", -8, 20); m.add("Pen", -6, 20); m.add("LampGold", -7, 20)
chair("ComfyChair", -7, 21, "S"); chair("Chair", -8, 19, "N"); chair("Chair", -6, 19, "N")
m.add("BookshelfFilled", -9, 21); m.add("BookshelfFilled", -9, 20); m.add("PottedPlantRandom", -5, 21)
m.add("WFAsfBanner", -9, 18); m.add("filingCabinetDrawerRandom", -5, 18)
m.add("WFAsfSpawnPointEnvoy", -8, 18)

# Shipyard hall (x -3..3, y 18..21), door a to the ready room at (0, 22), door a down at (0, 17)
console("WFAsfComputerShipyard", -2, 21, "N"); console("ComputerBankATM", 2, 21, "N")
m.add("WFAsfBanner", -3, 21); m.add("WFAsfMarineBanner", 3, 21)
on_table("TableReinforced", 3, 18, "AnomalyScanner"); m.add("TableReinforced", 3, 19)
chair("ChairOfficeDark", 2, 18, "E")
m.add("ClosetEmergencyFilledRandom", -3, 18); m.add("PottedPlantRandom", -2, 18)

# Quarters (x 5..9, y 18..21), door d from the hall at (4, 20), door d down to the commons at (7, 17)
for x in (6, 8):
    m.add("Bed", x, 21); m.add("BedsheetPurple", x, 21)
m.add("WardrobeMixedFilled", 9, 21); m.add("WardrobeMixedFilled", 9, 20)
m.add("CryogenicSleepUnit", 9, 18)
on_table("TableWood", 5, 18, "LampGold"); m.add("PottedPlantRandom", 5, 21); m.add("Dresser", 7, 21)
for x in (6, 7, 8):
    m.add("CarpetPurple", x, 19)
m.add("WFAsfSpawnPointEnforcer", 6, 19); m.add("WFAsfSpawnPointFieldResearcher", 7, 19)
m.add("WFAsfSpawnPointColonist", 8, 19)

# Post (x -9..-5, y 14..16): Enforcer desk, door d to the wardrobe room at (-4, 15), door A to the consular office
m.add("LockerSecurity", -9, 16); m.add("LockerSecurity", -8, 16)
on_table("TableReinforced", -6, 16, "WeaponCapacitorRecharger"); chair("ChairOfficeDark", -6, 15, "N")
console("ComputerCrewMonitoring", -9, 14, "W"); m.add("CrateGenericSteel", -8, 14)
m.add("PottedPlantRandom", -9, 15)

# Wardrobe room (x -3..3, y 14..16): the ASF drobe and lockers, purple runner between the doors
console("WFAsfDrobe", -3, 16, "N"); console("WFAsfDrobe", 3, 16, "N")
m.add("WardrobeMixedFilled", -3, 14); m.add("WardrobeMixedFilled", 3, 14)
m.add("ClosetEmergencyFilledRandom", -2, 16); m.add("PottedPlantRandom", 2, 16)
m.add("SteelBench", -2, 14); m.add("SteelBench", 2, 14)

# Commons (x 5..9, y 14..16), door d to the wardrobe room at (4, 15), door d up to the quarters at (7, 17)
m.add("TableWood", 8, 15); m.add("TableWood", 9, 15)
chair("Chair", 8, 16, "S"); chair("Chair", 9, 16, "S"); chair("Chair", 8, 14, "N"); chair("Chair", 9, 14, "N")
on_table("TableWood", 5, 16, "KitchenMicrowave"); m.add("WaterCooler", 6, 16)
console("VendingMachineCoffee", 5, 14, "W"); m.add("PottedPlantRandom", 6, 14)

# Consular office (x -9..-5, y 8..12): public reception, doors D at (-4, 10) and (-7, 7)
for x in (-8, -7, -6):
    m.add("TableReinforced", x, 11)
m.add("PaperBin5", -8, 11); m.add("Pen", -6, 11); m.add("LampGold", -7, 11)
chair("ChairOfficeDark", -7, 12, "S"); chair("Chair", -8, 10, "N"); chair("Chair", -6, 10, "N")
m.add("BookshelfFilled", -9, 12); m.add("filingCabinetDrawerRandom", -6, 12)
m.add("WaterCooler", -9, 9); m.add("PottedPlantRandom", -9, 8); m.add("PottedPlantRandom", -5, 8)
m.add("WFAsfBanner", -9, 11)
for x in (-8, -6):
    m.add("SteelBench", x, 8)

# Atrium (x -3..3, y 8..12): public garden around a wood boardwalk cross
for x, y in ((-3, 12), (-2, 12), (-3, 11)):
    m.add("FloorWaterEntity", x, y)
m.add("FloraTree", 2, 12, offset=(0.5, 0.2)); m.add("FloraTree", -2, 8, offset=(0.5, 0.2))
m.add("SteelBench", -1, 11); m.add("SteelBench", 1, 9); m.add("SteelBench", -2, 9); m.add("SteelBench", 2, 11)
m.add("PoweredLightPostSmall", 3, 11); m.add("PoweredLightPostSmall", -3, 9)
m.add("WFAsfBanner", 3, 8); m.add("WFAsfBanner", -3, 10)
flowers = ["Flowerspv1", "Flowerspv2", "Flowerspv3", "Flowersy1", "Flowersy2", "Flowersy3", "Flowersy4",
           "Flowersbr1", "Flowersbr3", "Bushb1", "Grassa1", "Grassb2"]
grass = [(x, y) for x in range(-3, 4) for y in range(8, 13) if x != 0 and y != 10 and not m.at(x, y)]
for i, (x, y) in enumerate(grass):
    m.add_decal(flowers[i % len(flowers)], x, y)

# Clinic (x 5..9, y 8..12), doors D at (4, 10) and (7, 7)
for x in (6, 8):
    m.add("MedicalBed", x, 12); m.add("BedsheetMedical", x, 12)
console("VendingMachineMedical", 9, 12, "E"); m.add("LockerMedicineFilled", 9, 11)
on_table("TableGlass", 9, 9, "MedkitFilled"); on_table("TableGlass", 9, 8, "HandheldHealthAnalyzer")
chair("Chair", 5, 12, "S"); chair("Chair", 5, 11, "E"); m.add("PottedPlantRandom", 5, 8)
m.add("StasisBed", 7, 12)

# Concourse (x -9..9, y 4..6): docks at (+-10, 5), doors up at y 7 and down at y 3
for x in (-5, -4, 4, 5):
    m.add("SteelBench", x, 6)
for x in (-3, 3):
    m.add("SteelBench", x, 4)
console("VendingMachineCoffee", -9, 6, "N"); console("VendingMachineCola", 9, 6, "N")
console("VendingMachineSnack", 9, 4, "S"); m.add("ClosetEmergencyFilledRandom", -9, 4)
for x, y in ((-2, 6), (2, 6), (-5, 4), (5, 4)):
    m.add("PottedPlantRandom", x, y)
m.add("WFAsfBanner", -1, 6); m.add("WFAsfBanner", 1, 6)
m.add("WarpPoint", 0, 5, comps=[{"type": "WarpPoint", "location": "ASF Lantern Post"}])
m.add("DefaultStationBeaconArrivals", -1, 5)

# Farm (x -9..-5, y -3..2): trays either side of a path at x -7, doors at (-7, 3) and (-7, -4)
for x in (-9, -8, -6, -5):
    for y in (1, -1):
        m.add("hydroponicsTray", x, y)
m.add("LockerBotanistFilled", -9, 2); console("VendingMachineSeeds", -5, 2, "N")
m.add("WaterTankFull", -9, -3); m.add("SeedExtractor", -8, -3); m.add("Biogenerator", -5, -3)
on_table("TableWood", -6, -3, "HydroponicsToolSpade", "HydroponicsToolMiniHoe", "HydroponicsToolClippers")
m.add("PottedPlantRandom", -9, 0); m.add("PottedPlantRandom", -5, 0)

# Gyro room (x -3..3, y 1..2), door a from the concourse at (0, 3), door a to the power room at (0, 0)
for x in (1, 2, 3):
    m.add("Gyroscope", x, 2)
m.add("GravityGeneratorMini", 3, 1)
m.add("StationAnchorOff", -2, 1)
m.add("ClosetToolFilled", -3, 2)

# Power room (x -3..3, y -3..-1): RTGs feed the substation; the station shield draws from here
gens = [m.add("GeneratorRTG", x, -3) for x in (-3, -2, -1, 1, 2, 3)] + [m.add("GeneratorRTG", x, -2) for x in (-2, 2)]
sub = m.add("SubstationBasic", -3, -1)
m.add("WFAsfShieldGenerator", 3, -1)
m.add("ClosetFireFilled", -2, -1); m.add("ClosetRadiationSuitFilled", 2, -1)

# R&D lab (x 5..9, y -3..2), door a from the concourse at (7, 3), door a to the observation room at (7, -4)
console("ComputerResearchAndDevelopment", 6, 2, "N"); chair("ChairOfficeLight", 6, 1, "N")
console("Protolathe", 8, 2, "N"); console("WFAsfLathe", 9, 2, "N")       # researched ASF vouchers
m.add("WFAsfResearchServer", 9, -3)
on_table("TableReinforced", 5, -1, "AnomalyScanner"); on_table("TableReinforced", 5, -2, "HandheldHealthAnalyzer")
m.add("LockerScienceFilled", 5, -3); m.add("LockerScienceFilled", 6, -3)
console("CircuitImprinter", 9, 0, "E"); m.add("PottedPlantRandom", 9, 1)

# Stores (x -9..-5, y -9..-5), door a from the farm at (-7, -4)
for x, y in ((-9, -9), (-8, -9), (-9, -8), (-5, -9), (-6, -9)):
    m.add("CrateGenericSteel", x, y)
m.add("ClosetMaintenanceFilledRandom", -9, -5); m.add("ClosetMaintenanceFilledRandom", -5, -5)
m.add("CrateEngineeringCableBulk", -5, -8)

# Atmos (x -3..3, y -9..-5): air supply, door a from the power room at (0, -4)
for x, y in ((-3, -9), (-2, -9), (-1, -9)):
    m.add("AirCanister", x, y)
m.add("NitrogenCanister", 3, -9); m.add("OxygenCanister", 2, -9)
m.add("ClosetFireFilled", -3, -5); m.add("ClosetEmergencyFilledRandom", 3, -5)

# Anomaly testing: the observation room (y -6..-5) looks through plasma glass into the test chamber (y -9..-8)
analyzer = m.add("MachineArtifactAnalyzer", 8, -9)
console("ComputerAnalysisConsole", 8, -5, "N", comps=[
    {"type": "DeviceLinkSource", "linkedPorts": {analyzer.uid: [["ArtifactAnalyzerSender", "ArtifactAnalyzerReceiver"]]}}])
chair("ChairOfficeLight", 8, -6, "N")
m.add("MachineAnomalyVessel", 9, -5)
on_table("TableGlass", 5, -5, "AnomalyScanner")
m.add("MachineAPE", 6, -9, q=dir_to_q("N", "E"))
m.add("FloorDrain", 7, -8)

# ---- 3. hull guns: sponsons that touch the hull so the grid stays whole --------------------
GUNS = [
    ("WeaponLaserTurretApollo", "HardpointEnergyHeavy", [(-10, 24, "N"), (10, 24, "N")]),
    ("WeaponLaserTurretPrometheus", "HardpointEnergyMedium", [(-16, 17, "W"), (16, 17, "E"), (-12, 0, "N"), (12, 0, "N")]),
    ("WeaponTurretSerpentMissile", "HardpointMissileMedium", [(-15, -7, "W"), (15, -7, "E")]),
    ("WeaponLaserTurretL1Phalanx", "HardpointEnergyLight", [(-11, 10, "W"), (11, 10, "E")]),
]
for gun, hardpoint, spots in GUNS:
    for x, y, facing in spots:
        m.set_tile(x, y, "Plating")
        m.add("AtmosFixBlockerMarker", x, y)
        m.add(hardpoint, x, y, q=dir_to_q("S", facing))      # a turret's barrel points S at rotation 0
        m.add(gun, x, y, q=dir_to_q("S", facing))

# ---- 4. lights ------------------------------------------------------------------------------
lights = [
    (-4, 27, "W"), (4, 27, "E"), (-2, 29, "W"), (2, 29, "E"),          # bridge
    (-6, 24, "N"), (6, 24, "N"), (-2, 23, "S"), (2, 23, "S"),          # ready room
    (-9, 19, "W"), (-6, 21, "N"),                                      # envoy office
    (-1, 21, "N"), (1, 21, "N"), (-2, 18, "S"), (1, 18, "S"),          # shipyard hall
    (5, 20, "W"), (8, 18, "S"),                                        # quarters
    (-7, 16, "N"), (-5, 14, "S"),                                      # post
    (-1, 16, "N"), (1, 14, "S"),                                       # wardrobe room
    (7, 16, "N"), (6, 15, "W"),                                        # commons
    (-8, 12, "N"), (-5, 9, "E"), (-9, 10, "W"),                        # consular office
    (9, 10, "E"), (7, 12, "N"), (6, 8, "S"),                           # clinic
    (-7, 6, "N"), (-1, 6, "N"), (3, 6, "N"), (7, 6, "N"),              # concourse north
    (-6, 4, "S"), (-1, 4, "S"), (2, 4, "S"), (6, 4, "S"),              # concourse south
    (-9, 1, "W"), (-5, -2, "E"), (-7, 2, "N"),                         # farm
    (-1, 2, "N"), (0, -1, "N"), (-1, -3, "S"),                         # gyro and power rooms
    (7, 2, "N"), (9, -1, "E"), (7, -3, "S"),                           # lab
    (-7, -9, "S"), (-9, -6, "W"),                                      # stores
    (0, -5, "N"), (0, -9, "S"),                                        # atmos
    (6, -6, "W"), (9, -8, "E"), (7, -9, "S"),                          # anomaly observation and chamber
]
for x, y, wall in lights:
    light(x, y, wall)
for x, y, wall in ((0, 26, "S"), (-3, 24, "N"), (0, 18, "S"), (0, 14, "S"), (-7, 8, "S"), (0, 4, "S"),
                   (-7, -3, "S"), (2, 1, "S"), (5, 0, "W"), (-3, -7, "W"), (5, -6, "W")):
    light(x, y, wall, "EmergencyLight")

# ---- 5. power: RTGs -> HV -> substation -> MV -> APCs -> LV ----------------------------------
lay_cable(m, "HV", [(x, -3) for x in range(-3, 4)] + [(-3, -2), (-3, -1), (-2, -2), (2, -2)])
apcs = [wm("APCBasic", -9, 23, "E"),      # bridge, ready room, forward engines
        wm("APCBasic", -4, 19, "E"),      # north block
        wm("APCBasic", -4, 14, "E"),      # post, wardrobe, commons
        wm("APCBasic", -4, 9, "E"),       # consular office, atrium, clinic
        wm("APCBasic", -2, 3, "N"),       # concourse
        wm("APCBasic", -4, 1, "E"),       # farm, gyro room, power room
        wm("APCBasic", 4, 1, "E"),        # lab
        wm("APCBasic", -4, -7, "E"),      # stores, atmos
        wm("APCBasic", 4, -6, "E")]       # anomaly rooms, aft guns


def tree(roots, targets, avoid=lambda t: False):
    net = {tuple(r) for r in roots}
    todo = [tuple(t) for t in targets if tuple(t) not in net]
    while todo:
        t = min(todo, key=lambda t: min(abs(t[0] - n[0]) + abs(t[1] - n[1]) for n in net))
        todo.remove(t)
        near = min(net, key=lambda n: abs(t[0] - n[0]) + abs(t[1] - n[1]))
        net |= set(route(m, near, t, floor_only=False, avoid=avoid))
    return net


lay_cable(m, "MV", tree([sub.tile], [a.tile for a in apcs]))

# ---- 6. atmos -------------------------------------------------------------------------------
# (vent tile, face), (scrubber tile, face) per room
rooms_air = {
    "bridge": ((-1, 27, "E"), (1, 27, "W")), "ready": ((-2, 24, "E"), (2, 24, "W")),
    "envoy": ((-9, 19, "E"), (-5, 19, "W")), "hall": ((-1, 20, "E"), (1, 20, "W")),
    "quarters": ((5, 19, "N"), (9, 19, "W")), "post": ((-7, 14, "E"), (-5, 16, "W")),
    "wardrobe": ((-1, 15, "E"), (1, 15, "W")), "commons": ((7, 14, "E"), (6, 15, "E")),
    "consular": ((-9, 10, "E"), (-5, 11, "W")), "atrium": ((1, 12, "W"), (-1, 8, "E")),
    "clinic": ((7, 9, "E"), (6, 10, "E")), "concourse_w": ((-6, 5, "E"), (-8, 5, "E")),
    "concourse_e": ((6, 5, "W"), (8, 5, "W")), "farm": ((-7, 1, "S"), (-7, -1, "N")),
    "gyro": ((-1, 2, "E"), (1, 1, "W")), "power": ((-1, -2, "E"), (1, -2, "W")),
    "lab": ((7, 0, "E"), (8, -2, "W")), "stores": ((-7, -6, "E"), (-7, -8, "E")),
    "atmos": ((-1, -6, "E"), (1, -6, "W")), "obs": ((7, -6, "E"), (6, -6, "E")),
    "chamber": ((5, -8, "E"), (9, -8, "W")),
}
vents, scrubs = {}, {}
for name, (v, s) in rooms_air.items():
    vents[name] = add_device(m, "GasVentPump", v[0], v[1], face=v[2], color="distro")
    scrubs[name] = add_device(m, "GasVentScrubber", s[0], s[1], face=s[2], layer=1, color="waste")
face = lambda t: (t[0] + DIR[t[2]][0], t[1] + DIR[t[2]][1])

# supply: air canister on a port -> pump -> distro
add_device(m, "GasPort", -3, -8, face="S", color="distro")
add_device(m, "GasPressurePumpOn", -3, -7, face="N", color="distro")
vent_tiles = {(v[0], v[1]) for v, _ in rooms_air.values()} | {(-3, -8), (-3, -7)}
lay_pipes(m, tree([(-3, -6)], [face(v) for v, _ in rooms_air.values()], avoid=lambda t: t in vent_tiles),
          layer=0, color="distro")

# waste: scrubbers -> passive vent between the main engines, under the engine block
m.set_tile(0, -13, "Plating")
m.add("Catwalk", 0, -13); m.add("AtmosFixBlockerMarker", 0, -13)
add_device(m, "GasPassiveVent", 0, -13, face="N", layer=1, color="waste")
scrub_tiles = {(s[0], s[1]) for _, s in rooms_air.values()} | {(0, -13)}
lay_pipes(m, tree([(0, -12)], [face(s) for _, s in rooms_air.values()], avoid=lambda t: t in scrub_tiles),
          layer=1, color="waste")

alarms = {
    "bow": (wm("AirAlarm", 9, 23, "W"), ["bridge", "ready"]),
    "north": (wm("AirAlarm", 4, 18, "W"), ["envoy", "hall", "quarters"]),
    "mid": (wm("AirAlarm", 4, 16, "E"), ["post", "wardrobe", "commons"]),
    "public": (wm("AirAlarm", 4, 12, "W"), ["consular", "atrium", "clinic", "concourse_w", "concourse_e"]),
    "aft": (wm("AirAlarm", 2, 0, "N"), ["farm", "gyro", "power", "lab"]),
    "stern": (wm("AirAlarm", 2, -4, "S"), ["stores", "atmos", "obs", "chamber"]),
}
for alarm, names in alarms.values():
    m.link_alarm(alarm, [vents[n] for n in names] + [scrubs[n] for n in names])

for x, y, side in ((-2, 22, "S"), (-2, 17, "S"), (-2, 13, "N"), (2, 7, "S"), (-2, 3, "S"), (-2, -4, "S")):
    wm("FireAlarm", x, y, side)
for x, y, side in ((-4, 21, "E"), (-4, 16, "E"), (4, 8, "E"), (-4, 5, "S"), (4, -2, "E"), (-4, -5, "E"), (2, 25, "S")):
    wm("ExtinguisherCabinetFilled", x, y, side)
for x, y in ((0, 13), (0, 7), (-7, 7), (7, 7), (0, 3)):
    m.add("FirelockGlass", x, y)
wm("DefibrillatorCabinetFilled", 4, 11, "E"); wm("SignMedical", 4, 9, "W")
wm("SignShipDock", -10, 6, "E"); wm("SignShipDock", 10, 6, "W")
wm("SignEngineering", 1, 3, "N"); wm("SignScience", 6, 3, "N"); wm("SignHydro1", -6, 3, "N"); wm("SignAnomaly2", 6, -4, "S")

# ---- 6b. dressing: small furniture, desk items and wall art, never on a door approach or a vent ------------
reserved = {(x + dx, y + dy) for x, y in doors for dx, dy in DIR.values()} | {(-9, 5), (9, 5)}
reserved |= {(v[0], v[1]) for v, _ in rooms_air.values()} | {(s[0], s[1]) for _, s in rooms_air.values()}
BLOCKING = {"object", "power", "wall", "window", "door", "diagwall"}


def place(proto, x, y, q=0):
    """Floor clutter that never lands on a door approach, a vent or another solid object."""
    if (x, y) in reserved or any(info(e.proto).category() in BLOCKING for e in m.at(x, y)):
        print(f"  skip {proto} at {(x, y)}")
        return None
    return m.add(proto, x, y, q=q)


def wall_art(proto, x, y, room_side):
    """Wall decoration on a bare wall tile."""
    if any(info(e.proto).category() not in ("wall", "cable", "pipe") for e in m.at(x, y)):
        print(f"  skip {proto} on wall {(x, y)}")
        return None
    return wm(proto, x, y, room_side)


for item, x, y in (("DrinkMug", -4, 26), ("BoxFolderBlue", 4, 26), ("DrinkMugBlue", -7, 20), ("BoxFolderBlue", -8, 20),
                   ("DrinkMug", -8, 11), ("BoxFolderClipboard", -6, 11), ("BoxFolderBlue", -6, 16), ("DrinkMug", 5, 18),
                   ("ToolboxEmergencyFilled", -5, 24), ("Beaker", 5, -1), ("LargeBeaker", 5, -2), ("ResearchDisk", 5, -5),
                   ("PlushieMoth", 8, 21), ("FoodTinPeaches", 8, 15), ("DrinkMug", 9, 15), ("BoxLightMixed", -9, 4)):
    m.add(item, x, y)
for proto, x, y in (("Rack", 6, 23), ("PottedPlantRandom", 5, 23), ("SteelBench", -1, 18), ("ComputerBankATM", -6, 6),
                    ("VendingMachineDiscount", 8, 6), ("PottedPlantRandom", -8, 6), ("PottedPlantRandom", 8, 4),
                    ("SteelBench", -8, 4), ("SinkWide", 5, 9), ("hydroponicsTray", -9, -2), ("hydroponicsTray", -5, -2),
                    ("ShelfMetal", 9, -2), ("Rack", -8, -5), ("Rack", -6, -5), ("CrateGenericSteel", -6, -8),
                    ("PortableScrubber", 3, -8), ("WeldingFuelTankFull", 3, -6), ("StorageCanister", 1, -9),
                    ("LockerScienceFilled", 9, -6), ("TableGlass", 6, -5), ("FloraTree", 2, 8), ("ChairWood", 7, 15)):
    place(proto, x, y)
m.add("ToolboxEmergencyFilled", 6, 23); m.add("SheetSteel", -8, -5); m.add("SheetGlass", -6, -5)
for item, x, y in (("Bucket", -6, -3), ("Bucket", -8, -3), ("Beaker", 9, -2), ("LargeBeaker", 9, -2), ("Pen", 3, 19),
                   ("PaperBin5", 3, 19), ("DrinkMug", -5, 24), ("DrinkMugBlue", 9, 9), ("BoxFolderBlue", 9, 8),
                   ("FoodTinPeaches", -8, -5), ("FoodTinPeaches", -6, -5), ("CableHVStack", -3, -2), ("DrinkMug", 4, 26),
                   ("PlushieLizard", 6, 21), ("Lamp", -6, 16), ("BoxFolderClipboard", 3, 18), ("ResearchDisk", 6, -5),
                   ("Beaker", 6, -5), ("DrinkMugBlue", 8, 15), ("Pen", -6, 16)):
    m.add(item, x, y)
for proto, x, y in (("ChairFolding", -6, 4), ("ChairFolding", 6, 4), ("PottedPlantRandom", -3, 23), ("PottedPlantRandom", -9, -1),
                    ("PottedPlantRandom", 9, 14), ("SpaceHeaterAnchored", -1, -8), ("ChairWood", -7, 19)):
    place(proto, x, y)
for proto, x, y, side in (("RandomPainting", -7, 22, "S"), ("RandomPainting", -4, 18, "E"), ("Mirror", 1, 17, "S"),
                          ("Mirror", 1, 13, "N"), ("RandomPainting", -7, 17, "S"), ("WallmountTelevision", 10, 15, "W"),
                          ("RandomPainting", -10, 12, "E"), ("LockerWallMedicalFilled", 10, 12, "W"),
                          ("RandomPainting", -5, 7, "S"), ("RandomPainting", 5, 7, "S"), ("RandomPainting", 4, 6, "S"),
                          ("RandomPainting", -9, 3, "N"), ("RandomPainting", 9, 3, "N"), ("RandomPainting", 2, 22, "N"),
                          ("ClosetWallEmergencyFilledRandom", -10, -6, "E"), ("ClosetWallFireFilledRandom", 10, -2, "W"),
                          ("SignDirectionalMed", 4, 7, "S"), ("RandomPainting", -4, -8, "W"), ("StationMap", 3, 13, "S")):
    wall_art(proto, x, y, side)

# LV last, so every receiver placed above is wired
receivers = [e for e in m.on_grid() if info(e.proto).receiver]
lay_cable(m, "LV", tree([a.tile for a in apcs], [r.tile for r in receivers]))

# ---- 7. decals ------------------------------------------------------------------------------
for name in ("envoy", "hall", "quarters", "post", "wardrobe", "commons", "consular", "clinic", "lab", "obs"):
    trim(name)
trim("concourse")
trim((-6, 23, 6, 24), color=LILAC, base="BrickTileDark")
trim("gyro", color="#FFFFFFFF", base="BrickTileDark"); trim("power", color="#FFFFFFFF", base="BrickTileDark")
trim("atmos", color="#52B4E9FF", base="BrickTileDark"); trim("stores", color="#FFFFFFFF", base="BrickTileSteel")
trim("chamber", color="#DE3A3AFF", base="BrickTileDark")
for y in range(26, 31):                                              # bridge edge lines
    e = 33 - y - 2
    m.add_decal("BrickTileDarkLineW", -e, y, color=PURPLE); m.add_decal("BrickTileDarkLineE", e, y, color=PURPLE)
for x in range(-3, 4):                                               # warnings ahead of the RTG bank
    m.add_decal("WarnLineN", x, -3)
for x, y in ((-8, 23), (5, 24), (6, 24), (7, 24), (-3, -1), (3, -1), (9, -3), (9, -5), (8, -9), (6, -9)):
    m.add_decal("Bot", x, y)
for x, y in ((-9, -9), (-8, -9), (-9, -8), (-5, -9), (-6, -9), (-5, -8)):
    m.add_decal("Box", x, y)
for x in (-9, 9):
    m.add_decal("StandClear", x, 5)
for x in (-8, -7, -6, 6, 7, 8):
    m.add_decal("ArrowsGreyscale", x, 5, color=LILAC, q=dir_to_q("N", "W" if x < 0 else "E"))
for x, y in ((-7, -1), (-7, 1), (-6, 2), (-8, -2), (-1, -2), (1, -1), (0, -6), (-2, -7), (2, -8), (-7, -7), (-6, -6)):
    m.add_decal("DirtLight", x, y)
for x in range(-2, 3):
    m.add_decal("WarnLineS", x, 1)
for x in range(5, 10):
    m.add_decal("WarnLineN", x, -8)
for y in range(18, 22):
    m.add_decal("MonoOverlay", 0, y, color="#A66BE040")
m.add_decal("Delivery", -7, -9); m.add_decal("Delivery", -7, -8)
m.add_decal("WarnBox", 7, -8)
for x in range(-8, 9):                                               # concourse runner and clinic tiling
    m.add_decal("MonoOverlay", x, 5, color="#C79DD030")
for x in range(5, 10):
    for y in range(8, 13):
        m.add_decal("MiniTileCheckerAOverlay", x, y, color="#FFFFFF20")
for x in range(-3, 4):
    m.add_decal("MonoOverlay", x, 19, color="#A66BE030")
for x, y in ((1, 2), (2, 2), (3, 2), (3, 1), (-2, 1), (-3, -1), (3, -1)):
    m.add_decal("WarnFull", x, y)
for x, y in ((-3, -9), (-2, -9), (-1, -9), (2, -9), (3, -9), (3, -8), (1, -9)):
    m.add_decal("WarnBox", x, y)
for y in range(-3, 3):
    m.add_decal("Grassa1" if y % 2 else "Grassb2", -7, y)
for x in (-8, -7, -6):
    m.add_decal("WoodTrimThinLineN", x, 21); m.add_decal("WoodTrimThinLineN", x, 12)
for x in (5, 6, 7):
    m.add_decal("WarnLineS", x, 23)
for x, y in ((-5, 26), (5, 26), (-3, 28), (3, 28), (-4, 27), (4, 27)):
    m.add_decal("QuarterTileOverlayGreyscale", x, y, color=PURPLE)
m.add_decal("LoadingArea", -7, -6)
for x, y in ((-8, -7), (-6, -7), (-5, -6), (-9, -7)):
    m.add_decal("DirtMedium", x, y)

m.save(out)
print("saved", out, len(m.ents), "entities")
