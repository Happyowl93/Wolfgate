#nullable enable
using System.Collections.Generic;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Station trade vendors charge a live price: a good turns scarce as its stock runs low, a shortage raises it further,
/// a freighter's restock lifts it, and it never falls below the vendor's base price.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeMarketSystem))]
public sealed class TradeBuyPriceTest
{
    private const string Vendor = "VendingMachineTraderDepot";
    private const string Fabric = TradeTestWorld.Fabric;
    private static readonly EntProtoId Paper = "PaperWrittenDispenserInstructions";

    /// <summary>A crate of fabric bought from a vendor.</summary>
    private static void Buy(IEntityManager entMan, EntityCoordinates coords)
    {
        entMan.AddComponent<VendingMachinePurchaseComponent>(entMan.SpawnEntity(Fabric, coords));
    }

    [Test]
    public async Task VendorChargesTheLivePrice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var vendor = entMan.SpawnEntity(Vendor, map.GridCoords);
            var fabric = server.ProtoMan.Index<EntityPrototype>(Fabric);

            var fresh = market.BuyQuote(vendor, Fabric)!.Value;
            Assert.That(fresh.Base, Is.GreaterThan(0));
            Assert.That(fresh.Amount, Is.EqualTo(fresh.Base), "A quiet station should charge the base price.");
            Assert.That(market.BuyPrice(vendor, fabric, fresh.Base), Is.EqualTo(fresh.Amount), "The vend hook should charge the quote.");

            // The price holds as stock falls, right down to where scarcity starts.
            var start = (int) System.MathF.Round(profile.VendorStock * profile.BuyScarcityStart);
            market.SetVendorStock(grid, Fabric, start);
            var holding = market.BuyQuote(vendor, Fabric)!.Value;
            Assert.That(holding.Amount, Is.EqualTo(fresh.Base), "A good at the start of scarcity should still charge base.");
            Assert.That(holding.Reason, Is.EqualTo((LocId) "wf-trade-reason-steady"));

            // A purchase leaves the stock at once but only reaches the price after a delay.
            Buy(entMan, map.GridCoords);
            Assert.That(market.VendorStock(grid, Fabric), Is.EqualTo(start - 1), "A purchase should come off the station's stock.");
            Assert.That(market.BuyQuote(vendor, Fabric)!.Value.Amount, Is.EqualTo(fresh.Base), "A purchase shouldn't move the price yet.");
            var unpriced = entMan.GetComponent<WFTradeMarketComponent>(grid).Unpriced[Fabric];
            Assert.That(unpriced[0] - timing.CurTime, Is.InRange(profile.PriceDelayMin, profile.PriceDelayMax));

            // After it, the good is scarce from the first crate below the start.
            unpriced.Clear();
            market.SetVendorStock(grid, Fabric, start - 1);
            var scarce = market.BuyQuote(vendor, Fabric)!.Value;
            var expected = (int) System.MathF.Round(fresh.Base * (1 + profile.BuyScarcityCap / start));
            Assert.That(scarce.Amount, Is.EqualTo(expected));
            Assert.That(scarce.Amount, Is.GreaterThan(fresh.Base), "One crate below the start should already cost more.");
            Assert.That(scarce.Reason, Is.EqualTo((LocId) "wf-trade-reason-scarce"));
            Assert.That(entMan.GetComponent<WFTradeVendorComponent>(vendor).Prices[Fabric], Is.EqualTo(expected),
                "The vendor's menu price wasn't updated after a purchase.");

            // It climbs steadily and reaches the cap at empty.
            market.SetVendorStock(grid, Fabric, 0);
            Assert.That(market.Scarcity(grid, Fabric), Is.EqualTo(profile.BuyScarcityCap).Within(0.001f));
            market.SetVendorStock(grid, Fabric, start / 2);
            var halfway = profile.BuyScarcityCap * (1f - (start / 2) / (float) start);
            Assert.That(market.Scarcity(grid, Fabric), Is.EqualTo(halfway).Within(0.001f));

            // A shortage there raises it on top.
            shortages.StartLocal(grid, new HashSet<string> { Fabric }, 2f, System.TimeSpan.FromMinutes(5), "wf-trade-goods-textiles");
            var short_ = market.BuyQuote(vendor, Fabric)!.Value;
            Assert.That(short_.Reason, Is.EqualTo((LocId) "wf-trade-reason-short"));
            Assert.That(short_.Factor, Is.EqualTo(2f * (1 + halfway)).Within(0.01f));
            shortages.Clear();

            // A freighter's restock lifts scarcity, and the price never drops below base.
            market.RestockVendors(grid, profile.VendorStock);
            Assert.That(market.BuyQuote(vendor, Fabric)!.Value.Amount, Is.EqualTo(fresh.Base));

            // Anything that isn't a trade good keeps the machine's own price.
            Assert.That(market.BuyPrice(vendor, server.ProtoMan.Index(Paper), 123), Is.EqualTo(123));
        });

        await pair.CleanReturnAsync();
    }
}
