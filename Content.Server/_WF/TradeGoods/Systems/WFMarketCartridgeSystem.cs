using System.Linq;
using Content.Server._Mono.AlertLevel;
using Content.Server._WF.CargoLoad;
using Content.Server.CartridgeLoader;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CargoLoad;
using Content.Shared._WF.TradeGoods;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>Marks the Market PDA app: live chute prices, sector alerts and the holder's hold.</summary>
[RegisterComponent]
public sealed partial class WFMarketCartridgeComponent : Component;

/// <summary>Pushes the Market app's state to every installed copy every few seconds and when one opens.</summary>
public sealed class WFMarketCartridgeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeShortageSystem _shortages = default!;
    [Dependency] private WFTradeRushSystem _rush = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;
    [Dependency] private WFTradeSpecialCrateSystem _special = default!;
    [Dependency] private WFTradeFreightSystem _freight = default!;
    [Dependency] private WFTradeCrateMarketSystem _crates = default!;
    [Dependency] private WFCargoLoadSystem _cargo = default!;
    [Dependency] private WarLevelSystem _war = default!;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMarketCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    private void OnUiReady(Entity<WFMarketCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        var (stations, alerts, hunterAlerts) = Sector();
        _cartridgeLoader.UpdateCartridgeUiState(args.Loader, State(args.Loader, stations, alerts, hunterAlerts));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;
        var loaders = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFMarketCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out _, out var cartridge))
        {
            // Only a loader showing the Market app gets its state; any other would have its own screen replaced.
            if (cartridge.LoaderUid is { } loader
                && CompOrNull<CartridgeLoaderComponent>(loader)?.ActiveProgram == uid)
                loaders.Add(loader);
        }

        if (loaders.Count == 0)
            return;

        var (stations, alerts, hunterAlerts) = Sector();
        foreach (var loader in loaders)
        {
            _cartridgeLoader.UpdateCartridgeUiState(loader, State(loader, stations, alerts, hunterAlerts));
        }
    }

    /// <summary>The prices and alerts every copy shows, and the extra alerts only hunters see.</summary>
    private (List<WFMarketStation> Stations, List<WFMarketAlert> Alerts, List<WFMarketAlert> HunterAlerts) Sector()
    {
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var stations = new Dictionary<EntityUid, WFMarketStation>();
        foreach (var (chute, dispenser, grid) in _market.Chutes())
        {
            if (!stations.TryGetValue(grid, out var station))
                stations[grid] = station = new WFMarketStation { Name = Name(grid) };

            foreach (var good in dispenser.Inventory.Keys)
            {
                if (_market.Quote(chute, good) is not { } quote || station.Prices.Any(price => price.Good == _market.GoodName(good)))
                    continue;

                station.Prices.Add(new WFMarketPrice(_market.GoodName(good), quote.Amount, quote.Factor,
                    Loc.GetString(quote.Reason), _cargo.GoodWeight(good), quote.Factor >= profile.Highlight));
            }

            station.Prices.Sort((a, b) => b.Amount.CompareTo(a.Amount));
        }

        var alerts = new List<WFMarketAlert>();
        foreach (var rush in _rush.Active)
        {
            alerts.Add(new WFMarketAlert(Loc.GetString("wf-market-alert-rush", ("station", Name(rush.Grid)),
                ("left", rush.Remaining), ("count", rush.Total), ("goods", Loc.GetString(rush.Goods)), ("factor", rush.Factor)), rush.Ends, true));
        }

        foreach (var (destination, carrier, deadline) in _special.Consignments())
        {
            alerts.Add(new WFMarketAlert(carrier == null
                ? Loc.GetString("wf-market-alert-consignment", ("station", destination))
                : Loc.GetString("wf-market-alert-consignment-carried", ("station", destination), ("ship", carrier)), deadline, true));
        }

        foreach (var (station, deadline) in _sector.Attempts())
        {
            alerts.Add(new WFMarketAlert(Loc.GetString("wf-market-alert-relief", ("station", Name(station)), ("factor", profile.ReliefFactor)), deadline, true));
        }

        foreach (var station in _sector.Blockaded())
        {
            alerts.Add(new WFMarketAlert(Loc.GetString("wf-market-alert-blockade", ("station", Name(station)), ("factor", profile.BlockadeFactor)), null, false));
        }

        foreach (var shortage in _shortages.Active)
        {
            alerts.Add(new WFMarketAlert(shortage.Grid is { } grid
                ? Loc.GetString("wf-market-alert-shortage", ("station", Name(grid)), ("goods", Loc.GetString(shortage.Name)), ("factor", shortage.Factor))
                : Loc.GetString("wf-market-alert-boom", ("goods", Loc.GetString(shortage.Name)), ("percent", (int) MathF.Round((shortage.Factor - 1f) * 100f))),
                shortage.Ends, false));
        }

        foreach (var (ship, station, goods) in _freight.Inbound())
        {
            alerts.Add(new WFMarketAlert(Loc.GetString("wf-market-alert-freight", ("ship", ship), ("station", station), ("goods", goods)), null, false));
        }

        if (_war.GetWarLevel(EntityUid.Invalid))
            alerts.Add(new WFMarketAlert(Loc.GetString("wf-market-alert-war", ("factor", profile.WarFactor)), null, false));

        var hunterAlerts = new List<WFMarketAlert>();
        foreach (var (cell, heard, ship, jammed) in _special.Sightings())
        {
            var minutes = (int) (_timing.CurTime - heard).TotalMinutes;
            hunterAlerts.Add(new WFMarketAlert(Loc.GetString(jammed ? "wf-market-alert-classified-jammed" : "wf-market-alert-classified",
                ("cell", cell), ("minutes", minutes), ("ship", ship ?? Loc.GetString("wf-market-adrift"))), null, true));
        }

        return (stations.Values.OrderBy(station => station.Name).ToList(), alerts, hunterAlerts);
    }

    /// <summary>One copy's state: the shared prices and alerts, plus its holder's hold.</summary>
    private WFMarketUiState State(EntityUid loader, List<WFMarketStation> stations, List<WFMarketAlert> alerts, List<WFMarketAlert> hunterAlerts)
    {
        var state = new WFMarketUiState { Stations = stations, Alerts = new List<WFMarketAlert>(alerts) };

        var holder = Transform(loader).ParentUid;
        var hunters = _proto.Index(WFTradeMarketSystem.DefaultProfile).ClassifiedHunters;
        if (CompOrNull<CompanyComponent>(holder)?.CompanyName is { } company && hunters.Contains(company))
            state.Alerts.InsertRange(0, hunterAlerts);

        if (Transform(loader).GridUid is not { } grid || !HasComp<ShuttleDeedComponent>(grid))
            return state;

        state.Ship = Name(grid);
        var set = _cargo.GetCargoSet(grid);
        state.HoldTonnes = _cargo.GetSetLoad(set);
        state.Thrust = _cargo.GetSpaceFactor(grid);

        var best = new Dictionary<string, (string Station, int Amount)?>();
        var query = EntityQueryEnumerator<WFCargoMassComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var mass, out var xform, out var meta))
        {
            // The total counts every load; the list is the trade goods, which are what the app can price.
            if (xform.GridUid is not { } aboard || !set.Contains(aboard) || meta.EntityPrototype is not { } proto)
                continue;

            // A console trading crate sells only at its destination.
            if (_crates.Destination(uid) is { } destination)
            {
                state.Hold.Add(new WFMarketHoldEntry(meta.EntityName, _cargo.Weight(uid, mass), destination.Station, destination.Amount, true));
                continue;
            }

            if (!HasComp<WFTradeOriginComponent>(uid))
                continue;

            if (!best.TryGetValue(proto.ID, out var top))
            {
                top = _market.Best(proto.ID, grid) is { } found ? (found.Station, found.Amount) : null;
                best[proto.ID] = top;
            }

            state.Hold.Add(new WFMarketHoldEntry(_market.GoodName(proto.ID), _cargo.Weight(uid, mass),
                top?.Station ?? Loc.GetString("wf-market-nowhere"), top?.Amount ?? 0));
        }

        return state;
    }
}
