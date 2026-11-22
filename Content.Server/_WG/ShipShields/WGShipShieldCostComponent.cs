using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipShields;

/// <summary>What a generator's raised field costs its ship: thrust, turning, stealth, heat for firing through it, and its guns when it is beaten down.</summary>
[RegisterComponent]
public sealed partial class WGShipShieldCostComponent : Component
{
    /// <summary>Share of linear thrust the ship keeps while this generator's field is up.</summary>
    [DataField]
    public float ThrustFactor = 0.8f;

    /// <summary>Share of turning thrust the ship keeps while this generator's field is up.</summary>
    [DataField]
    public float TurnFactor = 0.85f;

    /// <summary>Heat per second the raised field adds to the ship's thermal signature.</summary>
    [DataField]
    public float Signature = 1000000f;

    /// <summary>Signature heat added to the ship at once for each point of damage the field absorbs.</summary>
    [DataField]
    public float HitSignature = 400f;

    /// <summary>Share of a shot's damage the ship takes as heat when it fires through this generator's raised field.</summary>
    [DataField]
    public float FireHeat = 0.1f;

    /// <summary>Seconds the ship's guns stay locked after damage beats the field down.</summary>
    [DataField]
    public float GunLockout = 6f;

    /// <summary>Effect spawned on each of the ship's mounted guns for as long as they are locked.</summary>
    [DataField]
    public EntProtoId? GunLockEffect = "WGEffectShieldGunLock";
}
