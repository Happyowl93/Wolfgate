namespace Content.Shared._WF.Cyborgs;

/// <summary>
/// A cyborg module stack that tops back up while its cyborg sits in a working charger.
/// </summary>
[RegisterComponent]
public sealed partial class WFBorgStackRefillComponent : Component
{
    /// <summary>
    /// The count the charger refills the stack to. Picking up more by hand can go past it.
    /// </summary>
    [DataField]
    public int Target = 15;
}
