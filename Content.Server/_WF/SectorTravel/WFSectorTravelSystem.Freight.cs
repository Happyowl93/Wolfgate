using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.SectorTravel.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorTravel;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.SectorTravel;

/// <summary>
/// NPC crews between sectors. A freight run may take its last stop in a linked sector. A crew whose order aims at a
/// station or an escorted ship in another sector gets a Cross order first: fly into the beacon's zone and jump. A live
/// hull spools an NPC beacon jump, again after a broken spool; a simulated one is moved to the far ring after the same
/// spool and travel time. The order ends only once the ship is over. A blockaded link offers no way across.
/// </summary>
public sealed partial class WFSectorTravelSystem
{
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFJumpBeaconSystem _beacons = default!;

    /// <summary>The chance a freight run with two or more stops ends in a linked sector.</summary>
    public const float CrossChance = 0.35f;

    /// <summary>How long a crew may take over a crossing, flight to the beacon included, before giving it up, in seconds.</summary>
    private const float CrossBudget = 900f;

    /// <summary>A simulated ship stands in for this much hull when it lands on the far ring, in metres.</summary>
    private const float ProxySize = 60f;

    private static readonly TimeSpan FreightInterval = TimeSpan.FromSeconds(1);

    /// <summary>Simulated ships between beacons: where each goes and when it lands.</summary>
    private readonly Dictionary<EntityUid, (string To, TimeSpan Lands)> _proxies = new();

    private TimeSpan _nextFreight;

    private void InitializeFreight()
    {
        SubscribeLocalEvent<WFCrewTargetAwayEvent>(OnTargetAway);
        SubscribeLocalEvent<WFCrewCrossStepEvent>(OnCrossStep);
    }

    private void ClearFreight()
    {
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
    /// The way to a station or ship in another sector: the Cross order from this sector's beacon on the first link of an
    /// open path there. Player ships aren't followed across.
    /// </summary>
    private void OnTargetAway(ref WFCrewTargetAwayEvent args)
    {
        if (args.Leg != null || HasComp<ShuttleDeedComponent>(args.Target)
            || SectorOfEntity(args.Grid) is not { } from || TargetSector(args.Target) is not { } to || from == to
            || WFSectorGraph.NextHop(_proto, from, to, LinkClosed) is not { } next || FindBeacon(from, next) is not { } beacon)
            return;

        args.Leg = new WFCrewObjective
        {
            Kind = WFCrewObjectiveKind.Cross,
            Target = GetNetEntity(beacon),
            Position = _transform.GetWorldPosition(beacon) + _random.NextAngle().ToVec() * beacon.Comp.ZoneRadius * 0.3f,
            Range = 200f,
            Duration = CrossBudget,
        };
    }

    /// <summary>The sector a ship is in, or for one in hyperspace the sector it is jumping to.</summary>
    private string? TargetSector(EntityUid target)
    {
        if (SectorOfEntity(target) is { } sector)
            return sector;

        return TryComp<FTLComponent>(target, out var ftl) && ftl.TargetCoordinates.IsValid(EntityManager)
            ? SectorOf(_transform.GetMap(ftl.TargetCoordinates))
            : null;
    }

    /// <summary>
    /// One step of a crew's crossing: done once the ship is in the linked sector. Until then a live hull in the zone spools
    /// an NPC beacon jump, again if a spool broke, and a simulated one is timed across; outside the zone it flies on in.
    /// </summary>
    private void OnCrossStep(ref WFCrewCrossStepEvent args)
    {
        if (!TryComp<WFJumpBeaconComponent>(args.Beacon, out var beacon))
        {
            args.Progress = WFCrewCrossProgress.Failed;
            return;
        }

        var here = SectorOfEntity(args.Grid);
        if (here == beacon.Link.Id)
        {
            args.Progress = WFCrewCrossProgress.Done;
            return;
        }

        args.Progress = WFCrewCrossProgress.Waiting;
        if (args.Simulated ? _proxies.ContainsKey(args.Grid) : HasComp<FTLComponent>(args.Grid) || HasComp<WFBeaconJumpComponent>(args.Grid))
            return;

        if (here != beacon.Sector.Id || LinkClosed(beacon.Sector, beacon.Link))
        {
            args.Progress = WFCrewCrossProgress.Failed;
            return;
        }

        if (!InZone(args.Grid, (args.Beacon, beacon)))
            return;

        if (args.Simulated)
            _proxies[args.Grid] = (beacon.Link.Id, _timing.CurTime + TimeSpan.FromSeconds(beacon.Spool + TravelTime(beacon, beacon.Sector.Id, beacon.Link.Id)));
        else if (!_beacons.TryJump(args.Grid, beacon.Link.Id, out var refusal, npc: true))
            Log.Debug($"NPC ship {ToPrettyString(args.Grid)} couldn't cross to {beacon.Link}: {refusal}");
    }

    private void UpdateFreight()
    {
        if (_timing.CurTime < _nextFreight)
            return;

        _nextFreight = _timing.CurTime + FreightInterval;
        LandProxies();
    }

    private bool InZone(EntityUid ship, Entity<WFJumpBeaconComponent> beacon)
    {
        return Transform(ship).MapID == Transform(beacon).MapID
               && Vector2.Distance(_transform.GetWorldPosition(ship), _transform.GetWorldPosition(beacon)) <= beacon.Comp.ZoneRadius;
    }

    /// <summary>Puts simulated ships whose crossing time is up on the far beacon's arrival ring.</summary>
    private void LandProxies()
    {
        foreach (var (proxy, (to, lands)) in _proxies.ToList())
        {
            // Built into a hull while it waited: the hull makes the jump itself.
            if (TerminatingOrDeleted(proxy))
            {
                _proxies.Remove(proxy);
                continue;
            }

            if (_timing.CurTime < lands || SectorOfEntity(proxy) is not { } from || FindBeacon(to, from) is not { } arrival)
                continue;

            _proxies.Remove(proxy);
            _transform.SetCoordinates(proxy, ArrivalPoint(proxy, arrival, ProxySize));
        }
    }

    /// <summary>Whether a simulated ship is between beacons.</summary>
    public bool InCrossing(EntityUid proxy)
    {
        return _proxies.ContainsKey(proxy);
    }
}
