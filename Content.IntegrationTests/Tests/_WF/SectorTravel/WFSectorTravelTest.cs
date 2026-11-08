#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._WF.Hailing;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Server._NF.GameRule;
using Content.Shared._NF.Shipyard.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.SectorTravel;
using Content.Server._WF.SectorTravel.Components;
using Content.Server._WF.TradeGoods;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.SectorTravel;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Parallax;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.SectorTravel;

/// <summary>
/// The region: normal FTL stays inside its sector, a hailed beacon opens a portal and crossing it jumps with a lockout
/// after, a countdown breaks outside the zone and an uncrossed portal closes, a held beacon cuts its sector off, and
/// crates gain a bonus per link.
/// </summary>
[TestFixture]
[TestOf(typeof(WFSectorTravelSystem))]
public sealed class WFSectorTravelTest
{
    private const string Home = "WFSectorCore";
    private const string Far = "WFSectorFarReach";
    private const string Drive = "MachineFTLDrive";
    private const string Holder = "WFTestBeaconHolder";

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfSectorFaction
  id: WFTestBeaconHolder
  name: cmd-wf_region-hint-faction
  color: Red
  blockades: true
";

    private static void SetDefaultMap(GameTicker ticker, MapId map)
    {
        typeof(GameTicker).GetProperty(nameof(GameTicker.DefaultMap), BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(true)!.Invoke(ticker, new object[] { map });
    }

    /// <summary>Loads the region with the test map as the home sector, and makes beacon jumps quick.</summary>
    private static MapId LoadRegion(IEntityManager entMan, TestMapData map)
    {
        var ticker = entMan.System<GameTicker>();
        var old = ticker.DefaultMap;
        SetDefaultMap(ticker, map.MapId);
        entMan.System<WFSectorTravelSystem>().LoadRegion();
        var beacons = entMan.EntityQueryEnumerator<WFJumpBeaconComponent>();
        while (beacons.MoveNext(out var beacon))
        {
            beacon.Countdown = 0.5f;
            beacon.TravelBase = 1f;
            beacon.TravelPerUnit = 0f;
        }

        return old;
    }

    /// <summary>Deletes every loaded sector but the test map, and forgets the region.</summary>
    private static void UnloadRegion(IEntityManager entMan, TestMapData map, MapId oldDefault)
    {
        var sectors = entMan.System<WFSectorTravelSystem>();
        foreach (var (_, sectorMap) in sectors.SectorMaps().ToList())
        {
            if (sectorMap != map.MapUid)
                entMan.DeleteEntity(sectorMap);
        }

        sectors.ClearSectors();
        SetDefaultMap(entMan.System<GameTicker>(), oldDefault);
    }

    /// <summary>A small ship at a map position, with an FTL drive that needs no power grid unless left without one.</summary>
    private static EntityUid Ship(IEntityManager entMan, TestMapData map, MapCoordinates at, bool withDrive = true)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        var grid = mapSys.CreateGridEntity(at.MapId);
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                mapSys.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
        }

        var xforms = entMan.System<SharedTransformSystem>();
        xforms.SetCoordinates(grid.Owner, new EntityCoordinates(mapSys.GetMapOrInvalid(at.MapId), at.Position));
        entMan.EnsureComponent<ShuttleComponent>(grid.Owner);
        if (!withDrive)
            return grid.Owner;

        var drive = entMan.SpawnEntity(Drive, new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
        if (!entMan.GetComponent<TransformComponent>(drive).Anchored)
            xforms.AnchorEntity(drive);
        Unplug(entMan, drive);
        return grid.Owner;
    }

    /// <summary>Makes a machine count as powered without a power network.</summary>
    private static void Unplug(IEntityManager entMan, EntityUid machine)
    {
        entMan.System<PowerReceiverSystem>().SetNeedsPower(machine, false);
        entMan.GetComponent<ApcPowerReceiverComponent>(machine).Powered = true;
    }

    /// <summary>A crate left floating on the map at a point, not picked up by any grid there.</summary>
    private static EntityUid Floating(IEntityManager entMan, MapCoordinates at)
    {
        var crate = entMan.SpawnEntity("CrateGenericSteel", MapCoordinates.Nullspace);
        var xform = entMan.GetComponent<TransformComponent>(crate);
        xform.GridTraversal = false;
        var mapUid = entMan.System<SharedMapSystem>().GetMapOrInvalid(at.MapId);
        entMan.System<SharedTransformSystem>().SetCoordinates(crate, new EntityCoordinates(mapUid, at.Position));
        return crate;
    }

