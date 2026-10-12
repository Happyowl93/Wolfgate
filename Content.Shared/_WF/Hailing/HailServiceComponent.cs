using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Shared._WF.Hailing;

/// <summary>
/// A console whose UI a hail contact on the same grid can open remotely for the caller. The UI is relayed through the
/// caller's helm, so it must work from its state and messages alone.
/// </summary>
[RegisterComponent]
public sealed partial class HailServiceComponent : Component
{
    /// <summary>Name of the service shown in the call.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>UI key of the console's interface to relay.</summary>
    [DataField(required: true, customTypeSerializer: typeof(EnumSerializer))]
    public Enum Key = default!;

    /// <summary>
    /// The service sets its state on the helm for each caller (see HailServiceOpenedEvent) instead of having the
    /// console's state mirrored.
    /// </summary>
    [DataField]
    public bool PerCaller;
}
