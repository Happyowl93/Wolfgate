using System.Numerics;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicVisualsSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private Robust.Shared.Network.INetManager _net = default!; // WOLFGATE(Weapons)
    [Dependency] private ISharedPlayerManager _player = default!; // WOLFGATE(Weapons)

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanBasicVisualsComponent, HitscanRaycastFiredEvent>(OnHitscanHit, before: [ typeof(HitscanReflectSystem) ]);
    }

    private void OnHitscanHit(Entity<HitscanBasicVisualsComponent> hitscan, ref HitscanRaycastFiredEvent args)
    {
        FireEffects(args.FromCoordinates, args.DistanceTried, args.ShotDirection.ToAngle(), hitscan.Comp, args.Shooter, args.Predicted); // WOLFGATE(Weapons)
    }

    private void FireEffects(EntityCoordinates fromCoordinates, float distance, Angle shotAngle, HitscanBasicVisualsComponent hitscan, EntityUid? shooter = null, bool predicted = false) // WOLFGATE(Weapons)
    {
        if (distance == 0)
            return;

        var sprites = new List<(NetCoordinates coordinates, Angle angle, SpriteSpecifier sprite, float scale)>();
        var fromXform = Transform(fromCoordinates.EntityId);

        // We'll get the effects relative to the grid / map of the firer
        // Look you could probably optimise this a bit with redundant transforms at this point.

        var gridUid = fromXform.GridUid;
        if (gridUid != fromCoordinates.EntityId && TryComp(gridUid, out TransformComponent? gridXform))
        {
            var (_, gridRot, gridInvMatrix) = _transform.GetWorldPositionRotationInvMatrix(gridXform);
            var map = _transform.ToMapCoordinates(fromCoordinates);
            fromCoordinates = new EntityCoordinates(gridUid.Value, Vector2.Transform(map.Position, gridInvMatrix));
            shotAngle -= gridRot;
        }
        else
        {
            shotAngle -= _transform.GetWorldRotation(fromXform);
        }

        if (distance >= 1f)
        {
            if (hitscan.MuzzleFlash != null)
            {
                var coords = fromCoordinates.Offset(shotAngle.ToVec().Normalized() / 2);
                var netCoords = GetNetCoordinates(coords);

                sprites.Add((netCoords, shotAngle, hitscan.MuzzleFlash, 1f));
            }

            if (hitscan.TravelFlash != null)
            {
                var coords = fromCoordinates.Offset(shotAngle.ToVec() * (distance + 0.5f) / 2);
                var netCoords = GetNetCoordinates(coords);

                sprites.Add((netCoords, shotAngle, hitscan.TravelFlash, distance - 1.5f));
            }
        }

        if (hitscan.ImpactFlash != null)
        {
            var coords = fromCoordinates.Offset(shotAngle.ToVec() * distance);
            var netCoords = GetNetCoordinates(coords);

            sprites.Add((netCoords, shotAngle.FlipPositive(), hitscan.ImpactFlash, 1f));
        }

        // WOLFGATE(Weapons) START: predicted hitscan beams
        // upstream original, kept for merges:
        // if (sprites.Count > 0)
        // {
        //     RaiseNetworkEvent(new SharedGunSystem.HitscanEvent
        //     {
        //         Sprites = sprites,
        //     }, Filter.Pvs(fromCoordinates, entityMan: EntityManager));
        // }
        if (sprites.Count == 0)
            return;

        var ev = new SharedGunSystem.HitscanEvent
        {
            Sprites = sprites,
        };

        // WOLFGATE(Weapons): on the client this only runs for a beam the shooter is predicting, so draw it here and now
        if (_net.IsClient)
        {
            RaiseLocalEvent(ev);
            return;
        }

        var filter = Filter.Pvs(fromCoordinates, entityMan: EntityManager);

        // WOLFGATE(Weapons): the shooter's client already drew this beam the moment they fired
        if (predicted && shooter != null && _player.TryGetSessionByEntity(shooter.Value, out var session))
            filter = filter.RemovePlayer(session);

        RaiseNetworkEvent(ev, filter);
        // WOLFGATE END
    }
}
