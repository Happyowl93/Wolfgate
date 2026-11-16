using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Augments;

/// <summary>
/// Charges packed into the torso: an action fires a ring of pellets out from the body in every direction. The action
/// holds a few bursts and they come back one at a time; the wearer is never hit by their own pellets. Each model is
/// its own component, so the augment system can tell which one a body carries.
/// </summary>
public abstract partial class WFBurstLauncherComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Pellet;

    /// <summary>Pellets in one burst, spread evenly round the body.</summary>
    [DataField]
    public int Pellets = 24;

    [DataField]
    public float Speed = 20f;

    /// <summary>The action, whose charges are the bursts held.</summary>
    [DataField(required: true)]
    public EntProtoId Action;

    [ViewVariables]
    public EntityUid? ActionEntity;

    /// <summary>How long one spent burst takes to come back.</summary>
    [DataField]
    public TimeSpan RechargeTime = TimeSpan.FromMinutes(3);

    [ViewVariables]
    public TimeSpan? NextCharge;

    /// <summary>The shortest gap between two bursts, so both can't go off on one press.</summary>
    [DataField]
    public TimeSpan Spacing = TimeSpan.FromSeconds(1.5);

    [ViewVariables]
    public TimeSpan NextBurst;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/explosion_small1.ogg");
}

/// <summary>The lethal model: shrapnel.</summary>
[RegisterComponent]
public sealed partial class WFHaloBurstComponent : WFBurstLauncherComponent;

/// <summary>The stun model: rubber pellets.</summary>
[RegisterComponent]
public sealed partial class WFHaloStunComponent : WFBurstLauncherComponent;

public sealed partial class WFFireBurstLauncherEvent : InstantActionEvent;
