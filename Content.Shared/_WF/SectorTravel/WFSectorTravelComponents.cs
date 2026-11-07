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

/// <summary>On a ship whose grid centre is inside a jump beacon's zone: what the helm's beacon panel shows.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFBeaconZoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<WFBeaconLink> Links = new();

    /// <summary>Jump fuel in the ship's drive, and how much it holds; zero capacity means no drive.</summary>
    [DataField, AutoNetworkedField]
    public float Fuel;

    [DataField, AutoNetworkedField]
    public float Capacity;

    /// <summary>Until when the ship can't beacon-jump again after arriving.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? LockedUntil;

    /// <summary>The sector the ship is spooling toward, or null.</summary>
    [DataField, AutoNetworkedField]
    public string? Spooling;
}

/// <summary>On a ship docked to a station that sells fusion fuel: what topping up the jump fuel tank there costs.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFFuelServiceComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Station = string.Empty;

    /// <summary>Fuel the station can put in the tank now, up to what it holds.</summary>
    [DataField, AutoNetworkedField]
    public int Units;

    [DataField, AutoNetworkedField]
    public int Cost;

    [DataField, AutoNetworkedField]
    public float Fuel;

    [DataField, AutoNetworkedField]
    public float Capacity;
}

/// <summary>Helm request to buy jump fuel from the station the ship is docked to.</summary>
[Serializable, NetSerializable]
public sealed class WFFuelServiceMessage : BoundUserInterfaceMessage;

/// <summary>One sector a beacon reaches, with the jump's fuel cost and travel time for this ship.</summary>
[Serializable, NetSerializable]
public readonly record struct WFBeaconLink(string Sector, float Cost, float Travel, bool Closed);

/// <summary>Helm request to spool a beacon jump to a linked sector.</summary>
[Serializable, NetSerializable]
public sealed class WFBeaconJumpMessage(string sector) : BoundUserInterfaceMessage
{
    public readonly string Sector = sector;
}

/// <summary>Helm request to stop a beacon jump still spooling.</summary>
[Serializable, NetSerializable]
public sealed class WFBeaconCancelMessage : BoundUserInterfaceMessage;

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
