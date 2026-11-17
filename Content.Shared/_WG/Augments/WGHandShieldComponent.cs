using Robust.Shared.Audio;

namespace Content.Shared._WG.Augments;

/// <summary>
/// A shield projector in a hand. Raised, it fills the hand and stops shots and blows that come at the body from in
/// front, each one draining its charge. It recharges only while lowered, and a drained shield collapses and can't be
/// raised again until it is full.
/// </summary>
[RegisterComponent]
public sealed partial class WGHandShieldComponent : WGDeployableImplantComponent
{
    /// <summary>How much damage the shield takes before it collapses.</summary>
    [DataField]
    public float MaxCharge = 150f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float Charge = 150f;

    /// <summary>Whether the shield was drained and is waiting to be full again before it can be raised.</summary>
    [ViewVariables]
    public bool Collapsed;

    /// <summary>Charge regained per second while the shield is lowered.</summary>
    [DataField]
    public float RechargeRate = 10f;

    /// <summary>How wide the cone in front of the body is that the shield covers.</summary>
    [DataField]
    public Angle Arc = Angle.FromDegrees(135);

    /// <summary>How far a laser it turns aside strays from straight back.</summary>
    [DataField]
    public Angle DeflectSpread = Angle.FromDegrees(60);

    [DataField]
    public SoundSpecifier? ShotSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Hits/laser_sear_wall.ogg");

    [DataField]
    public SoundSpecifier? BlowSound = new SoundPathSpecifier("/Audio/Weapons/block_metal1.ogg");

    [DataField]
    public SoundSpecifier? CollapseSound = new SoundPathSpecifier("/Audio/Weapons/ebladeoff.ogg");

    /// <summary>The body the shield is raised in front of.</summary>
    [ViewVariables]
    public EntityUid? RaisedBy;
}

/// <summary>On a body while one of its hands holds a shield up.</summary>
[RegisterComponent]
public sealed partial class WGHandShieldUserComponent : Component
{
    /// <summary>The hand the shield is raised from.</summary>
    [ViewVariables]
    public EntityUid Part;
}
