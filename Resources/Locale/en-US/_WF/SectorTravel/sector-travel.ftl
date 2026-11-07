## Sectors

wf-sector-core = Caelestinus Sector
wf-sector-far-reach = True Sight
wf-sector-world-errant = Errant
wf-sector-beacon-name = Jump Beacon ({$sector})

## Crossing

wf-sector-travel-no-link = No beacon link reaches that sector from here.
wf-sector-travel-no-beacon = That sector has no beacon back to this one.
wf-sector-travel-cannot-jump = The ship can't jump right now.

## Beacon jumps

wf-sector-beacon-spooling = Beacon jump to {$sector} spooling: {$seconds} seconds. Stay inside the beacon's zone.
wf-sector-beacon-arrived = Arrived in {$sector}. The drive must settle before another beacon jump.
wf-sector-beacon-refuse-zone = The ship isn't inside that beacon's zone.
wf-sector-beacon-refuse-busy = The drive is already spooling or jumping.
wf-sector-beacon-refuse-lockout = The drive is still settling from the last beacon jump.
wf-sector-beacon-refuse-drive = The ship needs a powered FTL drive to jump.
wf-sector-beacon-refuse-fuel = Not enough jump fuel for that jump.
wf-sector-beacon-cancelled = Beacon jump cancelled.
wf-sector-beacon-cancel-zone = Beacon jump lost: the ship left the beacon's zone.
wf-sector-beacon-cancel-drive = Beacon jump lost: the FTL drive went down.
wf-sector-beacon-cancel-damage = Beacon jump lost: hull damage broke the spool.
wf-sector-beacon-cancel-interdicted = Beacon jump lost: an interdictor is jamming the spool.

## Jump fuel

wf-jump-fuel-full = The jump fuel tank can't take a whole crate.
wf-jump-fuel-loaded = Jump fuel loaded: {$fuel} / {$capacity}.
wf-jump-fuel-examine = Jump fuel: [color=cyan]{$fuel}[/color] / {$capacity}. A fusion fuel crate adds {$crate}.

## Helm panel

wf-sector-beacon-panel-title = JUMP BEACON
wf-sector-beacon-panel-cancel = Cancel jump
wf-sector-beacon-panel-no-drive = No FTL drive aboard.
wf-sector-beacon-panel-locked = Drive settling: {$seconds} s
wf-sector-beacon-panel-fuel = Jump fuel: {$fuel} / {$capacity}
wf-sector-beacon-panel-jump = Jump to {$sector}
wf-sector-beacon-panel-cost = {$cost} fuel, {$seconds} s in transit
wf-sector-beacon-panel-link = Spool inside the zone, then {$seconds} seconds in hyperspace. Costs {$cost} fuel.
wf-sector-beacon-panel-link-closed = A blockade holds this link: freight doesn't run through it. Costs {$cost} fuel, {$seconds} seconds in hyperspace.

## Region view

wf-region-map-button = Region
wf-region-map-cut-off = {$sector} (cut off)

## Trade

wf-trade-reason-jumps = hauled from another sector

## Admin command

cmd-wf_region-desc = Shows the region's sectors and jump beacons, moves ships between sectors and sets beacon holders.
cmd-wf_region-help = Usage: {$command} status | jump <grid> <sector> | fuel <grid> <amount> | hold <sector> <link> <faction|free> | raid <sector> <link>
cmd-wf_region-hint-sub = Subcommand
cmd-wf_region-hint-grid = Grid or entity on it (net id)
cmd-wf_region-hint-sector = Sector id
cmd-wf_region-hint-amount = Jump fuel
cmd-wf_region-hint-faction = Faction id, or free
cmd-wf_region-sector = {$sector}: map {$map}{$cut ->
    [1] , cut off from home
    *[0] {""}
}
cmd-wf_region-beacon = Beacon {$sector} -> {$link} at {$callsign}: held by {$holder}{$closed ->
    [1] , link closed
    *[0] {""}
}
cmd-wf_region-fuel-set = Jump fuel set to {$fuel}.
cmd-wf_region-jumping = Jumping to {$sector}.
cmd-wf_region-held = Beacon cell {$callsign} set to {$faction}.
cmd-wf_region-hold-failed = Couldn't set beacon cell {$callsign} to {$faction}.
cmd-wf_region-bad-grid = That isn't an entity on a grid.

## Fuel service and intake

wf-jump-fuel-intake-no-drive = No FTL drive aboard to feed.
wf-jump-fuel-service-none = The ship isn't docked to a station that sells fusion fuel.
wf-jump-fuel-service-dry = The station has no fusion fuel left to sell.
wf-jump-fuel-service-funds = Not enough money in the account to pay for the fuel.
wf-jump-fuel-service-bought = Jump fuel tank filled.
wf-sector-beacon-panel-refuel = Refuel {$units} for ${$cost}
wf-sector-beacon-panel-refuel-tooltip = Buys jump fuel from {$station} at its fusion fuel price, out of its stock.
wf-cockpit-fuel-jump = Jump fuel: {$fuel} / {$capacity}

## Raids and blockades

wf-sector-travel-sender = Beacon Traffic Control
wf-sector-beacon-raid = {$faction} are moving on the {$sector} beacon to {$link} at {$callsign}. Fly the cell within {$minutes} minutes or they will hold it and close the link.
wf-sector-cut-off = {$sector} is cut off: a blockade holds its beacon link and freight has stopped.
wf-sector-reopened = The beacon link to {$sector} is open again. Freight resumes.
cmd-wf_region-raided = The beacon's raiders are moving on its cell.
cmd-wf_region-raid-failed = No raid: the beacon has no raiders, or its cell is held, contested or protected.
