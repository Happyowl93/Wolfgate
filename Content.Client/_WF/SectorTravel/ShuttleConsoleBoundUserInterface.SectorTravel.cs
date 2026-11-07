using Content.Shared._WF.SectorTravel;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfBeaconJumpOpen()
    {
        if (_window == null)
            return;

        _window.WfBeaconPanel.JumpPressed += sector => SendMessage(new WFBeaconJumpMessage(sector));
        _window.WfBeaconPanel.CancelPressed += () => SendMessage(new WFBeaconCancelMessage());
        _window.WfBeaconPanel.RefuelPressed += () => SendMessage(new WFFuelServiceMessage());
    }
}
