using Content.Server._WG.Drones;
using Robust.Shared.Map;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipTargetingSystem
{
    [Dependency] private WGDroneFireGuardSystem _wgDroneFire = default!;

    /// <summary>Whether a drone's gun holds fire because its line to the aim point crosses its own side; it then stops.</summary>
    private bool WGDroneHoldsFire(EntityUid? user, EntityUid gun, MapCoordinates aim)
    {
        if (!_wgDroneFire.HoldsFire(user, gun, aim))
            return false;

        // A held gun mustn't keep firing the burst it was given before the line fouled.
        _gun.WgStopShots(gun);
        return true;
    }
}
