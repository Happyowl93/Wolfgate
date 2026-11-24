using Content.Server._WG.ShipShields;
using Content.Shared._WG.ShipShields;

namespace Content.Server._Crescent.ShipShields;

// Radars draw an overloaded ship from WGShipOverloadedComponent, kept in step with the generator and the gun lock here.
public sealed partial class ShipShieldsSystem
{
    private const float OverloadMarkInterval = 0.25f;
    private float _wgOverloadMarkAccumulator;

    /// <summary>Whether a ship is overloaded: its cooling generator is waiting out an overload or a collapse has locked its guns.</summary>
    public bool IsWolfgateShipOverloaded(EntityUid grid)
    {
        if (GetWolfgateGunLockSeconds(grid) > 0)
            return true;
        return TryComp<WGShipHeatComponent>(grid, out var hull) && hull.Cooler is { } cooler && !Deleted(cooler) &&
            Transform(cooler).GridUid == grid && IsWolfgateOverloaded(cooler);
    }

    private void UpdateWolfgateOverloadMarks(float frameTime)
    {
        _wgOverloadMarkAccumulator += frameTime;
        if (_wgOverloadMarkAccumulator < OverloadMarkInterval)
            return;
        _wgOverloadMarkAccumulator = 0f;

        var query = EntityQueryEnumerator<WGShipHeatComponent>();
        while (query.MoveNext(out var grid, out _))
        {
            var marked = HasComp<WGShipOverloadedComponent>(grid);
            var overloaded = IsWolfgateShipOverloaded(grid);
            if (overloaded && !marked)
                EnsureComp<WGShipOverloadedComponent>(grid);
            else if (!overloaded && marked)
                RemComp<WGShipOverloadedComponent>(grid);
        }
    }
}
