using Content.Server._WF.Encounters;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Trade;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Cargo-console trading crates in the station markets. Their pallet price is scaled by the selling grid's demand,
/// each sale adds pressure there, and they count toward a ship's cargo value.
/// </summary>
public sealed class WFTradeCrateMarketSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFTradeCrateMarketComponent, PriceCalculationEvent>(OnPrice, after: new[] { typeof(CargoSystem) });
        SubscribeLocalEvent<EntitySoldEvent>(OnSold);
        SubscribeLocalEvent<WFCargoValueQueryEvent>(OnCargoValue);
    }

    /// <summary>The share of its price a trading crate fetches at a grid now.</summary>
    public float Factor(EntityUid grid, WFTradeCrateMarketComponent crate)
    {
        var profile = _market.Profile(grid);
        return MathF.Max(1f - _market.Pressure(grid, crate.Good), profile.TradeCrateFloor);
    }

    /// <summary>Where a trading crate is bound and what it fetches there now, or null without a destination.</summary>
    public (string Station, int Amount)? Destination(EntityUid crate)
    {
        if (!TryComp<TradeCrateComponent>(crate, out var trade)
            || !TryComp<WFTradeCrateMarketComponent>(crate, out var market)
            || TerminatingOrDeleted(trade.DestinationStation))
            return null;

        var amount = (float) trade.ValueAtDestination;
        if (trade.ExpressDeliveryTime is { } due)
            amount += _timing.CurTime <= due ? trade.ExpressOnTimeBonus : -trade.ExpressLatePenalty;

        if (_station.GetLargestGrid(trade.DestinationStation) is { } grid)
            amount *= Factor(grid, market);

        return (Name(trade.DestinationStation), (int) MathF.Max(0f, amount));
    }

    private void OnPrice(Entity<WFTradeCrateMarketComponent> ent, ref PriceCalculationEvent args)
    {
        if (Transform(ent).GridUid is { } grid)
            args.Price *= Factor(grid, ent.Comp);
    }

    private void OnSold(ref EntitySoldEvent args)
    {
        foreach (var sold in args.Sold)
        {
            if (TryComp<WFTradeCrateMarketComponent>(sold, out var crate))
                _market.AddPressure(args.Grid, crate.Good, _market.Profile(args.Grid).TradeCratePerSale);
        }
    }

    private void OnCargoValue(ref WFCargoValueQueryEvent args)
    {
        var query = EntityQueryEnumerator<WFTradeCrateMarketComponent, TradeCrateComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var trade, out var xform))
        {
            if (xform.GridUid == args.Grid)
                args.Value += trade.ValueAtDestination;
        }
    }
}
