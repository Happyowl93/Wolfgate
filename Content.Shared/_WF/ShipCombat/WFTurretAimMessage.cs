using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipCombat;

/// <summary>Lays the gunner's selected guns on the cursor without firing.</summary>
[Serializable, NetSerializable]
public sealed class WFTurretAimMessage : BoundUserInterfaceMessage
{
    public List<NetEntity> Selected;
    public NetCoordinates Coordinates;

    public WFTurretAimMessage(List<NetEntity> selected, NetCoordinates coordinates)
    {
        Selected = selected;
        Coordinates = coordinates;
    }
}
