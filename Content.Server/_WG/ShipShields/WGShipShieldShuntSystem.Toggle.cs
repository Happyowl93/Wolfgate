using Content.Server.Shuttles.Components;
using Content.Shared._WG.ShipShields;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WG.ShipShields;

public sealed partial class WGShipShieldShuntSystem
{
    private void InitializeToggle()
    {
        SubscribeNetworkEvent<WGShipShieldToggleEvent>(OnToggle);
    }

    private void OnToggle(WGShipShieldToggleEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } user)
            TryToggleFromHelm(user);
    }

    /// <summary>
    /// Raises or lowers the shields of the ship a pilot's helm flies, under the same checks as the helm's shield switch.
    /// </summary>
    public bool TryToggleFromHelm(EntityUid pilot)
    {
        if (!TryComp<PilotComponent>(pilot, out var piloting)
            || !TryComp<ShuttleConsoleComponent>(piloting.Console, out var helm)
            || TryGetHelmGrid((piloting.Console.Value, helm), pilot) is not { } grid)
            return false;

        _shields.SetWolfgateShieldEnabled(grid, !GetState(grid).Enabled);
        return true;
    }
}
