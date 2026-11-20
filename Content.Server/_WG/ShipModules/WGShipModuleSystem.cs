using System.Linq;
using Content.Server.Shuttles;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Holds the modules fitted into ships' slots: refills their charges while the ship has a powered helm, publishes each
/// ship's strip to its helm and fires them for its pilots.
/// </summary>
public sealed partial class WGShipModuleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Grids with a powered helm at the last sweep.</summary>
    private readonly HashSet<EntityUid> _poweredHelms = new();

    private readonly List<WGShipModuleSlot> _slots = new();
    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGShipModuleActivateEvent>(OnActivateRequest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGShipModuleBayComponent>();
        while (query.MoveNext(out var grid, out var bay))
        {
            var powered = _poweredHelms.Contains(grid);
            foreach (var fitted in bay.Fitted)
            {
                if (!_prototype.TryIndex(fitted.Module, out var module))
                    continue;

                if (fitted.Charges < 0)
                    fitted.Charges = module.MaxCharges;

                if (fitted.Charges >= module.MaxCharges)
                    continue;

                // Without a powered helm a module keeps the progress it had.
                if (!powered)
                {
                    fitted.NextCharge += TimeSpan.FromSeconds(frameTime);
                    continue;
                }

                if (now < fitted.NextCharge)
                    continue;

                fitted.Charges++;
                fitted.NextCharge = now + module.RechargeTime;
            }
        }

        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;
        Sweep(now);
    }

    /// <summary>Notes which ships have a powered helm, then rebuilds every ship's module strip.</summary>
    private void Sweep(TimeSpan now)
    {
        _poweredHelms.Clear();
        var helms = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (helms.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && _power.IsPowered(uid))
                _poweredHelms.Add(grid);
        }

        var bays = EntityQueryEnumerator<WGShipModuleBayComponent>();
        while (bays.MoveNext(out var grid, out var bay))
        {
            UpdateStrip(grid, bay, now);
        }

        var strips = EntityQueryEnumerator<WGShipModulesComponent>();
        while (strips.MoveNext(out var grid, out _))
        {
            if (!HasComp<WGShipModuleBayComponent>(grid))
                RemCompDeferred<WGShipModulesComponent>(grid);
        }
    }

    /// <summary>Writes a ship's fitted modules to its helm strip, dirtying it only when something shown changed.</summary>
    private void UpdateStrip(EntityUid grid, WGShipModuleBayComponent bay, TimeSpan now)
    {
        if (TerminatingOrDeleted(grid))
            return;

        if (HoldingField(grid))
            bay.LastShielded = now;

        _slots.Clear();
        foreach (var fitted in bay.Fitted)
        {
            if (!_prototype.TryIndex(fitted.Module, out var module))
                continue;

            var charges = fitted.Charges < 0 ? module.MaxCharges : fitted.Charges;
            var locked = GetLock(grid, bay, module, _poweredHelms.Contains(grid), now);
            var charging = charges < module.MaxCharges && locked != WGShipModuleLock.Unpowered;
            _slots.Add(new WGShipModuleSlot(fitted.Slot, fitted.Module, charges, module.MaxCharges,
                charging ? fitted.NextCharge : TimeSpan.Zero, fitted.ReadyAt, locked));
        }

        _slots.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));
        var strip = EnsureComp<WGShipModulesComponent>(grid);
        if (strip.Slots.SequenceEqual(_slots))
            return;

        strip.Slots = new List<WGShipModuleSlot>(_slots);
        Dirty(grid, strip);
    }

    /// <summary>The module in one of a ship's slots, if any.</summary>
    public WGFittedModule? GetFitted(EntityUid grid, int slot)
    {
        if (!TryComp<WGShipModuleBayComponent>(grid, out var bay))
            return null;

        foreach (var fitted in bay.Fitted)
        {
            if (fitted.Slot == slot)
                return fitted;
        }

        return null;
    }

    /// <summary>
    /// Fits a module into a slot, replacing whatever was there; it starts with full charges. False when the slot is out
    /// of range or the module already sits in another slot, since a ship carries each module once.
    /// </summary>
    public bool Fit(EntityUid grid, int slot, ProtoId<WGShipModulePrototype> module)
    {
        if (slot < 0 || slot >= WGShipModulesComponent.SlotCount || FittedSlot(grid, module) is { } other && other != slot)
            return false;

        var bay = EnsureComp<WGShipModuleBayComponent>(grid);
        bay.Fitted.RemoveAll(f => f.Slot == slot);
        bay.Fitted.Add(new WGFittedModule { Slot = slot, Module = module });
        bay.Fitted.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));
        UpdateStrip(grid, bay, _timing.CurTime);
        return true;
    }

    /// <summary>The slot a module is fitted in on a ship, or null.</summary>
    public int? FittedSlot(EntityUid grid, ProtoId<WGShipModulePrototype> module)
    {
        if (!TryComp<WGShipModuleBayComponent>(grid, out var bay))
            return null;

        foreach (var fitted in bay.Fitted)
        {
            if (fitted.Module == module)
                return fitted.Slot;
        }

        return null;
    }

    /// <summary>Empties a slot; a ship left with no modules loses its strip.</summary>
    public void Unfit(EntityUid grid, int slot)
    {
        if (!TryComp<WGShipModuleBayComponent>(grid, out var bay) || bay.Fitted.RemoveAll(f => f.Slot == slot) == 0)
            return;

        if (bay.Fitted.Count > 0)
        {
            UpdateStrip(grid, bay, _timing.CurTime);
            return;
        }

        RemComp<WGShipModuleBayComponent>(grid);
        RemComp<WGShipModulesComponent>(grid);
    }

    /// <summary>Why a fitted module can't be used on its ship right now, whatever its charges.</summary>
    public WGShipModuleLock GetLock(EntityUid grid, WGShipModuleBayComponent bay, WGShipModulePrototype module, bool powered,
        TimeSpan now)
    {
        if (!powered)
            return WGShipModuleLock.Unpowered;

        if (TryComp<FTLComponent>(grid, out var ftl) && ftl.State is FTLState.Travelling or FTLState.Arriving)
            return WGShipModuleLock.Ftl;

        if (module.BlockedByShields
            && (HoldingField(grid)
                || bay.LastShielded is { } seen && now < seen + module.ShieldLockout))
            return WGShipModuleLock.Shields;

        var ev = new WGShipModuleLockEvent(grid, module);
        RaiseLocalEvent(ref ev);
        return ev.Lock;
    }

    /// <summary>Whether the ship has a shield field up that holds its hull; a deflector's partial field doesn't.</summary>
    public bool HoldingField(EntityUid grid)
    {
        return TryComp<ShipShieldedComponent>(grid, out var field)
               && !(field.Source is { } source && HasComp<WGShipShieldDeflectorComponent>(source));
    }

    /// <summary>Whether any helm on the grid has power.</summary>
    public bool HasPoweredHelm(EntityUid grid)
    {
        var helms = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (helms.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && _power.IsPowered(uid))
                return true;
        }

        return false;
    }

    private void OnActivateRequest(WGShipModuleActivateEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user
            || !TryComp<PilotComponent>(user, out var pilot)
            || pilot.Console is not { } console
            || FlownGrid(user) is not { } grid)
            return;

        TryActivate(grid, msg.Slot, user, console);
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

    /// <summary>
    /// Spends one charge of the module in a ship's slot and runs its effect; tells the user why not otherwise. A pilot's
    /// own helm has to be powered; with no helm given, any powered helm on the ship will do.
    /// </summary>
    public bool TryActivate(EntityUid grid, int slot, EntityUid? user, EntityUid? helm = null)
    {
        if (!TryComp<WGShipModuleBayComponent>(grid, out var bay)
            || GetFitted(grid, slot) is not { } fitted
            || !_prototype.TryIndex(fitted.Module, out var module))
            return false;

        var now = _timing.CurTime;
        // A held key repeats; the cooldown answers it without a popup.
        if (now < fitted.ReadyAt)
            return false;

        var powered = helm is { } console ? _power.IsPowered(console) : HasPoweredHelm(grid);
        var locked = GetLock(grid, bay, module, powered, now);
        if (locked != WGShipModuleLock.None)
        {
            Refuse(user, $"wg-ship-module-refused-{locked.ToString().ToLowerInvariant()}");
            return false;
        }

        if (fitted.Charges < 0)
            fitted.Charges = module.MaxCharges;

        if (fitted.Charges <= 0)
        {
            Refuse(user, "wg-ship-module-refused-empty");
            return false;
        }

        var attempt = new WGShipModuleActivateAttemptEvent(grid, module, user);
        RaiseLocalEvent(ref attempt);
        if (attempt.Cancelled)
        {
            if (attempt.Reason is { } reason)
                Refuse(user, reason);
            return false;
        }

        if (fitted.Charges >= module.MaxCharges)
            fitted.NextCharge = now + module.RechargeTime;

        fitted.Charges--;
        fitted.ReadyAt = now + module.Cooldown;
        // Everyone aboard, and a pilot flying it from elsewhere, hears it through the hull and feels it.
        var crew = Filter.BroadcastGrid(grid);
        if (TryComp<ActorComponent>(user, out var actor))
            crew.AddPlayer(actor.PlayerSession);
        _audio.PlayGlobal(module.ActivateSound, crew, true);
        RaiseNetworkEvent(new WGShipModuleFiredEvent(GetNetEntity(grid), slot, module.Jolt), crew);

        var ev = new WGShipModuleActivatedEvent(grid, module, user);
        RaiseLocalEvent(ref ev);
        UpdateStrip(grid, bay, now);
        return true;
    }

    private void Refuse(EntityUid? user, LocId reason)
    {
        if (user != null)
            _popup.PopupCursor(Loc.GetString(reason), user.Value);
    }
}
