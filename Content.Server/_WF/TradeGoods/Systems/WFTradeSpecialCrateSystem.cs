using System.Linq;
using Content.Server._Mono.AlertLevel;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.TradeGoods.Components;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Crates worth chasing. Priority consignments appear at a station for another one, pay well on time and are announced
/// when a ship takes them. Now and then a bought or freighted crate carries a TSF transponder: once it leaves its
/// station, PDV and the Underground are told roughly where it is, and whoever turns it in at their own drop-off is paid.
/// </summary>
public sealed class WFTradeSpecialCrateSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;
    [Dependency] private WarLevelSystem _war = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _nextCheck;
    private TimeSpan? _nextConsignment;
    private TimeSpan _nextClassified;

    /// <summary>Consignments each ship delivered on time this round, for the honours.</summary>
    public readonly Dictionary<string, int> Couriers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VendingMachinePurchaseComponent, ComponentStartup>(OnPurchased);
        SubscribeLocalEvent<WFConsignmentComponent, ExaminedEvent>(OnConsignmentExamined);
        SubscribeLocalEvent<WFClassifiedCargoComponent, ExaminedEvent>(OnClassifiedExamined);
        SubscribeLocalEvent<WFClassifiedCargoComponent, ComponentShutdown>(OnClassifiedShutdown);
        SubscribeLocalEvent<WFTransponderJammerComponent, AfterInteractEvent>(OnJammerUsed);
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _nextConsignment = null;
        _nextClassified = TimeSpan.Zero;
        Couriers.Clear();
    }

    private WFTradeMarketPrototype Profile => _proto.Index(WFTradeMarketSystem.DefaultProfile);

    /// <summary>Whether a chute should price a crate here instead of from its own table.</summary>
    public bool IsSpecial(EntityUid crate)
    {
        return HasComp<WFConsignmentComponent>(crate) || CompOrNull<WFClassifiedCargoComponent>(crate)?.Awake == true;
    }

    /// <summary>What a chute pays for a special crate; false, with a popup, if it won't take it.</summary>
    public bool TryPrice(EntityUid chute, EntityUid crate, EntityUid user, out int amount)
    {
        amount = 0;
        var grid = Transform(chute).GridUid;
        if (TryComp<WFConsignmentComponent>(crate, out var consignment))
        {
            if (consignment.Destination != grid)
            {
                amount = consignment.Elsewhere;
                return true;
            }

            amount = Math.Max(0, consignment.Value
                                 + (_timing.CurTime <= consignment.Deadline ? consignment.OnTimeBonus : -consignment.LatePenalty));
            return true;
        }

        if (!TryComp<WFClassifiedCargoComponent>(crate, out var classified) || !classified.Awake)
            return false;

        var profile = Profile;
        var chuteId = MetaData(chute).EntityPrototype?.ID;
        if (chuteId != null && profile.ClassifiedCarrierChutes.Contains(chuteId))
            amount = profile.ClassifiedCarrierReward;
        else if (chuteId != null && profile.ClassifiedPdvChutes.Contains(chuteId))
            amount = (int) (profile.ClassifiedPdvReward * (_war.GetWarLevel(chute) ? profile.ClassifiedWarBonus : 1f));
        else if (chuteId != null && profile.ClassifiedUndergroundChutes.Contains(chuteId))
            amount = profile.ClassifiedUndergroundReward;
        else
        {
            _popup.PopupEntity(Loc.GetString("wf-trade-classified-refused"), chute, user);
            return false;
        }

        return true;
    }

    private void OnSale(WFTradeSaleEvent args)
    {
        var ship = args.Ship is { } shipUid ? Name(shipUid) : Name(args.Seller);
        if (TryComp<WFConsignmentComponent>(args.Crate, out var consignment))
        {
            if (consignment.Destination == args.Grid && _timing.CurTime <= consignment.Deadline)
            {
                Couriers[ship] = Couriers.GetValueOrDefault(ship) + 1;
                _market.Radio(Loc.GetString("wf-trade-consignment-delivered", ("ship", ship), ("station", Name(args.Grid))));
            }

            return;
        }

        if (!TryComp<WFClassifiedCargoComponent>(args.Crate, out var classified) || !classified.Awake)
            return;

        classified.Delivered = true;
        Tell(Loc.GetString("wf-trade-classified-delivered", ("station", Name(args.Grid)), ("ship", ship)), args.Ship);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        UpdateConsignments();
        UpdateClassified();
    }

    #region Consignments

    private void UpdateConsignments()
    {
        var now = _timing.CurTime;
        var profile = Profile;
        var query = EntityQueryEnumerator<WFConsignmentComponent, WFTradeOriginComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var consignment, out var origin, out var xform))
        {
            // Never picked up: withdrawn when its time runs out.
            if (xform.GridUid == consignment.Origin && now > consignment.Deadline)
            {
                QueueDel(uid);
                continue;
            }

            if (origin.Carrier is { } carrier && carrier != consignment.Announced && xform.GridUid == carrier)
            {
                consignment.Announced = carrier;
                _market.Radio(Loc.GetString("wf-trade-consignment-taken",
                    ("ship", Name(carrier)), ("station", consignment.Destination is { } dest ? Name(dest) : "?")));
            }
        }

        _nextConsignment ??= now + TimeSpan.FromMinutes(_random.Next(profile.ConsignmentMinutes.X, Math.Max(profile.ConsignmentMinutes.X, profile.ConsignmentMinutes.Y) + 1));
        if (now < _nextConsignment)
            return;

        _nextConsignment = now + TimeSpan.FromMinutes(_random.Next(profile.ConsignmentMinutes.X, Math.Max(profile.ConsignmentMinutes.X, profile.ConsignmentMinutes.Y) + 1));
        SpawnConsignment();
    }

    /// <summary>Puts a priority consignment at one station's chute for another station.</summary>
    public EntityUid? SpawnConsignment()
    {
        var profile = Profile;
        if (profile.Consignments.Count == 0)
            return null;

        var chutes = _market.Chutes();
        var blockaded = _sector.Blockaded().ToHashSet();
        var taken = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WFConsignmentComponent>();
        while (query.MoveNext(out var existing))
        {
            if (existing.Destination is { } dest)
                taken.Add(dest);
        }

        var grids = chutes.Select(chute => chute.Grid).Distinct().Where(grid => !blockaded.Contains(grid)).ToList();
        var destinations = grids.Where(grid => !taken.Contains(grid)).ToList();
        if (grids.Count < 2 || destinations.Count == 0)
            return null;

        var destination = _random.Pick(destinations);
        var origins = chutes.Where(chute => chute.Grid != destination && !blockaded.Contains(chute.Grid)).ToList();
        if (origins.Count == 0)
            return null;

        var (chute, _, originGrid) = _random.Pick(origins);
        var total = profile.Consignments.Values.Sum();
        var roll = _random.NextFloat() * total;
        var picked = profile.Consignments.Keys.Last();
        foreach (var (proto, weight) in profile.Consignments)
        {
            roll -= weight;
            if (roll <= 0f)
            {
                picked = proto;
                break;
            }
        }

        var crate = Spawn(picked, Transform(chute).Coordinates);
        var consignment = EnsureComp<WFConsignmentComponent>(crate);
        consignment.Origin = originGrid;
        consignment.Destination = destination;
        consignment.Deadline = _timing.CurTime + consignment.Window;

        _market.Radio(Loc.GetString("wf-trade-consignment-start",
            ("station", Name(destination)), ("origin", Name(originGrid)),
            ("value", consignment.Value + consignment.OnTimeBonus), ("minutes", (int) consignment.Window.TotalMinutes)));

        return crate;
    }

    private void OnConsignmentExamined(Entity<WFConsignmentComponent> ent, ref ExaminedEvent args)
    {
        var minutes = Math.Max(0, (int) Math.Ceiling((ent.Comp.Deadline - _timing.CurTime).TotalMinutes));
        args.PushMarkup(Loc.GetString("wf-trade-consignment-examine",
            ("station", ent.Comp.Destination is { } dest ? Name(dest) : "?"),
            ("value", ent.Comp.Value + ent.Comp.OnTimeBonus),
            ("minutes", minutes),
            ("elsewhere", ent.Comp.Elsewhere)));
    }

    /// <summary>Open consignments for the Market app.</summary>
    public IEnumerable<(string Destination, string? Carrier, TimeSpan Deadline)> Consignments()
    {
        var query = EntityQueryEnumerator<WFConsignmentComponent>();
        while (query.MoveNext(out var consignment))
        {
            if (consignment.Destination is { } dest)
                yield return (Name(dest), consignment.Announced is { } ship ? Name(ship) : null, consignment.Deadline);
        }
    }

    #endregion

    #region Classified cargo

    private void OnPurchased(Entity<VendingMachinePurchaseComponent> ent, ref ComponentStartup args)
    {
        TryMakeClassified(ent);
    }

    /// <summary>Rolls a trade crate for a TSF transponder, unless one is already out there.</summary>
    public bool TryMakeClassified(EntityUid crate, bool force = false)
    {
        if (!HasComp<WFTradeOriginComponent>(crate) || HasComp<WFClassifiedCargoComponent>(crate))
            return false;

        var profile = Profile;
        if (!force && (_timing.CurTime < _nextClassified || !_random.Prob(profile.ClassifiedChance)))
            return false;

        var existing = EntityQueryEnumerator<WFClassifiedCargoComponent>();
        if (!force && existing.MoveNext(out _))
            return false;

        var classified = AddComp<WFClassifiedCargoComponent>(crate);
        classified.Home = Transform(crate).GridUid;
        _nextClassified = _timing.CurTime + profile.ClassifiedGap;
        return true;
    }

    private void UpdateClassified()
    {
        var now = _timing.CurTime;
        var profile = Profile;
        var query = EntityQueryEnumerator<WFClassifiedCargoComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var classified, out var xform))
        {
            var grid = xform.GridUid;
            if (!classified.Awake)
            {
                classified.Home ??= grid;
                if (grid == classified.Home || !_market.IsMarketMap(xform.MapID))
                    continue;

                classified.Awake = true;
                classified.Aboard = grid;
                Ping((uid, classified));
                var where = Callsign(classified);
                Tell(Loc.GetString("wf-trade-classified-detected", ("ship", grid is { } ship ? Name(ship) : "?"), ("cell", where)), grid);
                if (grid is { } carrier)
                    _market.TellAboard([carrier], Loc.GetString("wf-trade-classified-warning"));

                continue;
            }

            if (grid == null && !classified.Adrift)
            {
                classified.Adrift = true;
                classified.Aboard = null;
                Ping((uid, classified));
                Tell(Loc.GetString("wf-trade-classified-adrift", ("cell", Callsign(classified))), null);
            }
            else if (grid is { } aboard && aboard != classified.Aboard)
            {
                classified.Adrift = false;
                classified.Aboard = aboard;
                Ping((uid, classified));
                Tell(Loc.GetString("wf-trade-classified-moved", ("ship", Name(aboard)), ("cell", Callsign(classified))), aboard);
            }

            if (now < classified.JammedUntil || !_market.IsMarketMap(xform.MapID))
                continue;

            var cell = _territory.CellOf(_transform.GetMapCoordinates(uid, xform));
            var since = now - classified.LastPing;
            if (cell != classified.LastCell && since >= profile.PingMinGap || since >= profile.PingMaxSilence)
                Ping((uid, classified));
        }
    }

    /// <summary>Records where a transponder was heard; the Market app shows it to hunters.</summary>
    private void Ping(Entity<WFClassifiedCargoComponent> crate)
    {
        crate.Comp.LastCell = _territory.CellOf(_transform.GetMapCoordinates(crate));
        crate.Comp.LastPing = _timing.CurTime;
    }

    private static string Callsign(WFClassifiedCargoComponent classified)
    {
        return classified.LastCell is { } cell ? WFSectorHex.Callsign(cell) : "?";
    }

    /// <summary>Tells the hunting companies, and the crew of a ship involved.</summary>
    private void Tell(string message, EntityUid? ship)
    {
        _market.TellCompanies(Profile.ClassifiedHunters, message);
        if (ship is { } grid)
            _market.TellAboard([grid], message);
    }

    private void OnClassifiedExamined(Entity<WFClassifiedCargoComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Awake && args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("wf-trade-classified-examine"));
    }

    private void OnClassifiedShutdown(Entity<WFClassifiedCargoComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Awake && !ent.Comp.Delivered && _ticker.RunLevel == GameRunLevel.InRound)
            Tell(Loc.GetString("wf-trade-classified-destroyed"), null);
    }

    private void OnJammerUsed(Entity<WFTransponderJammerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target
            || !TryComp<WFClassifiedCargoComponent>(target, out var classified) || !classified.Awake)
            return;

        args.Handled = true;
        classified.JammedUntil = _timing.CurTime + ent.Comp.Duration;
        _popup.PopupEntity(Loc.GetString("wf-trade-jammer-used", ("minutes", (int) ent.Comp.Duration.TotalMinutes)), target, args.User);
        Tell(Loc.GetString("wf-trade-classified-jammed", ("cell", Callsign(classified))), null);
        QueueDel(ent);
    }

    /// <summary>Awake classified crates and where they were last heard, for the Market app's hunters.</summary>
    public IEnumerable<(string Cell, TimeSpan Heard, string? Ship, bool Jammed)> Sightings()
    {
        var query = EntityQueryEnumerator<WFClassifiedCargoComponent>();
        while (query.MoveNext(out var classified))
        {
            if (classified.Awake)
                yield return (Callsign(classified), classified.LastPing, classified.Aboard is { } ship ? Name(ship) : null, _timing.CurTime < classified.JammedUntil);
        }
    }

    #endregion
}
