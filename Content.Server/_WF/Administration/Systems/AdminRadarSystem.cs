using Content.Server.Administration.Managers;
using Content.Shared._WF.Administration.AdminRadar;
using Content.Shared.Administration;
using Content.Shared.Ghost;
using Robust.Shared.Player;

namespace Content.Server._WF.Administration.Systems;

/// <summary>
/// Tells an admin's mass scanner where the players are.
/// </summary>
public sealed partial class AdminRadarSystem : EntitySystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<AdminRadarPlayersRequestEvent>(OnPlayersRequest);
    }

    private void OnPlayersRequest(AdminRadarPlayersRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Admin))
            return;

        RaiseNetworkEvent(new AdminRadarPlayersEvent(GetPlayers()), args.SenderSession);
    }

    /// <summary>
    /// Every player whose body or ghost is on a map.
    /// </summary>
    public List<AdminRadarPlayer> GetPlayers()
    {
        var players = new List<AdminRadarPlayer>();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } uid)
                continue;

            var xform = Transform(uid);
            if (xform.MapUid is not { } map)
                continue;

            // A client always has the grids and maps, but not whatever else the player may be inside.
            var coordinates = _transform.WithEntityId(xform.Coordinates, xform.GridUid ?? map);
            players.Add(new AdminRadarPlayer(GetNetEntity(uid),
                Name(uid),
                session.Name,
                GetNetCoordinates(coordinates),
                HasComp<GhostComponent>(uid)));
        }

        return players;
    }
}
