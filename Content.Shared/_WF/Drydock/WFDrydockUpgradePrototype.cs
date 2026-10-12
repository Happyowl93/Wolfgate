using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// Which ship machine category an upgrade applies to.
/// </summary>
public enum WFDrydockUpgradeTarget : byte
{
    LinearThruster,
    Gyroscope,
    Power,
}

/// <summary>
/// A refit line the drydock sells: each tier swaps the target category's stock machine parts for higher-rated ones.
/// </summary>
[Prototype("wfDrydockUpgrade")]
public sealed partial class WFDrydockUpgradePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Name shown in the console.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Machine category this upgrade applies to.</summary>
    [DataField(required: true)]
    public WFDrydockUpgradeTarget Target;

    /// <summary>Tiers in order; index 0 is Mk II.</summary>
    [DataField(required: true)]
    public List<WFDrydockUpgradeTier> Tiers = new();
}

/// <summary>
/// One refit tier: the part rating it installs and its price per part.
/// </summary>
[DataDefinition]
public sealed partial class WFDrydockUpgradeTier
{
    /// <summary>Machine part rating installed by this tier.</summary>
    [DataField(required: true)]
    public int Rating;

    /// <summary>Price charged per machine part.</summary>
    [DataField(required: true)]
    public int Price;

    /// <summary>Materials per part for a console that pays in materials; empty falls back to the console's rates.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> Materials = new();
}
