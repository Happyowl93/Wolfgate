# TradeGoods

The trade-crate economy. Every station with a turn-in chute is a market whose prices move: each sale of a good adds
pressure that lowers its price at that station and decays over about half an hour, and sector events raise prices
with a reason players can see. Crates must be hauled about 3 km from where they were made before a chute takes them,
which closes the lathe faucet. The Market PDA app, chute examine and the chute's empty-hand paper show live prices,
each with its weight and price per tonne. The app's Alerts tab lists every event below with its countdown, plus alerts
for the holder alone: traders who won't deal with them, what the war pays their faction, and the convoy they are
escorting or that escorts them. Its Hold tab prices each crate aboard as a chute would (`WFMarketCartridgeSystem.BestFor`):
seals, destination bonuses, the seller's faction and special payouts count, and chutes that would refuse it, including
for a crate not yet stamped, are skipped; consignments and console trading crates show where they are bound. Every PDA
gets the app: `WFMarketCartridgeSystem` appends it to each PDA's preinstalled programs at startup, since most PDA
prototypes replace the base list. Only open copies refresh, every 5 s, and the hold's cargo is swept once per refresh.
Its Prices tab groups stations by faction (`marketFactions`, matched by chute). Hidden markets (`hiddenChutes`: PDV
Helios and the Underground fence) go to a separate Underground tab, and only holders of an `undergroundCompanies`
company (PDV and the Underground's godfathers and fugitives) are sent them, along with alerts naming them and hold sales
there. UI state reaches every client near a PDA, so for those holders the networked state is a public stand-in and the
full state goes to the viewers alone (`WFMarketPrivateStateEvent`, kept by the client `WFMarketSystem`). Radio lines
naming a hidden market go only to the underground companies (`WFTradeMarketSystem.Announce`).

What raises prices (`WFTradeQuoteEvent` subscribers, capped together at `modifierCap`):
- Shortages at one station and sector-wide booms (`WFTradeShortageSystem`, `wfTradeShortage`).
- Blockades, relief rates while a blockade is contested, war demand at the navy chutes and raid alerts
  (`WFTradeSectorSystem`, reading SectorControl, the war level and the Reaver campaign).
- Rush orders: a station wants a few crates fast (`WFTradeRushSystem`, `wfTradeRush`).
- NPC freight (`WFTradeFreightSystem`): a run's crates carry a manifest seal and pay 60% at lawful chutes, full price at
  a fence (the Underground fence chute, PDV Helios) and a bonus at their destination. A freighter docking at a stop
  supplies it (tagged *supplied*); a run lost to attack leaves its remaining stops short.

Priority consignments (`WFTradeSpecialCrateSystem`) appear at one chute for another station at least a haul away, never
a wandering merchant's, and pay well on time and less when late. One left at its origin past its deadline is withdrawn;
one carried off is recalled a window after its deadline, or as soon as its destination is gone.

Traffic and rewards: a blockade attempt that traffic breaks is announced with the ships that ran it, who split a relief
bounty; real deliveries stamp the station's cell as traffic. Crewed ships that fly beside a laden trader split an
escort bonus when it sells a big haul (`WFTradeEscortSystem`); players seen aboard the trader in the window get none of
it, so a crew can't pay itself by splitting into two ships. Wandering merchants (`WFEncounterWanderingMerchant`) carry a chute
that buys a few goods at a premium; a merchant won't buy from someone who hurt them.

Cargo-console trading crates (Frontier's `TradeCrate`, normal and express) sell at their destination's pallet, not a
chute. `WFTradeCrateMarketSystem` puts them in the same markets: their pallet price is scaled by the selling grid's
demand for one shared good (`WFTradeCrate`), each sale adds `tradeCratePerSale` there, the price never falls below
`tradeCrateFloor`, and they count toward a ship's cargo value for raids and extortion. A pallet sale prices its whole
batch in one tick before any of it adds pressure, so each crate priced in a tick is priced as if the ones before it had
sold. Cargo value is worked out once per tick for every grid, however many ships a raider weighs.

Entry points: `WFTradeMarketSystem` (quotes, pressure, sales, payout, provenance, the Sector Exchange's radio), the
partial `DispenserSystem.TradeGoods` hooked into the Crescent chute, `WFMarketCartridgeSystem` and the client `MarketUi`.
All tuning is in `WFTradeMarketDefault` (`Resources/Prototypes/_WF/TradeGoods/market.yml`). Market state lasts one round.

Provenance (`WFTradeOriginComponent`, not networked or saved): a crate is stamped where it first appears on a market
map (the sector map, or a map with `WFTradeMarketMapComponent`): at map init, on `FTLCompletedEvent`, by a 5 s sweep
that catches shipyard docking and z-level moves, or at sale as a last resort. An unknown origin counts as local, so a
crate printed in hyperspace, orbit or on an expedition is local to where the ship returns, and a ship bought or
reloaded at the shipyard is local to the station it is delivered to. A chute that moves (Jupiter, a merchant) and flies
to the crates has made the haul itself, but one that carried them docked has not: a stamp records the grids docked to
the crate's ship (`WFTradeOriginComponent.Docked`). A crate is refused if it was bought on the chute's grid, stamped on
it or on a ship docked to it, or stamped within `minHaulDistance` of the chute's current position. A crate already
taken by a chute and waiting for deletion is refused, so one crate can't pay at two chutes in a tick.

Faucet bound: above the floor, steady income per station and good is about `base x ln2 / (perSale x halfLife)` per
half-life. Fertilizer at 7000 with `perSale` 0.05 and a 25 minute half-life sells about 14 crates per half-life before
it nears its floor of 1000; printing it costs about 260 in materials and a 3 km haul each way.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/TradeGoods/Commands/WFTradeCommand.cs`](Commands/WFTradeCommand.cs)
- [`Content.Server/_WF/TradeGoods/Components/WFSpecialCrateComponents.cs`](Components/WFSpecialCrateComponents.cs)
- [`Content.Server/_WF/TradeGoods/Components/WFTradeMarketComponent.cs`](Components/WFTradeMarketComponent.cs)
- [`Content.Server/_WF/TradeGoods/DispenserSystem.TradeGoods.cs`](DispenserSystem.TradeGoods.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFMarketCartridgeSystem.cs`](Systems/WFMarketCartridgeSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeCrateMarketSystem.cs`](Systems/WFTradeCrateMarketSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeEscortSystem.cs`](Systems/WFTradeEscortSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeFreightSystem.cs`](Systems/WFTradeFreightSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeMarketSystem.Announce.cs`](Systems/WFTradeMarketSystem.Announce.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeMarketSystem.cs`](Systems/WFTradeMarketSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeMarketSystem.Origin.cs`](Systems/WFTradeMarketSystem.Origin.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeRushSystem.cs`](Systems/WFTradeRushSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeSectorSystem.cs`](Systems/WFTradeSectorSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeShortageSystem.cs`](Systems/WFTradeShortageSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeSpecialCrateSystem.cs`](Systems/WFTradeSpecialCrateSystem.cs)
- [`Content.Server/_WF/TradeGoods/Systems/WFTradeWarSystem.cs`](Systems/WFTradeWarSystem.cs)
- [`Content.Server/_WF/TradeGoods/WFTradeEvents.cs`](WFTradeEvents.cs)

### Shared

- [`Content.Shared/_WF/TradeGoods/WFMarketUiState.cs`](../../../Content.Shared/_WF/TradeGoods/WFMarketUiState.cs)
- [`Content.Shared/_WF/TradeGoods/WFTradeCrateMarketComponent.cs`](../../../Content.Shared/_WF/TradeGoods/WFTradeCrateMarketComponent.cs)
- [`Content.Shared/_WF/TradeGoods/WFTradeMarketPrototype.cs`](../../../Content.Shared/_WF/TradeGoods/WFTradeMarketPrototype.cs)
- [`Content.Shared/_WF/TradeGoods/WFTradeOriginComponent.cs`](../../../Content.Shared/_WF/TradeGoods/WFTradeOriginComponent.cs)

### Client

- [`Content.Client/_WF/TradeGoods/MarketUi.cs`](../../../Content.Client/_WF/TradeGoods/MarketUi.cs)
- [`Content.Client/_WF/TradeGoods/WFMarketSystem.cs`](../../../Content.Client/_WF/TradeGoods/WFMarketSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/TradeGoods/MarketAppTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/MarketAppTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/MarketHoldTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/MarketHoldTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/MarketPdaTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/MarketPdaTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/MarketUndergroundTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/MarketUndergroundTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeCargoTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeCargoTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeChuteTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeChuteTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeCommandTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeCommandTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeConsignmentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeConsignmentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeFreightTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeFreightTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeMarketTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeMarketTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradePricingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradePricingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeProvenanceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeProvenanceTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeRewardsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeRewardsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeTestWorld.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeTestWorld.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeVendorTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeVendorTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TradeGoods/TradeWarTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TradeGoods/TradeWarTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/TradeGoods/entities.yml`](../../../Resources/Prototypes/_WF/TradeGoods/entities.yml)
- [`Resources/Prototypes/_WF/TradeGoods/market.yml`](../../../Resources/Prototypes/_WF/TradeGoods/market.yml)
- [`Resources/Prototypes/_WF/TradeGoods/merchant.yml`](../../../Resources/Prototypes/_WF/TradeGoods/merchant.yml)

### Localization

- [`Resources/Locale/en-US/_WF/TradeGoods/trade-goods.ftl`](../../../Resources/Locale/en-US/_WF/TradeGoods/trade-goods.ftl)

### Textures

- [`Resources/Textures/_WF/TradeGoods/market.rsi/`](../../../Resources/Textures/_WF/TradeGoods/market.rsi/)

## Non-modular edits

- [`Content.Server/_Crescent/Dispenser/DispenserSystem.cs`](../../_Crescent/Dispenser/DispenserSystem.cs)
  - chutes refuse goods not hauled away from where they were made, and quote the rest live
  - chutes also take consignments they don't list
  - pay the live quote, print live prices
- [`Resources/Maps/_Mono/POI/underground.yml`](../../../Resources/Maps/_Mono/POI/underground.yml): The fence chute in the vendor room, beside the contraband vendor.
- [`Resources/Prototypes/_Crescent/Entities/Objects/Misc/trade.yml`](../../../Resources/Prototypes/_Crescent/Entities/Objects/Misc/trade.yml): chutes check where a crate was made
- [`Resources/Prototypes/_Mono/Entities/Structures/dispensers.yml`](../../../Resources/Prototypes/_Mono/Entities/Structures/dispensers.yml)
  - match the CC manifest, which lists no ship components and metals at 5000
  - match the Halcyon manifest, which buys fertilizer and no components
- [`Resources/Prototypes/_Mono/Entities/Structures/Machines/vending_machines.yml`](../../../Resources/Prototypes/_Mono/Entities/Structures/Machines/vending_machines.yml): no free crates from the eject wire or wrecking the vendor
- [`Resources/Prototypes/_NF/Entities/Structures/Specific/TradingCrates/crates.yml`](../../../Resources/Prototypes/_NF/Entities/Structures/Specific/TradingCrates/crates.yml): the pallet price follows the selling station's demand

<!-- WOLFGATE-GENERATED END -->
