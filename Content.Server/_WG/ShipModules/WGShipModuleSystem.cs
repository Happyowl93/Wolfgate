using System.Linq;
using Content.Server.Shuttles;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Refills every ship module's charges, publishes each ship's modules to its helm and fires them for its pilots.
/// </summary>
public sealed partial class WGShipModuleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(0.25);

    private readonly Dictionary<EntityUid, List<Entity<WGShipModuleComponent>>> _fitted = new();
    private readonly List<WGShipModuleSlot> _slots = new();
    private readonly HashSet<string> _kinds = new();
    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipModuleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGShipModuleComponent, ExaminedEvent>(OnExamined);
        SubscribeNetworkEvent<WGShipModuleActivateEvent>(OnActivateRequest);
    }

    private void OnMapInit(Entity<WGShipModuleComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Charges = ent.Comp.MaxCharges;
    }

    private void OnExamined(Entity<WGShipModuleComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("wg-ship-module-examine", ("charges", ent.Comp.Charges),
            ("max", ent.Comp.MaxCharges), ("seconds", (int) ent.Comp.RechargeTime.TotalSeconds)));
        args.PushMarkup(Loc.GetString("wg-ship-module-examine-helm"));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGShipModuleComponent>();
        while (query.MoveNext(out var uid, out var module))
        {
            if (module.Charges >= module.MaxCharges)
                continue;

            // An unpowered module keeps the progress it had.
            if (!_power.IsPowered(uid))
            {
                module.NextCharge += TimeSpan.FromSeconds(frameTime);
                continue;
            }

            if (now < module.NextCharge)
                continue;

            module.Charges++;
            module.NextCharge = now + module.RechargeTime;
        }

        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;
        Sweep(now);
    }

    /// <summary>Rebuilds every ship's module strip and syncs the ones that changed.</summary>
    private void Sweep(TimeSpan now)
    {
        foreach (var list in _fitted.Values)
        {
            list.Clear();
        }

        var modules = EntityQueryEnumerator<WGShipModuleComponent, TransformComponent>();
        while (modules.MoveNext(out var uid, out var module, out var xform))
        {
            if (!xform.Anchored || xform.GridUid is not { } grid)
                continue;

            if (!_fitted.TryGetValue(grid, out var list))
                _fitted[grid] = list = new List<Entity<WGShipModuleComponent>>();

            list.Add((uid, module));
        }

        foreach (var (grid, list) in _fitted)
        {
            if (list.Count == 0 || TerminatingOrDeleted(grid))
                continue;

            var ship = EnsureComp<WGShipModulesComponent>(grid);
            if (HoldingField(grid))
                ship.LastShielded = now;

            // Oldest fitting first, so a slot keeps its hotkey; a second module of a kind is a spare.
            list.Sort(static (a, b) => a.Owner.Id.CompareTo(b.Owner.Id));
            _slots.Clear();
            _kinds.Clear();
            foreach (var module in list)
            {
                if (_slots.Count >= WGShipModulesComponent.MaxSlots)
                    break;

                if (!_kinds.Add(Kind(module)))
                    continue;

                var locked = GetLock(module, grid, ship, now);
                var charging = module.Comp.Charges < module.Comp.MaxCharges && locked != WGShipModuleLock.Unpowered;
                _slots.Add(new WGShipModuleSlot(GetNetEntity(module), module.Comp.Label, module.Comp.Charges,
                    module.Comp.MaxCharges, charging ? module.Comp.NextCharge : TimeSpan.Zero, module.Comp.RechargeTime,
                    module.Comp.ReadyAt, locked));
            }

            if (ship.Slots.SequenceEqual(_slots))
                continue;

            ship.Slots = new List<WGShipModuleSlot>(_slots);
            Dirty(grid, ship);
        }

        var ships = EntityQueryEnumerator<WGShipModulesComponent>();
        while (ships.MoveNext(out var grid, out _))
        {
            if (!_fitted.TryGetValue(grid, out var list) || list.Count == 0)
                RemCompDeferred<WGShipModulesComponent>(grid);
        }

        foreach (var grid in _fitted.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToArray())
        {
            _fitted.Remove(grid);
        }
    }

    private string Kind(EntityUid module)
    {
        return MetaData(module).EntityPrototype?.ID ?? module.ToString();
    }

    /// <summary>Why a fitted module can't be used on its ship right now, whatever its charges.</summary>
    public WGShipModuleLock GetLock(Entity<WGShipModuleComponent> module, EntityUid grid, WGShipModulesComponent ship, TimeSpan now)
    {
        if (!_power.IsPowered(module.Owner))
            return WGShipModuleLock.Unpowered;

        if (TryComp<FTLComponent>(grid, out var ftl) && ftl.State is FTLState.Travelling or FTLState.Arriving)
            return WGShipModuleLock.Ftl;

        if (module.Comp.BlockedByShields
            && (HoldingField(grid)
                || ship.LastShielded is { } seen && now < seen + module.Comp.ShieldLockout))
            return WGShipModuleLock.Shields;

        var ev = new WGShipModuleLockEvent(grid);
        RaiseLocalEvent(module, ref ev);
        return ev.Lock;
    }

    /// <summary>Whether the ship has a shield field up that holds its hull; a deflector's partial field doesn't.</summary>
    public bool HoldingField(EntityUid grid)
    {
        return TryComp<ShipShieldedComponent>(grid, out var field)
               && !(field.Source is { } source && HasComp<WGShipShieldDeflectorComponent>(source));
    }

    private void OnActivateRequest(WGShipModuleActivateEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user
            || !TryGetEntity(msg.Module, out var uid)
            || !TryComp<WGShipModuleComponent>(uid, out var module)
            || FlownGrid(user) is not { } grid
            || Transform(uid.Value).GridUid != grid)
            return;

        TryActivate((uid.Value, module), user);
    }

    /// <summary>The grid an entity is piloting, if any.</summary>
    public EntityUid? FlownGrid(EntityUid user)
    {
        if (!TryComp<PilotComponent>(user, out var pilot) || pilot.Console is not { } console)
            return null;

        if (TryComp<DroneConsoleComponent>(console, out var drone) && drone.Entity is { } remote)
            console = remote;

        return Transform(console).GridUid;
    }

    /// <summary>Spends one charge of a module on its ship's strip and runs its effect; tells the user why not otherwise.</summary>
    public bool TryActivate(Entity<WGShipModuleComponent> module, EntityUid? user)
    {
        var xform = Transform(module);
        if (!xform.Anchored
            || xform.GridUid is not { } grid
            || !TryComp<WGShipModulesComponent>(grid, out var ship))
            return false;

        var net = GetNetEntity(module);
        var listed = false;
        foreach (var slot in ship.Slots)
        {
            listed |= slot.Module == net;
        }

        if (!listed)
            return false;

        var now = _timing.CurTime;
        // A held key repeats; the cooldown answers it without a popup.
        if (now < module.Comp.ReadyAt)
            return false;

        var locked = GetLock(module, grid, ship, now);
        if (locked != WGShipModuleLock.None)
        {
            Refuse(user, $"wg-ship-module-refused-{locked.ToString().ToLowerInvariant()}");
            return false;
        }

        if (module.Comp.Charges <= 0)
        {
            Refuse(user, "wg-ship-module-refused-empty");
            return false;
        }

        var attempt = new WGShipModuleActivateAttemptEvent(grid, user);
        RaiseLocalEvent(module, ref attempt);
        if (attempt.Cancelled)
        {
            if (attempt.Reason is { } reason)
                Refuse(user, reason);
            return false;
        }

        if (module.Comp.Charges >= module.Comp.MaxCharges)
            module.Comp.NextCharge = now + module.Comp.RechargeTime;

        module.Comp.Charges--;
        module.Comp.ReadyAt = now + module.Comp.Cooldown;
        // Everyone aboard, and a pilot flying it from elsewhere, hears it through the hull and feels it.
        var crew = Filter.BroadcastGrid(grid);
        if (TryComp<ActorComponent>(user, out var actor))
            crew.AddPlayer(actor.PlayerSession);
        _audio.PlayGlobal(module.Comp.ActivateSound, crew, true);
        RaiseNetworkEvent(new WGShipModuleFiredEvent(GetNetEntity(grid), net, module.Comp.Jolt), crew);

        var ev = new WGShipModuleActivatedEvent(grid, user);
        RaiseLocalEvent(module, ref ev);
        Sweep(now);
        return true;
    }

    private void Refuse(EntityUid? user, LocId reason)
    {
        if (user != null)
            _popup.PopupCursor(Loc.GetString(reason), user.Value);
    }
}
