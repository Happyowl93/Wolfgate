using Content.Server.Administration.Logs;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.DockLock;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WG.DockLock;

/// <summary>
/// Per-port dock locks. A locked port refuses new docking, from any helm or autopilot, and its own ship's helm refuses
/// to undock it, Undock All included. Another ship docked to it can still cast off from its own helm. Only the ship's
/// crew can lock or unlock a port, from its helm's docking screen.
/// </summary>
public sealed class WGDockLockSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private ShuttleConsoleSystem _consoles = default!;
    [Dependency] private WGShipAccessSystem _access = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Machines/airlock_deny.ogg");

    public override void Initialize()
    {
        base.Initialize();
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WGDockLockToggleMessage>(OnToggle);
        });
    }

    public bool IsLocked(EntityUid dock) => HasComp<WGDockLockComponent>(dock);

    private void OnToggle(EntityUid uid, ShuttleConsoleComponent component, WGDockLockToggleMessage args)
    {
        if (TryGetEntity(args.Dock, out var dock))
            TryToggle(uid, dock.Value, args.Locked, args.Actor);
    }

    /// <summary>Locks or unlocks a port of the console's own ship for one of its crew. False when refused.</summary>
    public bool TryToggle(EntityUid console, EntityUid dock, bool locked, EntityUid user)
    {
        if (!HasComp<DockingComponent>(dock) || Transform(console).GridUid is not { } grid || Transform(dock).GridUid != grid)
            return false;

        if (!IsCrew(user, grid))
        {
            _popup.PopupEntity(Loc.GetString("wg-dock-lock-not-crew"), console, user, PopupType.SmallCaution);
            _audio.PlayPvs(DenySound, console);
            return false;
        }

        if (!SetLocked(dock, locked))
            return true;

        var message = locked ? "wg-dock-lock-set-on" : "wg-dock-lock-set-off";
        _popup.PopupEntity(Loc.GetString(message, ("port", PortName(dock))), console, user);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} {(locked ? "locked" : "unlocked")} docking port {ToPrettyString(dock)} on {ToPrettyString(grid):grid}");
        return true;
    }

    /// <summary>Sets a port's lock and refreshes the docking screens of its ship. False when it was already so.</summary>
    public bool SetLocked(EntityUid dock, bool locked)
    {
        if (IsLocked(dock) == locked)
            return false;

        if (locked)
            AddComp<WGDockLockComponent>(dock);
        else
            RemComp<WGDockLockComponent>(dock);

        _consoles.WGForgetDocks();
        if (Transform(dock).GridUid is { } grid)
            _consoles.RefreshShuttleConsoles(grid);
        return true;
    }

    /// <summary>
    /// Whether a ship's own crew counts as such here: its owner or manager, a card on its allow list, crew or company
    /// lists, or someone of the same faction as the grid's company.
    /// </summary>
    public bool IsCrew(EntityUid user, EntityUid grid)
    {
        if (_access.CanManage(user, grid))
            return true;

        if (WGCompanyFamily.Same(CompOrNull<CompanyComponent>(grid)?.CompanyName.Id, CompOrNull<CompanyComponent>(user)?.CompanyName.Id))
            return true;

        if (!TryComp<WGShipAccessComponent>(grid, out var ship) || !_access.TryGetCard(user, out var card)
            || !_access.TryGetKey(card, out var key))
            return false;

        return _access.TryGetEntry(ship, key, out _) || ship.CrewKeys.Contains(key) || ship.CompanyKeys.Contains(key);
    }

    /// <summary>The locked port of a joint that holds it for a helm on that port's own ship, if any.</summary>
    public EntityUid? HoldingPort(EntityUid console, Entity<DockingComponent> dock)
    {
        if (Transform(console).GridUid is not { } grid)
            return null;

        if (IsLocked(dock) && Transform(dock).GridUid == grid)
            return dock;

        if (dock.Comp.DockedWith is { } other && IsLocked(other) && Transform(other).GridUid == grid)
            return other;

        return null;
    }

    /// <summary>Whether a helm's undock request is refused by a lock on its own ship's side; tells the user why.</summary>
    public bool RefusesUndock(EntityUid console, Entity<DockingComponent> dock, EntityUid? user)
    {
        if (HoldingPort(console, dock) is not { } port)
            return false;

        Deny(console, user, Loc.GetString("wg-dock-lock-refuse-undock", ("port", PortName(port))));
        return true;
    }

    /// <summary>Whether a helm's dock request is refused because either port is locked; tells the user why.</summary>
    public bool RefusesDock(EntityUid console, EntityUid ours, EntityUid target, EntityUid? user)
    {
        if (IsLocked(ours))
        {
            Deny(console, user, Loc.GetString("wg-dock-lock-refuse-dock-own", ("port", PortName(ours))));
            return true;
        }

        if (IsLocked(target))
        {
            Deny(console, user, Loc.GetString("wg-dock-lock-refuse-dock-target", ("port", PortName(target))));
            return true;
        }

        return false;
    }

    /// <summary>Takes the ports a lock holds out of an Undock All request and says how many stay docked.</summary>
    public void HoldLocked(EntityUid console, List<NetEntity> docks, EntityUid? user)
    {
        var held = docks.RemoveAll(net => TryGetEntity(net, out var dock) && TryComp<DockingComponent>(dock, out var comp)
                                          && HoldingPort(console, (dock.Value, comp)) != null);
        if (held > 0)
            Deny(console, user, Loc.GetString("wg-dock-lock-held-all", ("count", held)));
    }

    private void Deny(EntityUid console, EntityUid? user, string message)
    {
        if (user is { } actor)
            _popup.PopupEntity(message, console, actor, PopupType.MediumCaution);
        else
            _popup.PopupEntity(message, console, PopupType.MediumCaution);
        _audio.PlayPvs(DenySound, console);
    }

    private string PortName(EntityUid dock)
    {
        return TryComp<DockingComponent>(dock, out var comp) && comp.Name != null ? Loc.GetString(comp.Name) : Name(dock);
    }
}