    private static Entity<WFJumpBeaconComponent> Beacon(IEntityManager entMan, string sector, string link)
    {
        return entMan.System<WFSectorTravelSystem>().FindBeacon(sector, link)!.Value;
    }

    /// <summary>A point inside a beacon's zone.</summary>
    private static MapCoordinates InZone(IEntityManager entMan, EntityUid beacon)
    {
        var position = entMan.System<SharedTransformSystem>().GetMapCoordinates(beacon);
        return new MapCoordinates(position.Position + new Vector2(300f, 0f), position.MapId);
    }

    [Test]
    public async Task NormalFtlStaysInsideItsSector()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var oldDefault = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            var farMap = sectors.MapOf(Far);
            Assert.That(farMap, Is.Not.Null, "Far Reach should be loaded.");
            Assert.That(sectors.SectorOf(map.MapUid), Is.EqualTo(Home));
            Assert.That(sectors.SectorOf(farMap), Is.EqualTo(Far));
            entMan.TryGetComponent<ParallaxComponent>(map.MapUid, out var homeSky);
            entMan.TryGetComponent<ParallaxComponent>(farMap!.Value, out var farSky);
            Assert.That(farSky?.Parallax, Is.EqualTo(homeSky?.Parallax), "A sector without its own sky copies the home sector's.");

