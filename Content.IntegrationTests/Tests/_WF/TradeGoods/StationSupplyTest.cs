#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server._WF.Traders;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.TradeGoods;
using Content.Shared._WF.Traders;
using Content.Shared.GameTicking;
using Content.Shared.Station.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>Vendors hold finite stock, supply lines refill stations, and disruption slows them until it is relieved.</summary>
[TestFixture]
[TestOf(typeof(WFTradeSupplySystem))]
public sealed class StationSupplyTest
{
    private const string Vendor = "VendingMachineTraderTradeMall";
    private const string Fence = "WFVendingMachineFence";
    private const string Luxuries = "TradeGoodLuxuries";
    private const string Contraband = "WFTradeGoodContraband";
    private const string Medicine = "TradeGoodMedicalSupplies";
    private const string Steel = "SheetSteel1";
    private const string Blockader = "WFTestSupplyBlockader";

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfSectorFaction
  id: WFTestSupplyBlockader
  name: cmd-wf_sector-hint-faction
  color: Red
  blockades: true
";

    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);

    /// <summary>A vendor on a grid, repriced so its stock is finite.</summary>
    private static (EntityUid Vendor, VendingMachineInventoryEntry Entry) StockedVendor(IEntityManager entMan, EntityUid grid)
    {
        var vendor = entMan.SpawnEntity(Vendor, new EntityCoordinates(grid, 0.5f, 0.5f));
        entMan.System<WFTradeMarketSystem>().AddScarcity(grid, Luxuries, 0f);
        return (vendor, entMan.GetComponent<VendingMachineComponent>(vendor).Inventory[Luxuries]);
    }

    private static HashSet<string> Medical => new() { "TradeGoodMedicalSupplies" };

    [Test]
    public async Task VendorsRunOutAndSupplyRefillsThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var supply = entMan.System<WFTradeSupplySystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var (_, entry) = StockedVendor(entMan, grid);
            var full = market.Profile(grid).VendorStock;
            Assert.That(entry.Amount, Is.EqualTo((uint) full), "A trade vendor should start with a full, finite stock.");

            market.SetVendorStock(grid, Luxuries, 0);
            Assert.That(entry.Amount, Is.Zero);
            Assert.That(market.StationBuyQuotes(grid).ContainsKey(Luxuries), Is.False, "A sold-out good should drop off the app.");

            supply.Pulse();
            Assert.That(entry.Amount, Is.EqualTo(1u), "A pulse should bring a crate.");
            Assert.That(market.StationBuyQuotes(grid).ContainsKey(Luxuries), Is.True);

            market.RestockVendors(grid, full * 2);
            Assert.That(entry.Amount, Is.EqualTo((uint) full), "Restocking should stop at a full stock.");

            var fence = entMan.SpawnEntity(Fence, map.GridCoords);
            market.AddScarcity(grid, Luxuries, 0f);
            var contraband = entMan.GetComponent<VendingMachineComponent>(fence).Inventory[Contraband];
            Assert.That(contraband.Amount, Is.EqualTo(uint.MaxValue), "Contraband should never run out.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AShortageHalvesVendorRefillUntilItIsFilled()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var supply = entMan.System<WFTradeSupplySystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            shortages.Clear();
            var (_, entry) = StockedVendor(entMan, grid);
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var user = entMan.SpawnEntity(null, map.GridCoords);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far");

            shortages.StartLocal(grid, new HashSet<string> { TradeTestWorld.Fabric }, 1.5f, Duration, "wf-trade-goods-textiles");
            var shortage = shortages.Active[0];
            Assert.That(shortage.Needed, Is.GreaterThan(0));
            Assert.That(shortages.IsShort(grid), Is.True);

            market.SetVendorStock(grid, Luxuries, 0);
            for (var i = 0; i < 4; i++)
            {
                supply.Pulse();
            }

            Assert.That(entry.Amount, Is.EqualTo(2u), "A short station's vendors should refill at half speed.");

            var needed = shortage.Needed;
            market.Sell(chute, entMan.SpawnEntity(TradeTestWorld.Fabric, map.GridCoords), user);
            Assert.That(shortage.Needed, Is.EqualTo(needed), "A crate made on the station shouldn't fill its shortage.");

            for (var i = 0; i < needed; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(entMan, far, map.GridCoords), user);
            }

            Assert.That(shortages.IsShort(grid), Is.False, "Enough hauled crates should end a shortage.");

            shortages.StartLocal(grid, Medical, 1.5f, Duration, "wf-trade-goods-medical");
            shortages.Relieve(grid);
            Assert.That(shortages.IsShort(grid), Is.False, "A freighter getting through should end a shortage.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FuelShortagesRaiseRefuelling()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var supply = entMan.System<WFTradeSupplySystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();

        await server.WaitAssertion(() =>
        {
            shortages.Clear();
            TradeTestWorld.MarketChute(entMan, map);
            var trader = entMan.SpawnEntity(null, map.GridCoords);
            var elsewhere = entMan.SpawnEntity(null, new EntityCoordinates(TradeTestWorld.Grid(entMan, map, new Vector2(400f, 0f), "WF test other"), 0.5f, 0.5f));
            Assert.That(supply.RefuelFactor(trader), Is.EqualTo(1f));

            var shortage = shortages.Start("WFTradeShortageFuel");
            Assert.That(shortage, Is.Not.Null);
            Assert.That(supply.RefuelFactor(trader), Is.EqualTo(shortage!.Factor).Within(0.001f));
            Assert.That(supply.RefuelFactor(elsewhere), Is.EqualTo(1f), "Another station's shortage shouldn't raise refuelling here.");
            shortages.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABlockadeCutsMaterialsAndSlowsVendors()
    {
        // Dirty: the test makes its map the sector map in a round, so the pair needs a round restart before reuse.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var stock = entMan.System<MarketSystem>();
        var supply = entMan.System<WFTradeSupplySystem>();
        var territory = entMan.System<WFSectorTerritorySystem>();
        var sector = entMan.System<WFTradeSectorSystem>();

        await server.WaitAssertion(() =>
        {
            var ticker = entMan.System<GameTicker>();
            SetPrivate(ticker, nameof(GameTicker.DefaultMap), map.MapId);
            SetPrivate(ticker, nameof(GameTicker.RunLevel), GameRunLevel.InRound);

            var blockadedCell = new WFSectorCell(4, -2);
            var openCell = new WFSectorCell(-4, 2);
            var (blockaded, blockadedStation) = Station(entMan, map, WFSectorHex.Centre(blockadedCell, territory.CellSize), "WF test cut off");
            var (open, openStation) = Station(entMan, map, WFSectorHex.Centre(openCell, territory.CellSize), "WF test open");
            Assert.That(territory.TryClaim(map.MapId, blockadedCell, Blockader, null), Is.True);
            Assert.That(sector.IsBlockaded(blockaded), Is.True);
            Assert.That(sector.IsBlockaded(open), Is.False);

            var (_, cutOff) = StockedVendor(entMan, blockaded);
            var (_, flowing) = StockedVendor(entMan, open);
            var market = entMan.System<WFTradeMarketSystem>();
            market.SetVendorStock(blockaded, Luxuries, 0);
            market.SetVendorStock(open, Luxuries, 0);
            for (var i = 0; i < 4; i++)
            {
                supply.Pulse();
            }

            Assert.That(TradeTestWorld.Stock(entMan, blockadedStation, Steel), Is.Zero, "A blockade should cut a station's materials.");
            Assert.That(TradeTestWorld.Stock(entMan, openStation, Steel), Is.GreaterThan(0), "An open station should keep getting them.");
            Assert.That(cutOff.Amount, Is.EqualTo(2u), "A blockaded station's vendors should still refill slowly.");
            Assert.That(flowing.Amount, Is.EqualTo(4u));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStationsVendorsShareOneStock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var (_, first) = StockedVendor(entMan, grid);
            var (_, second) = StockedVendor(entMan, grid);
            var full = (uint) market.Profile(grid).VendorStock;
            Assert.That(first.Amount, Is.EqualTo(full));
            Assert.That(second.Amount, Is.EqualTo(full));

            var crate = entMan.SpawnEntity(Luxuries, map.GridCoords);
            entMan.AddComponent<VendingMachinePurchaseComponent>(crate);
            Assert.That(market.VendorStock(grid, Luxuries), Is.EqualTo((int) full - 1), "A purchase should come off the station's stock.");
            Assert.That(first.Amount, Is.EqualTo(full - 1), "Every vendor should show the shared stock.");
            Assert.That(second.Amount, Is.EqualTo(full - 1), "Every vendor should show the shared stock.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FuelVendorsChargeShortagePrices()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        EntityUid vendor = default;
        var factor = 1f;

        await server.WaitAssertion(() =>
        {
            shortages.Clear();
            TradeTestWorld.MarketChute(entMan, map);
            vendor = entMan.SpawnEntity("VendingMachineFuelVend", map.GridCoords);
            Assert.That(market.BuyPrice(vendor, protos.Index<EntityPrototype>("FuelPlasma"), 100), Is.EqualTo(100));
            factor = shortages.Start("WFTradeShortageFuel")!.Factor;
        });

        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var expected = (int) MathF.Round(100 * factor);
            Assert.That(entMan.GetComponent<WFShortagePricedVendorComponent>(vendor).Factor, Is.EqualTo(factor).Within(0.001f),
                "The vendor's factor should follow the shortage so its menu shows the price.");
            Assert.That(market.BuyPrice(vendor, protos.Index<EntityPrototype>("FuelPlasma"), 100), Is.EqualTo(expected));
            shortages.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FuelShortagesRaiseTheRefuelQuote()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var refuel = entMan.System<TraderRefuelSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();

        await server.WaitAssertion(() =>
        {
            shortages.Clear();
            var grid = map.Grid.Owner;
            TradeTestWorld.MarketChute(entMan, map);
            var trader = entMan.SpawnEntity("WFTraderFuelTechnician", map.GridCoords);
            entMan.SpawnEntity("PortableGeneratorJrPacmanShuttle", map.GridCoords);
            var comp = entMan.GetComponent<TraderRefuelComponent>(trader);

            Assert.That(refuel.GetQuote((trader, comp), grid, out var calm), Is.Not.Empty, "The generator should need fuel.");
            var shortage = shortages.Start("WFTradeShortageFuel");
            refuel.GetQuote((trader, comp), grid, out var dear);
            Assert.That(dear, Is.EqualTo(calm * shortage!.Factor).Within(1.0), "The final quote should carry the shortage's factor.");
            shortages.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FreightRunsOutsideTheStorytellersCap()
    {
        // Dirty: the test makes its map the sector map in a round, so the pair needs a round restart before reuse.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var scheduler = entMan.System<WFEncounterSchedulerSystem>();
        var encounters = entMan.System<WFEncounterSystem>();

        await server.WaitAssertion(() =>
        {
            SetPrivate(entMan.System<GameTicker>(), nameof(GameTicker.DefaultMap), map.MapId);
            foreach (var (x, name) in new[] { (0f, "WF test a"), (9000f, "WF test b"), (18000f, "WF test c") })
            {
                var (grid, _) = Station(entMan, map, new Vector2(x, 0f), name);
                entMan.EnsureComponent<DockingComponent>(entMan.SpawnEntity(null, new EntityCoordinates(grid, 0.5f, 0.5f)));
            }

            var budget = encounters.ActiveCost();
            var count = encounters.ActiveCount();
            Assert.That(scheduler.TryScheduleFreight(out var run), Is.True, "A freight run should start on its own schedule.");
            var comp = entMan.GetComponent<WFEncounterComponent>(run);
            Assert.That(comp.Freight, Is.True);
            Assert.That(comp.Stops.Count, Is.GreaterThanOrEqualTo(2), "A freight run should call at stations.");
            Assert.That(encounters.ActiveFreight(), Is.EqualTo(1));
            Assert.That(encounters.ActiveCount(), Is.EqualTo(count), "A freight run shouldn't take a storyteller slot.");
            Assert.That(encounters.ActiveCost(), Is.EqualTo(budget), "A freight run shouldn't take storyteller budget.");

            for (var i = 0; i < 6; i++)
            {
                scheduler.TryScheduleFreight(out _);
            }

            var cap = server.CfgMan.GetCVar(Content.Shared._WF.CCVar.EncountersCVars.FreightMaxActive);
            Assert.That(encounters.ActiveFreight(), Is.LessThanOrEqualTo(cap), "Freight should keep to its own cap.");

            foreach (var uid in entMan.EntityQuery<WFEncounterComponent>().Select(running => running.Owner).ToList())
            {
                encounters.End(uid);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RaidersOnTheApproachesBlockadeAStation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var sector = entMan.System<WFTradeSectorSystem>();
        var encounters = entMan.System<WFEncounterSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var chute = TradeTestWorld.MarketChute(entMan, map);
            entMan.EnsureComponent<DockingComponent>(entMan.SpawnEntity(null, map.GridCoords));
            var profile = market.Profile(grid);
            var calm = market.Quote(chute, Medicine)!.Value.Amount;

            var run = entMan.Spawn();
            var comp = entMan.AddComponent<WFEncounterComponent>(run);
            comp.Prototype = "WFEncounterStationBlockade";
            comp.Blockades = true;
            comp.Stops.Add(grid);
            Assert.That(sector.IsBlockaded(grid), Is.False, "An encounter that hasn't begun blockades nothing.");

            comp.Begun = true;
            Assert.That(sector.IsBlockaded(grid), Is.True);
            Assert.That(sector.Blockaded(), Does.Contain(grid), "The Market app should list the blockade.");
            var scheduler = entMan.System<WFEncounterSchedulerSystem>();
            Assert.That(scheduler.Stations(map.MapId).Select(station => station.Owner), Does.Not.Contain(grid),
                "Freighters should stay away from a blockaded station.");
            Assert.That(entMan.System<WFSectorTerritorySystem>().Stations(map.MapId).Select(station => station.Owner), Does.Contain(grid),
                "Sector control's own station list should keep a blockaded station.");
            var quote = market.Quote(chute, Medicine)!.Value;
            Assert.That(quote.Amount, Is.EqualTo((int) (calm * profile.BlockadeFactor)).Within(1), "A blockaded station should pay relief prices.");
            Assert.That(quote.Reason, Is.EqualTo((LocId) "wf-trade-reason-blockade"));

            encounters.Resolve(run, WFEncounterResolution.Destroyed);
            Assert.That(sector.IsBlockaded(grid), Is.False, "Destroying the raiders should lift the blockade.");
            entMan.DeleteEntity(run);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A named station grid with a cargo market, and its station.</summary>
    private static (EntityUid Grid, EntityUid Station) Station(IEntityManager entMan, TestMapData map, Vector2 position, string name)
    {
        var grid = TradeTestWorld.Grid(entMan, map, position, name);
        var station = entMan.Spawn();
        entMan.EnsureComponent<StationDataComponent>(station);
        entMan.System<StationSystem>().AddGridToStation(station, grid);
        entMan.EnsureComponent<CargoMarketDataComponent>(station);
        return (grid, station);
    }

    private static void SetPrivate(GameTicker ticker, string property, object value)
    {
        typeof(GameTicker).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(ticker, new[] { value });
    }
}
