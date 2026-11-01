using System.Linq;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Extensions;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Cargo.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Market;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._NF.Market.Systems;

/// <summary>What a station pays and charges per unit of a material now, zero where it can't; prices include console modifiers.</summary>
public readonly record struct WFMaterialQuote(string Good, EntityUid Station, string Name, double Pays, double Charges, int Stock);

/// <summary>
/// Station stock as a market for materials. Pallets pay and consoles charge by how much of a material a station holds,
/// sold lots reach the console after a delay, stock drains over time and supply lines restock the everyday materials.
/// Materials in a console's cart stay the station's stock until they are bought, so a cart can't move prices or lock one.
/// Materials are appraised after <c>PriceCalculationEvent</c>, so a pallet adds the difference itself.
/// </summary>
public sealed partial class MarketSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFTradeMarketSystem _tradeMarket = default!;

    private static readonly TimeSpan WfStepInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _wfNextStep;

    /// <summary>Each entity's adjustment this tick, so an appraisal and a sale agree.</summary>
    private readonly Dictionary<EntityUid, double> _wfAdjusted = new();

    /// <summary>Units priced this tick per station and material: a sale walks down the curve instead of paying one rate.</summary>
    private readonly Dictionary<(EntityUid Station, string Good), int> _wfAhead = new();

    private GameTick _wfTick;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _wfNextStep)
            return;

        _wfNextStep = _timing.CurTime + WfStepInterval;
        var query = EntityQueryEnumerator<CargoMarketDataComponent>();
        while (query.MoveNext(out var uid, out var market))
        {
            WfStep((uid, market), WfStepInterval);
        }
    }

    /// <summary>Advances one station's materials: lots arrive, stock drains and console prices follow what is left.</summary>
    public void WfStep(Entity<CargoMarketDataComponent> ent, TimeSpan elapsed)
    {
        var profile = _tradeMarket.Profile(null);
        var market = ent.Comp;
        for (var i = market.Arriving.Count - 1; i >= 0; i--)
        {
            var lot = market.Arriving[i];
            lot.Left -= elapsed;
            if (lot.Left > TimeSpan.Zero)
                continue;

            market.MarketDataList.Upsert(lot.Good, lot.Count, WfUnitPrice(lot.Good), WfStackId(lot.Good));
            market.Arriving.RemoveAt(i);
        }

        var share = 1.0 - Math.Pow(0.5, elapsed.TotalSeconds / Math.Max(1.0, profile.HalfLife.TotalSeconds));
        var carts = WfCarts(ent);
        WfSettle(market.MarketDataList, market, carts, profile, share);
        foreach (var cart in carts)
        {
            WfSettle(cart, market, carts, profile, share);
        }
    }

    /// <summary>Drains and reprices the materials in one list of a station's stock.</summary>
    private void WfSettle(List<MarketData> stock, CargoMarketDataComponent market, List<List<MarketData>> carts,
        WFTradeMarketPrototype profile, double share)
    {
        for (var i = stock.Count - 1; i >= 0; i--)
        {
            var entry = stock[i];
            if (!profile.MaterialGoods.Contains(entry.Prototype))
                continue;

            // Rounded by chance so small stocks of dear materials drain at the same rate.
            var drained = entry.Quantity * share;
            entry.Quantity -= (int) drained + (_random.Prob((float) (drained % 1.0)) ? 1 : 0);
            if (entry.Quantity <= 0)
            {
                stock.RemoveAt(i);
                continue;
            }

            var unit = WfUnitPrice(entry.Prototype);
            entry.Price = unit * WfChargeFactor(profile, WfSaturation(profile, WfSupply(market, carts, entry.Prototype), unit));
        }
    }

    /// <summary>What a sold entity and its contents gain or lose from the selling station's stock, before console modifiers.</summary>
    public double WfPalletAdjustment(EntityUid sold, EntityUid grid)
    {
        if (_station.GetOwningStation(grid) is not { } station || !TryComp<CargoMarketDataComponent>(station, out var market))
            return 0.0;

        if (_wfTick != _timing.CurTick)
        {
            _wfTick = _timing.CurTick;
            _wfAdjusted.Clear();
            _wfAhead.Clear();
        }

        if (_wfAdjusted.TryGetValue(sold, out var known))
            return known;

        var adjustment = WfAdjustment(sold, (station, market), WfCarts(station), _tradeMarket.Profile(null));
        _wfAdjusted[sold] = adjustment;
        return adjustment;
    }

    private double WfAdjustment(EntityUid uid, Entity<CargoMarketDataComponent> market, List<List<MarketData>> carts,
        WFTradeMarketPrototype profile)
    {
        var adjustment = 0.0;
        if (TryComp<StackComponent>(uid, out var stack) && WfGood(stack) is { } good)
        {
            var unit = WfUnitPrice(good);
            var key = (market.Owner, good);
            var ahead = _wfAhead.GetValueOrDefault(key);
            _wfAhead[key] = ahead + stack.Count;
            // The curve is linear either side of the target, so its midpoint prices the whole stack.
            var supply = WfSupply(market.Comp, carts, good) + ahead + stack.Count / 2.0;
            adjustment += unit * stack.Count * (WfPayFactor(profile, WfSaturation(profile, supply, unit)) - 1.0);
        }

        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return adjustment;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                adjustment += WfAdjustment(contained, market, carts, profile);
            }
        }

        return adjustment;
    }

    /// <summary>What a pallet pays for a material at a stock level, as a share of its value.</summary>
    public static float WfPayFactor(WFTradeMarketPrototype profile, double saturation)
    {
        return WfCurve(profile, saturation, profile.MaterialPayMax, profile.MaterialPayMin);
    }

    /// <summary>What a console charges for a material at a stock level, as a share of its value.</summary>
    public static float WfChargeFactor(WFTradeMarketPrototype profile, double saturation)
    {
        return WfCurve(profile, saturation, profile.MaterialChargeMax, profile.MaterialChargeMin);
    }

    /// <summary>Falls from a high at no stock to 1 at the target, then to a low at a glut.</summary>
    private static float WfCurve(WFTradeMarketPrototype profile, double saturation, float high, float low)
    {
        if (saturation <= 1.0)
            return (float) (high + (1.0 - high) * Math.Max(saturation, 0.0));

        return (float) (1.0 + (low - 1.0) * Math.Min((saturation - 1.0) / Math.Max(profile.MaterialGlut - 1.0, 0.01), 1.0));
    }

    private static double WfSaturation(WFTradeMarketPrototype profile, double supply, double unit)
    {
        return supply * unit / Math.Max(profile.MaterialStockTarget, 1f);
    }

    /// <summary>Whether a stack trades through station stock.</summary>
    public bool WfIsMaterial(StackComponent stack)
    {
        return WfGood(stack) != null;
    }

    /// <summary>A material's single-unit prototype, or null for a stack that doesn't trade through station stock.</summary>
    private string? WfGood(StackComponent stack)
    {
        if (!_prototypeManager.TryIndex<StackPrototype>(stack.StackTypeId, out var proto))
            return null;

        return _tradeMarket.Profile(null).MaterialGoods.Contains(proto.Spawn) ? proto.Spawn.Id : null;
    }

    /// <summary>What one unit of a material appraises at.</summary>
    private double WfUnitPrice(string good)
    {
        return _prototypeManager.TryIndex<EntityPrototype>(good, out var proto) ? _pricingSystem.GetEstimatedPrice(proto) : 0.0;
    }

    private string? WfStackId(string good)
    {
        return _prototypeManager.TryIndex<EntityPrototype>(good, out var proto)
               && proto.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory)
            ? stack.StackTypeId
            : null;
    }

    /// <summary>The carts of a station's market consoles: stock players have set aside but not bought.</summary>
    private List<List<MarketData>> WfCarts(EntityUid station)
    {
        var carts = new List<List<MarketData>>();
        var consoles = EntityQueryEnumerator<MarketConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var xform))
        {
            if (console.CartDataList.Count > 0 && _station.GetOwningStation(uid, xform) == station)
                carts.Add(console.CartDataList);
        }

        return carts;
    }

    /// <summary>A station's stock of a material, counting what is in carts and on its way to the console.</summary>
    private int WfSupply(CargoMarketDataComponent market, List<List<MarketData>> carts, string good)
    {
        var supply = market.MarketDataList.FirstOrDefault(entry => entry.Prototype == good)?.Quantity ?? 0;
        foreach (var cart in carts)
        {
            supply += cart.FirstOrDefault(entry => entry.Prototype == good)?.Quantity ?? 0;
        }

        foreach (var lot in market.Arriving)
        {
            if (lot.Good == good)
                supply += lot.Count;
        }

        return supply;
    }

    /// <summary>Holds a sold material back from the console, or returns false for anything else.</summary>
    private bool WfQueueMaterial(CargoMarketDataComponent market, string good, int count)
    {
        var profile = _tradeMarket.Profile(null);
        if (!profile.MaterialGoods.Contains(good))
            return false;

        market.Arriving.Add(new WFArrivingMaterial(good, count, profile.MaterialArrival));
        return true;
    }

    /// <summary>One supply pulse of everyday materials at a station, never past the target. Returns whether it added any.</summary>
    public bool WfRestock(EntityUid grid)
    {
        if (_station.GetOwningStation(grid) is not { } station || !TryComp<CargoMarketDataComponent>(station, out var market))
            return false;

        var profile = _tradeMarket.Profile(null);
        var carts = WfCarts(station);
        var restocked = false;
        foreach (var good in profile.SupplyMaterials)
        {
            var unit = WfUnitPrice(good);
            if (unit <= 0.0)
                continue;

            var target = profile.MaterialStockTarget / unit;
            var amount = (int) Math.Min(target * profile.MaterialPulse, target - WfSupply(market, carts, good));
            if (amount <= 0)
                continue;

            market.MarketDataList.Upsert(good, amount, unit, WfStackId(good));
            restocked = true;
        }

        return restocked;
    }

    /// <summary>Every market map station's prices for every material, at its best pallet and cheapest console.</summary>
    public List<WFMaterialQuote> WfQuotes()
    {
        var pays = new Dictionary<EntityUid, float>();
        var pallets = EntityQueryEnumerator<CargoPalletConsoleComponent, MarketModifierComponent, TransformComponent>();
        while (pallets.MoveNext(out var uid, out _, out var modifier, out var xform))
        {
            if (!modifier.Buy && _tradeMarket.IsMarketMap(xform.MapID) && _station.GetOwningStation(uid, xform) is { } station)
                pays[station] = Math.Max(pays.GetValueOrDefault(station), modifier.Mod);
        }

        var charges = new Dictionary<EntityUid, float>();
        var consoles = EntityQueryEnumerator<MarketConsoleComponent, MarketModifierComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var modifier, out var xform))
        {
            if (_tradeMarket.IsMarketMap(xform.MapID) && _station.GetOwningStation(uid, xform) is { } station)
                charges[station] = charges.TryGetValue(station, out var lowest) ? Math.Min(lowest, modifier.Mod) : modifier.Mod;
        }

        var profile = _tradeMarket.Profile(null);
        var quotes = new List<WFMaterialQuote>();
        var markets = EntityQueryEnumerator<CargoMarketDataComponent>();
        while (markets.MoveNext(out var uid, out var market))
        {
            if (!pays.TryGetValue(uid, out var pay) & !charges.TryGetValue(uid, out var charge))
                continue;

            var carts = WfCarts(uid);
            var name = Name(_station.GetLargestGrid(uid) ?? uid);
            foreach (var good in profile.MaterialGoods)
            {
                var unit = WfUnitPrice(good);
                var entry = market.MarketDataList.FirstOrDefault(data => data.Prototype == good.Id);
                var stock = entry?.Quantity ?? 0;
                quotes.Add(new WFMaterialQuote(good,
                    uid,
                    name,
                    unit * pay * WfPayFactor(profile, WfSaturation(profile, WfSupply(market, carts, good), unit)),
                    stock > 0 ? entry!.Price * charge : 0.0,
                    stock));
            }
        }

        return quotes;
    }
}
