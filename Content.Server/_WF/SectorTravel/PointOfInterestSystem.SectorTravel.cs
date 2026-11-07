using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._NF.GameRule;

public sealed partial class PointOfInterestSystem
{
    /// <summary>Loads one point of interest onto a sector's map at a set position, as the adventure rule loads its own.</summary>
    public bool WfSpawnSectorPoi(MapId map, PointOfInterestPrototype proto, Vector2 position, string? name, out EntityUid? grid)
    {
        if (!TrySpawnPoiGrid(map, proto, position, out grid, name))
            return false;

        AddStationCoordsToSet(position);
        return true;
    }
}
