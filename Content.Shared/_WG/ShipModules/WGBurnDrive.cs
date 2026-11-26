namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Burn drive: for the module's duration the hull is driven hard along its nose and the pilot can neither steer, brake
/// nor thrust. Works under a raised field and pays in heat.
/// </summary>
public sealed partial class WGBurnDriveEffect : WGShipModuleEffect
{
    /// <summary>Forward acceleration for a hull up to <see cref="ReferenceMass"/>, in metres per second squared.</summary>
    [DataField]
    public float Acceleration = 12f;

    /// <summary>Forward speed past which the drive stops adding more, in metres per second.</summary>
    [DataField]
    public float TopSpeed = 75f;

    /// <summary>Hull mass up to which the drive gives its full push; a heavier hull gets the same force.</summary>
    [DataField]
    public float ReferenceMass = 60f;

    /// <summary>Smallest share of <see cref="Acceleration"/> a heavy hull still gets.</summary>
    [DataField]
    public float MinScale = 0.25f;

    /// <summary>Share of the ship's heat capacity the drive adds each second it burns, as soft heat.</summary>
    [DataField]
    public float HeatPerSecond = 0.06f;

    /// <summary>Thermal signature the burn adds each second for long-range sensors.</summary>
    [DataField]
    public float Signature = 600000f;
}
