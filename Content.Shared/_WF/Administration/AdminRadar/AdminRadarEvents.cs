using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Administration.AdminRadar;

/// <summary>
/// An admin's mass scanner asks where every player is. Ignored for anyone else.
/// </summary>
[Serializable, NetSerializable]
public sealed class AdminRadarPlayersRequestEvent : EntityEventArgs
{
}

/// <summary>
/// Every player with a body or ghost on a map, for the admin scanner's player markers and jump list.
/// </summary>
[Serializable, NetSerializable]
public sealed class AdminRadarPlayersEvent : EntityEventArgs
{
    public List<AdminRadarPlayer> Players;

    public AdminRadarPlayersEvent(List<AdminRadarPlayer> players)
    {
        Players = players;
    }
}

/// <summary>
/// One player on the admin scanner. The coordinates hang off a grid or map, which every client knows about.
/// </summary>
[Serializable, NetSerializable]
public record struct AdminRadarPlayer(NetEntity Entity, string Name, string Username, NetCoordinates Coordinates, bool Ghost);
