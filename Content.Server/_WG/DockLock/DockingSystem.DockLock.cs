using Content.Server._WG.DockLock;
using Content.Server.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class DockingSystem
{
    /// <summary>Whether a port is locked against new docking.</summary>
    private bool WgDockLocked(EntityUid dock) => HasComp<WGDockLockComponent>(dock);

    /// <summary>Whether a dock lock refuses a helm's dock request, telling its user why.</summary>
    private bool WgDockLockRefusesDock(EntityUid console, EntityUid ours, EntityUid target, EntityUid user)
    {
        return EntityManager.System<WGDockLockSystem>().RefusesDock(console, ours, target, user);
    }

    /// <summary>Whether a lock on the helm's own side refuses its undock request, telling its user why.</summary>
    private bool WgDockLockRefusesUndock(EntityUid console, Entity<DockingComponent> dock, EntityUid user)
    {
        // A drone pilot console works the drone it controls, so the lock checked is the drone's.
        return EntityManager.System<WGDockLockSystem>().RefusesUndock(_console.GetDroneConsole(console) ?? console, dock, user);
    }

    /// <summary>Takes locked ports out of an Undock All request.</summary>
    private void WgDockLockHoldAll(EntityUid console, List<NetEntity> docks, EntityUid user)
    {
        EntityManager.System<WGDockLockSystem>().HoldLocked(_console.GetDroneConsole(console) ?? console, docks, user);
    }
}
