using System.Numerics;
using Content.Server._WF.ShipCombat;
using Robust.Shared.Map;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    [Dependency] private WFTurretTraverseSystem _wfTraverse = default!;

    /// <summary>Whether the weapon has a clear line of fire to the point, using the native firing check.</summary>
    public bool WfClearShot(EntityUid weapon, Vector2 from, Vector2 to, MapId map)
    {
        return HasLineOfSight(weapon, from, to, map);
    }
}
