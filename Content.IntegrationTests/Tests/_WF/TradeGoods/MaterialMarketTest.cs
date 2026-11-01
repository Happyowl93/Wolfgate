#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server._WF.Encounters;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Market.Events;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Maps;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>Materials trade through station stock: prices follow supply, lots arrive late, stock drains and supply refills it.</summary>
[TestFixture]
[TestOf(typeof(MarketSystem))]
public sealed class MaterialMarketTest
{
    private const string Steel = "SheetSteel1";
    private const string Plasma = "SheetPlasma1";

    /// <summary>A station on its own grid with a cargo market.</summary>
    private static (EntityUid Station, EntityUid Grid) Market(IEntityManager entMan, TestMapData map)
    {
        var grid = TradeTestWorld.Grid(entMan, map, new Vector2(0f, 80f), "WF test market");
        var station = entMan.Spawn();
        entMan.EnsureComponent<StationDataComponent>(station);
        entMan.System<StationSystem>().AddGridToStation(station, grid);
        entMan.EnsureComponent<CargoMarketDataComponent>(station);
        return (station, grid);
    }

    private static EntityUid Stack(IEntityManager entMan, EntityUid grid, string proto, int count)
    {
        var stack = entMan.SpawnEntity(proto, new EntityCoordinates(grid, 0.5f, 0.5f));
        entMan.System<StackSystem>().SetCount(stack, count);
        return stack;
    }

    /// <summary>Full stacks of a material, since a stack holds at most 100.</summary>
    private static HashSet<EntityUid> Stacks(IEntityManager entMan, EntityUid grid, string proto, int stacks)
    {
        var spawned = new HashSet<EntityUid>();
        for (var i = 0; i < stacks; i++)
        {
            spawned.Add(Stack(entMan, grid, proto, 100));
        }

        return spawned;
    }

    private static void Sell(IEntityManager entMan, EntityUid grid, HashSet<EntityUid> sold)
    {
        var ev = new EntitySoldEvent(sold, grid);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }

    private static void Step(IEntityManager entMan, EntityUid station, TimeSpan elapsed)
    {
        entMan.System<MarketSystem>().WfStep((station, entMan.GetComponent<CargoMarketDataComponent>(station)), elapsed);
    }

    [Test]
    public async Task SoldMaterialsArriveLateAndThenDrain()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<MarketSystem>();

