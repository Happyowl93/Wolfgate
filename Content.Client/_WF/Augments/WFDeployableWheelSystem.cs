using Content.Client._WF.Augments.UI;
using Content.Shared._WF.Augments;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Player;

namespace Content.Client._WF.Augments;

/// <summary>Opens the wheel of a deployable implant under the mouse and sends the server the item picked on it.</summary>
public sealed class WFDeployableWheelSystem : EntitySystem
{
    [Dependency] private IClyde _display = default!;
    [Dependency] private IInputManager _input = default!;

    private WFDeployableWheel? _wheel;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFDeployableWheelOpenEvent>(OnOpen);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => _wheel?.Close());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _wheel?.Close();
    }

    private void OnOpen(WFDeployableWheelOpenEvent msg)
    {
        _wheel?.Close();

        var action = msg.Action;
        var wheel = new WFDeployableWheel(msg.Items, msg.Deployed);
        wheel.OnPicked += index => RaiseNetworkEvent(new WFDeployableWheelPickEvent { Action = action, Item = index });
        wheel.OnClose += () =>
        {
            if (_wheel == wheel)
                _wheel = null;
        };
        _wheel = wheel;
        wheel.OpenCenteredAt(_input.MouseScreenPosition.Position / _display.ScreenSize);
    }
}
