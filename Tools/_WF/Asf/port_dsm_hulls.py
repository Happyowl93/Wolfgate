"""Adapts DSM hulls from a Hullrot: Eclipsion checkout into ASF vessels (needs the ss14-mapping kit).

    python Tools/_WF/Asf/port_dsm_hulls.py <eclipsion checkout>

Prototypes Wolfgate lacks are swapped for local equivalents or dropped; the layouts are otherwise untouched.
Every DSM mount listed in LOADOUT is re-armed with a local laser or self-loading torpedo launcher on a matching
hardpoint; other mounts and their DSM guns are removed. Armed hulls get a gunnery server sized to their guns,
and the hulls in SHIELDS get a shield generator.

The committed hulls were fixed by hand afterwards (diagonal corners, cable pruning, air supply; see
Docs/_WF/Asf/README.md), so re-running this overwrites those fixes.
"""
import sys
from pathlib import Path

sys.path.insert(0, r"C:/Users/alvar/.claude/skills/ss14-mapping")
from mapkit import GridMap, protos, route, lay_cable  # noqa: E402

SRC = Path(sys.argv[1]) / "Resources/Maps/_Crescent/Shuttles/DSM"
OUT = Path("Resources/SharedMaps/_WF/Asf")

# source hull -> (file, name)
HULLS = {
    "peasant": ("gleaner", "ASF Gleaner"),
    "impel": ("porter", "ASF Porter"),
    "gnosis": ("lamplighter", "ASF Lamplighter"),
    "redemptor": ("vigil", "ASF Vigil"),
    "imparator": ("bulwark", "ASF Bulwark"),
    "dragoon": ("guardian", "ASF Guardian"),
    "praxis": ("lodestar", "ASF Lodestar"),
}

SWAP = {
    "ThrusterDSMFighter": "WFAsfThruster", "ThrusterDSMWarship": "WFAsfThruster",
    "WallReinforcedDSM": "WallReinforced", "WallShuttleDSM": "WallShuttle", "WallPlastitaniumDSM": "WallPlastitanium",
    "BoriaticGeneratorHerculesShuttle": "PortableGeneratorSuperPacmanShuttle",
    "BoriaticGeneratorEinsteinShuttle": "PortableGeneratorSuperPacmanShuttle",
    "C_ComputerShuttle": "ComputerShuttle", "C_ComputerTabletopShuttle": "ComputerTabletopShuttle",
    "ComputerTabletopCommsShip": "ComputerTabletopComms", "ComputerTabletopTargeting": "ComputerTabletopGunneryConsole",
    "AirlockMaintHorizontal": "AirlockMaint", "AirlockCommandHorizontal": "AirlockCommand",
    "AirlockCommandLockedDSMHorizontalScribe": "WFAsfAirlock", "AirlockCommandLockedDSM": "WFAsfAirlock",
    "AirlockCommandLockedDSMHorizontal": "WFAsfAirlock",
    "PoweredSmallLightMaintenanceRed": "PoweredSmallLight",
    "OreMagnet": "MachineOreMagnet", "ShuttleGunExhumer": "ShuttleGunKinetic",
    "ClothingHeadHelmetImperialEVA": "WFAsfClothingHeadEVA", "ClothingOuterCoatImperialSoftsuit": "WFAsfClothingOuterEVA",
    "ClothingHandsGlovesImperialLonggloves": "WFAsfClothingHandsGloves",
    "ClothingShoesBootsImperialJackboots": "WFAsfClothingShoesBoots",
    "ClothingUniformJumpsuitImperialCombat": "WFAsfClothingUniformService",
    "N14TableMetalGrate": "TableReinforced", "N14BedWood": "Bed", "N14BedWoodBunk": "Bed",
    "N14ChairArmchair": "ComfyChair", "N14ChairWood1": "ChairWood",
    "AirlockScienceHorizontal": "AirlockScience", "WallShuttleDiagonalCurved": "WallShuttleDiagonal",
    "WallShuttleDiagonalHollow": "WallShuttleDiagonal", "MedkitBloodFilled": "MedkitFilled",
    "ClothingShoesBootsMagEng": "ClothingShoesBootsMag", "ClothingMaskImperialCombatGasmask": "ClothingMaskGas",
    "ClothingOuterHardsuitImperialWorker": "WFAsfClothingOuterEVA",
    "ClothingHandsGlovesImperialWorkerGloves": "WFAsfClothingHandsGloves",
}
for wall in ("Solid", "Plastitanium", "Reinforced"):
    for kind in ("Curved", "Hollow"):
        SWAP[f"Wall{wall}Diagonal{kind}"] = f"Wall{wall}Diagonal"
for corner in ("Northeast", "Northwest", "Southeast", "Southwest"):
    for kind in ("Curved", "Hollow"):
        SWAP[f"WallShuttleDiagonal{corner}{kind}"] = "WallShuttleDiagonal"

