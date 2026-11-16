using Robust.Shared.GameStates;

namespace Content.Shared._WF.Augments;

/// <summary>A gun link in a hand: a gun held in it spreads less, and more than an unlinked one while an EMP has it scrambled.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFSmartlinkComponent : Component
{
    /// <summary>What the spread of a gun in this hand is multiplied by.</summary>
    [DataField]
    public float SpreadMultiplier = 0.6f;

    /// <summary>The multiplier while scrambled.</summary>
    [DataField]
    public float ScrambledSpreadMultiplier = 1.5f;

    /// <summary>Set by an EMP until it wears off.</summary>
    [DataField, AutoNetworkedField]
    public bool Scrambled;
}

/// <summary>On a body with a smartlinked hand, so the guns it holds ask the link for their spread.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSmartlinkUserComponent : Component;
