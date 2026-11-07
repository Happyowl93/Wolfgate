using Content.Client._WF.SectorTravel;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    private WFBeaconJumpPanel? _wfBeaconPanel;

    /// <summary>The jump beacon panel, added under the FTL state the first time it is asked for.</summary>
    public WFBeaconJumpPanel WfBeaconPanel
    {
        get
        {
            if (_wfBeaconPanel != null)
                return _wfBeaconPanel;

            _wfBeaconPanel = new WFBeaconJumpPanel { Shuttle = () => _shuttleEntity };
            RightDisplayMap.AddChild(_wfBeaconPanel);
            _wfBeaconPanel.SetPositionInParent(2);
            return _wfBeaconPanel;
        }
    }
}
