# Drydock

Anvilhold Drydock is a POI station (`WFDrydock`, `Resources/Maps/_WF/Drydock/drydock.yml`) with four berths, North,
East, South and West, each a bay that ships park in without docking. Each berth has a `WFComputerDrydock` (its `berth`
field names it) and two cranes. Ships reach it two ways: walk up to a berth console, or hail the dockmaster from their
own helm (Hailing module).

Cranes come in three sizes: `WFDrydockCrane` (36-tile arm), `WFDrydockCraneLarge` (60) and `WFDrydockCraneHuge` (80,
the station's), each with a `Mirrored` variant. They are 2x2 HV machines that draw 500 W idle and 15 kW while working,
and their speed scales with the power they receive. Below 25% a crane stalls; a job whose cranes stay stalled for
`powerAbortTime` (60 s) aborts and refunds its unfinished work, and starting work needs every berth crane at half
power. The arm runs down the bay, then the telescopic boom swings across the ship. A multitool links the console's
sender port to a crane's receiver port (`ports.yml`); a crane serves one console, so linking it elsewhere moves it. A
console's berth is its linked cranes, and its work area is the union of their reach, approached from the arms'
direction. A crane's `BerthGap` starts its work area that many tiles down the arm.

Each crane's arm lane is scanned every 2 s, gap included. A solid entity there (walls, windows, machines; not cables,
pipes or decals) blocks the crane: it is left out of the berth's working cranes, refuses work, and stalls its slot if
it blocks mid-job. The boom also stops short of a solid entity on its row, so items beyond it are out of reach.
Clients start cranes stowed and animate them out. The arm and boom draw on a client-only rig parented to the map,
since sprites are culled per grid and an arm reaching past its grid would vanish when the camera left that grid.

The dockmaster, Hammer Ironstrike (`WFDrydockDockmaster`, a dwarf with HailResponder), mans
`WFComputerDrydockTraffic` by the desk and answers ringing hails. The booking window is a per-caller hail service
(`WFDrydockOperator`, HailService), so each caller gets their own. The POI has `PreventPilot`. Walk-in consoles quote
and sell work directly on the ship in their berth, and their berth lights button frames the work area with guide
lights for `BerthLightsTime` seconds, so a pilot can line up without a booking.

A booking, from either route, takes the smallest free berth that fits the ship, for 5 minutes. The ship gets a guide
frame and approach lights (`WFDrydockGuideComponent`, drawn by `WFDrydockGuideOverlay`). Work starts once the ship has
held still in the berth for 2 s, and is paid then from the caller's bank account. A booking that lapses or is
cancelled releases its berth without charge.

Jobs restore missing hull tiles and entities and heal damage from the ship's repair snapshot
(`ShipRepairDataComponent`). A hull tile costs 120 cr. Rebuilding an entity costs 60 cr labor plus the more of twice its
estimated price and 35% of its construction materials' value (`WFDrydockSystem.Construction.cs`), so a plastitanium
wall, priced low but built from four 600 cr sheets, comes to about 960 cr. Healing costs the rebuild price times the
share of health lost. Work is capped at 50% of the ship's shipyard price (`VesselComponent`), or 40% of its full
rebuild price for ships not bought at one. A fee of 2.5% of that value (at least 1,500 cr) comes on top and is never
refunded. A job runs at most 10 minutes (`maxDuration`); longer work is sped up to fit. When it ends, the console says
so on its radio channel (`noticeChannel`, Common), at the console, and to a payer elsewhere. Cranes take the nearest
item and skip any they cannot reach within 10 s, refunding its cost.

Refits swap the machine parts of thrusters, gyroscopes and power systems for higher-rated stock parts, one tier at a
time (`upgrades.yml`), priced per stock part swapped: 1,000 cr for Mk II, 4,000 for Mk III and 10,000 for Mk IV.

The console window is an instrument deck in the cockpit style (CombatConsole's instrument kit). A live berth plot
(`WFDrydockBerthPlot`) draws the bay, the station around it, the ship's hull, the work left on it (missing hull red,
missing parts as red diamonds, damage amber) and each crane as the client animates it. A ship outside the plot gets
an edge pointer with its range. Open consoles refresh once a second.

