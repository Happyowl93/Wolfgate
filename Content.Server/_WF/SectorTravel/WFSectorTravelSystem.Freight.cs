using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.SectorTravel.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorTravel;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.SectorTravel;

/// <summary>
/// Freight between sectors. A freight run may take its last stop in a linked sector; its route then flies to the beacon,
/// holds there while it crosses, and docks on the far side. A live hull spools a beacon jump like a player ship; a
/// simulated one is moved to the far ring after the same spool and travel time. A closed link sends the run home.
/// </summary>
public sealed partial class WFSectorTravelSystem
{
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFJumpBeaconSystem _beacons = default!;

    /// <summary>The chance a freight run with two or more stops ends in a linked sector.</summary>
    public const float CrossChance = 0.35f;

    /// <summary>Extra time the freighter holds at the beacon beyond its spool and travel, in seconds.</summary>
    private const float CrossingSlack = 20f;

    /// <summary>A simulated ship stands in for this much hull when it lands on the far ring, in metres.</summary>
    private const float ProxySize = 60f;

    private static readonly TimeSpan FreightInterval = TimeSpan.FromSeconds(1);

    /// <summary>Encounter ships that have made their crossing this run, by encounter and ship key.</summary>
    private readonly HashSet<(EntityUid Encounter, string Key)> _crossed = new();

    /// <summary>
    /// Simulated ships between beacons: their encounter and key, where they go and when they land. A stand-in built
    /// into a hull before it lands drops out of here uncrossed, and the hull then makes the jump itself.
    /// </summary>
    private readonly Dictionary<EntityUid, (EntityUid Encounter, string Key, string To, TimeSpan Lands)> _proxies = new();

    private TimeSpan _nextFreight;

    private void ClearFreight()
    {
        _crossed.Clear();
        _proxies.Clear();
        _nextFreight = TimeSpan.Zero;
    }

    /// <summary>
    /// May move a freight run's last stop to a station in a sector linked to the first stop's, over an open link. The
    /// scheduler calls it after placing the run.
    /// </summary>
    public void ExtendAcrossSectors(WFEncounterPrototype prototype, List<EntityUid> stops)
    {
        if (!prototype.Freight || stops.Count < 2 || !_random.Prob(CrossChance) || SectorOfEntity(stops[0]) is not { } from
            || !_proto.TryIndex<WFSectorPrototype>(from, out var sector))
            return;

        var links = sector.Beacons.Select(beacon => beacon.Link.Id)
            .Where(link => WFSectorGraph.Linked(_proto, from, link) && !LinkClosed(from, link) && !IsCutOff(link) && MapOf(link) != null)
            .ToList();
        if (links.Count == 0)
            return;

        var to = _random.Pick(links);
        if (FindBeacon(to, from) is not { } arrival)
            return;

        var stations = _scheduler.Stations(Transform(MapOf(to)!.Value).MapID).Where(station => !stops.Contains(station)).ToList();
        if (stations.Count == 0)
            return;

        var landing = _transform.GetWorldPosition(arrival);
        stops[^1] = stations.MinBy(station => Vector2.DistanceSquared(_transform.GetWorldPosition(station), landing)).Owner;
    }

    /// <summary>
    /// The legs a route flies between two stops in different sectors: out to the beacon's zone, then a hold while it
    /// crosses. Null for stops in the same sector.
    /// </summary>
    public List<WFCrewObjective>? CrossingLeg(EntityUid from, EntityUid to)
    {
        if (SectorOfEntity(from) is not { } a || SectorOfEntity(to) is not { } b || a == b || FindBeacon(a, b) is not { } beacon)
            return null;

        var position = _transform.GetWorldPosition(beacon) + _random.NextAngle().ToVec() * beacon.Comp.ZoneRadius * 0.3f;
        return new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = position, Range = 200f },
            new() { Kind = WFCrewObjectiveKind.Hold, Duration = beacon.Comp.Spool * 2f + TravelTime(beacon.Comp, a, b) + CrossingSlack },
        };
    }

    private void UpdateFreight()
    {
        if (_timing.CurTime < _nextFreight)
            return;

        _nextFreight = _timing.CurTime + FreightInterval;
        LandProxies();

        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution != null || encounter.Stops.Count < 2 || TerminatingOrDeleted(encounter.Stops[^1])
                || SectorOfEntity(encounter.Stops[^1]) is not { } destination)
                continue;

            foreach (var (key, ship) in encounter.Ships)
            {
                if (TerminatingOrDeleted(ship.Grid) || _crossed.Contains((uid, key)) || _proxies.ContainsKey(ship.Grid)
                    || SectorOfEntity(ship.Grid) is not { } here || here == destination
                    || FindBeacon(here, destination) is not { } beacon || !InZone(ship.Grid, beacon))
                    continue;

                Cross((uid, encounter), key, ship, here, destination, beacon);
            }
        }
    }

    private bool InZone(EntityUid ship, Entity<WFJumpBeaconComponent> beacon)
    {
        return Transform(ship).MapID == Transform(beacon).MapID
               && Vector2.Distance(_transform.GetWorldPosition(ship), _transform.GetWorldPosition(beacon)) <= beacon.Comp.ZoneRadius;
    }

    /// <summary>Starts one encounter ship's crossing, or turns it back if a blockade closed the link.</summary>
    private void Cross(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship, string from, string to,
        Entity<WFJumpBeaconComponent> beacon)
    {
        if (LinkClosed(from, to))
        {
            _crossed.Add((encounter, key));
            var away = _transform.GetWorldPosition(beacon) + _random.NextAngle().ToVec() * beacon.Comp.ZoneRadius * 2f;
            _encounters.SetOrders((encounter.Owner, encounter.Comp), key,
                new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.GoTo, Position = away, Range = 300f } });
            return;
        }

        if (ship.Virtual)
        {
            var lands = _timing.CurTime + TimeSpan.FromSeconds(beacon.Comp.Spool + TravelTime(beacon.Comp, from, to));
            _proxies[ship.Grid] = (encounter, key, to, lands);
            return;
        }

        if (HasComp<FTLComponent>(ship.Grid) || HasComp<WFBeaconJumpComponent>(ship.Grid))
            return;

        if (_beacons.TryJump(ship.Grid, to, out var refusal, npc: true))
            _crossed.Add((encounter, key));
        else
            Log.Debug($"Encounter ship {ToPrettyString(ship.Grid)} couldn't cross to {to}: {refusal}");
    }

    /// <summary>Puts simulated ships whose crossing time is up on the far beacon's arrival ring.</summary>
    private void LandProxies()
    {
        foreach (var (proxy, (encounter, key, to, lands)) in _proxies.ToList())
        {
            if (TerminatingOrDeleted(proxy))
            {
                _proxies.Remove(proxy);
                continue;
            }

            if (_timing.CurTime < lands || SectorOfEntity(proxy) is not { } from || FindBeacon(to, from) is not { } arrival)
                continue;

            _proxies.Remove(proxy);
            _crossed.Add((encounter, key));
            _transform.SetCoordinates(proxy, ArrivalPoint(proxy, arrival, ProxySize));
        }
    }

    /// <summary>Whether a simulated ship is between beacons.</summary>
    public bool InCrossing(EntityUid proxy)
    {
        return _proxies.ContainsKey(proxy);
    }
}
