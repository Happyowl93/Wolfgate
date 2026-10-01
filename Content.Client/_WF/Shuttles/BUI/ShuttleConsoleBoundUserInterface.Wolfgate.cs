using Content.Shared._WF.ShipPa;
using Content.Shared._WF.Shuttles;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfOpen()
    {
        if (_window == null)
            return;

        _window.ShipStatusActiveChanged += (active, overlays) =>
            SendMessage(new ShipStatusRequestMessage(active, overlays));

        _window.ShipCodeRequested += code => SendMessage(new ShipAlertCodeRequestMessage(code));
        _window.ShipGeneralQuartersRequested += active => SendMessage(new ShipGeneralQuartersRequestMessage(active));
        _window.ShipAnnounceRequested += text => SendMessage(new ShipPaAnnounceRequestMessage(text));
        _window.ShipSoundRequested += url => SendMessage(new ShipPaInternetSoundRequestMessage(url));
        _window.ShipSoundStopRequested += () => SendMessage(new ShipPaInternetSoundStopMessage());
        _window.ShipCollisionAlertRequested += enabled => SendMessage(new CollisionWarningToggleMessage(enabled));
        _window.ShipCameraRequested += (view, zoom, lowLight) => SendMessage(new ShuttleCameraSetMessage(view, zoom, lowLight));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        WfShieldReceiveMessage(message); // WOLFGATE(ShipShields): update shield controls without refreshing navigation.

        if (message is ShipStatusMessage status)
            _window?.UpdateShipStatus(status);
    }
}