A mobile drydock can be built on any ship: `WFComputerMobileDrydock` (board `WFComputerMobileDrydockCircuitboard`)
and `WFMobileDrydockCrane` (board `WFMobileDrydockCraneMachineCircuitboard`, MV power, a 26-tile berth starting 3
tiles past the machine, 13 tiles across). Both boards print at the circuit imprinter after Shuttlecraft research. To
mirror a mobile crane, unbolt it, open its panel and cut with wirecutters for 2 s (`WFDrydockCraneMirrorSystem`): the
body keeps its tiles and the arm moves to its other column. It works walk-in only and never on its own grid.

The mobile console charges no fee and pays in materials (`WFDrydockMaterialPaymentComponent`), from its cranes' own
storage or their linked material silo. Work is billed at material units per credit; refits use their tier's
`materials` per part instead. Rates are pegged to a T2 ship voucher (100 steel, 50 plasteel, 50 glass and 10
plastitanium sheets for a 150k to 500k ship): 25k cr of work takes about 14 steel sheets, and `maxBill` caps a repair
at about 60% of the voucher for a 250k ship, scaled by ship price. A rebuild also takes 1% of any construction material
the rates don't cover, so a plastitanium wall adds 4 units of plastitanium. Refunds go back into the job's first crane.

Entry points: `WFDrydockSystem` (berths, quotes, payment; `.Job.cs` for jobs and cranes, `.Booking.cs` for the hail
service, bookings and guides, `.Materials.cs` for material bills), `WFDrydockUpgradeSystem` (refit tiers), and the
client `WFDrydockCraneVisualSystem` (crane animation and rig) and `WFDrydockConsoleWindow` (the console deck).

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Drydock/WFDrydockBerth.cs`](WFDrydockBerth.cs)
- [`Content.Server/_WF/Drydock/WFDrydockBookingComponent.cs`](WFDrydockBookingComponent.cs)
- [`Content.Server/_WF/Drydock/WFDrydockJobComponent.cs`](WFDrydockJobComponent.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.BerthLights.cs`](WFDrydockSystem.BerthLights.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Blockers.cs`](WFDrydockSystem.Blockers.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Booking.cs`](WFDrydockSystem.Booking.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Construction.cs`](WFDrydockSystem.Construction.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.cs`](WFDrydockSystem.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Job.cs`](WFDrydockSystem.Job.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Linking.cs`](WFDrydockSystem.Linking.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Materials.cs`](WFDrydockSystem.Materials.cs)
- [`Content.Server/_WF/Drydock/WFDrydockUpgradeSystem.cs`](WFDrydockUpgradeSystem.cs)
- [`Content.Server/_WF/Drydock/WFDrydockWorkItem.cs`](WFDrydockWorkItem.cs)

### Shared

- [`Content.Shared/_WF/Drydock/WFDrydockConsoleComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockConsoleComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockConsoleUi.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockConsoleUi.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockCraneComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockCraneComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockCraneExamineSystem.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockCraneExamineSystem.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockCraneMirrorDoAfterEvent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockCraneMirrorDoAfterEvent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockCraneMirrorSystem.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockCraneMirrorSystem.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockGuideComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockGuideComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockMaterialPaymentComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockMaterialPaymentComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockOperatorComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockOperatorComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockUpgradePrototype.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockUpgradePrototype.cs)

### Client

