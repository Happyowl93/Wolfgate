using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._WF.SectorTravel.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.SectorTravel;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.SectorTravel;

public sealed partial class WFSectorTravelSystem
{
    [Dependency] private ShuttleSystem _shuttles = default!;

    private const int ArrivalTries = 16;

    /// <summary>Clearance kept around an arriving ship beyond its own size, in metres.</summary>
    private const float ArrivalClearance = 100f;

    private void InitializeTransit()
    {
    }

    /// <summary>
    /// Starts a ship's crossing to a linked sector: the upstream FTL sequence with the given spool and travel time,
    /// arriving on a ring around that sector's beacon back. Every crossing goes through here.
    /// </summary>
    public bool TryTransit(EntityUid ship, string to, float spool, float travel, [NotNullWhen(false)] out string? reason)
    {
        reason = null;
        if (SectorOfEntity(ship) is not { } from || from == to || !WFSectorGraph.Linked(_proto, from, to))
        {
            reason = "wf-sector-travel-no-link";
            return false;
        }

        if (FindBeacon(to, from) is not { } arrival)
        {
            reason = "wf-sector-travel-no-beacon";
            return false;
        }

        if (!TryComp<ShuttleComponent>(ship, out var shuttle) || !_shuttles.CanFTL(ship, out _))
        {
            reason = "wf-sector-travel-cannot-jump";
            return false;
        }

        var target = ArrivalPoint(ship, arrival);
        _shuttles.FTLToCoordinates(ship, shuttle, target, _random.NextAngle(), spool, travel);
        if (!HasComp<FTLComponent>(ship))
        {
            reason = "wf-sector-travel-cannot-jump";
            return false;
        }

        return true;
    }

    /// <summary>Hyperspace time between two sectors for a jump from a beacon with these settings.</summary>
    public float TravelTime(WFJumpBeaconComponent beacon, string from, string to)
    {
        return beacon.TravelBase + beacon.TravelPerUnit * WFSectorGraph.Distance(_proto, from, to);
    }

    /// <summary>The beacon in one sector that links to another.</summary>
    public Entity<WFJumpBeaconComponent>? FindBeacon(string sector, string link)
    {
        var query = EntityQueryEnumerator<WFJumpBeaconComponent>();
        while (query.MoveNext(out var uid, out var beacon))
        {
            if (beacon.Sector == sector && beacon.Link == link)
                return (uid, beacon);
        }

        return null;
    }

    /// <summary>A clear point on the arrival ring around a beacon, big enough for the ship.</summary>
    private EntityCoordinates ArrivalPoint(EntityUid ship, Entity<WFJumpBeaconComponent> beacon, float? size = null)
    {
        var map = Transform(beacon).MapUid!.Value;
        var mapId = Transform(map).MapID;
        var centre = _transform.GetWorldPosition(beacon);
        size ??= TryComp<MapGridComponent>(ship, out var grid) ? grid.LocalAABB.Size.Length() / 2f : 50f;

        var point = centre;
        for (var i = 0; i < ArrivalTries; i++)
        {
            var radius = _random.NextFloat(beacon.Comp.ArrivalMin, MathF.Max(beacon.Comp.ArrivalMin, beacon.Comp.ArrivalMax));
            point = centre + _random.NextAngle().ToVec() * radius;
            if (Clear(mapId, point, size.Value + ArrivalClearance))
                break;
        }

        return new EntityCoordinates(map, point);
    }
}