            var ship = Ship(entMan, map, new MapCoordinates(new Vector2(-2000f, 0f), map.MapId));
            var shuttles = entMan.System<SharedShuttleSystem>();
            var farMapId = entMan.GetComponent<TransformComponent>(farMap!.Value).MapID;
            Assert.That(shuttles.CanFTLTo(ship, farMapId, ship), Is.False, "Console FTL must not reach another sector.");
            Assert.That(shuttles.CanFTLTo(ship, map.MapId, ship), Is.True, "Console FTL inside the sector is unchanged.");
            Assert.That(Beacon(entMan, Home, Far).Comp.Protected, Is.True);
            Assert.That(Beacon(entMan, Far, Home).Comp.Protected, Is.False);
        });

        await server.WaitPost(() => UnloadRegion(entMan, map, oldDefault));
        await pair.CleanReturnAsync();
    }

    /// <summary>Each platform is the mapped grid: its own beacon gets the link data, and its relay is on board.</summary>
    [Test]
    public async Task BeaconPlatformCarriesMappedBeaconAndRelay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var oldDefault = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            var platforms = 0;
            var beacons = entMan.EntityQueryEnumerator<WFJumpBeaconComponent, TransformComponent>();
            while (beacons.MoveNext(out _, out _, out var xform))
            {
                platforms++;
                var grid = xform.GridUid!.Value;
                var onGrid = new List<string>();
                var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID is { } id)
                        onGrid.Add(id);
                }

                Assert.That(onGrid.Count(id => id == "WFJumpBeacon"), Is.EqualTo(1), "The mapped beacon is the one configured.");
                Assert.That(onGrid.Count(id => id == "WFSectorRelay"), Is.EqualTo(1));
                Assert.That(onGrid, Does.Contain("WallPlastitaniumIndestructible"), "The platform is the mapped grid.");
            }

            Assert.That(platforms, Is.EqualTo(2));
        });

        await server.WaitPost(() => UnloadRegion(entMan, map, oldDefault));
        await pair.CleanReturnAsync();
    }

    /// <summary>True Sight sets up its own Lattice, held by one anchor at Errant in the centre, with no anchor station.</summary>
    [Test]
    public async Task TrueSightIsHeldByErrant()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var oldDefault = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            var farMap = entMan.System<WFSectorTravelSystem>().MapOf(Far)!.Value;
            Assert.That(entMan.GetComponent<WFLatticeFieldComponent>(farMap).Profile, Is.EqualTo("WFTrueSight"));

            var xforms = entMan.System<SharedTransformSystem>();
            var anchors = new List<System.Numerics.Vector2>();
            var query = entMan.EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var anchor, out var xform))
            {
                if (anchor.Primary && xform.MapUid == farMap)
                    anchors.Add(xforms.GetWorldPosition(uid));
            }

            Assert.That(anchors, Has.Count.EqualTo(1), "True Sight has a single primary anchor.");
            Assert.That(anchors[0].Length(), Is.LessThan(1f), "Errant, at the centre, holds the field.");
            Assert.That(entMan.EntityQuery<WFLatticeAnchorStationComponent>()
                .Count(station => entMan.GetComponent<TransformComponent>(station.Owner).MapUid == farMap), Is.Zero, "True Sight has no anchor station.");

            var errant = entMan.EntityQuery<MetaDataComponent>()
                .Where(meta => meta.EntityName == "Errant" && entMan.GetComponent<TransformComponent>(meta.Owner).MapUid == farMap)
                .ToList();
            Assert.That(errant, Has.Count.EqualTo(1));
            Assert.That(xforms.GetWorldPosition(errant[0].Owner).Length(), Is.LessThan(1f), "Errant is at the centre.");
        });

        await server.WaitPost(() => UnloadRegion(entMan, map, oldDefault));
        await pair.CleanReturnAsync();
    }

    /// <summary>A shuttle console on a ship that needs no power grid, and a stand-in with no mob state to man it.</summary>
    private static (EntityUid Console, EntityUid Operator) Helm(IEntityManager entMan, EntityUid ship)
    {
        var console = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(ship, -0.5f, 0.5f));
        if (!entMan.GetComponent<TransformComponent>(console).Anchored)
            entMan.System<SharedTransformSystem>().AnchorEntity(console);
        Unplug(entMan, console);
        return (console, entMan.SpawnEntity(null, new EntityCoordinates(ship, 0.5f, -0.5f)));
    }

    private static EntityUid Platform(IEntityManager entMan, string sector, string link)
    {
        return entMan.GetComponent<TransformComponent>(Beacon(entMan, sector, link)).GridUid!.Value;
    }

    [Test]
    public async Task HailedBeaconConfirmsAndCountsDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var hailing = entMan.System<HailingSystem>();
        var oldDefault = MapId.Nullspace;
        var ship = EntityUid.Invalid;
        var helm = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            Beacon(entMan, Home, Far).Comp.Countdown = 30f;
            ship = Ship(entMan, map, InZone(entMan, Beacon(entMan, Home, Far)));
            var (console, pilot) = Helm(entMan, ship);
            helm = console;
            Assert.That(hailing.TryHailAutomated(console, pilot, Platform(entMan, Home, Far), out var refused), Is.True,
                $"The beacon platform can be hailed: {refused}");
        });

        await PoolManager.WaitUntil(server, () => entMan.HasComponent<WFBeaconJumpComponent>(ship), maxTicks: 300);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<HailConsoleComponent>(helm, out var hail), "The channel is still open.");
            Assert.That(hail!.Log.Any(line => !line.Own && line.Text.StartsWith("CONFIRMED")), "The beacon confirms the jump.");
            Assert.That(entMan.GetComponent<WFBeaconJumpComponent>(ship).Portal, Is.Null, "The portal waits for the countdown.");
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(ship);
            UnloadRegion(entMan, map, oldDefault);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeaconPortalJumpsAShipWithNoDriveAndLocksItOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var beacons = entMan.System<WFJumpBeaconSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var oldDefault = MapId.Nullspace;
        var ship = EntityUid.Invalid;
        var loose = EntityUid.Invalid;
        var portal = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            ship = Ship(entMan, map, InZone(entMan, Beacon(entMan, Home, Far)), withDrive: false);
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() => Assert.That(beacons.TryJump(ship, Far, out var refusal), Is.True,
            $"The beacon jumps a ship with no FTL drive: {refusal}"));

        await PoolManager.WaitUntil(server, () => entMan.GetComponent<WFBeaconJumpComponent>(ship).Portal != null, maxTicks: 300);

        await server.WaitPost(() =>
        {
            var jump = entMan.GetComponent<WFBeaconJumpComponent>(ship);
            portal = jump.Portal!.Value;
            var nose = xforms.GetWorldRotation(ship).RotateVec(Vector2.UnitY);
            var ahead = Vector2.Dot(xforms.GetWorldPosition(portal) - xforms.GetWorldPosition(ship), nose);
            Assert.That(ahead, Is.GreaterThan(0f), "The portal opens ahead of the ship's nose.");
            Assert.That(jump.HalfWidth * 2f, Is.GreaterThanOrEqualTo(3f), "The portal is at least as wide as the ship.");
            Assert.That(entMan.HasComponent<FTLComponent>(ship), Is.False, "Nothing happens until the ship crosses.");

            // Bring only the ship's nose into the portal's near edge, with a crate floating free just off the hull.
            var bounds = entMan.GetComponent<MapGridComponent>(ship).LocalAABB;
            var centre = jump.PortalAt - jump.Forward * (jump.HalfDepth * 0.9f + bounds.Height / 2f - 0.5f);
            var origin = centre - xforms.GetWorldRotation(ship).RotateVec(bounds.Center);
            Assert.That(Vector2.Distance(centre, jump.PortalAt), Is.GreaterThan(jump.HalfDepth), "The ship's centre stays outside.");
            xforms.SetCoordinates(ship, new EntityCoordinates(map.MapUid, origin));
            loose = Floating(entMan, new MapCoordinates(origin + new Vector2(3f, 0f), map.MapId));
            Assert.That(entMan.GetComponent<TransformComponent>(portal).ParentUid, Is.EqualTo(map.MapUid),
                "The ship flying into the portal doesn't pick it up.");
        });

        await PoolManager.WaitUntil(server, () => entMan.HasComponent<WFBeaconLockoutComponent>(ship), maxTicks: 3000);

        await server.WaitAssertion(() =>
        {
            var far = Beacon(entMan, Far, Home);
            Assert.That(sectors.SectorOfEntity(ship), Is.EqualTo(Far), "The ship should arrive in Far Reach.");
            var distance = Vector2.Distance(xforms.GetWorldPosition(ship), xforms.GetWorldPosition(far));
            Assert.That(distance, Is.InRange(far.Comp.ArrivalMin - 10f, far.Comp.ArrivalMax + 10f), "Arrivals land on the ring.");
            Assert.That(entMan.Deleted(portal) || entMan.IsQueuedForDeletion(portal), "The portal closes behind the ship.");
            Assert.That(sectors.SectorOfEntity(loose), Is.EqualTo(Far), "Loose entities hugging the hull cross with it.");

            // Nothing under an arriving hull on a sector map is crushed.
            var under = Floating(entMan, xforms.GetMapCoordinates(ship));
            entMan.System<ShuttleSystem>().Smimsh(ship);
            Assert.That(entMan.Deleted(under) || entMan.IsQueuedForDeletion(under), Is.False, "An arrival on a sector map crushes nothing.");
            entMan.DeleteEntity(under);
            Assert.That(beacons.TryJump(ship, Home, out var refusal), Is.False, "A fresh arrival is locked out.");
            Assert.That(refusal, Is.EqualTo("wf-sector-beacon-refuse-zone").Or.EqualTo("wf-sector-beacon-refuse-lockout")
                .Or.EqualTo("wf-sector-beacon-refuse-busy"));
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(loose);
            entMan.DeleteEntity(ship);
            UnloadRegion(entMan, map, oldDefault);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CountdownBreaksOutsideTheZoneAndAnUncrossedPortalCloses()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var beacons = entMan.System<WFJumpBeaconSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var oldDefault = MapId.Nullspace;
        var ship = EntityUid.Invalid;
        var portal = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            var home = Beacon(entMan, Home, Far);
            home.Comp.Countdown = 30f;
            home.Comp.PortalLifetime = TimeSpan.FromSeconds(1);
            ship = Ship(entMan, map, InZone(entMan, home));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(beacons.TryJump(ship, Far, out var refusal), Is.True, refusal);
            var away = xforms.GetMapCoordinates(Beacon(entMan, Home, Far)).Position + new Vector2(5000f, 0f);
            xforms.SetCoordinates(ship, new EntityCoordinates(map.MapUid, away));
        });

        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFBeaconJumpComponent>(ship), Is.False, "Leaving the zone breaks the countdown.");
            Assert.That(sectors.SectorOfEntity(ship), Is.EqualTo(Home));

            xforms.SetCoordinates(ship, new EntityCoordinates(map.MapUid, InZone(entMan, Beacon(entMan, Home, Far)).Position));
            Beacon(entMan, Home, Far).Comp.Countdown = 0.5f;
            Assert.That(beacons.TryJump(ship, Far, out var refusal), Is.True, refusal);
        });

        await PoolManager.WaitUntil(server, () => entMan.GetComponent<WFBeaconJumpComponent>(ship).Portal != null, maxTicks: 300);
        await server.WaitPost(() => portal = entMan.GetComponent<WFBeaconJumpComponent>(ship).Portal!.Value);
        await PoolManager.WaitUntil(server, () => !entMan.HasComponent<WFBeaconJumpComponent>(ship), maxTicks: 300);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(portal) || entMan.IsQueuedForDeletion(portal), "An uncrossed portal closes.");
            Assert.That(entMan.HasComponent<FTLComponent>(ship), Is.False);
            Assert.That(sectors.SectorOfEntity(ship), Is.EqualTo(Home));
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(ship);
            UnloadRegion(entMan, map, oldDefault);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HeldBeaconCutsItsSectorOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var territory = entMan.System<WFSectorTerritorySystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var oldDefault = MapId.Nullspace;
        var wasEnabled = config.GetCVar(SectorControlCVars.Enabled);

        await server.WaitAssertion(() =>
        {
            config.SetCVar(SectorControlCVars.Enabled, true);
            oldDefault = LoadRegion(entMan, map);
            var home = Beacon(entMan, Home, Far);
            var (homeMap, homeCell) = sectors.BeaconCell(home);
            Assert.That(territory.TryClaim(homeMap, homeCell, Holder, null), Is.False, "The home side's beacon is protected.");

            var far = Beacon(entMan, Far, Home);
            var (farMap, farCell) = sectors.BeaconCell(far);
            Assert.That(territory.IsSectorMap(farMap), "Territory runs on the new sector's map.");
            Assert.That(territory.TryClaim(farMap, farCell, Holder, null), Is.True);
            sectors.RefreshRegion();
            Assert.That(sectors.LinkClosed(Home, Far));
            Assert.That(sectors.IsCutOff(Far), "A held beacon on the only link cuts Far Reach off.");

            FarStation(entMan, sectors);
            var station = territory.Stations(farMap).FirstOrDefault();
            if (station.Owner.IsValid())
                Assert.That(entMan.System<WFTradeSectorSystem>().IsBlockaded(station), "Far Reach's stations are blockaded.");

            territory.Free(farMap, farCell);
            sectors.RefreshRegion();
            Assert.That(sectors.IsCutOff(Far), Is.False, "Freeing the beacon reopens the link.");
        });

        await server.WaitPost(() =>
        {
            UnloadRegion(entMan, map, oldDefault);
            config.SetCVar(SectorControlCVars.Enabled, wasEnabled);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CratesHauledAcrossSectorsEarnABonusPerLink()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var oldDefault = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            var farMap = sectors.MapOf(Far)!.Value;
            var chute = entMan.SpawnEntity(null, new EntityCoordinates(farMap, new Vector2(100f, 0f)));
            var crate = entMan.SpawnEntity(null, new EntityCoordinates(farMap, new Vector2(101f, 0f)));
            entMan.EnsureComponent<WFTradeOriginComponent>(crate).Sector = Home;

            var quote = new WFTradeQuoteEvent(chute, chute, "TradeGoodFabric", crate, null);
            entMan.EventBus.RaiseEvent(EventSource.Local, quote);
            Assert.That(quote.Modifiers.Any(modifier => System.Math.Abs(modifier.Factor - (1f + WFSectorTravelSystem.JumpBonus)) < 0.001f),
                "A crate from the next sector over earns one link's bonus.");

            entMan.GetComponent<WFTradeOriginComponent>(crate).Sector = Far;
            var local = new WFTradeQuoteEvent(chute, chute, "TradeGoodFabric", crate, null);
            entMan.EventBus.RaiseEvent(EventSource.Local, local);
            Assert.That(local.Modifiers.Any(modifier => modifier.Reason == "wf-trade-reason-jumps"), Is.False,
                "A crate sold in its own sector earns none.");
        });

        await server.WaitPost(() => UnloadRegion(entMan, map, oldDefault));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RaidersContestTheFarBeacon()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var territory = entMan.System<WFSectorTerritorySystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var oldDefault = MapId.Nullspace;
        var wasEnabled = config.GetCVar(SectorControlCVars.Enabled);

        await server.WaitAssertion(() =>
        {
            config.SetCVar(SectorControlCVars.Enabled, true);
            oldDefault = LoadRegion(entMan, map);
            Assert.That(sectors.TryRaid(Beacon(entMan, Home, Far)), Is.False, "The home beacon has no raiders.");

            var far = Beacon(entMan, Far, Home);
            far.Comp.Raiders = "WFSectorFactionReavers";
            Assert.That(sectors.TryRaid(far), "Raiders move on the far beacon.");
            var (farMap, farCell) = sectors.BeaconCell(far);
            Assert.That(territory.ContestOf(farMap, farCell)?.Faction, Is.EqualTo(far.Comp.Raiders), "Its cell is contested by them.");
            Assert.That(sectors.TryRaid(far), Is.False, "A contested cell isn't raided twice.");
            territory.DropContest(farMap, farCell);
        });

        await server.WaitPost(() =>
        {
            UnloadRegion(entMan, map, oldDefault);
            config.SetCVar(SectorControlCVars.Enabled, wasEnabled);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A trade station in the far sector: a cargo depot loaded there, since the sector has none of its own.</summary>
    private static EntityUid FarStation(IEntityManager entMan, WFSectorTravelSystem sectors)
    {
        var farMap = entMan.GetComponent<TransformComponent>(sectors.MapOf(Far)!.Value).MapID;
        var depot = IoCManager.Resolve<IPrototypeManager>().Index<PointOfInterestPrototype>("CargoDepotAlt");
        entMan.System<PointOfInterestSystem>().WfSpawnSectorPoi(farMap, depot, new Vector2(6000f, 0f), null, out _);
        var stations = entMan.EntityQueryEnumerator<WFTradeStationComponent, TransformComponent>();
        while (stations.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == farMap)
                return uid;
        }

        return EntityUid.Invalid;
    }

    [Test]
    public async Task CrewOrdersReachADockInAnotherSector()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var objectives = entMan.System<WFCrewObjectiveSystem>();
        var territory = entMan.System<WFSectorTerritorySystem>();
        var oldDefault = MapId.Nullspace;
        var proxy = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        var player = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            station = FarStation(entMan, sectors);
            var home = Beacon(entMan, Home, Far);
            proxy = entMan.SpawnEntity(null, InZone(entMan, home));
            entMan.AddComponent<WFCrewVirtualShipComponent>(proxy).Speed = 2000f;

            // The way over is a crossing from this sector's beacon.
            var away = new WFCrewTargetAwayEvent(proxy, station);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref away);
            Assert.That(away.Leg, Is.Not.Null, "A station in the linked sector has a way over.");
            Assert.That(away.Leg!.Kind, Is.EqualTo(WFCrewObjectiveKind.Cross));
            Assert.That(away.Leg.Target, Is.EqualTo(entMan.GetNetEntity(home.Owner)), "It crosses from the beacon here.");

            // Player ships aren't chased across.
            var farMap = entMan.GetComponent<TransformComponent>(sectors.MapOf(Far)!.Value).MapID;
            player = Ship(entMan, map, new MapCoordinates(new Vector2(3000f, 3000f), farMap));
            entMan.AddComponent<ShuttleDeedComponent>(player);
            var chase = new WFCrewTargetAwayEvent(proxy, player);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref chase);
            Assert.That(chase.Leg, Is.Null, "No crew follows a player ship through a beacon.");

            // A blockade on the link leaves no way over.
            var (beaconMap, cell) = sectors.BeaconCell(Beacon(entMan, Far, Home));
            Assert.That(territory.TryClaim(beaconMap, cell, Holder, null), Is.True);
            var closed = new WFCrewTargetAwayEvent(proxy, station);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref closed);
            Assert.That(closed.Leg, Is.Null, "A blockaded link offers no way over.");
            territory.Free(beaconMap, cell);

            // The order itself is valid from here; the crew flies the crossing first.
            Assert.That(objectives.SetQueue(proxy, "test", new List<WFCrewObjective>
            {
                new() { Kind = WFCrewObjectiveKind.Dock, Target = entMan.GetNetEntity(station), Range = 100f },
            }), Is.True, "A dock in another sector is a valid order.");
        });

        await PoolManager.WaitUntil(server, () => sectors.SectorOfEntity(proxy) == Far, maxTicks: 3000);
        await PoolManager.WaitUntil(server,
            () => entMan.GetComponent<WFCrewVirtualShipComponent>(proxy).DockedTo == station, maxTicks: 3000);

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(proxy);
            entMan.DeleteEntity(player);
            UnloadRegion(entMan, map, oldDefault);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>One step of a crossing for a live hull, as its crew's order runs it.</summary>
    private static WFCrewCrossProgress CrossStep(IEntityManager entMan, EntityUid ship, EntityUid beacon)
    {
        var step = new WFCrewCrossStepEvent(ship, beacon, false) { Progress = WFCrewCrossProgress.Waiting };
        entMan.EventBus.RaiseEvent(EventSource.Local, ref step);
        return step.Progress;
    }

    [Test]
    public async Task ALiveHullRetriesABrokenCountdownUntilItCrosses()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var oldDefault = MapId.Nullspace;
        var hull = EntityUid.Invalid;
        var beacon = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            var home = Beacon(entMan, Home, Far);
            home.Comp.Countdown = 10f;
            beacon = home.Owner;
            hull = Ship(entMan, map, InZone(entMan, beacon));

            Assert.That(CrossStep(entMan, hull, beacon), Is.EqualTo(WFCrewCrossProgress.Waiting));
            Assert.That(entMan.HasComponent<WFBeaconJumpComponent>(hull), "The hull starts a beacon countdown.");

            // Hull damage or leaving the zone breaks the countdown: the next step starts it again.
            entMan.RemoveComponent<WFBeaconJumpComponent>(hull);
            Assert.That(CrossStep(entMan, hull, beacon), Is.EqualTo(WFCrewCrossProgress.Waiting));
            Assert.That(entMan.HasComponent<WFBeaconJumpComponent>(hull), "A broken countdown is started again.");
        });

        await PoolManager.WaitUntil(server, () => sectors.SectorOfEntity(hull) == Far, maxTicks: 3000);
        await server.WaitAssertion(() =>
        {
            Assert.That(CrossStep(entMan, hull, beacon), Is.EqualTo(WFCrewCrossProgress.Done), "Over in the linked sector, the crossing is done.");
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(hull);
            UnloadRegion(entMan, map, oldDefault);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStandInBuiltIntoAHullMidCrossingStillCrosses()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var objectives = entMan.System<WFCrewObjectiveSystem>();
        var oldDefault = MapId.Nullspace;
        var proxy = EntityUid.Invalid;
        var hull = EntityUid.Invalid;
        var beacon = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            var home = Beacon(entMan, Home, Far);
            home.Comp.Countdown = 30f;
            beacon = home.Owner;
            proxy = entMan.SpawnEntity(null, InZone(entMan, beacon));
            entMan.AddComponent<WFCrewVirtualShipComponent>(proxy);
            Assert.That(objectives.SetQueue(proxy, "test", new List<WFCrewObjective>
            {
                new() { Kind = WFCrewObjectiveKind.Dock, Target = entMan.GetNetEntity(FarStation(entMan, sectors)), Range = 100f },
            }), Is.True);
        });

        await PoolManager.WaitUntil(server, () => sectors.InCrossing(proxy), maxTicks: 600);

        // A player comes near and the stand-in is built into a hull before it lands; the hull takes its queue.
        await server.WaitPost(() =>
        {
            hull = Ship(entMan, map, InZone(entMan, beacon));
            objectives.MoveQueue(proxy, hull, "test");
            entMan.DeleteEntity(proxy);
        });
        await server.WaitRunTicks(server.Timing.TickRate * 2);

        await server.WaitPost(() =>
        {
            Assert.That(sectors.InCrossing(proxy), Is.False, "The deleted stand-in's crossing is dropped.");
            Assert.That(objectives.TryGetQueue(hull, "test", out var queue) && queue[0].Kind == WFCrewObjectiveKind.Cross,
                "The hull still has the crossing ahead.");
            Assert.That(CrossStep(entMan, hull, beacon), Is.EqualTo(WFCrewCrossProgress.Waiting));
            Assert.That(entMan.HasComponent<WFBeaconJumpComponent>(hull), "The hull makes the jump itself.");
        });

        await PoolManager.WaitUntil(server, () => sectors.SectorOfEntity(hull) == Far, maxTicks: 6000);

        await server.WaitPost(() =>
        {
            objectives.Cancel(hull, "test");
            entMan.DeleteEntity(hull);
            UnloadRegion(entMan, map, oldDefault);
        });
        await pair.CleanReturnAsync();
    }
}
