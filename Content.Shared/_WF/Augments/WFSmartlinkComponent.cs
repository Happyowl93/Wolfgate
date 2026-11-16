using Robust.Shared.GameStates;

namespace Content.Shared._WF.Augments;

/// <summary>
/// A gun link in a hand that compensates for recoil: a gun held in it spreads slower as it fires and settles sooner,
/// and the reverse while an EMP has it scrambled. Its tightest and widest spread stay the gun's own.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFSmartlinkComponent : Component
{
    /// <summary>What the spread each shot adds is multiplied by.</summary>
    [DataField]
    public float IncreaseMultiplier = 0.5f;

    /// <summary>What the rate the spread settles at is multiplied by.</summary>
    [DataField]
    public float DecayMultiplier = 1.5f;

    /// <summary>What the spread each shot adds is multiplied by while scrambled.</summary>
    [DataField]
    public float ScrambledIncreaseMultiplier = 1.5f;

    /// <summary>What the rate the spread settles at is multiplied by while scrambled.</summary>
    [DataField]
    public float ScrambledDecayMultiplier = 0.67f;

    /// <summary>Set by an EMP until it wears off.</summary>
    [DataField, AutoNetworkedField]
    public bool Scrambled;
}

/// <summary>On a body with a smartlinked hand, so the guns it holds ask the link for their spread.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSmartlinkUserComponent : Component;
