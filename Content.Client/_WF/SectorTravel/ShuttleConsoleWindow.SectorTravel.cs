using Content.Client._WF.SectorTravel;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>The MAP tab's jump beacon panel.</summary>
    public WFBeaconJumpPanel WfBeaconPanel => MapContainer.WfBeaconPanel;
}
