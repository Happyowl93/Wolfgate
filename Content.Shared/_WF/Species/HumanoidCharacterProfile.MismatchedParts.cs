namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    /// Lets the character wear every species' markings, hair and facial hair. Set in the creator; random profiles leave it off.
    /// </summary>
    [DataField]
    public bool MismatchedParts { get; set; }

    public HumanoidCharacterProfile WithMismatchedParts(bool enabled)
    {
        return new(this) { MismatchedParts = enabled };
    }
}
