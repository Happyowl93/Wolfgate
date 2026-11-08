using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.SectorTravel;

/// <summary>Marks a map as belonging to a sector. Normal FTL never crosses between two maps of different sectors.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFSectorMapComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<WFSectorPrototype> Sector;

    /// <summary>True on the sector's own space map; false on maps that only belong to it, such as planet layers and expeditions.</summary>
    [DataField, AutoNetworkedField]
    public bool Primary = true;
}

/// <summary>One end of a beacon link and the faction holding its cell, for the region view.</summary>
[Serializable, NetSerializable]
public readonly record struct WFRegionBeaconState(string Sector, string Link, string? Holder, bool Closed);

/// <summary>Sent to clients when who holds the beacons changes: every beacon, and the sectors cut off from home.</summary>
[Serializable, NetSerializable]
public sealed class WFRegionStateEvent : EntityEventArgs
{
    public List<WFRegionBeaconState> Beacons = new();
    public List<string> CutOff = new();
}
