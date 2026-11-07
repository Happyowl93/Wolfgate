using Content.Shared._WF.SectorTravel;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedShuttleSystem
{
    /// <summary>
    /// Whether normal FTL may go from the ship's map to another: never between maps of two different sectors, which
    /// only a jump beacon joins. A map that belongs to no sector is not limited.
    /// </summary>
    protected bool WfSameSector(EntityUid shuttleUid, EntityUid targetMapUid)
    {
        if (!TryComp<WFSectorMapComponent>(targetMapUid, out var target)
            || _xformQuery.GetComponent(shuttleUid).MapUid is not { } shuttleMapUid
            || !TryComp<WFSectorMapComponent>(shuttleMapUid, out var own))
            return true;

        return own.Sector == target.Sector;
    }
}
