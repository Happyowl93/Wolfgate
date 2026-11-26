namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Fortress shield: a switch that hardens the ship's raised field. Hits it absorbs heat the ship far less, but the
/// field builds hard heat every second it is on and the ship's guns are silent.
/// </summary>
public sealed partial class WGFortressShieldEffect : WGShipModuleEffect
{
    /// <summary>Share of the usual heat an absorbed hit adds while it is on.</summary>
    [DataField]
    public float HitFactor = 0.25f;

    /// <summary>Share of the ship's heat capacity it adds each second it is on, as hard heat.</summary>
    [DataField]
    public float HeatPerSecond = 0.04f;
}
