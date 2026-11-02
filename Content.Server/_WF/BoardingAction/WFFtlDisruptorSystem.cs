using System.Numerics;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Detection;
using Content.Shared._WF.BoardingAction;
using Content.Shared.Examine;
using Content.Shared.Input;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Shuttles.Components;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.BoardingAction;

/// <summary>
/// The FTL disruptor: the pilot picks a contact with the ship module key, and once the machine has locked on that ship
/// can't jump until the lock is dropped, it leaves range or the machine loses power.
/// </summary>
public sealed partial class WFFtlDisruptorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private DetectionSystem _detection = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private WFBoardingHelmSystem _helm = default!;
    [Dependency] private WFFtlDisruptionSystem _disruption = default!;

    /// <summary>Seconds between checks on the locks in progress and held.</summary>
    private const float CheckInterval = 0.25f;

    private List<Entity<MapGridComponent>> _grids = new();
    private readonly List<EntityUid> _lost = new();
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFFtlDisruptorToggleEvent>(OnToggle);
        SubscribeLocalEvent<WFFtlDisruptorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFFtlDisruptorComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<WFFtlDisruptorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFFtlDisruptorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WFFtlDisruptorComponent> ent, ref MapInitEvent args) => UpdateMachine(ent);

    private void OnPowerChanged(Entity<WFFtlDisruptorComponent> ent, ref PowerChangedEvent args) => UpdateMachine(ent);

    private void OnShutdown(Entity<WFFtlDisruptorComponent> ent, ref ComponentShutdown args) => Drop(ent);

    private void OnExamined(Entity<WFFtlDisruptorComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("wf-boarding-disruptor-examine", ("range", (int) ent.Comp.Range)));
    }

    private void OnToggle(WFFtlDisruptorToggleEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } pilot)
            Toggle(pilot, msg.Cursor);
    }

    /// <summary>A ship module key press: drops the running lock, or starts one on the contact at the cursor.</summary>
    public void Toggle(EntityUid pilot, MapCoordinates cursor)
    {
        if (!TryComp<PilotComponent>(pilot, out var piloting) || piloting.Console is not { } console ||
            _console.GetDroneConsole(console) is not { } controlled || Transform(controlled).GridUid is not { } ship)
            return;

        if (!TryGetDisruptor(ship, out var disruptor))
        {
            _popup.PopupEntity(Loc.GetString("wf-boarding-disruptor-missing"), pilot, pilot, PopupType.Medium);
            return;
        }

        if (disruptor.Comp.Target != null)
        {
            Drop(disruptor);
            _popup.PopupEntity(Loc.GetString("wf-boarding-disruptor-released"), pilot, pilot);
            return;
        }

        if (!_power.IsPowered(disruptor.Owner))
        {
            _popup.PopupEntity(Loc.GetString("wf-boarding-disruptor-unpowered"), pilot, pilot, PopupType.Medium);
            return;
        }

        if (FindContact(disruptor, ship, console, cursor) is not { } contact)
        {
            _popup.PopupEntity(Loc.GetString("wf-boarding-disruptor-no-contact"), pilot, pilot, PopupType.Medium);
            return;
        }

        disruptor.Comp.Target = contact;
        disruptor.Comp.Locked = false;
        disruptor.Comp.LockDone = _timing.CurTime + disruptor.Comp.LockTime;
        UpdateMachine(disruptor);
        _helm.Refresh(ship);
    }

    /// <summary>The disruptor a ship is running: the one with a lock, or else the first anchored on it.</summary>
    public bool TryGetDisruptor(EntityUid ship, out Entity<WFFtlDisruptorComponent> disruptor)
    {
        disruptor = default;
        var found = false;
        var query = EntityQueryEnumerator<WFFtlDisruptorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid != ship || !xform.Anchored)
                continue;

            if (comp.Target != null)
            {
                disruptor = (uid, comp);
                return true;
            }

            if (found)
                continue;

            disruptor = (uid, comp);
            found = true;
        }

        return found;
    }

    /// <summary>The nearest ship to the cursor that the disruptor could hold.</summary>
    private EntityUid? FindContact(Entity<WFFtlDisruptorComponent> disruptor, EntityUid ship, EntityUid console, MapCoordinates cursor)
    {
        if (cursor.MapId == MapId.Nullspace || cursor.MapId != Transform(disruptor).MapID)
            return null;

        _grids.Clear();
        var reach = disruptor.Comp.CursorReach;
        _maps.FindGridsIntersecting(cursor.MapId, Box2.CenteredAround(cursor.Position, new Vector2(reach * 2f)), ref _grids);

        EntityUid? best = null;
        var nearest = float.MaxValue;
        foreach (var grid in _grids)
        {
            var distance = _disruption.DistanceToHull(cursor.Position, grid);
            if (distance > reach || distance >= nearest || !CanHold(disruptor, ship, console, grid))
                continue;

            nearest = distance;
            best = grid;
        }

        return best;
    }

    /// <summary>Whether a ship is a contact this disruptor can hold from where it is.</summary>
    private bool CanHold(Entity<WFFtlDisruptorComponent> disruptor, EntityUid ship, EntityUid? console, EntityUid target)
    {
        if (target == ship || !_disruption.IsTackleable(target) || Transform(target).MapID != Transform(disruptor).MapID ||
            _disruption.DistanceToHull(_transform.GetWorldPosition(disruptor), target) > disruptor.Comp.Range)
            return false;

        if (!TryComp<IFFComponent>(target, out var iff))
            return true;

        return (iff.Flags & IFFFlags.Hide) == 0 && ((iff.Flags & IFFFlags.HideLabel) == 0 || console is not { } viewer ||
                                                    _detection.IsGridDetected(target, viewer) != DetectionLevel.Undetected);
    }

    /// <summary>Lets go of the target, if there is one.</summary>
    private void Drop(Entity<WFFtlDisruptorComponent> disruptor)
    {
        if (disruptor.Comp.Target is not { } target)
            return;

        disruptor.Comp.Target = null;
        disruptor.Comp.Locked = false;
        _disruption.Release(target, disruptor);
        if (!TerminatingOrDeleted(disruptor))
            UpdateMachine(disruptor);

        _helm.Refresh(target);
        _helm.Refresh(Transform(disruptor).GridUid);
    }

    /// <summary>Sets the lights and the power draw.</summary>
    private void UpdateMachine(Entity<WFFtlDisruptorComponent> disruptor)
    {
        var active = disruptor.Comp.Target != null;
        var status = !_power.IsPowered(disruptor.Owner) ? WFFtlDisruptorStatus.Off
            : !active ? WFFtlDisruptorStatus.Idle
            : disruptor.Comp.Locked ? WFFtlDisruptorStatus.Locked
            : WFFtlDisruptorStatus.Locking;

        _appearance.SetData(disruptor, WFFtlDisruptorVisuals.Status, status);
        if (TryComp<ApcPowerReceiverComponent>(disruptor, out var receiver))
            receiver.Load = active ? disruptor.Comp.ActiveLoad : disruptor.Comp.IdleLoad;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < CheckInterval)
            return;

        _accumulator = 0f;
        _lost.Clear();
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFFtlDisruptorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Target is not { } target)
                continue;

            var disruptor = new Entity<WFFtlDisruptorComponent>(uid, comp);
            if (TerminatingOrDeleted(target) || !xform.Anchored || xform.GridUid is not { } ship ||
                !_power.IsPowered(uid) || !CanHold(disruptor, ship, null, target))
            {
                _lost.Add(uid);
                continue;
            }

            if (comp.Locked || now < comp.LockDone)
                continue;

            comp.Locked = true;
            _disruption.Disrupt(target, uid, ship);
            _audio.PlayPvs(comp.LockedSound, uid);
            UpdateMachine(disruptor);
            _helm.Refresh(ship);
            _helm.Refresh(target);
        }

        foreach (var uid in _lost)
        {
            if (TryComp<WFFtlDisruptorComponent>(uid, out var comp))
                Drop((uid, comp));
        }
    }
}
