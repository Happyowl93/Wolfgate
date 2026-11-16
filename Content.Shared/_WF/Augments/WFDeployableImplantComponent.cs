using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Augments;

/// <summary>
/// An implant in a body part that puts an item in that side's hand on command and takes it back. Every such implant
/// is a component of its own, so a part can hold several and an autosurgeon can tell which it has.
/// </summary>
public abstract partial class WFDeployableImplantComponent : Component
{
    /// <summary>The item that folds out into the hand.</summary>
    [DataField(required: true)]
    public EntProtoId Item;

    /// <summary>The action that folds it out and back, raising <see cref="WFToggleDeployableImplantEvent"/>.</summary>
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

/// <summary>An omnitool folded into a forearm.</summary>
[RegisterComponent]
public sealed partial class WFToolArmComponent : WFDeployableImplantComponent;

/// <summary>A lighter in a fingertip.</summary>
[RegisterComponent]
public sealed partial class WFFingerLighterComponent : WFDeployableImplantComponent;

public sealed partial class WFToggleDeployableImplantEvent : InstantActionEvent;
