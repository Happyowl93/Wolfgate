using System.Linq;
using Content.Server._Mono.AlertLevel;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.TradeGoods.Components;
using Content.Server.CartridgeLoader;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CargoLoad;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.TradeGoods;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>Marks the Market PDA app: live chute prices, sector alerts and the holder's hold.</summary>
[RegisterComponent]
public sealed partial class WFMarketCartridgeComponent : Component;

/// <summary>
/// Pushes the Market app's state to every open copy every few seconds and when one opens. UI state reaches every client
/// near the PDA, so a holder who sees the hidden markets gets a public copy there and the full one sent to the viewers.
/// </summary>
public sealed class WFMarketCartridgeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeShortageSystem _shortages = default!;
    [Dependency] private WFTradeRushSystem _rush = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;
    [Dependency] private WFTradeSpecialCrateSystem _special = default!;
    [Dependency] private WFTradeFreightSystem _freight = default!;
    [Dependency] private WFTradeCrateMarketSystem _crates = default!;
    [Dependency] private WFTradeEscortSystem _escort = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;
    [Dependency] private WFCargoLoadSystem _cargo = default!;
    [Dependency] private WarLevelSystem _war = default!;

    public static readonly EntProtoId MarketCartridge = "WFMarketCartridge";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>Hold crates whose origins fall in the same square of this many metres share one best-price lookup.</summary>
    private const float OriginCell = 250f;

    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMarketCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<PdaComponent, ComponentStartup>(OnPdaStartup);
    }

    /// <summary>
    /// Adds the Market app to every PDA's preinstalled programs before they install at map init, since most PDA
    /// prototypes replace the base list. Last in the list, so a full disk drops it rather than a job program.
    /// </summary>
    private void OnPdaStartup(Entity<PdaComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<CartridgeLoaderComponent>(ent, out var loader) && !loader.PreinstalledPrograms.Contains(MarketCartridge))
            loader.PreinstalledPrograms.Add(MarketCartridge);
    }

    private void OnUiReady(Entity<WFMarketCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        Push(args.Loader, Sector());
    }

    /// <summary>What one loader's Market app shows right now, hidden markets included for those who see them.</summary>
    public WFMarketUiState BuildState(EntityUid loader)
    {
        return State(loader, Sector(), out _);
    }

    /// <summary>
    /// Sends one loader its state. A holder who sees the hidden markets gets them in a private event to the viewers, and
    /// only the open markets in the networked UI state.
    /// </summary>
    private void Push(EntityUid loader, MarketSnapshot sector)
    {
        if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp))
            return;

        var state = State(loader, sector, out var underground);
        if (!underground)
        {
            _cartridgeLoader.UpdateCartridgeUiState(loader, state, loader: loaderComp);
            return;
        }

        var ev = new WFMarketPrivateStateEvent(GetNetEntity(loader), state);
        foreach (var actor in _ui.GetActors(loader, loaderComp.UiKey))
        {
            if (TryComp<ActorComponent>(actor, out var actorComp))
                RaiseNetworkEvent(ev, actorComp.PlayerSession);
        }

        _cartridgeLoader.UpdateCartridgeUiState(loader, new WFMarketUiState
        {
            Private = true,
            Ship = state.Ship,
            Stations = sector.Stations.Where(station => !station.Underground).ToList(),
            Alerts = sector.Alerts.Where(entry => !entry.Hidden).Select(entry => entry.Alert).ToList(),
        }, loader: loaderComp);
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
            // Only an open loader showing the Market app gets its state; any other would have its own screen replaced.
            if (cartridge.LoaderUid is { } loader
                && TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
                && loaderComp.ActiveProgram == uid
                && _ui.IsUiOpen(loader, loaderComp.UiKey))
                loaders.Add(loader);
        }

        if (loaders.Count == 0)
            return;

        var sector = Sector();
        foreach (var loader in loaders)
        {
            Push(loader, sector);
        }
    }

    /// <summary>
    /// What every copy is built from this refresh: all markets and alerts, which of them are off the books, and the
    /// cargo on each grid, gathered once on first use.
    /// </summary>
    private sealed record MarketSnapshot(
        List<WFMarketStation> Stations,
        List<(WFMarketAlert Alert, bool Hidden)> Alerts,
        List<(EntityUid Chute, DispenserComponent Dispenser, EntityUid Grid)> Chutes,
        HashSet<EntityUid> HiddenChutes)
    {
        public Dictionary<EntityUid, List<(EntityUid Uid, MetaDataComponent Meta)>>? Cargo;
    }

    /// <summary>The cargo aboard each grid, swept once per refresh however many copies are open.</summary>
    private Dictionary<EntityUid, List<(EntityUid Uid, MetaDataComponent Meta)>> Cargo(MarketSnapshot sector)
    {
        if (sector.Cargo is { } cached)
            return cached;

        var cargo = new Dictionary<EntityUid, List<(EntityUid, MetaDataComponent)>>();
        var query = EntityQueryEnumerator<WFCargoMassComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (xform.GridUid is not { } grid || meta.EntityPrototype == null)
                continue;

            if (!cargo.TryGetValue(grid, out var list))
                cargo[grid] = list = new List<(EntityUid, MetaDataComponent)>();

            list.Add((uid, meta));
        }

        sector.Cargo = cargo;
        return cargo;
    }

    /// <summary>The prices and alerts every copy shows, and the hidden markets.</summary>
    private MarketSnapshot Sector()
    {
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var stations = new Dictionary<EntityUid, WFMarketStation>();
        var order = new Dictionary<EntityUid, int>();
        var hiddenChutes = new HashSet<EntityUid>();
        var chutes = _market.Chutes();
        foreach (var (chute, dispenser, grid) in chutes)
        {
            var chuteId = MetaData(chute).EntityPrototype?.ID;
            if (!stations.TryGetValue(grid, out var station))
            {
                var index = profile.MarketFactions.FindIndex(faction => chuteId != null && faction.Chutes.Contains(chuteId));
                var faction = index >= 0 ? profile.MarketFactions[index] : null;
                stations[grid] = station = new WFMarketStation
                {
                    Name = Name(grid),
                    Faction = Loc.GetString(faction?.Name ?? profile.DefaultMarketFaction),
                    FactionColor = faction?.Color ?? profile.DefaultMarketFactionColor,
                };
                order[grid] = index >= 0 ? index : profile.MarketFactions.Count;
            }

            if (chuteId != null && profile.HiddenChutes.Contains(chuteId))
            {
                station.Underground = true;
                hiddenChutes.Add(chute);
            }

            foreach (var good in dispenser.Inventory.Keys)
            {
                if (_market.Quote(chute, good) is not { } quote || station.Prices.Any(price => price.Good == _market.GoodName(good)))
                    continue;

                station.Prices.Add(new WFMarketPrice(_market.GoodName(good), quote.Amount, quote.Factor,
                    Loc.GetString(quote.Reason), _cargo.GoodWeight(good), quote.Factor >= profile.Highlight));
            }

            station.Prices.Sort((a, b) => b.Amount.CompareTo(a.Amount));
        }

        // Trade vendors list what the station charges; a station with vendors and no chute still gets a card.
        var vendorGrids = new HashSet<EntityUid>();
        var vendors = EntityQueryEnumerator<WFTradeVendorComponent, TransformComponent>();
        while (vendors.MoveNext(out _, out _, out var vendorXform))
        {
            if (vendorXform.GridUid is { } grid && _market.IsMarketMap(vendorXform.MapID))
                vendorGrids.Add(grid);
        }

        foreach (var grid in vendorGrids)
        {
            if (!stations.TryGetValue(grid, out var station))
            {
                stations[grid] = station = new WFMarketStation
                {
                    Name = Name(grid),
                    Faction = Loc.GetString(profile.DefaultMarketFaction),
                    FactionColor = profile.DefaultMarketFactionColor,
                };
                order[grid] = profile.MarketFactions.Count;
            }

            foreach (var (good, quote) in _market.StationBuyQuotes(grid))
            {
                station.Buys.Add(new WFMarketPrice(_market.GoodName(good), quote.Amount, quote.Factor,
                    Loc.GetString(quote.Reason), _cargo.GoodWeight(good), false));
            }

            station.Buys.Sort((a, b) => a.Amount.CompareTo(b.Amount));
        }

        // An alert naming a hidden market is off the books too.
        var hiddenNames = stations.Values.Where(station => station.Underground).Select(station => station.Name).ToHashSet();
        var alerts = new List<(WFMarketAlert, bool)>();
        void Add(WFMarketAlert alert, string? station = null) => alerts.Add((alert, station != null && hiddenNames.Contains(station)));

        foreach (var rush in _rush.Active)
        {
            Add(new WFMarketAlert(Loc.GetString("wf-market-alert-rush", ("station", Name(rush.Grid)),
                ("left", rush.Remaining), ("count", rush.Total), ("goods", Loc.GetString(rush.Goods)), ("factor", rush.Factor)), rush.Ends, true), Name(rush.Grid));
        }

        foreach (var (destination, carrier, deadline) in _special.Consignments())
        {
            var name = Name(destination);
            Add(new WFMarketAlert(carrier == null
                ? Loc.GetString("wf-market-alert-consignment", ("station", name))
                : Loc.GetString("wf-market-alert-consignment-carried", ("station", name), ("ship", carrier)), deadline, true), name);
        }

        foreach (var (station, deadline) in _sector.Attempts())
        {
            Add(new WFMarketAlert(Loc.GetString("wf-market-alert-relief", ("station", Name(station)), ("factor", profile.ReliefFactor),
                ("bounty", _market.Profile(station).ReliefBounty)), deadline, true), Name(station));
        }

        foreach (var station in _sector.Blockaded())
        {
            Add(new WFMarketAlert(Loc.GetString("wf-market-alert-blockade", ("station", Name(station)), ("factor", profile.BlockadeFactor)), null, false, WFMarketAlertKind.Warning), Name(station));
        }

        foreach (var (station, ends) in _sector.Raided())
        {
            Add(new WFMarketAlert(Loc.GetString("wf-market-alert-raid", ("station", Name(station)), ("factor", _market.Profile(station).RaidFactor)), ends, false), Name(station));
        }

        foreach (var shortage in _shortages.Active)
        {
            Add(new WFMarketAlert(shortage.Grid is { } grid
                ? Loc.GetString("wf-market-alert-shortage", ("station", Name(grid)), ("goods", Loc.GetString(shortage.Name)), ("factor", shortage.Factor))
                : Loc.GetString("wf-market-alert-boom", ("goods", Loc.GetString(shortage.Name)), ("percent", (int) MathF.Round((shortage.Factor - 1f) * 100f))),
                shortage.Ends, false), shortage.Grid is { } shortageGrid ? Name(shortageGrid) : null);
        }

        foreach (var (ship, station, goods) in _freight.Inbound())
        {
            Add(new WFMarketAlert(Loc.GetString("wf-market-alert-freight", ("ship", ship), ("station", station), ("goods", goods)), null, false, WFMarketAlertKind.Traffic), station);
        }

        // A chute aboard an NPC hull is a wandering merchant's.
        foreach (var grid in chutes.Select(chute => chute.Grid).Distinct().Where(HasComp<WFEncounterGridComponent>))
        {
            var cell = WFSectorHex.Callsign(_territory.CellOf(_transform.GetMapCoordinates(grid)));
            Add(new WFMarketAlert(Loc.GetString("wf-market-alert-merchant", ("ship", Name(grid)), ("cell", cell)), null, false), Name(grid));
        }

        if (_war.GetWarLevel(EntityUid.Invalid))
            Add(new WFMarketAlert(Loc.GetString("wf-market-alert-war", ("factor", profile.WarFactor)), null, false));

        // Grouped by faction in the profile's order, then by name.
        var sorted = stations
            .OrderBy(entry => order[entry.Key])
            .ThenBy(entry => entry.Value.Name)
            .Select(entry => entry.Value)
            .ToList();
        return new MarketSnapshot(sorted, alerts, chutes, hiddenChutes);
    }

    /// <summary>
    /// One copy's state: the shared prices and alerts, its holder's own alerts, and their hold. Hidden markets, alerts
    /// naming them and hold sales there go only to holders of an underground company.
    /// </summary>
    private WFMarketUiState State(EntityUid loader, MarketSnapshot sector, out bool underground)
    {
        var holder = Transform(loader).ParentUid;
        var profile = _proto.Index(WFTradeMarketSystem.DefaultProfile);
        var company = CompOrNull<CompanyComponent>(holder)?.CompanyName;
        var sees = company is { } member && profile.UndergroundCompanies.Contains(member);
        underground = sees;

        var state = new WFMarketUiState
        {
            Stations = sees ? sector.Stations : sector.Stations.Where(station => !station.Underground).ToList(),
            Alerts = sector.Alerts.Where(entry => sees || !entry.Hidden).Select(entry => entry.Alert).ToList(),
        };

        var chutes = sees ? sector.Chutes : sector.Chutes.Where(chute => !sector.HiddenChutes.Contains(chute.Chute)).ToList();
        state.Alerts.AddRange(PersonalAlerts(holder, company, profile, chutes));

        if (Transform(loader).GridUid is not { } grid || !HasComp<ShuttleDeedComponent>(grid))
            return state;

        state.Ship = Name(grid);
        state.Alerts.AddRange(EscortAlerts(grid));
        var cargo = Cargo(sector);
        var best = new Dictionary<(string, EntityUid?, string?, EntityUid?, int, int), (string Station, int Amount)?>();
        var aboardCargo = _cargo.GetCargoSet(grid)
            .SelectMany(aboard => cargo.TryGetValue(aboard, out var crates) ? crates : Enumerable.Empty<(EntityUid Uid, MetaDataComponent Meta)>());
        foreach (var (uid, meta) in aboardCargo)
        {
            // The list is the trade goods, which are what the app can price.
            var proto = meta.EntityPrototype!;

            // A console trading crate sells only at its destination.
            if (_crates.Destination(uid) is { } destination)
            {
                state.Hold.Add(new WFMarketHoldEntry(meta.EntityName, destination.Station, destination.Amount, true));
                continue;
            }

            if (!TryComp<WFTradeOriginComponent>(uid, out var origin))
                continue;

            if (TryComp<WFConsignmentComponent>(uid, out var consignment) && consignment.Destination is { } consignee
                && !TerminatingOrDeleted(consignee))
            {
                state.Hold.Add(new WFMarketHoldEntry(meta.EntityName, Name(consignee),
                    _special.ConsignmentPayout(consignment), true));
                continue;
            }

            (string Station, int Amount)? top;
            var special = _special.IsSpecial(uid);
            if (special)
            {
                top = BestFor(uid, proto.ID, grid, holder, chutes);
            }
            else
            {
                // Crates of one good from one run and place get the same answer, so a full hold stays cheap.
                var key = (proto.ID, origin.Grid, origin.Run, origin.Destination,
                    (int) MathF.Floor(origin.Point.Position.X / OriginCell), (int) MathF.Floor(origin.Point.Position.Y / OriginCell));
                if (!best.TryGetValue(key, out top))
                    best[key] = top = BestFor(uid, proto.ID, grid, holder, chutes);
            }

            state.Hold.Add(new WFMarketHoldEntry(special ? meta.EntityName : _market.GoodName(proto.ID),
                top?.Station ?? Loc.GetString("wf-market-nowhere"), top?.Amount ?? 0));
        }

        return state;
    }

    /// <summary>
    /// Where one crate sells best now, pricing it as the chute would: its seal, destination bonus, the seller's faction and
    /// special payouts count, and chutes that would refuse it (too close to its origin, not yet stamped, or a trader the
    /// seller hurt) are skipped. Chutes on the seller's own ship are left out.
    /// </summary>
    public (string Station, int Amount)? BestFor(EntityUid crate, string good, EntityUid? except, EntityUid? seller,
        List<(EntityUid Chute, DispenserComponent Dispenser, EntityUid Grid)> chutes)
    {
        var special = _special.IsSpecial(crate);
        (string Station, int Amount)? best = null;
        foreach (var (chute, _, grid) in chutes)
        {
            if (grid == except
                || !_market.WasHauled(chute, crate)
                || seller is { } user && _market.Shunned(chute, user))
                continue;

            var amount = special ? _special.Price(chute, crate) : _market.Quote(chute, good, crate, seller)?.Amount;
            if (amount is { } price && (best is not { } current || price > current.Amount))
                best = (_market.StationName(chute), price);
        }

        return best;
    }

    /// <summary>Alerts about the holder: traders who won't deal with them and what the war offers their faction.</summary>
    private IEnumerable<WFMarketAlert> PersonalAlerts(EntityUid holder, ProtoId<CompanyPrototype>? company, WFTradeMarketPrototype profile,
        List<(EntityUid Chute, DispenserComponent Dispenser, EntityUid Grid)> chutes)
    {
        var shunned = new HashSet<EntityUid>();
        foreach (var (chute, _, grid) in chutes)
        {
            if (!shunned.Contains(grid) && _market.Shunned(chute, holder))
                shunned.Add(grid);
        }

        foreach (var grid in shunned)
        {
            yield return new WFMarketAlert(Loc.GetString("wf-market-alert-shunned", ("station", Name(grid))), null, false, WFMarketAlertKind.Warning);
        }

        if (company is not { } own || !_war.GetWarLevel(EntityUid.Invalid))
            yield break;

        var home = profile.Navies.FirstOrDefault(navy => navy.Companies.Contains(own));
        if (home is { CreditEntity: not null } && profile.WarEffortCredits > 0)
        {
            yield return new WFMarketAlert(Loc.GetString("wf-market-alert-war-effort", ("navy", Loc.GetString(home.Name)),
                ("credits", profile.WarEffortCredits)), null, false, WFMarketAlertKind.Personal);
        }

        foreach (var navy in profile.Navies.Where(navy => navy.Enemies.Contains(own)))
        {
            yield return new WFMarketAlert(Loc.GetString(home is { PostsDefectorWarrants: true } ? "wf-market-alert-defector-warrant" : "wf-market-alert-defector",
                ("navy", Loc.GetString(navy.Name)), ("factor", profile.DefectorFactor), ("warrant", profile.DefectorWarrant),
                ("own", home is { } ownNavy ? Loc.GetString(ownNavy.Name) : string.Empty)), null, false, WFMarketAlertKind.Personal);
        }
    }

    /// <summary>Convoy alerts for the holder's ship: the traders it is escorting, or the escorts beside it.</summary>
    private IEnumerable<WFMarketAlert> EscortAlerts(EntityUid ship)
    {
        foreach (var trader in _escort.EscortedBy(ship))
        {
            var (sold, threshold) = _escort.Progress(trader);
            yield return new WFMarketAlert(Loc.GetString("wf-market-alert-escorting", ("trader", Name(trader)),
                ("percent", WFTradeEscortSystem.SharePercent), ("sold", sold), ("threshold", threshold)), null, false, WFMarketAlertKind.Personal);
        }

        var escorts = _escort.EscortsOf(ship).Where(escort => !TerminatingOrDeleted(escort)).ToList();
        if (escorts.Count > 0)
        {
            var (sold, threshold) = _escort.Progress(ship);
            yield return new WFMarketAlert(Loc.GetString("wf-market-alert-escorted", ("ships", string.Join(", ", escorts.Select(escort => Name(escort)))),
                ("percent", WFTradeEscortSystem.SharePercent), ("sold", sold), ("threshold", threshold)), null, false, WFMarketAlertKind.Personal);
        }
    }
}
