using Content.Server._WG.DockLock;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>Whether a port is locked, for the docking screen.</summary>
    private bool WgPortLocked(EntityUid dock) => HasComp<WGDockLockComponent>(dock);
}
