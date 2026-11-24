using Content.Shared._WG.ShipShields;
using Content.Shared.Shuttles.Components;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.ShipShields;

/// <summary>Sends the pilot's shield and vent hotkeys to the server, which checks the helm and acts on the ship.</summary>
public sealed partial class WGShipShieldKeysSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
            .Bind(WGShipShieldKeys.WGToggleShipShields, InputCmdHandler.FromDelegate(_ => Send(new WGShipShieldToggleEvent()),
                handle: true, outsidePrediction: true))
            .Bind(WGShipShieldKeys.WGVentShipHeat, InputCmdHandler.FromDelegate(_ => Send(new WGShipVentKeyEvent()),
                handle: true, outsidePrediction: true))
            .Register<WGShipShieldKeysSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGShipShieldKeysSystem>();
    }

    private void Send(EntityEventArgs message)
    {
        if (_player.LocalEntity is { } user && TryComp<PilotComponent>(user, out var pilot) && pilot.Console != null)
            RaiseNetworkEvent(message);
    }
}
