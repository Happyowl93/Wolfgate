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

wf-sector-beacon-countdown = Jump to {$sector} confirmed. Portal opens in {$seconds}s. Stay in the beacon zone.
wf-sector-beacon-portal-open = Portal open ahead. Fly through to jump to {$sector}.
wf-sector-beacon-portal-closed = The portal closed.
wf-sector-beacon-arrived = Arrived in {$sector}.
wf-sector-beacon-refuse-zone = Not in the beacon zone.
wf-sector-beacon-refuse-busy = Already jumping.
wf-sector-beacon-refuse-lockout = Jumped too recently.
wf-sector-beacon-refuse-cannot = The ship can't jump right now.
wf-sector-beacon-cancel-zone = Jump cancelled: left the beacon zone.
wf-sector-beacon-cancel-damage = Jump cancelled: hull damage.

## The beacon's answers to a hail

wf-sector-beacon-reply-confirmed = CONFIRMED. FTL SEQUENCE INITIATED. DESTINATION {$sector}. PORTAL IN {$seconds} SECONDS.
wf-sector-beacon-refuse-zone-reply = NEGATIVE. OUTSIDE BEACON ZONE. CLOSE TO {$radius} METRES.
wf-sector-beacon-refuse-busy-reply = NEGATIVE. JUMP ALREADY IN PROGRESS.
wf-sector-beacon-refuse-lockout-reply = NEGATIVE. RECENT JUMP. RETRY IN {$seconds} SECONDS.
wf-sector-beacon-refuse-cannot-reply = NEGATIVE. VESSEL CANNOT JUMP.

## Region view

wf-region-map-button = Region
wf-region-map-cut-off = {$sector} (cut off)

## Trade

wf-trade-reason-jumps = hauled from another sector

## Admin command

cmd-wf_region-desc = Shows the region's sectors and jump beacons, moves ships between sectors and sets beacon holders.
cmd-wf_region-help = Usage: {$command} status | jump <grid> <sector> | hold <sector> <link> <faction|free> | raid <sector> <link>
cmd-wf_region-hint-sub = Subcommand
cmd-wf_region-hint-grid = Grid or entity on it (net id)
cmd-wf_region-hint-sector = Sector id
cmd-wf_region-hint-faction = Faction id, or free
cmd-wf_region-sector = {$sector}: map {$map}{$cut ->
    [1] , cut off from home
    *[0] {""}
}
cmd-wf_region-beacon = Beacon {$sector} -> {$link} at {$callsign}: held by {$holder}{$closed ->
    [1] , link closed
    *[0] {""}
}
cmd-wf_region-jumping = Jumping to {$sector}.
cmd-wf_region-held = Beacon cell {$callsign} set to {$faction}.
cmd-wf_region-hold-failed = Couldn't set beacon cell {$callsign} to {$faction}.
cmd-wf_region-bad-grid = That isn't an entity on a grid.

## Raids and blockades

wf-sector-travel-sender = Beacon Traffic Control
wf-sector-beacon-raid = {$faction} are moving on the {$sector} beacon to {$link} at {$callsign}. Fly the cell within {$minutes} minutes or they will hold it and close the link.
wf-sector-cut-off = {$sector} is cut off: a blockade holds its beacon link and freight has stopped.
wf-sector-reopened = The beacon link to {$sector} is open again. Freight resumes.
cmd-wf_region-raided = The beacon's raiders are moving on its cell.
cmd-wf_region-raid-failed = No raid: the beacon has no raiders, or its cell is held, contested or protected.
