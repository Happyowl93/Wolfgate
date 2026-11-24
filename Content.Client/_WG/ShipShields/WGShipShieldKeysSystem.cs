using Content.Shared._WG.ShipShields;
using Content.Shared.Shuttles.Components;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.ShipShields;

/// <summary>Sends the pilot's shield hotkey to the server, which checks the helm and flips the shields.</summary>
public sealed partial class WGShipShieldKeysSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
            .Bind(WGShipShieldKeys.WGToggleShipShields, InputCmdHandler.FromDelegate(_ => Toggle(), handle: true,
                outsidePrediction: true))
            .Register<WGShipShieldKeysSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGShipShieldKeysSystem>();
    }

    private void Toggle()
    {
        if (_player.LocalEntity is { } user && TryComp<PilotComponent>(user, out var pilot) && pilot.Console != null)
            RaiseNetworkEvent(new WGShipShieldToggleEvent());
    }
}
