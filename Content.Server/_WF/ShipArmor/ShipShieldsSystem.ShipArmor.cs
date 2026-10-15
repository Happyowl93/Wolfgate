using Content.Server._WF.ShipArmor;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Switches the grid's shields off if any shield-disrupting armor is anchored to it.</summary>
    private bool BlockWolfgateShieldForArmor(EntityUid grid)
    {
        var query = EntityQueryEnumerator<WFShieldDisruptingArmorComponent, TransformComponent>();
        while (query.MoveNext(out _, out var xform))
        {
            if (!xform.Anchored || xform.GridUid != grid)
                continue;
            SetWolfgateShieldEnabled(grid, false);
            return true;
        }
        return false;
    }
}
