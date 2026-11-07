using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._Mono.Radar;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.SectorTravel.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Ships;
using Content.Shared._WF.SectorTravel;
using Content.Shared._WF.Shuttles;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorTravel;

/// <summary>
/// Jump beacons: finds the ships in each beacon's zone for the helm's panel, spools beacon jumps, cancels a spool when
/// the ship leaves the zone, loses its drive, takes hull damage or comes near an interdictor, and locks a ship out of
/// another jump for a while after it arrives.
/// </summary>
public sealed partial class WFJumpBeaconSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private WFCargoLoadSystem _cargo = default!;
    [Dependency] private WFJumpFuelSystem _fuel = default!;
    [Dependency] private WFSectorTravelSystem _sectors = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Loose entities this far outside a jumping grid's bounds go with it, in metres.</summary>
    private const float CarryMargin = 6f;

    private const float MassConstant = 50f;
    private const float MassFactorMin = 0.5f;
    private const float MassFactorMax = 5f;

    private TimeSpan _nextCheck;
    private readonly HashSet<EntityUid> _inZone = new();
    private readonly List<Entity<MapGridComponent>> _grids = new();
    private readonly HashSet<EntityUid> _group = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WFBeaconJumpMessage>(OnJumpMessage);
            subs.Event<WFBeaconCancelMessage>(OnCancelMessage);
            subs.Event<WFFuelServiceMessage>(OnFuelServiceMessage);
        });
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
        SubscribeLocalEvent<WFJumpInterdictorComponent, PowerChangedEvent>(OnInterdictorPower);
    }

    /// <summary>A running interdictor shows on the radar of every ship within its range.</summary>
    private void OnInterdictorPower(Entity<WFJumpInterdictorComponent> ent, ref PowerChangedEvent args)
    {
        if (TryComp<RadarBlipComponent>(ent, out var blip))
            blip.Enabled = args.Powered;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        UpdateSpools();
        UpdateZones();
    }

    /// <summary>The mass factor the upstream FTL drive applies to spool and travel.</summary>
    public static float MassFactor(float mass, FTLDriveComponent drive)
    {
        return drive.MassAffectedDrive ? MassCurve(mass * drive.DriveMassMultiplier) : 1f;
    }

    /// <summary>The curve upstream FTL timing puts a mass on, from 0.5 to 5.</summary>
    private static float MassCurve(float mass)
    {
        return Math.Clamp(MathF.Log(MathF.Sqrt(mass / MassConstant + MathF.E)), MassFactorMin, MassFactorMax);
    }

    /// <summary>
    /// The fuel a ship spends to jump from a beacon to its linked sector, or null without a drive:
    /// <c>FuelPerUnit x distance x massFactor x drive.DriveMassMultiplier</c>, where the mass factor is the docked
    /// group's, on the curve FTL timing uses.
    /// </summary>
    public float? FuelCost(EntityUid ship, Entity<WFJumpBeaconComponent> beacon)
    {
        if (!_shuttle.TryGetFTLDrive(ship, out _, out var drive))
            return null;

        var distance = WFSectorGraph.Distance(_proto, beacon.Comp.Sector, beacon.Comp.Link);
        var mass = drive.MassAffectedDrive ? MassCurve(_shuttle.GetFtlGroupMass(ship)) : 1f;
        return beacon.Comp.FuelPerUnit * distance * mass * drive.DriveMassMultiplier;
    }

    /// <summary>The beacon whose zone a ship's grid centre is in, if any.</summary>
    public Entity<WFJumpBeaconComponent>? ZoneOf(EntityUid ship)
    {
        var position = GridCentre(ship);
        var map = Transform(ship).MapID;
        var query = EntityQueryEnumerator<WFJumpBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beacon, out var xform))
        {
            if (xform.MapID == map && xform.GridUid != ship
                && Vector2.Distance(_transform.GetWorldPosition(xform), position) <= beacon.ZoneRadius)
                return (uid, beacon);
        }

        return null;
    }

    private Vector2 GridCentre(EntityUid grid)
    {
        return TryComp<MapGridComponent>(grid, out var comp)
            ? Vector2.Transform(comp.LocalAABB.Center, _transform.GetWorldMatrix(grid))
            : _transform.GetWorldPosition(grid);
    }

    /// <summary>Gives every ship in a beacon zone the panel state its helm shows, and takes it off ships that left.</summary>
    private void UpdateZones()
    {
        _inZone.Clear();
        var beacons = EntityQueryEnumerator<WFJumpBeaconComponent, TransformComponent>();
        while (beacons.MoveNext(out var beaconUid, out var beacon, out var beaconXform))
        {
            var centre = _transform.GetWorldPosition(beaconXform);
            _grids.Clear();
            var grids = _grids;
            _maps.FindGridsIntersecting(beaconXform.MapID, Box2.CenteredAround(centre, new Vector2(beacon.ZoneRadius * 2f)),
                ref grids, approx: true, includeMap: false);
            foreach (var grid in grids)
            {
                if (grid.Owner == beaconXform.GridUid || !HasComp<ShuttleComponent>(grid)
                    || Vector2.Distance(GridCentre(grid), centre) > beacon.ZoneRadius)
                    continue;

                _inZone.Add(grid);
                Refresh(grid, (beaconUid, beacon));
            }
        }

        var zoned = EntityQueryEnumerator<WFBeaconZoneComponent>();
        var left = new List<EntityUid>();
        while (zoned.MoveNext(out var uid, out _))
        {
            if (!_inZone.Contains(uid) && !HasComp<WFBeaconJumpComponent>(uid))
                left.Add(uid);
        }

        foreach (var uid in left)
        {
            RemComp<WFBeaconZoneComponent>(uid);
        }
    }

    /// <summary>Writes a ship's beacon panel state, dirtying it only when something it shows changed.</summary>
    private void Refresh(EntityUid ship, Entity<WFJumpBeaconComponent> beacon)
    {
        var zone = EnsureComp<WFBeaconZoneComponent>(ship);
        var cost = FuelCost(ship, beacon) ?? 0f;
        var travel = _sectors.TravelTime(beacon.Comp, beacon.Comp.Sector, beacon.Comp.Link);
        var link = new WFBeaconLink(beacon.Comp.Link, MathF.Ceiling(cost), MathF.Round(travel),
            _sectors.LinkClosed(beacon.Comp.Sector, beacon.Comp.Link));
        var (fuel, capacity) = _fuel.Reserve(ship);
        var locked = TryComp<WFBeaconLockoutComponent>(ship, out var lockout) && lockout.Until > _timing.CurTime ? lockout.Until : (TimeSpan?) null;
        var spooling = TryComp<WFBeaconJumpComponent>(ship, out var jump) ? jump.To.Id : null;

        var changed = zone.Links.Count != 1 || zone.Links[0] != link
            || MathF.Abs(zone.Fuel - MathF.Floor(fuel)) > 0.01f || MathF.Abs(zone.Capacity - capacity) > 0.01f
            || zone.LockedUntil != locked || zone.Spooling != spooling;
        if (!changed)
            return;

        zone.Links.Clear();
        zone.Links.Add(link);
        zone.Fuel = MathF.Floor(fuel);
        zone.Capacity = capacity;
        zone.LockedUntil = locked;
        zone.Spooling = spooling;
        Dirty(ship, zone);
    }

    private EntityUid? ShipOf(EntityUid console)
    {
        var drone = _console.GetDroneConsole(console) ?? console;
        return Transform(drone).GridUid;
    }

    private void OnJumpMessage(Entity<ShuttleConsoleComponent> ent, ref WFBeaconJumpMessage args)
    {
        if (ShipOf(ent) is { } ship && !TryJump(ship, args.Sector, out var refusal))
            _popup.PopupEntity(Loc.GetString(refusal), ent, args.Actor, PopupType.Medium);
    }

    /// <summary>Spools a ship's beacon jump to a linked sector from the zone it is in; the refusal says why not.</summary>
    /// <remarks>An NPC ship needs neither a working drive nor fuel; its spool still breaks like anyone's.</remarks>
    public bool TryJump(EntityUid ship, string sector, [NotNullWhen(false)] out string? refusal, bool npc = false)
    {
        refusal = Refuse(ship, sector, npc, out var beacon, out var drive);
        if (refusal != null)
            return false;

        var cost = npc ? 0f : FuelCost(ship, beacon) ?? 0f;
        var spool = drive == null
            ? beacon.Comp.Spool
            : beacon.Comp.Spool * MassFactor(_shuttle.GetFtlGroupMass(ship), drive) + _cargo.SpoolPenalty(ship, drive);
        var travel = _sectors.TravelTime(beacon.Comp, beacon.Comp.Sector, beacon.Comp.Link);
        if (!_sectors.TryTransit(ship, sector, spool, travel, out refusal))
            return false;

        var (damage, structure) = Hull(ship);
        var jump = EnsureComp<WFBeaconJumpComponent>(ship);
        jump.Beacon = beacon;
        jump.To = sector;
        jump.Cost = cost;
        jump.Damage = damage;
        jump.Structure = structure;
        jump.Departed = false;
        jump.Npc = npc;
        Refresh(ship, beacon);
        TellAboard(ship, Loc.GetString("wf-sector-beacon-spooling",
            ("sector", SectorName(sector)), ("seconds", (int) MathF.Ceiling(spool))));
        return true;
    }

    /// <summary>Why a ship can't beacon-jump to a sector now, or null if it can.</summary>
    private string? Refuse(EntityUid ship, string sector, bool npc, out Entity<WFJumpBeaconComponent> beacon, out FTLDriveComponent? drive)
    {
        beacon = default;
        drive = null;
        if (ZoneOf(ship) is not { } zone || zone.Comp.Link != sector)
            return "wf-sector-beacon-refuse-zone";

        beacon = zone;
        if (HasComp<WFBeaconJumpComponent>(ship) || HasComp<FTLComponent>(ship))
            return "wf-sector-beacon-refuse-busy";

        if (TryComp<WFBeaconLockoutComponent>(ship, out var lockout) && lockout.Until > _timing.CurTime)
            return "wf-sector-beacon-refuse-lockout";

        if (npc)
        {
            drive = _shuttle.TryGetFTLDrive(ship, out _, out var any) ? any : null;
            return null;
        }

        if (_shuttle.GetFtlDriveStatus(ship) != WFFtlDriveStatus.Ready || !_shuttle.TryGetFTLDrive(ship, out _, out var found))
            return "wf-sector-beacon-refuse-drive";

        drive = found;
        if (_fuel.Reserve(ship).Fuel < (FuelCost(ship, zone) ?? 0f))
            return "wf-sector-beacon-refuse-fuel";

        return null;
    }

    private void OnFuelServiceMessage(Entity<ShuttleConsoleComponent> ent, ref WFFuelServiceMessage args)
    {
        if (ShipOf(ent) is not { } ship)
            return;

        _popup.PopupEntity(Loc.GetString(_fuel.TryBuy(ship, args.Actor, out var refusal) ? "wf-jump-fuel-service-bought" : refusal!),
            ent, args.Actor, PopupType.Medium);
    }

    private void OnCancelMessage(Entity<ShuttleConsoleComponent> ent, ref WFBeaconCancelMessage args)
    {
        if (ShipOf(ent) is { } ship && HasComp<WFBeaconJumpComponent>(ship))
            Abort(ship, "wf-sector-beacon-cancelled");
    }

    /// <summary>Cancels every spool whose ship left the zone, lost its drive, took damage or came near an interdictor.</summary>
    private void UpdateSpools()
    {
        var spooling = new List<(EntityUid Ship, string Reason)>();
        var stale = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFBeaconJumpComponent>();
        while (query.MoveNext(out var ship, out var jump))
        {
            if (jump.Departed)
                continue;

            if (!TryComp<FTLComponent>(ship, out var ftl) || ftl.State != FTLState.Starting)
            {
                stale.Add(ship);
                continue;
            }

            if (SpoolBroken(ship, jump) is { } reason)
                spooling.Add((ship, reason));
        }

        foreach (var ship in stale)
        {
            RemComp<WFBeaconJumpComponent>(ship);
        }

        foreach (var (ship, reason) in spooling)
        {
            Abort(ship, reason);
        }
    }

    private string? SpoolBroken(EntityUid ship, WFBeaconJumpComponent jump)
    {
        if (!TryComp<WFJumpBeaconComponent>(jump.Beacon, out var beacon) || ZoneOf(ship)?.Owner != jump.Beacon)
            return "wf-sector-beacon-cancel-zone";

        if (!jump.Npc && _shuttle.GetFtlDriveStatus(ship) != WFFtlDriveStatus.Ready)
            return "wf-sector-beacon-cancel-drive";

        var (damage, structure) = Hull(ship);
        var taken = damage - jump.Damage + Math.Max(0, jump.Structure - structure) * beacon.CancelDamage;
        if (taken >= beacon.CancelDamage)
            return "wf-sector-beacon-cancel-damage";

        if (Interdicted(ship))
            return "wf-sector-beacon-cancel-interdicted";

        return null;
    }

    /// <summary>Whether a powered interdictor outside the ship's docked group is within its range of the ship.</summary>
    private bool Interdicted(EntityUid ship)
    {
        _group.Clear();
        _shuttle.GetAllDockedShuttles(ship, _group);
        var position = GridCentre(ship);
        var map = Transform(ship).MapID;
        var query = EntityQueryEnumerator<WFJumpInterdictorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var interdictor, out var xform))
        {
            if (xform.MapID != map || xform.GridUid is { } grid && _group.Contains(grid) || !_power.IsPowered(uid))
                continue;

            if (Vector2.Distance(_transform.GetWorldPosition(xform), position) <= interdictor.Range)
                return true;
        }

        return false;
    }

    /// <summary>Damage summed over the ship's anchored damageable structure, and how many such entities it has.</summary>
    private (float Damage, int Structure) Hull(EntityUid ship)
    {
        var damage = 0f;
        var count = 0;
        var children = Transform(ship).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<DamageableComponent>(child, out var damageable) || !Transform(child).Anchored)
                continue;

            damage += damageable.TotalDamage.Float();
            count++;
        }

        return (damage, count);
    }

    private void Abort(EntityUid ship, string reason)
    {
        _shuttle.WfAbortFtlSpool(ship);
        RemComp<WFBeaconJumpComponent>(ship);
        TellAboard(ship, Loc.GetString(reason));
    }

    /// <summary>On entering hyperspace: spend the fuel and bring along loose entities hugging the hull.</summary>
    private void OnFtlStarted(ref FTLStartedEvent args)
    {
        if (!TryComp<WFBeaconJumpComponent>(args.Entity, out var jump) || jump.Departed)
            return;

        jump.Departed = true;
        _fuel.Spend(args.Entity, jump.Cost);
        if (args.FromMapUid is { } from)
            CarryLoose(args.Entity, from, args.FTLFrom);
    }

    /// <summary>Moves entities floating free near the grid's old place onto the grid, so EVA crew and cargo go along.</summary>
    private void CarryLoose(EntityUid ship, EntityUid fromMap, Matrix3x2 oldMatrix)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid) || !Matrix3x2.Invert(oldMatrix, out var inverse))
            return;

        var bounds = grid.LocalAABB.Enlarged(CarryMargin);
        var moved = new List<(EntityUid Uid, Vector2 Local)>();
        var children = Transform(fromMap).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<MapGridComponent>(child) || !HasComp<PhysicsComponent>(child) || Transform(child).Anchored)
                continue;

            var local = Vector2.Transform(_transform.GetWorldPosition(child), inverse);
            if (bounds.Contains(local))
                moved.Add((child, local));
        }

        foreach (var (uid, local) in moved)
        {
            _transform.SetCoordinates(uid, new EntityCoordinates(ship, local));
        }
    }

    /// <summary>On arrival the jump is done, and the ship can't beacon-jump again until its lockout ends.</summary>
    private void OnFtlCompleted(ref FTLCompletedEvent args)
    {
        if (!TryComp<WFBeaconJumpComponent>(args.Entity, out var jump))
            return;

        var lockout = TryComp<WFJumpBeaconComponent>(jump.Beacon, out var beacon) ? beacon.Lockout : TimeSpan.FromSeconds(120);
        RemComp<WFBeaconJumpComponent>(args.Entity);
        EnsureComp<WFBeaconLockoutComponent>(args.Entity).Until = _timing.CurTime + lockout;
        TellAboard(args.Entity, Loc.GetString("wf-sector-beacon-arrived", ("sector", SectorName(jump.To))));
    }

    private string SectorName(string sector)
    {
        return _proto.TryIndex<WFSectorPrototype>(sector, out var proto) ? Loc.GetString(proto.Name) : sector;
    }

    /// <summary>Shows a message to everyone aboard a ship or a ship docked to it.</summary>
    private void TellAboard(EntityUid ship, string message)
    {
        _group.Clear();
        _shuttle.GetAllDockedShuttles(ship, _group);
        _group.Add(ship);
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && _group.Contains(grid))
                _popup.PopupEntity(message, uid, uid, PopupType.Large);
        }
    }
}
