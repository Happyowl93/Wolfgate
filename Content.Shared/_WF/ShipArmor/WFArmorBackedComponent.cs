namespace Content.Shared._WF.ShipArmor;

/// <summary>
/// A ship mount that moves part of the damage it takes onto the thickest hull armor next to it.
/// </summary>
[RegisterComponent]
public sealed partial class WFArmorBackedComponent : Component
{
    /// <summary>Share of explosion damage moved to fully covering armor.</summary>
    [DataField]
    public float ExplosionTransfer = 0.8f;

    /// <summary>Share of non-penetrating projectile damage moved to fully covering armor.</summary>
    [DataField]
    public float ProjectileTransfer = 0.4f;

    /// <summary>Share of penetrating (armor-piercing) projectile damage moved to fully covering armor.</summary>
    [DataField]
    public float PenetratingTransfer;

    /// <summary>Armor thickness that gives full coverage; thinner armor scales the transfer down.</summary>
    [DataField]
    public int FullThickness = 10;

    /// <summary>Tile radius searched for backing armor, diagonals included.</summary>
    [DataField]
    public int Radius = 1;
}
