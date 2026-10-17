using Content.Shared._WF.Administration.AdminRadar;
using Content.Shared.Ghost;
using Robust.Shared.Timing;

namespace Content.Client._WF.Administration.AdminRadar;

/// <summary>
/// Holds the admin mass scanner's settings and its list of players.
/// </summary>
public sealed partial class AdminRadarSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(1);

    private List<AdminRadarPlayer> _players = new();
    private TimeSpan _nextRequest;

    /// <summary>
    /// Whether the scanner draws grids and beacons as they are, whatever their IFF hides. Kept here, like
    /// <see cref="ShowPlayers"/>, so it survives the window closing.
    /// </summary>
    public bool TrueIff = true;

    /// <summary>
    /// Whether the scanner marks the players on its map.
    /// </summary>
    public bool ShowPlayers;

    /// <summary>
    /// The players as the server last listed them.
    /// </summary>
    public IReadOnlyList<AdminRadarPlayer> Players => _players;

    /// <summary>
    /// A new player list arrived.
    /// </summary>
    public event Action? PlayersUpdated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<AdminRadarPlayersEvent>(OnPlayers);
    }

    private void OnPlayers(AdminRadarPlayersEvent ev)
    {
        _players = ev.Players;
        PlayersUpdated?.Invoke();
    }

    /// <summary>
    /// Asks the server where the players are, at most once a second unless forced.
    /// </summary>
    public void RequestPlayers(bool force = false)
    {
        if (!force && _timing.RealTime < _nextRequest)
            return;

        _nextRequest = _timing.RealTime + RequestInterval;
        RaiseNetworkEvent(new AdminRadarPlayersRequestEvent());
    }

    /// <summary>
    /// Warps the local ghost to a player, the way the ghost warp menu does.
    /// </summary>
    public void JumpTo(NetEntity player)
    {
        RaiseNetworkEvent(new GhostWarpToTargetRequestEvent(player));
    }
}
