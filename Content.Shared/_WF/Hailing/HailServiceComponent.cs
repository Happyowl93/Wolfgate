using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Shared._WF.Hailing;

/// <summary>
/// An entity whose UI a caller can open over a hail to its grid. It is relayed through the caller's helm, so it must
/// work from its state and messages alone.
/// </summary>
[RegisterComponent]
public sealed partial class HailServiceComponent : Component
{
    /// <summary>Name of the service shown in the call.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>UI key of the service's interface to relay.</summary>
    [DataField(required: true, customTypeSerializer: typeof(EnumSerializer))]
    public Enum Key = default!;

    /// <summary>
    /// The service sets its state on the helm per caller (see HailServiceOpenedEvent) instead of mirroring the console.
    /// </summary>
    [DataField]
    public bool PerCaller;
}
