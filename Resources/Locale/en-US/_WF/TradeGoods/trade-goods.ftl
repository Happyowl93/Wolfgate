wf-trade-chute-local-goods = This chute only takes goods shipped in from elsewhere.
wf-trade-refuse-made-here = Made on this station. Haul it at least {$distance} km away before selling it here.
wf-trade-refuse-too-close = Made too close to this market. Haul it at least {$distance} km before selling here.
wf-trade-refuse-unknown = Origin unknown, logged here. Haul it about {$distance} km before selling.
wf-trade-refuse-shunned = The merchant won't buy from someone who hurt them.

wf-encounter-name-merchant = Merchant {$designation}
wf-encounter-announce-merchant = {$name} is in the sector buying luxury goods, medicine, reactor fuel, weapon parts and fabric at a premium. Bring cargo alongside before it moves on.

wf-trade-exchange = Sector Exchange

## Live prices

wf-trade-sale = Paying {$amount}.
wf-trade-sale-low = Paying {$amount}. Best now: {$station}, {$best}.
wf-trade-examine-header = [color=#d9a441]Live prices:[/color]
wf-trade-price-line = {$good}: {$amount} {$trend} ({$reason}), {$tonnes}t, {$perTonne}/t
wf-trade-paper-header = [bold]{$station}: live turn-in prices[/bold]
wf-trade-paper-footer = Prices fall as crates come in and recover over half an hour. Sector events raise them. The Market app on your PDA has every station's prices.

wf-trade-reason-steady = steady
wf-trade-reason-glutted = glutted
wf-trade-reason-supplied = supplied
wf-trade-reason-short = short
wf-trade-reason-boom = boom
wf-trade-reason-blockade = blockade
wf-trade-reason-relief = relief
wf-trade-reason-war = war demand
wf-trade-reason-raid = raid alert
wf-trade-reason-rush = rush order
wf-trade-reason-sealed = sealed freight
wf-trade-reason-salvage = salvage bonus
wf-trade-reason-defector = enemy buyer

wf-trade-goods-medical = medical supplies
wf-trade-goods-supplies = supplies
wf-trade-goods-ammo = ammunition
wf-trade-goods-ship-components = ship components
wf-trade-goods-chemicals = chemicals
wf-trade-goods-fuel = reactor fuel
wf-trade-goods-industrial = industrial goods
wf-trade-goods-agriculture = fertilizer
wf-trade-goods-luxuries = luxury goods
wf-trade-goods-textiles = fabric

## Sector Exchange radio

wf-trade-shortage-start = {$station} is short of {$goods}: paying {$factor}x for {$minutes} minutes.
wf-trade-boom-start = {CAPITALIZE($goods)} boom: every station pays {$percent}% more for {$minutes} minutes.
wf-trade-war-demand = War demand: {$stations} pay {$factor}x for ammunition, weapon parts and medical supplies.
wf-trade-blockade-landed = {$station} is blockaded. It pays up to {$factor}x for essentials while the blockade holds.
wf-trade-blockade-broken = The blockade of {$station} was broken by traffic: {$ships}.
wf-trade-blockade-driven-off = The blockade fleet off {$station} was driven off.
wf-trade-relief-name = running the blockade of {$station}
wf-trade-rush-start = RUSH: {$station} needs {$count} crates of {$goods} within {$minutes} minutes, paying {$factor}x.
wf-trade-rush-filled = Rush order filled at {$station}: {$fills}.
wf-trade-rush-expired = The rush order for {$goods} at {$station} ran out.
wf-trade-freight-supplied = {$ship} delivered {$goods} to {$station}.
wf-trade-freight-lost = {$station}'s {$run} never arrived. Its goods are short there.

## War

wf-trade-navy-tsf = the TSF
wf-trade-navy-pdv = the PDV
wf-trade-war-effort = War effort: {$navy} adds {$credits} credits.
wf-trade-defector-warrant = Sold war goods to {$navy} in wartime.

## Consignments

wf-trade-consignment-start = A priority consignment for {$station} is waiting at {$origin}: {$value} if delivered within {$minutes} minutes.
wf-trade-consignment-taken = {$ship} has taken the {$station} consignment.
wf-trade-consignment-delivered = {$ship} delivered the {$station} consignment on time.
wf-trade-consignment-examine = [color=#e8c46a]Priority consignment for {$station}.[/color] Pays {$value} if delivered within {$minutes} min, less after. Any other chute pays {$elsewhere}.

## Classified cargo

wf-trade-classified-detected = Classified TSF cargo detected aboard {$ship}, near {$cell}.
wf-trade-classified-warning = Your hold is carrying classified TSF cargo. Its transponder is pinging; PDV and the Underground have been told. Turn it in at Halcyon or CC.
wf-trade-classified-moved = Classified TSF cargo is now aboard {$ship}, near {$cell}.
wf-trade-classified-adrift = Classified TSF cargo is adrift near {$cell}.
wf-trade-classified-jammed = The classified cargo's transponder went silent near {$cell}.
wf-trade-classified-delivered = Classified TSF cargo was turned in at {$station} by {$ship}.
wf-trade-classified-destroyed = The classified TSF cargo was destroyed.
wf-trade-classified-refused = Nobody here will touch classified cargo.
wf-trade-classified-examine = [color=#e05050]A TSF transponder blinks under the label.[/color]
wf-trade-jammer-used = The transponder goes quiet. It will be back in {$minutes} minutes.

## Freight

wf-trade-seal-examine = Manifest seal: {$run} freight run, bound for {$station}. Lawful chutes pay {$percent}%; deliver it to {$station} for a salvage bonus.
wf-trade-thermal-examine = [color=#9aa7b1]Warm: it shows on thermal sensors.[/color]
wf-trade-escort-name = escorting {$ship}
wf-trade-escort-paid = Convoy bonus: {$ships} paid {$amount} for escorting {$trader}.

## Round end

wf-trade-honours-header = [color=#d9a441]Sector Exchange honours[/color]
wf-trade-honours-hauler = Top hauler: {$ship}, {$spesos} delivered in {$crates} crates.
wf-trade-honours-runner = Blockade runner: {$ship}, {$count} blockades broken.
wf-trade-honours-rush = Rush champion: {$ship}, {$count} rush crates.
wf-trade-honours-courier = Courier of the round: {$ship}, {$count} consignments on time.
wf-trade-honours-escort = Best escort: {$ship}, {$spesos} in convoy bonuses.

## Market app

wf-market-program-name = Market
wf-market-title = Sector Exchange
wf-market-status = {$stations} markets open
wf-market-status-ship = {$stations} markets open · aboard {$ship}
wf-market-tab-prices = Prices
wf-market-tab-alerts = Alerts
wf-market-tab-alerts-count = Alerts ({$count})
wf-market-tab-hold = Hold
wf-market-tab-hold-tonnes = Hold ({$tonnes}t)
wf-market-filter = Filter goods...
wf-market-price-amount = {$amount}
wf-market-price-weight = {$tonnes}t · {$perTonne}/t
wf-market-station-best = ★ best for {$count}
wf-market-no-stations = No stations are buying right now.
wf-market-no-match = No market buys that.
wf-market-no-alerts = Nothing happening in the sector right now.
wf-market-minutes = {$minutes} min
wf-market-alert-kind-deal = DEAL
wf-market-alert-kind-traffic = TRAFFIC
wf-market-alert-kind-warning = WARNING
wf-market-alert-kind-personal = FOR YOU
wf-market-alert-kind-hunt = HUNT
wf-market-alert-rush = RUSH {$station}: {$left} of {$count} crates of {$goods} wanted at {$factor}x
wf-market-alert-consignment = Consignment for {$station} waiting for a carrier
wf-market-alert-consignment-carried = Consignment for {$station}, carried by {$ship}
wf-market-alert-relief = {$station} pays relief rates ({$factor}x) while the blockade is contested; ships that break it split {$bounty}
wf-market-alert-raid = Raid on {$station}: ship components and medical supplies pay {$factor}x
wf-market-alert-blockade = {$station} is blockaded: essentials pay up to {$factor}x
wf-market-alert-shortage = {$station} is short of {$goods}: {$factor}x
wf-market-alert-boom = {CAPITALIZE($goods)} boom: +{$percent}% everywhere
wf-market-alert-war = War demand: the navies pay {$factor}x for munitions and medicine
wf-market-alert-freight = Freighter {$ship} inbound to {$station} with {$goods}
wf-market-alert-merchant = Merchant {$ship} buying near {$cell}; it moves on
wf-market-alert-shunned = {$station} won't trade with you: you hurt its people
wf-market-alert-war-effort = War effort: {$navy} pays you {$credits} faction credits a crate for munitions and ship parts
wf-market-alert-defector = {$navy} pays you {$factor}x for war goods
wf-market-alert-defector-warrant = {$navy} pays you {$factor}x for war goods, but {$own} posts a {$warrant} warrant on anyone who sells
wf-market-alert-escorting = Escorting {$trader}: you share {$percent}% when it sells {$threshold} within 10 min ({$sold} so far)
wf-market-alert-escorted = Escorted by {$ships}: they share {$percent}% on top when you sell {$threshold} within 10 min ({$sold} so far)
wf-market-alert-classified = Classified cargo last heard near {$cell}, {$minutes} min ago, aboard {$ship}
wf-market-alert-classified-jammed = Classified cargo jammed, last heard near {$cell}, {$minutes} min ago, aboard {$ship}
wf-market-adrift = nothing (adrift)
wf-market-nowhere = nowhere
wf-market-no-ship = You are not aboard a ship.
wf-market-hold-summary = {$ship}: {$tonnes}t aboard, thrust -{$percent}%
wf-market-hold-stored = of which {$tonnes}t is stored materials
wf-market-hold-thrust = Thrust {$percent}%
wf-market-hold-count = {$good} ×{$count}
wf-market-hold-tonnes = {$tonnes}t
wf-market-hold-best = → {$station} {$amount}
wf-market-hold-bound = bound for {$station}: {$amount}
wf-market-hold-empty = The hold is empty.
