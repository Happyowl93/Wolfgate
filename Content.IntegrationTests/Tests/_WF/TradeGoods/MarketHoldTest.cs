#nullable enable
using System.Numerics;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// The Market app's Hold tab prices each crate as a chute would: sealed freight at its sealed share, and nothing at
/// chutes too close to where the crate was made or for a crate not yet stamped, which a chute would refuse.
/// </summary>
[TestFixture]
[TestOf(typeof(WFMarketCartridgeSystem))]
public sealed class MarketHoldTest
{
    private const string Fabric = TradeTestWorld.Fabric;

    [Test]
    public async Task HoldPricesEachCrateAsTheChuteWould()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var cartridge = entMan.System<WFMarketCartridgeSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far yard");
            var near = TradeTestWorld.Grid(entMan, map, new Vector2(30f, 0f), "WF test near yard");
            var (_, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test fence", TradeTestWorld.Fence);

            // Sealed freight pays its sealed share at the lawful chute and full price at the fence.
            var sealedCrate = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            var origin = entMan.GetComponent<WFTradeOriginComponent>(sealedCrate);
            origin.Run = "WF test freighter";
            origin.Destination = near;
            var lawful = market.Quote(chute, Fabric, sealedCrate)!.Value.Amount;
            Assert.That(lawful, Is.LessThan(market.Quote(chute, Fabric)!.Value.Amount), "The seal should cut the lawful price.");
            var fenced = market.Quote(fence, Fabric, sealedCrate)?.Amount ?? 0;
            var best = cartridge.BestFor(sealedCrate, Fabric, null, null, market.Chutes());
            Assert.That(best?.Amount, Is.EqualTo(System.Math.Max(lawful, fenced)), "The hold should price sealed freight with its seal.");

            // Made right beside both chutes: neither takes it, so it sells nowhere yet.
            var local = TradeTestWorld.Hauled(entMan, near, map.GridCoords);
            Assert.That(cartridge.BestFor(local, Fabric, null, null, market.Chutes()), Is.Null);

            // Not yet stamped: a chute would stamp it there and refuse it, so the hold doesn't price it.
            var unstamped = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            entMan.GetComponent<WFTradeOriginComponent>(unstamped).Pending = true;
            Assert.That(cartridge.BestFor(unstamped, Fabric, null, null, market.Chutes()), Is.Null);
        });

        await pair.CleanReturnAsync();
    }
}
