using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Augments;

/// <summary>
/// A plasma cannon on an arm behind the shoulder. One action raises it over the shoulder or folds it away; while it is
/// up, the other aims at a spot, and the cannon charges for a moment, then throws a slow, heavy bolt at it, leaving the
/// hands free. An EMP folds it away and knocks it out for a while.
/// </summary>
[RegisterComponent]
public sealed partial class WGShoulderCannonComponent : Component
{
    [DataField]
    public EntProtoId Action = "WGActionShoulderCannon";

    [ViewVariables]
    public EntityUid? ActionEntity;

    [DataField]
    public EntProtoId DeployAction = "WGActionDeployShoulderCannon";

    [ViewVariables]
    public EntityUid? DeployActionEntity;

    [DataField]
    public EntProtoId Bolt = "WGShoulderCannonBolt";

    /// <summary>How fast the bolt flies, in tiles per second.</summary>
    [DataField]
    public float BoltSpeed = 10f;

    /// <summary>How long the cannon charges before each shot.</summary>
    [DataField]
    public TimeSpan ChargeTime = TimeSpan.FromSeconds(0.8);

    /// <summary>How long an EMP keeps it from firing.</summary>
    [DataField]
    public TimeSpan EmpLockout = TimeSpan.FromSeconds(60);

    [DataField]
    public SoundSpecifier? DeploySound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/batrifle_magin.ogg");

    [DataField]
    public SoundSpecifier? StowSound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagOut/batrifle_magout.ogg");

    [DataField]
    public SoundSpecifier? ChargeSound = new SoundPathSpecifier("/Audio/Weapons/plasma_cutter.ogg");

    [DataField]
    public SoundSpecifier? FireSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/laser_cannon2.ogg");
}

/// <summary>On a body whose shoulder cannon is raised: draws it over the shoulder, glowing while it charges.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGShoulderCannonDeployedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Charging;

    [DataField]
    public SpriteSpecifier.Rsi Sprite = new(new ResPath("/Textures/_WG/Augments/shoulder_cannon.rsi"), "deployed");

    [DataField]
    public SpriteSpecifier.Rsi ChargingSprite = new(new ResPath("/Textures/_WG/Augments/shoulder_cannon.rsi"), "charging");
}

public sealed partial class WGFireShoulderCannonEvent : WorldTargetActionEvent;

public sealed partial class WGToggleShoulderCannonEvent : InstantActionEvent;

/// <summary>The cannon finished charging and fires at <see cref="Target"/>.</summary>
[Serializable, NetSerializable]
public sealed partial class WGShoulderCannonDoAfterEvent : DoAfterEvent
{
    public NetCoordinates Target;

    private WGShoulderCannonDoAfterEvent()
    {
    }

    public WGShoulderCannonDoAfterEvent(NetCoordinates target)
    {
        Target = target;
    }

    public override DoAfterEvent Clone() => this;
}
