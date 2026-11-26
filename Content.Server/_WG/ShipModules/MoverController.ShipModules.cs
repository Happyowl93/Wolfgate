using Content.Server._WG.ShipModules;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private WGShipModuleThrustSystem _wgModuleThrust = default!;

    /// <summary>Multiplier the ship's modules put on the helm's linear thrust.</summary>
    private float WgModuleThrustFactor(EntityUid grid) => _wgModuleThrust.LinearFactor(grid);

    /// <summary>Multiplier the ship's modules put on the helm's turning.</summary>
    private float WgModuleTurnFactor(EntityUid grid) => _wgModuleThrust.TurnFactor(grid);
}