# ASF doctrine: lasers first, a few self-loading torpedoes, good shields. Loadouts were sized against other
# factions' hulls of similar tile count (hardpoint weight: light 1, medium 2, heavy 4).
PHALANX = ("WeaponLaserTurretL1Phalanx", "HardpointEnergyLight")
PROMETHEUS = ("WeaponLaserTurretPrometheus", "HardpointEnergyMedium")
APOLLO = ("WeaponLaserTurretApollo", "HardpointEnergyHeavy")
SERPENT = ("WeaponTurretSerpentMissile", "HardpointMissileMedium")
# hull -> {DSM mount tile: (gun, hardpoint)}; a DSM mount not listed here is removed with its gun
LOADOUT = {
    "vigil": {(-2, 5): PHALANX, (0, 5): PROMETHEUS},
    "bulwark": {(-3, 2): PROMETHEUS, (0, 2): SERPENT},
    "guardian": {(8, 13): APOLLO, (1, 7): PROMETHEUS, (15, 7): PROMETHEUS, (0, 2): SERPENT, (16, 2): SERPENT},
    "lodestar": {(-8, 13): APOLLO, (8, 13): APOLLO, (-9, 12): PROMETHEUS, (9, 12): PROMETHEUS,
                 (-4, 13): PROMETHEUS, (4, 13): PROMETHEUS, (-5, 13): SERPENT, (5, 13): SERPENT,
                 (-10, -7): PHALANX, (10, -7): PHALANX},
}
DSM_GUN = ("WeaponTurret", "BaseWeaponTurret", "ShuttleGun")
# fire-control cost per gun class (FireControlSystem) and the server tiers that cover it
GUN_COST = {"Superlight": 1, "Light": 3, "Medium": 6, "Heavy": 9, "Superheavy": 12}
SERVERS = [("GunneryServerLow", 24), ("GunneryServerMedium", 42), ("GunneryServerHigh", 60),
           ("GunneryServerUltra", 90)]
# hull -> (shield generator, tile or None for the free floor nearest the power generator)
SHIELDS = {"bulwark": ("ShieldGeneratorSmall", None), "guardian": ("ShieldGeneratorSmall", None),
           "lodestar": ("ShieldGeneratorMedium", None)}

# Components that exist only in Eclipsion; the engine logs an error for each one it meets.
STRIP = {"VesselIcon", "SelfDeleteGrid", "NamedModules", "VesselDesignation", "GridSplitNode", "ChunkContainer",
         "ActiveDeviceLinkSink"}
PASSABLE = ("cable", "pipe", "light", "catwalk", "marker")
# Armed ASF hulls are a step tougher than their DSM originals: plastitanium hull walls, and the forward
# light mounts listed here carry a Prometheus laser cannon instead of a Phalanx.
ARMOURED = {"vigil", "bulwark", "guardian", "lodestar"}
ARMOUR = {"WallReinforced": "WallPlastitanium", "WallShuttle": "WallPlastitanium", "WallSolid": "WallPlastitanium",
          "WallReinforcedDiagonal": "WallPlastitaniumDiagonal", "WallShuttleDiagonal": "WallPlastitaniumDiagonal",
          "WallSolidDiagonal": "WallPlastitaniumDiagonal"}
# Hand-picked fire control tiles (console, server) where the nearest free floor would block a walkway.
FIRE_CONTROL = {"vigil": [(-1, 3), (-2, 2)]}