        await server.WaitAssertion(() =>
        {
            var (station, grid) = Market(entMan, map);
            var profile = entMan.System<WFTradeMarketSystem>().Profile(null);
            Sell(entMan, grid, Stacks(entMan, grid, Steel, 20));
            Assert.That(TradeTestWorld.Stock(entMan, station, Steel), Is.Zero, "A sold lot reached the console at once.");

            Step(entMan, station, profile.MaterialArrival / 2);
            Assert.That(TradeTestWorld.Stock(entMan, station, Steel), Is.Zero, "A sold lot arrived early.");

            // The step it lands on also drains it, which is a few percent over half the delay.
            Step(entMan, station, profile.MaterialArrival / 2);
            var stock = TradeTestWorld.Stock(entMan, station, Steel);
            Assert.That(stock, Is.InRange(1800, 2000), "A sold lot never arrived.");

            Step(entMan, station, profile.HalfLife);
            Assert.That(TradeTestWorld.Stock(entMan, station, Steel), Is.InRange(stock / 2 - 2, stock / 2 + 2), "Stock should halve over a half-life.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PalletPayFollowsSupplyAndWalksDownTheCurve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<MarketSystem>();
        var pricing = entMan.System<PricingSystem>();

        await server.WaitAssertion(() =>
        {
            var (station, grid) = Market(entMan, map);
            var small = Stack(entMan, grid, Steel, 1);
            var premium = market.WfPalletAdjustment(small, grid);
            Assert.That(premium, Is.GreaterThan(0.0), "A station with none should pay over value.");
            Assert.That(market.WfPalletAdjustment(small, grid), Is.EqualTo(premium), "An appraisal and a sale in one tick should agree.");

            // A sale walks the curve: by the 60th stack this tick the station is past a glut.
            var glut = Stacks(entMan, grid, Steel, 60).ToList();
            var adjustments = glut.Select(stack => market.WfPalletAdjustment(stack, grid)).ToList();
            var value = pricing.GetPrice(glut[^1]);
            var profile = entMan.System<WFTradeMarketSystem>().Profile(null);
            Assert.That(adjustments[0], Is.GreaterThan(adjustments[^1]), "Later stacks in a sale should fetch less.");
            Assert.That(value + adjustments[^1], Is.EqualTo(value * profile.MaterialPayMin).Within(value * 0.01));

            var cash = entMan.SpawnEntity("SpaceCash1000", new EntityCoordinates(grid, 0.5f, 0.5f));
            Assert.That(market.WfPalletAdjustment(cash, grid), Is.Zero, "Cash is not a material.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public void TheCurvesFallThroughFullValueAtTheTarget()
    {
        var profile = new WFTradeMarketPrototype();
        Assert.Multiple(() =>
        {
            Assert.That(MarketSystem.WfPayFactor(profile, 0.0), Is.EqualTo(profile.MaterialPayMax).Within(0.001f));
            Assert.That(MarketSystem.WfPayFactor(profile, 1.0), Is.EqualTo(1f).Within(0.001f));
            Assert.That(MarketSystem.WfPayFactor(profile, 100.0), Is.EqualTo(profile.MaterialPayMin).Within(0.001f));
            Assert.That(MarketSystem.WfChargeFactor(profile, 0.0), Is.EqualTo(profile.MaterialChargeMax).Within(0.001f));
            Assert.That(MarketSystem.WfChargeFactor(profile, 100.0), Is.EqualTo(profile.MaterialChargeMin).Within(0.001f));
        });
    }

    /// <summary>Selling a lot and buying it back must lose money at the tightest pallet and console pair on any station.</summary>
    [Test]
    public async Task SellingAndBuyingBackNeverProfits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            const float pallet = 1.25f;
            const float console = 1.5f;
            var profile = protoMan.Index(WFTradeMarketSystem.DefaultProfile);
            for (var sold = 0.05; sold <= 10.0; sold += 0.05)
            {
                // A lot sold from empty fetches its midpoint rate, and buys back at the rate its stock leaves.
                var pays = pallet * MarketSystem.WfPayFactor(profile, sold / 2.0);
                var charges = console * MarketSystem.WfChargeFactor(profile, sold);
                Assert.That(pays, Is.LessThan(charges), $"A lot of {sold}x the target sold back at a profit.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SupplyRestocksEverydayMaterialsUpToTheTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<MarketSystem>();

        await server.WaitAssertion(() =>
        {
            var (station, grid) = Market(entMan, map);
            var profile = entMan.System<WFTradeMarketSystem>().Profile(null);
            var target = (int) (profile.MaterialStockTarget / entMan.System<PricingSystem>().GetPrice(Stack(entMan, grid, Steel, 1)));

            Assert.That(market.WfRestock(grid), Is.True);
            Assert.That(TradeTestWorld.Stock(entMan, station, Steel), Is.EqualTo((int) (target * profile.MaterialPulse)).Within(1));
            Assert.That(TradeTestWorld.Stock(entMan, station, Plasma), Is.Zero, "Supply should leave precious materials to miners.");

            for (var i = 0; i < 1.5f / profile.MaterialPulse; i++)
            {
                market.WfRestock(grid);
            }

            Assert.That(TradeTestWorld.Stock(entMan, station, Steel), Is.EqualTo(target).Within(1), "Supply should top up to the target and stop.");
            Assert.That(market.WfRestock(grid), Is.False, "A full station should take nothing.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MaterialsCountAsCargoAndCashDoesNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid ship = default;
        var value = 0;

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, 120f), "WF test miner", 4);
            entMan.SpawnEntity("SpaceCash1000", new EntityCoordinates(ship, 0.5f, 0.5f));
            var ev = new WFCargoValueQueryEvent(ship);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(ev.Value, Is.Zero, "Cash is not cargo.");
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            ship = TradeTestWorld.Grid(entMan, map, new Vector2(0f, 140f), "WF test laden miner", 4);
            value = (int) entMan.System<PricingSystem>().GetPrice(Stack(entMan, ship, Plasma, 100));
            var ev = new WFCargoValueQueryEvent(ship);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(ev.Value, Is.EqualTo(value).Within(1), "Loose sheets should count as cargo.");

            var silo = entMan.SpawnEntity("MachineMaterialSilo", new EntityCoordinates(ship, 1.5f, 0.5f));
            Assert.That(entMan.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(silo, "Plasma", 1000), Is.True);
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            var ev = new WFCargoValueQueryEvent(ship);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(ev.Value, Is.EqualTo(value + 200).Within(1), "Stored materials should count as cargo.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A market station with a console and a pallet on a market map, as the Market app and carts see one.</summary>
    private static (EntityUid Station, EntityUid Grid, EntityUid Console) Trading(IEntityManager entMan, TestMapData map)
    {
        entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
        var (station, grid) = Market(entMan, map);
        var console = entMan.SpawnEntity("ComputerMarketConsoleNFNormal", new EntityCoordinates(grid, 0.5f, 0.5f));
        entMan.SpawnEntity("ComputerPalletConsoleNFNormalMarket", new EntityCoordinates(grid, 0.5f, 0.5f));
        return (station, grid, console);
    }

    [Test]
    public async Task TheAppCopesWithMaterialsNobodyStocks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cartridge = entMan.System<WFMarketCartridgeSystem>();

        await server.WaitAssertion(() =>
        {
            var (station, grid, _) = Trading(entMan, map);
            var pda = entMan.SpawnEntity(null, map.GridCoords);

            var empty = cartridge.BuildState(pda).Materials.First(material => material.Good == "steel");
            Assert.That(empty.BestPay, Is.GreaterThan(0.0), "A pallet should still quote a material nobody stocks.");
            Assert.That(empty.Cheapest, Is.Zero);
            Assert.That(empty.CheapestStation, Is.Empty);

            entMan.System<MarketSystem>().WfRestock(grid);
            Step(entMan, station, TimeSpan.FromSeconds(5));
            var stocked = cartridge.BuildState(pda).Materials.First(material => material.Good == "steel");
            Assert.That(stocked.Cheapest, Is.GreaterThan(0.0));
            Assert.That(stocked.Stock, Is.GreaterThan(0));
            Assert.That(stocked.CheapestStation, Is.EqualTo("WF test market"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABuyOnlyStationIsNeverListedAsStock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cartridge = entMan.System<WFMarketCartridgeSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            var (station, grid) = Market(entMan, map);
            entMan.SpawnEntity("ComputerPalletConsoleNFHighMarket", new EntityCoordinates(grid, 0.5f, 0.5f));
            entMan.System<MarketSystem>().WfRestock(grid);
            Step(entMan, station, TimeSpan.FromSeconds(5));
            Assert.That(TradeTestWorld.Stock(entMan, station, Steel), Is.GreaterThan(0));

            var steel = cartridge.BuildState(entMan.SpawnEntity(null, map.GridCoords)).Materials.First(material => material.Good == "steel");
            Assert.That(steel.BestPayStation, Is.EqualTo("WF test market"), "A pallet with no console should still be listed as a sale.");
            Assert.That(steel.Cheapest, Is.Zero);
            Assert.That(steel.CheapestStation, Is.Empty, "Stock with no console to buy it from shouldn't be listed.");
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("CargoDepot")]
    [TestCase("CargoDepotAlt")]
    [TestCase("WFAsfLantern")]
    [TestCase("TSFMCIndustry")]
    public async Task StationsWithPalletsHaveACargoMarket(string gameMap)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var stationProto = server.ProtoMan.Index<GameMapPrototype>(gameMap).Stations[gameMap].StationPrototype;

        Assert.That(server.ProtoMan.Index<EntityPrototype>(stationProto).TryGetComponent<CargoMarketDataComponent>(out _, server.EntMan.ComponentFactory),
            $"{gameMap} sells materials outside the market.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CartsStayPartOfTheStock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<MarketSystem>();

        await server.WaitAssertion(() =>
        {
            var (station, grid, console) = Trading(entMan, map);
            market.WfRestock(grid);
            Step(entMan, station, TimeSpan.FromSeconds(5));
            double Pays() => market.WfQuotes().First(quote => quote.Good == Steel).Pays;
            var before = Pays();
            var quantity = TradeTestWorld.Stock(entMan, station, Steel);

            var buyer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<BankAccountComponent>(buyer);
            var carting = new MarketConsoleCartMessage(quantity, Steel, false) { Actor = buyer };
            entMan.EventBus.RaiseLocalEvent(console, carting);
            Assert.That(TradeTestWorld.Stock(entMan, station, Steel), Is.Zero, "The whole stock should be in the cart.");
            Assert.That(Pays(), Is.EqualTo(before).Within(0.001), "Carting stock shouldn't raise what the pallet pays.");

            var cart = entMan.GetComponent<MarketConsoleComponent>(console).CartDataList;
            var calm = cart[0].Price;
            Sell(entMan, grid, Stacks(entMan, grid, Steel, 20));
            Step(entMan, station, TimeSpan.FromSeconds(5));
            Assert.That(cart[0].Price, Is.LessThan(calm), "A cart shouldn't lock in an old price.");

            var held = cart[0].Quantity;
            Step(entMan, station, entMan.System<WFTradeMarketSystem>().Profile(null).HalfLife);
            Assert.That(cart[0].Quantity, Is.InRange(held / 2 - 2, held / 2 + 2), "A cart shouldn't keep stock from draining.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryMaterialGoodIsAPricedSingleStack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;
        var pricing = server.EntMan.System<PricingSystem>();

        await server.WaitAssertion(() =>
        {
            var profile = protoMan.Index(WFTradeMarketSystem.DefaultProfile);
            Assert.That(profile.MaterialGoods, Is.Not.Empty);
            foreach (var good in profile.MaterialGoods)
            {
                var proto = protoMan.Index<EntityPrototype>(good);
                Assert.That(proto.TryGetComponent<MaterialComponent>(out _, factory), Is.True, $"{good} isn't a material.");
                Assert.That(proto.TryGetComponent<StackComponent>(out var stack, factory), Is.True, $"{good} isn't a stack.");
                Assert.That(protoMan.Index<StackPrototype>(stack!.StackTypeId).Spawn.Id, Is.EqualTo(good.Id), $"{good} isn't its stack's single unit.");
                Assert.That(pricing.GetEstimatedPrice(proto), Is.GreaterThan(0.0), $"{good} has no value.");
            }

            foreach (var good in profile.SupplyMaterials)
            {
                Assert.That(profile.MaterialGoods, Does.Contain(good), $"{good} is restocked by supply lines but doesn't trade through stock.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
