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

    /// <summary>Whose game the cabinet shows: player 1 of a 2-player game, or someone playing alone.</summary>
    [ViewVariables]
    public EntityUid? Player;

    [ViewVariables]
    public string PlayerGame = string.Empty;

    /// <summary>Bystanders receiving the picture.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Watchers = new();

    /// <summary>Times anyone started watching, so the player knows to send a keyframe.</summary>
    [ViewVariables]
    public int WatcherJoins;

    /// <summary>Stream bytes the player may still send this second, refilled each second.</summary>
    [ViewVariables]
    public int AudioAllowance;

    [ViewVariables]
    public int VideoAllowance;

    [ViewVariables]
    public TimeSpan AllowanceRefill;
}
