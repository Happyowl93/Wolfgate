using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Augments;

/// <summary>
/// An implant in a body part that puts an item in that side's hand on command and takes it back. With several items
/// its action opens a wheel to pick one from. Every such implant is a component of its own, so a part can hold several
/// and an autosurgeon can tell which it has.
/// </summary>
public abstract partial class WFDeployableImplantComponent : Component
{
    /// <summary>The items that fold out into the hand, one at a time.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Items = new();

    /// <summary>The action that folds one out and back, raising <see cref="WFToggleDeployableImplantEvent"/>.</summary>
    [DataField(required: true)]
    public EntProtoId Action;

    [DataField]
    public SoundSpecifier? DeploySound;

    [DataField]
    public SoundSpecifier? RetractSound;

    [ViewVariables]
    public EntityUid? ActionEntity;

    /// <summary>The item while it is out.</summary>
    [ViewVariables]
    public EntityUid? Deployed;
}

/// <summary>A set of tools folded into a forearm.</summary>
[RegisterComponent]
public sealed partial class WFToolArmComponent : WFDeployableImplantComponent;

/// <summary>A lighter in a fingertip.</summary>
[RegisterComponent]
public sealed partial class WFFingerLighterComponent : WFDeployableImplantComponent;

public sealed partial class WFToggleDeployableImplantEvent : InstantActionEvent;

/// <summary>Sent to the player whose implant holds several items, to open the wheel they pick one from.</summary>
[Serializable, NetSerializable]
public sealed class WFDeployableWheelOpenEvent : EntityEventArgs
{
    public NetEntity Action;
    public List<EntProtoId> Items = new();

    /// <summary>The index of the item that is out, which picking again folds back in.</summary>
    public int? Deployed;
}

/// <summary>Sent by the player who picked an item on their implant's wheel.</summary>
[Serializable, NetSerializable]
public sealed class WFDeployableWheelPickEvent : EntityEventArgs
{
    public NetEntity Action;
    public int Item;
}
