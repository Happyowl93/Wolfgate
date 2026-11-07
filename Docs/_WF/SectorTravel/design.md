# Sector travel

Status: built as the proof of concept in the decisions below. Numbers are starting values to replace after playtests.
How each part was built, and the two additions, are under [Implementation notes](#implementation-notes).

## Why

One map can't grow past about 30 km before floating-point error shows, and the engine can't be changed without forking
the launcher. So the region becomes several sector maps, each kept to the size the current sector already has, joined by
**jump beacons**. A beacon is the only way between sectors. Holding one is the strongest blockade in the game, and
crossing costs jump fuel, which the economy makes and blockades can choke.

Three designs were weighed:

- **Seams** (SS13 edge transitions): a ship hundreds of metres wide crosses half at a time, loose entities stay behind, and
  nobody can see or shoot across the edge. There are no chokepoints. Rejected.
- **Gates** (X4): fly through and arrive instantly. Every crossing is an escape hatch, and arrivals bunch at one exit.
- **Beacons**: spool inside a beacon's zone and jump to the linked beacon. The spool is a window to interdict, the arrival
  is spread over a ring, and the jump reuses upstream FTL almost entirely. Chosen.

Starsector-style hyperspace (a map you fly across, with fuel and a fall back into the nearest gravity well) was weighed as
well. It costs several times more code and playtesting, and open hyperspace has no chokepoints. This design keeps the move
to it cheap (see [Moving to hyperspace](#moving-to-hyperspace)).

## What exists already

| Need | Exists | Gap |
|---|---|---|
| Moving a grid between maps | Upstream FTL: spool, a singleton hyperspace map (`EnsureFTLMap`), travel time, arrival placement with `TryGetFTLProximity` (15 tries for a clear spot). Planets hop hulls between maps with `WFPlanetHopSystem`. | Nothing in the engine. |
| Cross-map FTL rules | `SharedShuttleSystem.CanFTLTo` lets a console jump to any map with an enabled `FTLDestination`. Every station map gets one (`OnStationPostInit`), and range is measured in raw world coordinates across maps. `WfAllowFTL` (`Content.Shared/_WF/Planets/SharedShuttleSystem.Wolfgate.cs`) is already the Wolfgate hook. | A sector rule in `WfAllowFTL`: today any second sector holding a station would be a free FTL target. |
| FTL drive | `FTLDriveComponent` (`_Mono`): range, cooldown, spool, travel time, mass multiplier. Spool and travel scale with docked mass and cargo. | No fuel of any kind. FTL only needs power. |
| Sector-wide services | `SectorServiceSystem` keeps one service entity per server: bank, mail, records, alert and war level, bounties, dead drops. | None. It stays one per region. |
| Territory | `WFSectorTerritoryComponent` is per map and the claim API takes a `MapId`. | The activity scan, notices, sync and command only run on `SectorMap` (`DefaultMap`). |
| Fuel as a good | TradeGoods has Fusion Fuel, made by Reactors at Caelestinus from Fission Fuel, which has no maker and is the strategic import. | Nothing links it to ships. |
| Freighters | Encounters freighters start as virtual contacts and become grids within 2 km of a player. They fly sublight and never FTL. | Routes that cross sectors. |

## Sectors

A `wfSector` prototype describes one sector:

| Field | Meaning |
|---|---|
| `name` | LocId. |
| `galaxyPosition` | Where the sector sits in the region, in galaxy units. Fuel and travel time use the distance between positions, and the region map draws them. |
| `home` | The one sector on `DefaultMap`. Players arrive here, and upstream code that reads `DefaultMap` keeps working. |
| `pois` | The POI groups `PointOfInterestSystem` places on this map. The home sector keeps today's groups. |
| `worldgen` | The worldgen config (asteroids, debris). Upstream applies it only to `DefaultMap`. |
| `beacons` | Each beacon's position in the map and the sector and beacon it links to. |

`WFSectorTravelSystem` loads every non-home sector at round start and stamps its map with `WFSectorMapComponent`.
`SectorOf(map)` answers which sector any map belongs to:

- a sector map: itself;
- a planet's orbit and surface layers: the sector of the planet body's map;
- an expedition map: the sector the expedition left from;
- the FTL map, shipyard and staging maps: no sector.

Every sector map must also:

- skip the crushing of whatever lies under an arriving hull (`Smimsh`), which upstream skips on `DefaultMap` only;
- carry `WFTradeMarketMapComponent`, so its stations are markets (`IsMarketMap`);
- hold a telecom server, because non-long-range radio needs one on the sender's map. Long-range channels already work
  everywhere, and receivers hear from any map.

**Region size.** An empty map costs almost nothing. Physics steps one global set of awake bodies, and PVS only builds
what players can see. A sector costs what is in it: stations, atmos, NPC crews, live encounters. Splitting today's content
over several maps adds no load. A sector with no players could be paused (`MapPaused`) if load ever needs it, at the cost
of freezing its live freighters.

## Crossing between sectors

Every crossing goes through one call:

```
WFSectorTravelSystem.TryTransit(ship, from, to, arrival)
```

Beacon jumps, NPC freighters, salvage returns and the admin command all use it. It checks fuel, starts the upstream FTL
sequence with the destination and travel time worked out from the two sectors, and spends fuel when the ship enters
hyperspace. If the region later becomes flyable hyperspace, only this call changes.

**Normal FTL stays inside its sector.** `WfAllowFTL` refuses a destination map whose sector isn't the ship's own, so
station maps in other sectors drop out of the MAP tab and console jumps. Planet hops and expeditions stay legal because
their maps belong to the ship's sector. Admin tools that move grids directly are unaffected.

## Beacons

A beacon is a large, indestructible structure on its own anchored grid (`WFJumpBeacon`), with an IFF label naming the
sector it reaches. Its zone is a circle of `zoneRadius` (1500 m) around it, inside mass-scanner range (3 km), so anyone
holding the beacon sees who is spooling.

**Jumping.** A ship whose grid centre is in the zone and whose FTL drive is powered and ready sees a JUMP entry in the
MAP tab for each linked sector, with the fuel it costs. Choosing one calls `TryTransit`.

| Step | Rule |
|---|---|
| Spool | 20 s, scaled by mass and cargo the way FTL spool already is. Leaving the zone cancels it. |
| Interruption | Hull damage over `cancelDamage` (structural, summed over the spool) cancels the jump. A drive powered down cancels it. No fuel is spent. |
| Travel | 30 s plus 10 s per galaxy unit between the sectors, on the shared FTL map. |
| Arrival | A random point on a ring 1 to 3 km from the linked beacon, placed with `TryFTLProximity`. Ships never stack on one exit point. |
| Lockout | After arriving, a ship can't beacon-jump again for 120 s. Normal FTL keeps its own cooldown. |

The spool is the combat window. Holding a beacon means stopping ships in that 20 s, and fleeing across a beacon needs a
clean spool, so jumping is not an instant escape. The arrival ring spreads newcomers over about 25 km², which takes most of
the profit out of camping the far side.

**Interdictor** (later phase). A ship module that, while powered, cancels any beacon spool within 1 km and shows on
radar to every ship in that range.

## Fuel

Beacon jumps cost fuel. Normal FTL inside a sector does not, as in Starsector, where travel inside a system is free.

```
cost = baseCost x distance(from, to) x massFactor x drive.DriveMassMultiplier
```

`massFactor` uses the docked group's mass, as FTL timing already does, so a heavy hull pays more and the heavy drive's
0.25 multiplier makes it the efficient hauler. Even when every link has the same distance at first, the cost is computed
from galaxy positions.

The FTL drive gets a jump-fuel tank (`WFJumpFuelComponent`: `Fuel`, `Capacity`). The helm's fuel gauge and the MAP tab
show it next to the cost of each jump. Ships refuel in two ways:

- **At a station.** A fuel service sells units at the station's Fusion Fuel price and draws them from its Fusion Fuel
  stock. A dry station sells none.
- **From crates.** A fuel intake machine on the ship takes a Fusion Fuel crate and converts it to units. A ship hauling
  fuel can top up others, or itself.

The good is the existing **Fusion Fuel**. Its chain already runs from the Fission Fuel import through Reactors at
Caelestinus, so blockading Caelestinus or the import route starves jump fuel. A ship with an empty tank stays in its
sector until it buys or is brought fuel. There is no free way out.

## Freight and the economy

The ledger and pool stay region-wide. Stations keep their own ledgers, and `SectorServices` stays global, so bank, mail
and records don't change.

- **Routes.** Freight plans on the graph of sectors, where each edge is a link between two beacons. A virtual freighter
  flies to the beacon, spools for the same 20 s, vanishes, and after the travel time appears on the far ring. A freighter
  that is live as a grid when it reaches the zone jumps through `TryTransit` like a player ship.
- **Blockades.** A beacon whose cell is held by a faction with `blockades: true` closes its edge to freight. A station that
  no open path reaches from where its imports come from counts as blockaded under the existing rule: imports run off the
  shelf toward level 0.25, halving every 15 minutes. Holding one beacon can therefore cut a whole sector off.
- **Hauls.** `WFTradeMarketSystem.Origin` requires the origin and the chute to share a map. That check becomes "a
  different station", so a haul between sectors counts. A chute pays `1 + jumpBonus x jumps` times its price, where
  `jumps` is the shortest number of beacon links between the origin station's sector and the chute's (`jumpBonus` 0.15
  to start). The bonus pays for the spool, the travel and the fuel. It must stay above the fuel a typical hauler burns
  per crate, or no one hauls between sectors.
- **Encounters.** Station, route and circuit encounters are scheduled per sector instead of on `DefaultMap` only.
  Open-space placement already follows player maps.

## Territory

Each sector map gets its own hex grid. The activity scan, notices and sync run on every sector map instead of
`SectorMap`. A beacon's cell is claimable like any other, and holding it is what blockades its link. The ReaverCampaign
and AdsIncursion campaigns stay on the home sector until they are made sector-aware.

## What else reads `DefaultMap`

Each place becomes per sector, or stays on the home sector on purpose:

| Code | Change |
|---|---|
| `NfAdventureRuleSystem` POI generation | Per sector, from `wfSector.pois`. |
| `WorldgenConfigSystem` | Per sector, from `wfSector.worldgen`. |
| `SalvageSystem.Runner` expedition return | To the sector the expedition left from. |
| `FasterThanLight` `Smimsh` skip | Every sector map. |
| `AlertLevelSystem` alert sound | Every sector map. The alert level is already region-wide. |
| `StationEventSystem` announcements | Unchanged. They are sent from the `DefaultMap` entity, and radio receivers on any map hear them. |
| `PublicTransitSystem` stops | Home sector only, at first. |
| `WFEncounterSchedulerSystem` stations | Per sector. |
| `WFTradeMarketSystem.IsMarketMap` | Already accepts `WFTradeMarketMapComponent`. |
| `WFShipScenarioSystem`, `WFDoomedScenarioSystem` | Home sector. |
| `WFStationPatrolSystem.FindStation` | Unchanged. It already falls back to any map. |
| Sector map window | Draws the player's own sector. A region tab shows sectors at their galaxy positions, their links and who holds each beacon. |

## Moving to hyperspace

Four rules keep a later move to flyable hyperspace cheap:

1. Every sector has a galaxy position, even though links alone would do.
2. Fuel cost is a function of distance.
3. Every crossing goes through `TryTransit`.
4. Freight routes on the sector graph, not on a list of beacon pairs.

With these, the move replaces `TryTransit`'s middle (a hyperspace map you fly across instead of the FTL map), turns fuel
per jump into fuel per distance flown, and gives freight new edges. Beacons become the jump points where ships enter and
leave hyperspace, so chokepoints survive at each sector's entry.

## Phases

1. **Sectors.** `wfSector`, multi-map round setup with the home sector and one new sector, `SectorOf`, the `WfAllowFTL`
   sector rule, `TryTransit`, the per-map fixes above and an admin command to move a ship between sectors.
2. **Beacons.** The structure, zone, MAP tab entry, spool, interruption, arrival ring, lockout and the region tab.
3. **Fuel.** The tank, the costs, the station fuel service, the intake and the gauge.
4. **Freight.** Sector-graph routing, beacon blockades, cross-sector hauls and per-sector encounters.
5. **Military.** The interdictor, beacon claims in SectorControl and protection rules.

Each phase lands with integration tests under `Content.IntegrationTests/Tests/_WF/SectorTravel`: a grid and the loose
entities near it cross intact, cross-sector console FTL is refused, an arrival on a sector map crushes nothing, fuel is
spent once per jump and not on a cancelled spool, and a held beacon blockades the stations behind it.

## Decisions

1. **Launch.** A proof of concept with two sectors: today's sector unchanged as the home sector, and one new sector, joined
   by a single pair of beacons. More sectors come once the plumbing is proven.
2. **Fuel.** Fusion Fuel.
3. **Protection.** Provisional: the home side's beacon cell is protected like Halcyon's (`WFSectorProtectionSystem`), and
   the far side's is open. With one link, a claim on the home side would cut the whole new sector off, and the home
   sector's campaign drivers (Reaver, ADS) could do that by accident. The far side is where beacon fights happen.
   Revisit after playtests.
4. **Hauls.** Each jump between the origin and the chute adds a bonus (see [Freight and the economy](#freight-and-the-economy)).
5. **Round start.** No faction holds a beacon. Beacons are taken in play.

## Implementation notes

What the plan names, where it lives, and where the build differs in detail.

**Sectors.** `Resources/Prototypes/_WF/SectorTravel/sectors.yml` has **Core Space** (home) and **Far Reach** (one cargo
depot, the alternate depot's role and vendors). Both names are placeholders. `WFSectorTravelSystem.LoadRegion` runs on
`StationsGeneratedEvent`, before the encounter scheduler, and gives each new map the home map's parallax and light.
`pois` lists POI prototypes, each with a station name, a distance band and, for a depot, its trade crate destination.
Planet layers are stamped by a 10 s sweep, because they are built lazily. Expedition maps are stamped when their ship
enters hyperspace. The telecom server is a sealed `WFSectorRelay` that needs no power, built onto every beacon
platform.

**Crossing.** The call is `TryTransit(ship, to, spool, travel, out reason)`: the caller passes the timings, and fuel is
checked and spent by the beacon jump around it. Expedition returns stay inside a sector, so they don't cross and don't
call it. The sector rule is `WfSameSector`, a marked line in `CanFTLTo` next to `WfAllowFTL`. Arrival points are picked
clear of other grids by the module itself, because upstream position FTL does not use `TryFTLProximity`.

**Beacons.** The platform is a static, protected, cleanup-immune grid. The JUMP entry is a JUMP BEACON panel
(`WFBeaconJumpPanel`) with a cancel button: on the flight deck's STRATEGIC tab between DRIVE CONTROL and DESTINATION
REGISTER, and in the cockpit's STRATEGIC page details. It hides its contents, never itself, because a hidden control
gets no frame updates. The damage check sums damage over the ship's anchored structure,
and a destroyed structure entity counts as the whole threshold. Entities floating free within 6 m of the hull go with
it. The interdictor is a ship machine whose board is printed at the circuit imprinter (cargo boards). While powered it
shows as a red star on the radar of ships within its 1 km range (Mono `RadarBlip`).

**Fuel.** Every FTL drive that comes with a ship or station gets a full 300-unit tank. A drive built or rebuilt by
construction starts empty, so rebuilding one is no free refill. A crate adds 100. The fuel service is a Refuel button
on the same panel while the ship is docked to a station whose trade vendors sell Fusion Fuel; it charges the pilot's
bank account at that station's vendor price and takes the units off its ledger stock. The fuel intake (`WFJumpFuelIntake`)
is printed at the circuit imprinter. The helm's fuel gauge shows jump fuel on its tooltip; its bar still shows generator
fuel.

**Freight.** A freight run with two or more stops ends in a linked sector 35% of the time, over an open link only
(`ExtendAcrossSectors`). Its route gets a crossing leg between stops in different sectors: fly into the beacon's zone,
then hold (`CrossingLeg`). A live hull or escort in the zone spools an NPC beacon jump, which needs no fuel or working
drive but breaks like any other spool. A simulated stand-in is moved to the far ring after the same spool and travel
time; it counts as crossed only once it lands, so a stand-in built into a hull while it waits makes the jump as a hull.
A run that finds its link closed at the beacon is sent away and doesn't cross.

**Encounter placement.** Station encounters try sectors with living players first, each drawn by its share of them
(`EncounterMaps`); ghosts don't count, and a player on a planet layer or expedition counts toward its sector. One
visitor in Far Reach so pulls about one encounter in thirty away from a 30-player home sector, not all of them.

**Territory and region view.** `WFSectorTerritorySystem.SectorMaps` feeds the activity scan, notices and sync. The
region tab is a Region toggle on the sector map window. The server sends it `WFRegionStateEvent` when beacon holders
change.

**Additions beyond the plan.**

- The FTL drive itself also takes a Fusion Fuel crate, so a ship without an intake can still be refuelled by hand.
- Beacon raids drive beacon claims in play. A beacon may name `raiders`, a blockading faction (Far Reach's names the
  Reavers). About every 60 minutes those raiders contest its cell for 10 minutes. If no ship flies the cell in that
  time, they take it, the link closes and Far Reach is cut off, announced region-wide. Players free the cell with the
  existing retake rule. The protected home-side beacon is never raided.

**Admin command.** `wf_region status | jump <grid> <sector> | fuel <grid> <amount> | hold <sector> <link> <faction|free> | raid <sector> <link>`.

**Tests.** `Content.IntegrationTests/Tests/_WF/SectorTravel/WFSectorTravelTest.cs`, 12 tests:
- Console FTL is refused between sectors and unchanged inside one.
- A beacon jump lands on the far ring, spends its fuel once, carries a loose crate along, crushes nothing on arrival and
  locks the ship out.
- A spool breaks outside the zone and near an interdictor without spending fuel.
- A held far beacon cuts Far Reach off and blockades its depot, while the home beacon refuses claims.
- A crate from the next sector over earns one link's bonus.
- Crates refuel a drive directly and through an intake.
- A docked depot quotes jump fuel from its stock and refuses a buyer who can't pay.
- Raiders contest the far beacon.
- A freight stand-in and a live escort both cross a beacon.
- A stand-in built into a hull mid-crossing still crosses, and a run whose last stop is deleted is skipped.
- A drive built by construction starts with an empty tank.

`Content.Tests/_WF/SectorTravel/WFSectorGraphTest.cs` checks the link count between sectors: a closed link doesn't
hide a sector that another open way still reaches.
