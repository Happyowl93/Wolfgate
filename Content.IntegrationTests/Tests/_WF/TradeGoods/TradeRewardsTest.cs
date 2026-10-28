#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Server._WF.TradeGoods;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.GameTicking;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Payouts beyond the chute price: rush orders raise a station's quote until filled or expired, and ships
/// that fly beside a laden trader split a bonus when it sells a big haul.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeRushSystem))]
public sealed class TradeRewardsTest
{
    private const string Fabric = TradeTestWorld.Fabric;
    private const string Metals = "TradeGoodMetals";
    private const string Scrap = "TradeGoodScrap";
    private static readonly ProtoId<WFTradeRushPrototype> IndustrialRush = "WFTradeRushIndustrial";

    /// <summary>A real round: rush orders only expire while one is running.</summary>
    [Test]
    public async Task RushOrdersPayCountAndExpire()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, DummyTicker = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var rushes = entMan.System<WFTradeRushSystem>();
        var ticker = entMan.System<GameTicker>();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid chute = default;
        WFTradeRush expiring = default!;

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "The test needs a round running.");
            chute = TradeTestWorld.MarketChute(entMan, map);
            var grid = map.Grid.Owner;
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far yard");
            var seller = entMan.SpawnEntity(null, map.GridCoords);
            const string hauler = "WF rush test hauler";
            entMan.System<MetaDataSystem>().SetEntityName(seller, hauler);

            var rush = rushes.Start(IndustrialRush)!;
            Assert.That(rush, Is.Not.Null, "No station could post an industrial rush.");
            Assert.That(rush.Grid, Is.EqualTo(grid), "The rush went to a station other than the test's.");
            Assert.That(rush.Remaining, Is.EqualTo(rush.Total));

            var quote = market.Quote(chute, Metals)!.Value;
            Assert.That(quote.Reason, Is.EqualTo((LocId) "wf-trade-reason-rush"));
            Assert.That(quote.Factor, Is.EqualTo(rush.Factor).Within(0.01f));

            market.Sell(chute, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Metals), seller);
            Assert.That(rush.Remaining, Is.EqualTo(rush.Total - 1));
            Assert.That(rush.Filled.GetValueOrDefault(hauler), Is.EqualTo(1));

            for (var i = 0; i < 20 && rush.Remaining > 0; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Metals), seller);
            }

            Assert.That(rush.Remaining, Is.Zero);
            Assert.That(rushes.Active, Does.Not.Contain(rush), "A filled rush stayed open.");
            Assert.That(market.Quote(chute, Scrap)!.Value.Reason, Is.Not.EqualTo((LocId) "wf-trade-reason-rush"));

            // A second order nobody fills runs out at its end time.
            expiring = rushes.Start(IndustrialRush)!;
            Assert.That(expiring, Is.Not.Null);
            Assert.That(expiring.Grid, Is.EqualTo(grid));
            Assert.That(market.Quote(chute, Scrap)!.Value.Reason, Is.EqualTo((LocId) "wf-trade-reason-rush"));
            expiring.Ends = timing.CurTime;
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(rushes.Active, Does.Not.Contain(expiring), "An expired rush stayed open.");
            var scrap = market.Quote(chute, Scrap)!.Value;
            Assert.That(scrap.Amount, Is.EqualTo(scrap.Base), "An expired rush still raised the price.");
        });

        await pair.CleanReturnAsync();
    }

    /// <remarks>
    /// Escorts are sampled only from crewed ships, which needs player sessions; the samples are put in by hand and
    /// the sale path is driven for real.
    /// </remarks>
    [Test]
    public async Task EscortsSplitABigHaul()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var escorts = entMan.System<WFTradeEscortSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var grid = map.Grid.Owner;
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far yard");
            var trader = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF escort test trader");
            var wingman = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -120f), "WF escort test wingman");
            var straggler = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -180f), "WF escort test straggler");
            var seller = entMan.SpawnEntity(null, map.GridCoords);

            var samplesField = typeof(WFTradeEscortSystem).GetField("_samples", BindingFlags.NonPublic | BindingFlags.Instance);
            var thresholdField = typeof(WFTradeEscortSystem).GetField("Threshold", BindingFlags.NonPublic | BindingFlags.Static);
            var shareField = typeof(WFTradeEscortSystem).GetField("Share", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(samplesField, Is.Not.Null, "WFTradeEscortSystem._samples was renamed; update this test.");
            Assert.That(thresholdField, Is.Not.Null, "WFTradeEscortSystem.Threshold was renamed; update this test.");
            Assert.That(shareField, Is.Not.Null, "WFTradeEscortSystem.Share was renamed; update this test.");
            var samples = (Dictionary<EntityUid, List<(TimeSpan, HashSet<EntityUid>)>>) samplesField!.GetValue(escorts)!;
            var threshold = (int) thresholdField!.GetValue(null)!;
            var share = (float) shareField!.GetValue(null)!;

            // The wingman flew beside the trader all along; the straggler once in three looks.
            var now = timing.CurTime;
            samples[trader] = new List<(TimeSpan, HashSet<EntityUid>)>
            {
                (now, new HashSet<EntityUid> { wingman, straggler }),
                (now, new HashSet<EntityUid> { wingman }),
                (now, new HashSet<EntityUid> { wingman }),
            };

            var first = threshold / 2;
            Sell(first);
            Assert.That(escorts.Progress(trader).Sold, Is.EqualTo(first), "The trader's sales weren't counted toward escort pay.");

            // The haul is big enough: it is shared out (to nobody, with no one aboard the escorts) and the window starts over.
            Sell(threshold - first);
            Assert.That(escorts.Progress(trader).Sold, Is.Zero, "A big haul wasn't shared out among the escorts.");
            Assert.That(share, Is.GreaterThan(0f));

            void Sell(int amount)
            {
                var crate = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
                entMan.EventBus.RaiseEvent(EventSource.Local, new WFTradeSaleEvent(grid, chute, Fabric, amount, seller, trader, crate));
            }
        });

        await pair.CleanReturnAsync();
    }
}
