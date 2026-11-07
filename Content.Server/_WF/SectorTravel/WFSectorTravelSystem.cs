using System.Linq;
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._NF.GameRule;
using Content.Server._NF.GameTicking.Events;
using Content.Server._NF.Trade;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Planets;
using Content.Server._WF.SectorTravel.Components;
using Content.Server._WF.TradeGoods.Components;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Events;
using Content.Server.Station.Systems;
using Content.Server.Worldgen.Prototypes;
using Content.Shared._WF.Planets;
using Content.Shared._WF.SectorTravel;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Parallax;
using Content.Server.Salvage.Expeditions;
using Content.Shared.Tiles;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.SectorTravel;

/// <summary>
/// Splits the region into sector maps joined by jump beacons. Loads every sector at round start, answers which sector
/// a map belongs to, and moves ships between sectors through <see cref="TryTransit"/>.
/// </summary>
public sealed partial class WFSectorTravelSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PointOfInterestSystem _poi = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StationSystem _station = default!;

    private static readonly EntProtoId BeaconPrototype = "WFJumpBeacon";
    /// <summary>The beacon platform grid; it carries the beacon and the sector's radio relay.</summary>
    private static readonly ResPath BeaconPlatformPath = new("/Maps/_WF/SectorTravel/jump_beacon_platform.yml");

    /// <summary>How far a beacon keeps from other grids, and a sector's stations from its beacons, in metres.</summary>
    private const float BeaconClearance = 2500f;

    private const int PlacementTries = 12;

    private static readonly TimeSpan StampInterval = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, EntityUid> _sectorMaps = new();
    private readonly List<Entity<MapGridComponent>> _grids = new();
    private TimeSpan _nextStamp;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationsGeneratedEvent>(OnStationsGenerated, before: [typeof(WFEncounterSchedulerSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted);
        InitializeTransit();
        InitializeRegion();
        InitializeTrade();
        InitializeFreight();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateRegion();
        UpdateFreight();
        if (_timing.CurTime < _nextStamp)
            return;

        _nextStamp = _timing.CurTime + StampInterval;
        StampPlanetLayers();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        ClearSectors();
    }

    /// <summary>Forgets every loaded sector, as a round restart does.</summary>
    public void ClearSectors()
    {
        _sectorMaps.Clear();
        ClearRegion();
        ClearFreight();
    }

    /// <summary>The adventure rule has placed the home sector's stations: load the region around them.</summary>
    private void OnStationsGenerated(StationsGeneratedEvent args)
    {
        LoadRegion();
    }

    /// <summary>Stamps the default map as the home sector, then loads every other sector and every beacon.</summary>
    public void LoadRegion()
    {
        if (WFSectorGraph.Home(_proto) is not { } home || !_maps.TryGetMap(_ticker.DefaultMap, out var defaultMap))
            return;

        Stamp(defaultMap.Value, home.ID, true);
        _sectorMaps[home.ID] = defaultMap.Value;

        foreach (var sector in _proto.EnumeratePrototypes<WFSectorPrototype>().OrderBy(sector => sector.ID))
        {
            if (!sector.Home)
                LoadSector(sector, defaultMap.Value);
        }

        foreach (var sector in _proto.EnumeratePrototypes<WFSectorPrototype>().OrderBy(sector => sector.ID))
        {
            if (_sectorMaps.TryGetValue(sector.ID, out var map))
                SpawnBeacons(sector, map);
        }

        foreach (var sector in _proto.EnumeratePrototypes<WFSectorPrototype>())
        {
            if (!sector.Home && _sectorMaps.TryGetValue(sector.ID, out var map))
                SpawnPois(sector, map);
        }
    }

    /// <summary>Makes a sector's map, lit and backed like the home sector's, with its asteroids.</summary>
    private void LoadSector(WFSectorPrototype sector, EntityUid home)
    {
        var mapUid = _maps.CreateMap(out _);
        _meta.SetEntityName(mapUid, Loc.GetString(sector.Name));
        Stamp(mapUid, sector.ID, true);
        EnsureComp<WFTradeMarketMapComponent>(mapUid);
        if (TryComp<ParallaxComponent>(home, out var parallax))
            EntityManager.CopyComponent(home, mapUid, parallax);
        if (TryComp<MapLightComponent>(home, out var light))
            EntityManager.CopyComponent(home, mapUid, light);

        if (sector.Worldgen && _cfg.GetCVar(CCVars.WorldgenEnabled)
            && _proto.TryIndex<WorldgenConfigPrototype>(_cfg.GetCVar(CCVars.WorldgenConfig), out var worldgen))
            worldgen.Apply(mapUid, _serialization, EntityManager);

        _sectorMaps[sector.ID] = mapUid;
        Log.Info($"Loaded sector {sector.ID} on {ToPrettyString(mapUid)}");
    }

    private void SpawnPois(WFSectorPrototype sector, EntityUid mapUid)
    {
        var mapId = Transform(mapUid).MapID;
        foreach (var entry in sector.Pois)
        {
            if (!_proto.TryIndex<PointOfInterestPrototype>(entry.Poi, out var poi))
            {
                Log.Error($"Sector {sector.ID} names unknown point of interest {entry.Poi}");
                continue;
            }

            var position = PickPoiPosition(sector, mapId, entry);
            var name = entry.Name is { } locName ? Loc.GetString(locName) : null;
            if (!_poi.WfSpawnSectorPoi(mapId, poi, position, name, out var grid) || grid is not { } loaded)
            {
                Log.Error($"Sector {sector.ID} failed to load point of interest {entry.Poi}");
                continue;
            }

            if (entry.Destination is { } destination
                && _station.GetOwningStation(loaded) is { } station
                && TryComp<TradeCrateDestinationComponent>(station, out var crates))
                crates.DestinationProto = destination;
        }
    }

    /// <summary>A spot in the POI's distance band that keeps clear of the sector's beacons and grids.</summary>
    private Vector2 PickPoiPosition(WFSectorPrototype sector, MapId map, WFSectorPoi entry)
    {
        var position = _random.NextVector2(entry.MinDistance, entry.MaxDistance);
        for (var i = 0; i < PlacementTries; i++)
        {
            if (sector.Beacons.All(beacon => Vector2.Distance(beacon.Position, position) >= BeaconClearance) && Clear(map, position, 1000f))
                break;

            position = _random.NextVector2(entry.MinDistance, entry.MaxDistance);
        }

        return position;
    }

    private bool Clear(MapId map, Vector2 position, float radius)
    {
        _grids.Clear();
        var grids = _grids;
        _maps.FindGridsIntersecting(map, Box2.CenteredAround(position, new Vector2(radius * 2f)), ref grids, approx: true, includeMap: false);
        return grids.Count == 0;
    }

    /// <summary>Builds a beacon platform for each of the sector's links, with the beacon and a radio relay on it.</summary>
    private void SpawnBeacons(WFSectorPrototype sector, EntityUid mapUid)
    {
        var mapId = Transform(mapUid).MapID;
        foreach (var entry in sector.Beacons)
        {
            if (!_proto.TryIndex(entry.Link, out var link))
            {
                Log.Error($"Sector {sector.ID} has a beacon to unknown sector {entry.Link}");
                continue;
            }

            var position = entry.Position;
            for (var i = 0; i < PlacementTries && !Clear(mapId, position, BeaconClearance); i++)
            {
                position = new Angle(MathF.PI / 6f).RotateVec(position);
            }

            if (!_mapLoader.TryLoadGrid(mapId, BeaconPlatformPath, out var loaded, offset: position))
            {
                Log.Error($"Failed to load the beacon platform for {sector.ID} -> {entry.Link}");
                continue;
            }

            var grid = loaded.Value;
            _meta.SetEntityName(grid.Owner, Loc.GetString("wf-sector-beacon-name", ("sector", Loc.GetString(link.Name))));
            _shuttles.SetIFFColor(grid.Owner, Color.FromHex("#7fd4ff"));
            var protection = EnsureComp<ProtectedGridComponent>(grid.Owner);
            protection.PreventFloorRemoval = true;
            protection.PreventFloorPlacement = true;
            protection.PreventRCDUse = true;
            protection.PreventExplosions = true;
            protection.PreventEmpEvents = true;
            _physics.SetBodyType(grid.Owner, BodyType.Static);
            EnsureComp<CleanupImmuneComponent>(grid.Owner);

            var beacon = FindOnGrid(grid.Owner, BeaconPrototype)
                         ?? Spawn(BeaconPrototype, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            var comp = EnsureComp<WFJumpBeaconComponent>(beacon);
            comp.Sector = sector.ID;
            comp.Link = entry.Link;
            comp.Protected = entry.Protected;
            comp.Raiders = entry.Raiders;
            comp.RaidInterval = entry.RaidInterval;
            comp.RaidWarning = entry.RaidWarning;
            comp.NextRaid = _timing.CurTime + NextRaidIn(comp);
            _meta.SetEntityName(beacon, Loc.GetString("wf-sector-beacon-name", ("sector", Loc.GetString(link.Name))));
        }
    }

    /// <summary>The first entity of a prototype directly on a grid, or null.</summary>
    private EntityUid? FindOnGrid(EntityUid grid, EntProtoId proto)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (MetaData(child).EntityPrototype?.ID == proto.Id)
                return child;
        }

        return null;
    }

    /// <summary>Marks a map as part of a sector.</summary>
    public void Stamp(EntityUid map, string sector, bool primary)
    {
        var stamp = EnsureComp<WFSectorMapComponent>(map);
        stamp.Sector = sector;
        stamp.Primary = primary;
        Dirty(map, stamp);
    }

    /// <summary>The sector a map belongs to, or null.</summary>
    public string? SectorOf(EntityUid? map)
    {
        return TryComp<WFSectorMapComponent>(map, out var stamp) ? stamp.Sector.Id : null;
    }

    /// <summary>The sector of the map an entity is on, or null, also for a deleted entity.</summary>
    public string? SectorOfEntity(EntityUid uid)
    {
        return TryComp(uid, out TransformComponent? xform) ? SectorOf(xform.MapUid) : null;
    }

    /// <summary>A sector's own space map, if it is loaded.</summary>
    public EntityUid? MapOf(string sector)
    {
        return _sectorMaps.TryGetValue(sector, out var map) && !TerminatingOrDeleted(map) ? map : null;
    }

    /// <summary>Every loaded sector's own space map, the home sector's first.</summary>
    public IEnumerable<(string Sector, EntityUid Map)> SectorMaps()
    {
        foreach (var (sector, map) in _sectorMaps.OrderByDescending(pair => _proto.TryIndex<WFSectorPrototype>(pair.Key, out var proto) && proto.Home))
        {
            if (!TerminatingOrDeleted(map))
                yield return (sector, map);
        }
    }

    /// <summary>Whether a map is a sector's own space map.</summary>
    public bool IsSectorMap(MapId map)
    {
        return map != MapId.Nullspace && _maps.TryGetMap(map, out var uid)
               && TryComp<WFSectorMapComponent>(uid, out var stamp) && stamp.Primary;
    }

    /// <summary>
    /// The sector maps station encounters are placed on, in the order to try them. Sectors with living players come
    /// first, each next one drawn by its share of those players, so a lone visitor rarely pulls an encounter away;
    /// empty sectors follow at random. Just the default map before any sector is loaded.
    /// </summary>
    public List<MapId> EncounterMaps()
    {
        var sectors = SectorMaps().ToList();
        if (sectors.Count == 0)
            return new List<MapId> { _ticker.DefaultMap };

        // Ghosts don't count; a player on a planet layer or an expedition counts toward its sector.
        var living = new Dictionary<string, int>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (!HasComp<GhostComponent>(uid) && SectorOf(xform.MapUid) is { } sector)
                living[sector] = living.GetValueOrDefault(sector) + 1;
        }

        var order = new List<MapId>();
        var left = sectors.Where(pair => living.ContainsKey(pair.Sector)).ToList();
        while (left.Count > 0)
        {
            var roll = _random.Next(left.Sum(pair => living[pair.Sector]));
            var pick = left.First(pair => (roll -= living[pair.Sector]) < 0);
            left.Remove(pick);
            order.Add(Transform(pick.Map).MapID);
        }

        var empty = sectors.Where(pair => !living.ContainsKey(pair.Sector)).Select(pair => Transform(pair.Map).MapID).ToList();
        _random.Shuffle(empty);
        order.AddRange(empty);
        return order;
    }

    /// <summary>Adds every player on a sector's own space map to a filter, for region-wide sounds.</summary>
    public void AddSectorMaps(Filter filter)
    {
        foreach (var (_, map) in SectorMaps())
        {
            if (map != _maps.GetMapOrInvalid(_ticker.DefaultMap))
                filter.AddInMap(Transform(map).MapID, EntityManager);
        }
    }

    /// <summary>Where a ship leaving an expedition returns to: the sector map it left from.</summary>
    public MapId ReturnMap(EntityUid expeditionMap)
    {
        return SectorOf(expeditionMap) is { } sector && MapOf(sector) is { } map
            ? Transform(map).MapID
            : _ticker.DefaultMap;
    }

    /// <summary>An expedition map belongs to the sector its ship left from.</summary>
    private void OnFtlStarted(ref FTLStartedEvent args)
    {
        var target = args.TargetCoordinates.EntityId;
        var map = TryComp(target, out TransformComponent? xform) ? xform.MapUid : null;
        if (map is not { } expedition || !HasComp<SalvageExpeditionComponent>(expedition) || HasComp<WFSectorMapComponent>(expedition))
            return;

        if (SectorOf(args.FromMapUid) is { } sector)
            Stamp(expedition, sector, false);
    }

    /// <summary>Planet layers belong to the sector of the planet's body; built lazily, so they are stamped as they appear.</summary>
    private void StampPlanetLayers()
    {
        var query = EntityQueryEnumerator<WFPlanetLayerComponent>();
        while (query.MoveNext(out var map, out var layer))
        {
            if (HasComp<WFSectorMapComponent>(map)
                || GetEntity(layer.Network) is not { } network
                || !TryComp<WFPlanetNetworkComponent>(network, out var planetNetwork)
                || planetNetwork.Planet is not { } planet
                || SectorOfEntity(planet) is not { } sector)
                continue;

            Stamp(map, sector, false);
        }
    }
}
