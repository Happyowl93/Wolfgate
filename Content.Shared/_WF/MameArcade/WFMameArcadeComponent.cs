namespace Content.Shared._WF.MameArcade;

/// <summary>An arcade cabinet that runs the player's own MAME ROMs, for one player or two. Lobby state is server-only.</summary>
[RegisterComponent]
public sealed partial class WFMameArcadeComponent : Component
{
    /// <summary>Offered a 2-player game, and is player 1 in it.</summary>
    [ViewVariables]
    public EntityUid? Host;

    /// <summary>Joined the host's game as player 2.</summary>
    [ViewVariables]
    public EntityUid? Guest;

    [ViewVariables]
    public string Game = string.Empty;

    [ViewVariables]
    public string Fingerprint = string.Empty;
}
