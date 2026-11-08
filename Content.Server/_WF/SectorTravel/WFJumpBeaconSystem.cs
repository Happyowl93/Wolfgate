using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._WF.Hailing;
using Content.Server._WF.SectorTravel.Components;
using Content.Server.Beam;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.SectorTravel;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Sprite;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorTravel;

/// <summary>
/// Jump beacons: a pilot inside a beacon's zone hails it, it confirms and counts down, then throws a bolt that opens a
/// portal across the ship's nose. Crossing the portal starts the jump to the linked sector. Leaving the zone or taking
/// hull damage during the countdown breaks it, and an arrival is locked out of another jump for a while.
/// </summary>
public sealed partial class WFJumpBeaconSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedScaleVisualsSystem _scale = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BeamSystem _beam = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private WFSectorTravelSystem _sectors = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>How long the beacon keeps the channel open after answering.</summary>
    private static readonly TimeSpan HangUpDelay = TimeSpan.FromSeconds(5);

    private static readonly EntProtoId PortalPrototype = "WFJumpPortal";
    private static readonly EntProtoId BoltPrototype = "WFJumpBeaconBolt";
    private static readonly EntProtoId BoltAnchorPrototype = "WFJumpBeaconBoltAnchor";
    private static readonly SoundSpecifier PortalOpenSound = new SoundPathSpecifier("/Audio/Effects/Lightning/lightningbolt.ogg");

    /// <summary>Loose entities this far outside a jumping grid's bounds go with it, in metres.</summary>
    private const float CarryMargin = 6f;

    /// <summary>The portal is this much wider than the ship on each side, in metres.</summary>
    private const float PortalMargin = 3f;

    /// <summary>The portal's depth as a share of its width: a ring seen tilted.</summary>
    private const float PortalTilt = 0.3f;

    /// <summary>Longest stretch of bolt drawn at each end, in metres; the beam spawns an entity per metre.</summary>
    private const float BoltReach = 48f;

    /// <summary>The spool after crossing, in seconds: the beacon has already done the charging.</summary>
    private const float CrossingSpool = 1f;

    /// <summary>How far past the portal plane a ship's centre still counts as going through it, in metres.</summary>
    private const float CrossDepth = 20f;

    private TimeSpan _nextCheck;
    private readonly HashSet<EntityUid> _group = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
        SubscribeLocalEvent<WFBeaconJumpComponent, ComponentShutdown>(OnJumpShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdatePortals();
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        UpdateHails();
        UpdateCountdowns();
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

    /// <summary>Answers hails at every beacon console, gives the caller the beacon's reply, and hangs up after.</summary>
    private void UpdateHails()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFJumpBeaconConsoleComponent>();
        while (query.MoveNext(out var console, out var control))
        {
            if (!TryComp<HailConsoleComponent>(console, out var hail))
            {
                control.Answered = false;
                continue;
            }

            switch (hail.State)
            {
                case HailState.Ringing:
                    control.Answered = false;
                    _hailing.AnswerAutomated(console, control.Beacon, true);
                    break;

                case HailState.Connected when hail.OperatorUid == control.Beacon:
                    if (!control.Answered)
                    {
                        control.Answered = true;
                        control.HangUp = now + HangUpDelay;
                        Reply(console, control.Beacon, hail.PeerGrid);
                    }
                    else if (now >= control.HangUp)
                    {
                        _hailing.End(console);
                    }

                    break;
            }
        }
    }

    /// <summary>Starts the caller's jump and confirms it, or tells them why not.</summary>
    private void Reply(EntityUid console, EntityUid beaconUid, EntityUid? caller)
    {
        if (!TryComp<WFJumpBeaconComponent>(beaconUid, out var beacon))
            return;

        string line;
        if (caller is not { } ship)
            line = Loc.GetString("wf-sector-beacon-reply-refuse-zone", ("radius", (int) beacon.ZoneRadius));
        else if (TryJump(ship, beacon.Link, out var refusal, beacon: beaconUid))
            line = Loc.GetString("wf-sector-beacon-reply-confirmed", ("sector", SectorName(beacon.Link)), ("seconds", (int) beacon.Countdown));
        else
            line = Loc.GetString(refusal + "-reply", ("radius", (int) beacon.ZoneRadius), ("seconds", LockoutLeft(ship)));

        _hailing.Say(console, beaconUid, line);
    }

    private int LockoutLeft(EntityUid ship)
    {
        return TryComp<WFBeaconLockoutComponent>(ship, out var lockout) && lockout.Until > _timing.CurTime
            ? (int) Math.Ceiling((lockout.Until - _timing.CurTime).TotalSeconds)
            : 0;
    }

    /// <summary>
    /// Starts a ship's beacon jump to a linked sector from the zone it is in: the countdown, then the portal. The refusal
    /// is a loc id saying why not; with <paramref name="beacon"/> set, the ship must be in that beacon's zone.
    /// </summary>
    /// <remarks>No FTL drive is needed: the beacon does the jumping. An NPC ship goes through as soon as the portal opens.</remarks>
    public bool TryJump(EntityUid ship, string sector, [NotNullWhen(false)] out string? refusal, bool npc = false, EntityUid? beacon = null)
    {
        refusal = Refuse(ship, sector, npc, beacon, out var zone);
        if (refusal != null)
            return false;

        var (damage, structure) = Hull(ship);
        var jump = EnsureComp<WFBeaconJumpComponent>(ship);
        jump.Beacon = zone;
        jump.To = sector;
        jump.Fires = _timing.CurTime + TimeSpan.FromSeconds(zone.Comp.Countdown);
        jump.Damage = damage;
        jump.Structure = structure;
        jump.Portal = null;
        jump.Departed = false;
        jump.Npc = npc;
        if (!npc)
            TellAboard(ship, Loc.GetString("wf-sector-beacon-countdown",
                ("sector", SectorName(sector)), ("seconds", (int) zone.Comp.Countdown)));
        return true;
    }

    /// <summary>Why a ship can't beacon-jump to a sector now, or null if it can.</summary>
    private string? Refuse(EntityUid ship, string sector, bool npc, EntityUid? from, out Entity<WFJumpBeaconComponent> beacon)
    {
        beacon = default;
        if (ZoneOf(ship) is not { } zone || zone.Comp.Link != sector || from != null && zone.Owner != from)
            return "wf-sector-beacon-refuse-zone";

        beacon = zone;
        if (HasComp<WFBeaconJumpComponent>(ship) || HasComp<FTLComponent>(ship))
            return "wf-sector-beacon-refuse-busy";

        if (TryComp<WFBeaconLockoutComponent>(ship, out var lockout) && lockout.Until > _timing.CurTime)
            return "wf-sector-beacon-refuse-lockout";

        if (!npc && !_shuttle.CanFTL(ship, out _))
            return "wf-sector-beacon-refuse-cannot";

        return null;
    }

    /// <summary>Breaks countdowns whose ship left the zone or took damage, fires the due ones, and closes stale portals.</summary>
    private void UpdateCountdowns()
    {
        var now = _timing.CurTime;
        var broken = new List<(EntityUid Ship, string Reason)>();
        var due = new List<EntityUid>();
        var stale = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFBeaconJumpComponent>();
        while (query.MoveNext(out var ship, out var jump))
        {
            // Crossed, but the FTL sequence was cut short before hyperspace.
            if (jump.Departed)
            {
                if (!HasComp<FTLComponent>(ship))
                    stale.Add(ship);
                continue;
            }

            if (jump.Portal != null)
            {
                if (now >= jump.Expires)
                    broken.Add((ship, "wf-sector-beacon-portal-closed"));
                continue;
            }

            if (CountdownBroken(ship, jump) is { } reason)
                broken.Add((ship, reason));
            else if (now >= jump.Fires)
                due.Add(ship);
        }

        foreach (var ship in stale)
        {
            RemComp<WFBeaconJumpComponent>(ship);
        }

        foreach (var (ship, reason) in broken)
        {
            Abort(ship, reason);
        }

        foreach (var ship in due)
        {
            if (TryComp<WFBeaconJumpComponent>(ship, out var jump))
                OpenPortal((ship, jump));
        }
    }

    private string? CountdownBroken(EntityUid ship, WFBeaconJumpComponent jump)
    {
        if (!TryComp<WFJumpBeaconComponent>(jump.Beacon, out var beacon) || ZoneOf(ship)?.Owner != jump.Beacon)
            return "wf-sector-beacon-cancel-zone";

        var (damage, structure) = Hull(ship);
        var taken = damage - jump.Damage + Math.Max(0, jump.Structure - structure) * beacon.CancelDamage;
        if (taken >= beacon.CancelDamage)
            return "wf-sector-beacon-cancel-damage";

        return null;
    }

    /// <summary>The ship's nose: its grid's local north, the way upstream FTL and the ship builders face.</summary>
    private Vector2 Forward(EntityUid ship)
    {
        return _transform.GetWorldRotation(ship).RotateVec(Vector2.UnitY);
    }

    /// <summary>Throws the bolt and opens a portal as wide as the ship across its nose; an NPC ship goes straight through.</summary>
    private void OpenPortal(Entity<WFBeaconJumpComponent> ship)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid) || Transform(ship).MapUid is not { } map)
        {
            Abort(ship, "wf-sector-beacon-cancel-zone");
            return;
        }

        var bounds = grid.LocalAABB;
        var forward = Forward(ship);
        var gap = TryComp<WFJumpBeaconComponent>(ship.Comp.Beacon, out var beacon) ? beacon.PortalGap : 12f;
        var position = GridCentre(ship) + forward * (bounds.Height / 2f + gap);
        var width = bounds.Width + PortalMargin * 2f;

        var portal = Spawn(PortalPrototype, new EntityCoordinates(map, position));
        _transform.SetWorldRotation(portal, _transform.GetWorldRotation(ship));
        _scale.SetSpriteScale(portal, new Vector2(width, width * PortalTilt));
        _audio.PlayPvs(PortalOpenSound, portal);
        Bolt(ship.Comp.Beacon, portal);

        ship.Comp.Portal = portal;
        ship.Comp.PortalAt = position;
        ship.Comp.Forward = forward;
        ship.Comp.HalfWidth = width / 2f;
        ship.Comp.Expires = _timing.CurTime + (beacon?.PortalLifetime ?? TimeSpan.FromSeconds(60));

        if (ship.Comp.Npc)
        {
            Cross(ship);
            return;
        }

        TellAboard(ship, Loc.GetString("wf-sector-beacon-portal-open", ("sector", SectorName(ship.Comp.To))));
    }

    /// <summary>
    /// A lightning bolt from the beacon to the portal. The beam spawns an entity per metre, so a long one is drawn as a
    /// stretch out of the beacon and a stretch into the portal, along the same line.
    /// </summary>
    private void Bolt(EntityUid beacon, EntityUid portal)
    {
        if (!Exists(beacon))
            return;

        var from = _transform.GetMapCoordinates(beacon);
        var to = _transform.GetMapCoordinates(portal);
        if (from.MapId != to.MapId)
            return;

        var line = to.Position - from.Position;
        var length = line.Length();
        var state = $"lightning_{_random.Next(1, 13)}";
        if (length <= BoltReach * 2f)
        {
            _beam.TryCreateBeam(beacon, portal, BoltPrototype, state);
            return;
        }

        var direction = line / length;
        var outward = Spawn(BoltAnchorPrototype, new MapCoordinates(from.Position + direction * BoltReach, from.MapId));
        var inward = Spawn(BoltAnchorPrototype, new MapCoordinates(to.Position - direction * BoltReach, to.MapId));
        _beam.TryCreateBeam(beacon, outward, BoltPrototype, state);
        _beam.TryCreateBeam(inward, portal, BoltPrototype, state);
    }

    /// <summary>Sends every ship whose centre has passed through its open portal on its jump.</summary>
    private void UpdatePortals()
    {
        var crossed = new List<Entity<WFBeaconJumpComponent>>();
        var query = EntityQueryEnumerator<WFBeaconJumpComponent>();
        while (query.MoveNext(out var ship, out var jump))
        {
            if (jump.Departed || jump.Portal is not { } portal || !Exists(portal))
                continue;

            // Through the portal plane from behind, within its width.
            var offset = GridCentre(ship) - jump.PortalAt;
            var depth = Vector2.Dot(offset, jump.Forward);
            var lateral = Vector2.Dot(offset, new Vector2(jump.Forward.Y, -jump.Forward.X));
            if (Transform(ship).MapUid == Transform(portal).MapUid
                && depth is >= 0f and <= CrossDepth && MathF.Abs(lateral) <= jump.HalfWidth)
                crossed.Add((ship, jump));
        }

        foreach (var ship in crossed)
        {
            Cross(ship);
        }
    }

    /// <summary>Starts the FTL sequence to the linked sector for a ship going through its portal.</summary>
    private void Cross(Entity<WFBeaconJumpComponent> ship)
    {
        if (!TryComp<WFJumpBeaconComponent>(ship.Comp.Beacon, out var beacon))
        {
            Abort(ship, "wf-sector-beacon-cancel-zone");
            return;
        }

        var travel = _sectors.TravelTime(beacon, beacon.Sector, beacon.Link);
        if (!_sectors.TryTransit(ship, ship.Comp.To, CrossingSpool, travel, out var refusal))
        {
            Abort(ship, refusal);
            return;
        }

        ship.Comp.Departed = true;
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
        RemComp<WFBeaconJumpComponent>(ship);
        TellAboard(ship, Loc.GetString(reason));
    }

    /// <summary>The portal closes with the jump it was opened for.</summary>
    private void OnJumpShutdown(Entity<WFBeaconJumpComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Portal is { } portal && !TerminatingOrDeleted(portal))
            QueueDel(portal);
    }

    /// <summary>On entering hyperspace: bring along loose entities hugging the hull.</summary>
    private void OnFtlStarted(ref FTLStartedEvent args)
    {
        if (!TryComp<WFBeaconJumpComponent>(args.Entity, out var jump) || !jump.Departed)
            return;

        if (jump.Portal is { } portal)
        {
            QueueDel(portal);
            jump.Portal = null;
        }

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
