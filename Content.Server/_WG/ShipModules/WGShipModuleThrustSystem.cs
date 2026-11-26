using Content.Shared._WG.ShipModules;

namespace Content.Server._WG.ShipModules;

/// <summary>How a ship's modules scale its helm's thrust: a running burn drive takes it, chemical fuel adds to it.</summary>
public sealed class WGShipModuleThrustSystem : EntitySystem
{
    private EntityQuery<WGBurnDriveActiveComponent> _burns;
    private EntityQuery<WGChemicalThrustComponent> _chemical;

    public override void Initialize()
    {
        base.Initialize();
        _burns = GetEntityQuery<WGBurnDriveActiveComponent>();
        _chemical = GetEntityQuery<WGChemicalThrustComponent>();
    }

    /// <summary>Multiplier on the helm's linear thrust.</summary>
    public float LinearFactor(EntityUid grid)
    {
        if (_burns.HasComp(grid))
            return 0f;

        return _chemical.TryComp(grid, out var chemical)
            ? chemical.Fueled ? chemical.ThrustFactor : chemical.EmptyFactor
            : 1f;
    }

    /// <summary>Multiplier on the helm's turning.</summary>
    public float TurnFactor(EntityUid grid)
    {
        if (_burns.HasComp(grid))
            return 0f;

        return _chemical.TryComp(grid, out var chemical)
            ? chemical.Fueled ? chemical.TurnFactor : chemical.EmptyFactor
            : 1f;
    }
}
