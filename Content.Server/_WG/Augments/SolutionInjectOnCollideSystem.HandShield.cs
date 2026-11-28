using System.Linq;
using Content.Server._WG.Augments;

namespace Content.Server.Chemistry.EntitySystems;

public sealed partial class SolutionInjectOnCollideSystem
{
    [Dependency] private WGHandShieldSystem _wgHandShield = default!;

    /// <summary>The hit targets whose raised hand shield doesn't face the attacker; a blocked bite injects nothing.</summary>
    private IReadOnlyList<EntityUid> WgUnshielded(IReadOnlyList<EntityUid> targets, EntityUid attacker)
    {
        return targets.Where(target => !_wgHandShield.ShieldsFrom(target, attacker)).ToList();
    }
}