known = protos()
OUT.mkdir(parents=True, exist_ok=True)
for src, (dst, name) in HULLS.items():
    m = GridMap(SRC / f"{src}.yml")
    swapped, dropped = 0, []

    # guns first: each DSM mount in the loadout gets its local hardpoint and gun, facing as the DSM gun did
    loadout = LOADOUT.get(dst, {})
    mounts = {}
    for ent in list(m.on_grid()):
        if not ent.proto.startswith("AAAHardpoint"):
            continue
        tile = ent.tile
        dsm_guns = [e for e in m.on_grid() if e.tile == tile and e.proto.startswith(DSM_GUN)]
        q = dsm_guns[0].q if dsm_guns else ent.q
        m.remove(ent)
        if tile in loadout:
            for gun in dsm_guns:
                m.remove(gun)
            gun, hardpoint = loadout[tile]
            m.add(hardpoint, *tile, q=q)
            m.add(gun, *tile, q=q)
            mounts[tile] = gun
    assert set(mounts) == set(loadout), (dst, set(loadout) - set(mounts))

    for ent in list(m.ents.values()):          # includes items stored inside lockers
        if ent.proto in SWAP:
            ent.proto = SWAP[ent.proto]
            swapped += 1
        elif ent.proto and ent.proto not in known:
            dropped.append(ent.proto)
            m.remove(ent)
    if dst in ARMOURED:
        for ent in m.on_grid():
            ent.proto = ARMOUR.get(ent.proto, ent.proto)
    for ent in m.ents.values():
        ent.comps[:] = [c for c in ent.comps if c.get("type") not in STRIP]
    for ent in m.on_grid():
        if ent.q and known.info(ent.proto).norot:     # a rotated noRot entity aborts the grid load
            m.move(ent, *ent.tile, q=0)
    seen = set()
    for ent in list(m.on_grid()):                # the source maps stack some cables, pipes, lights and walls twice
        if ent.proto.startswith(("Cable", "GasPipe")) or known.info(ent.proto).category() in ("light", "wall"):
            layer = next((c.get("pipeLayer") for c in ent.comps if c.get("type") == "AtmosPipeLayers"), None)
            key = (ent.proto, ent.tile, ent.q, layer)
            if key in seen:
                m.remove(ent)
            seen.add(key)
    # Eclipsion lets distro and waste pipes cross on one layer; here they must not share a tile,
    # so a hull whose nets cross gets its whole waste net (red) moved to the secondary layer.
    pipes = [e for e in m.on_grid() if e.proto.startswith("Gas")]
    tiles = [e.tile for e in pipes if e.proto.startswith("GasPipe")]
    if len(tiles) != len(set(tiles)):
        for e in pipes:
            if any(c.get("type") == "AtmosPipeColor" and c.get("color", "").upper().startswith("#990000") for c in e.comps):
                e.comps[:] = [c for c in e.comps if c.get("type") != "AtmosPipeLayers"]
                e.comps.append({"type": "AtmosPipeLayers", "pipeLayer": "Secondary"})
    for pos, tile in list(m.tiles.items()):
        if tile not in known.tiles:
            m.set_tile(*pos, "Plating")

    # fire control: a gunnery console and server on free floor near the helm, wired to the nearest LV cable
    if mounts:
        m._tile_index = None
        index = m.index()
        helm = next(e for e in m.on_grid() if e.proto in ("ComputerShuttle", "ComputerTabletopShuttle"))
        hx, hy = helm.tile
        lv = [e.tile for e in m.on_grid() if e.proto == "CableApcExtension"]
        solid = lambda t: any(known.info(e.proto).category() not in PASSABLE for e in index.get(t, []))
        dist = lambda t: abs(t[0] - hx) + abs(t[1] - hy)
        spots = sorted((t for t in m.tiles if m.is_floor(*t) and not solid(t) and dist(t) > 1), key=dist)
        spots = FIRE_CONTROL.get(dst, spots)[:]
        placed = []
        if not any(e.proto == "ComputerTabletopGunneryConsole" for e in m.on_grid()):
            t = spots.pop(0)
            m.add("TableReinforced", *t)
            m.add("ComputerTabletopGunneryConsole", *t)
            placed.append(t)
        cost = sum(GUN_COST[known.info(g).comps["ShipGunClass"]["shipClass"]] for g in mounts.values())
        server = next(name for name, power in SERVERS if power >= cost)
        t = spots.pop(0)
        m.add(server, *t)
        placed.append(t)
        for t in placed:
            near = min(lv, key=lambda c: abs(c[0] - t[0]) + abs(c[1] - t[1]))
            lay_cable(m, "LV", [c for c in route(m, near, t, floor_only=False) if c not in lv])
        print(f"  fire control at {placed}, helm at {helm.tile}, {server} for {cost} of processing")

    # shields: the generator goes on free floor nearest the hull's power generator, wired like fire control
    if dst in SHIELDS:
        proto, tile = SHIELDS[dst]
        m._tile_index = None
        index = m.index()
        gen = next(e for e in m.on_grid() if e.proto.startswith("PortableGenerator"))
        gx, gy = gen.tile
        lv = [e.tile for e in m.on_grid() if e.proto == "CableApcExtension"]
        solid = lambda t: any(known.info(e.proto).category() not in PASSABLE for e in index.get(t, []))
        if tile is None:
            tile = min((t for t in m.tiles if m.is_floor(*t) and not solid(t) and abs(t[0] - gx) + abs(t[1] - gy) > 1),
                       key=lambda t: abs(t[0] - gx) + abs(t[1] - gy))
        m.add(proto, *tile)
        near = min(lv, key=lambda c: abs(c[0] - tile[0]) + abs(c[1] - tile[1]))
        lay_cable(m, "LV", [c for c in route(m, near, tile, floor_only=False) if c not in lv])
        print(f"  {proto} at {tile}, power generator at {gen.tile}")

    left = sorted({e.proto for e in m.ents.values() if e.proto and e.proto not in known})
    m.save(OUT / f"{dst}.yml", name=name)
    print(f"{src} -> {dst}: swapped {swapped}, guns {len(mounts)}, dropped {sorted(set(dropped))}, still unknown {left}")
