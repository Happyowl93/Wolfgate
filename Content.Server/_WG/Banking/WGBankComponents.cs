using Robust.Shared.Network;

namespace Content.Server._WG.Banking;

/// <summary>Marks the Bank PDA app.</summary>
[RegisterComponent]
public sealed partial class WGBankCartridgeComponent : Component;

/// <summary>The account an ID card signs into: the character it was issued to at spawn.</summary>
[RegisterComponent]
public sealed partial class WGBankCardComponent : Component
{
    [ViewVariables]
    public NetUserId User;

    /// <summary>The character's slot in the player's preferences.</summary>
    [ViewVariables]
    public int Slot;

    /// <summary>The character's name, checked against the slot so a replaced character doesn't inherit the card.</summary>
    [ViewVariables]
    public string Name = string.Empty;
}
