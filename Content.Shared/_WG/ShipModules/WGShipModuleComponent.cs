using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// A ship system bolted to the hull that the pilot fires from the helm. It holds charges, refills them while powered
/// and reports its state to the ship's <see cref="WGShipModulesComponent"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WGShipModuleComponent : Component
{
    /// <summary>Short Fluent name on the helm's module strip.</summary>
    [DataField(required: true)]
    public LocId Label;

    /// <summary>Uses it can hold.</summary>
    [DataField]
    public int MaxCharges = 1;

    /// <summary>Uses left; filled on map init.</summary>
    [DataField]
    public int Charges;

    /// <summary>Time a powered module takes to refill one charge.</summary>
    [DataField]
    public TimeSpan RechargeTime = TimeSpan.FromSeconds(10);

    /// <summary>Shortest time between two uses.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    /// <summary>Whether a raised shield field on the ship locks the module. A deflector's partial field never does.</summary>
    [DataField]
    public bool BlockedByShields;

    /// <summary>How long the module stays locked after the ship's field drops.</summary>
    [DataField]
    public TimeSpan ShieldLockout = TimeSpan.FromSeconds(5);

    /// <summary>Played through the whole hull on each use.</summary>
    [DataField]
    public SoundSpecifier? ActivateSound;

    /// <summary>How hard each use shakes the crew's view and the ship's helm screens; 0 for none.</summary>
    [DataField]
    public float Jolt = 1f;

    /// <summary>When the next charge arrives, while below <see cref="MaxCharges"/>.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextCharge;

    /// <summary>When the cooldown from the last use ends.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReadyAt;
}
