using Content.Shared._WF.MameArcade;
using Robust.Shared.Network;

namespace Content.Client._WF.MameArcade;

/// <summary>Sends and receives 2-player inputs as soon as they arrive, outside the game tick.</summary>
public sealed partial class MameArcadeNetManager
{
    [Dependency] private IClientNetManager _net = default!;

    public event Action<MsgMameArcadeInput>? InputReceived;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgMameArcadeInput>(msg => InputReceived?.Invoke(msg));
    }

    public void Send(MsgMameArcadeInput message)
    {
        _net.ClientSendMessage(message);
    }
}
