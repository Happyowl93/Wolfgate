using Content.Client.Shuttles.UI;
using Content.Shared._WF.BoardingAction;
using Content.Shared.Input;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace Content.Client._WF.BoardingAction;

/// <summary>Sends the ship module key to the server with where the cursor is, on the radar or in the world.</summary>
public sealed partial class WFShipModuleInputSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.ShuttleModule, InputCmdHandler.FromDelegate(_ => OnPressed()))
            .Register<WFShipModuleInputSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WFShipModuleInputSystem>();
    }

    private void OnPressed()
    {
        if (HasComp<PilotComponent>(_player.LocalEntity))
            RaiseNetworkEvent(new WFFtlDisruptorToggleEvent(Cursor()));
    }

    /// <summary>The world point under the cursor, through whichever radar or viewport it is over.</summary>
    private MapCoordinates Cursor()
    {
        var mouse = _input.MouseScreenPosition;
        for (var control = _ui.MouseGetControl(mouse); control != null; control = control.Parent)
        {
            if (control is ShuttleNavControl radar)
            {
                return _transform.ToMapCoordinates(
                    radar.WfCockpitAimCoordinates(mouse.Position / radar.UIScale - radar.GlobalPosition));
            }

            if (control is IViewportControl viewport)
                return viewport.PixelToMap(mouse.Position);
        }

        return _eye.PixelToMap(mouse);
    }
}