- [`Content.Client/_WF/Drydock/WFDrydockBerthPlot.cs`](../../../Content.Client/_WF/Drydock/WFDrydockBerthPlot.cs)
- [`Content.Client/_WF/Drydock/WFDrydockConsoleBoundUserInterface.cs`](../../../Content.Client/_WF/Drydock/WFDrydockConsoleBoundUserInterface.cs)
- [`Content.Client/_WF/Drydock/WFDrydockConsoleWindow.xaml`](../../../Content.Client/_WF/Drydock/WFDrydockConsoleWindow.xaml)
- [`Content.Client/_WF/Drydock/WFDrydockConsoleWindow.xaml.cs`](../../../Content.Client/_WF/Drydock/WFDrydockConsoleWindow.xaml.cs)
- [`Content.Client/_WF/Drydock/WFDrydockCraneVisualSystem.cs`](../../../Content.Client/_WF/Drydock/WFDrydockCraneVisualSystem.cs)
- [`Content.Client/_WF/Drydock/WFDrydockGuideOverlay.cs`](../../../Content.Client/_WF/Drydock/WFDrydockGuideOverlay.cs)
- [`Content.Client/_WF/Drydock/WFDrydockGuideSystem.cs`](../../../Content.Client/_WF/Drydock/WFDrydockGuideSystem.cs)
- [`Content.Client/_WF/Drydock/WFDrydockReadout.cs`](../../../Content.Client/_WF/Drydock/WFDrydockReadout.cs)
- [`Content.Client/_WF/Drydock/WFDrydockTierPips.cs`](../../../Content.Client/_WF/Drydock/WFDrydockTierPips.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Drydock/DrydockHailTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/DrydockHailTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/DrydockTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/DrydockTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockBerthTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockBerthTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockClientTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockClientTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Drydock/consoles.yml`](../../../Resources/Prototypes/_WF/Drydock/consoles.yml)
- [`Resources/Prototypes/_WF/Drydock/cranes.yml`](../../../Resources/Prototypes/_WF/Drydock/cranes.yml)
- [`Resources/Prototypes/_WF/Drydock/dockmaster.yml`](../../../Resources/Prototypes/_WF/Drydock/dockmaster.yml)
- [`Resources/Prototypes/_WF/Drydock/lathe_recipes.yml`](../../../Resources/Prototypes/_WF/Drydock/lathe_recipes.yml)
- [`Resources/Prototypes/_WF/Drydock/mobile.yml`](../../../Resources/Prototypes/_WF/Drydock/mobile.yml)
- [`Resources/Prototypes/_WF/Drydock/poi.yml`](../../../Resources/Prototypes/_WF/Drydock/poi.yml)
- [`Resources/Prototypes/_WF/Drydock/ports.yml`](../../../Resources/Prototypes/_WF/Drydock/ports.yml)
- [`Resources/Prototypes/_WF/Drydock/sounds.yml`](../../../Resources/Prototypes/_WF/Drydock/sounds.yml)
- [`Resources/Prototypes/_WF/Drydock/upgrades.yml`](../../../Resources/Prototypes/_WF/Drydock/upgrades.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Drydock/drydock-berth-lights.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-berth-lights.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-booking.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-booking.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-console-ui.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-console-ui.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-console.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-console.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-crane-mirror.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-crane-mirror.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-dockmaster.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-dockmaster.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-linking.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-linking.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-materials-ui.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-materials-ui.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-materials.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-materials.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-upgrades.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-upgrades.ftl)

### Textures

- [`Resources/Textures/_WF/Drydock/crane.rsi/`](../../../Resources/Textures/_WF/Drydock/crane.rsi/)
- [`Resources/Textures/_WF/Drydock/crane_machine.rsi/`](../../../Resources/Textures/_WF/Drydock/crane_machine.rsi/)

### Maps

- [`Resources/Maps/_WF/Drydock/drydock.yml`](../../../Resources/Maps/_WF/Drydock/drydock.yml)

## Non-modular edits

- [`Resources/Prototypes/_Mono/Entities/Objects/Devices/ship_vouchers.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Devices/ship_vouchers.yml): single use, so a drydock repair undercuts printing another card
- [`Resources/Prototypes/Recipes/Lathes/Packs/science.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/science.yml): mobile drydock boards
- [`Resources/Prototypes/Research/industrial.yml`](../../../Resources/Prototypes/Research/industrial.yml): mobile drydock boards

<!-- WOLFGATE-GENERATED END -->
