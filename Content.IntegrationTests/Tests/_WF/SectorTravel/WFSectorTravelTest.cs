#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.NpcCrew;
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
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.SectorTravel;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Interaction;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.SectorTravel;

/// <summary>
/// The region: normal FTL stays inside its sector, a beacon jump crosses with fuel spent and a lockout after, a spool
/// breaks outside the zone or near an interdictor, a held beacon cuts its sector off, crates gain a bonus per link,
/// and a fusion fuel crate refuels a drive.
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
            beacon.Spool = 1f;
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

    /// <summary>A small ship with a drive that needs no power grid, at a map position.</summary>
    private static EntityUid Ship(IEntityManager entMan, TestMapData map, MapCoordinates at)
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

    [Test]
    public async Task BeaconJumpCrossesSpendsFuelAndLocksOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var beacons = entMan.System<WFJumpBeaconSystem>();
        var fuel = entMan.System<WFJumpFuelSystem>();
        var oldDefault = MapId.Nullspace;
        var ship = EntityUid.Invalid;
        var loose = EntityUid.Invalid;
        var before = 0f;
        var cost = 0f;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            var zone = InZone(entMan, Beacon(entMan, Home, Far));
            ship = Ship(entMan, map, zone);
            // A crate floating free just off the hull, as EVA crew or loose cargo would be.
            loose = Floating(entMan, new MapCoordinates(zone.Position + new Vector2(3f, 0f), zone.MapId));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            before = fuel.Reserve(ship).Fuel;
            cost = beacons.FuelCost(ship, Beacon(entMan, Home, Far))!.Value;
            Assert.That(before, Is.GreaterThan(cost), "A new drive starts with a full tank.");
            Assert.That(beacons.TryJump(ship, Far, out var refusal), Is.True, refusal);
            Assert.That(entMan.HasComponent<WFBeaconJumpComponent>(ship));
        });

        await PoolManager.WaitUntil(server, () => entMan.HasComponent<WFBeaconLockoutComponent>(ship), maxTicks: 3000);

        await server.WaitAssertion(() =>
        {
            var far = Beacon(entMan, Far, Home);
            var xforms = entMan.System<SharedTransformSystem>();
            Assert.That(sectors.SectorOfEntity(ship), Is.EqualTo(Far), "The ship should arrive in Far Reach.");
            var distance = Vector2.Distance(xforms.GetWorldPosition(ship), xforms.GetWorldPosition(far));
            Assert.That(distance, Is.InRange(far.Comp.ArrivalMin - 10f, far.Comp.ArrivalMax + 10f), "Arrivals land on the ring.");
            Assert.That(fuel.Reserve(ship).Fuel, Is.EqualTo(before - cost).Within(0.01f), "The jump spends its fuel once.");
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
    public async Task SpoolBreaksOutsideTheZoneAndNearAnInterdictor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var beacons = entMan.System<WFJumpBeaconSystem>();
        var fuel = entMan.System<WFJumpFuelSystem>();
        var oldDefault = MapId.Nullspace;
        var ship = EntityUid.Invalid;
        var full = 0f;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            foreach (var beacon in entMan.EntityQuery<WFJumpBeaconComponent>())
            {
                beacon.Spool = 30f;
            }

            ship = Ship(entMan, map, InZone(entMan, Beacon(entMan, Home, Far)));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            full = fuel.Reserve(ship).Fuel;
            Assert.That(beacons.TryJump(ship, Far, out var refusal), Is.True, refusal);
            var away = entMan.System<SharedTransformSystem>().GetMapCoordinates(Beacon(entMan, Home, Far)).Position + new Vector2(5000f, 0f);
            entMan.System<SharedTransformSystem>().SetCoordinates(ship, new EntityCoordinates(map.MapUid, away));
        });

        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<FTLComponent>(ship), Is.False, "Leaving the zone breaks the spool.");
            Assert.That(entMan.HasComponent<WFBeaconJumpComponent>(ship), Is.False);
            Assert.That(sectors.SectorOfEntity(ship), Is.EqualTo(Home));
            Assert.That(fuel.Reserve(ship).Fuel, Is.EqualTo(full), "A broken spool spends nothing.");

            var zone = InZone(entMan, Beacon(entMan, Home, Far));
            entMan.System<SharedTransformSystem>().SetCoordinates(ship, new EntityCoordinates(map.MapUid, zone.Position));
            var jammer = Ship(entMan, map, new MapCoordinates(zone.Position + new Vector2(200f, 0f), zone.MapId));
            var interdictor = entMan.SpawnEntity("WFJumpInterdictor", new EntityCoordinates(jammer, 1.5f, 0.5f));
            Unplug(entMan, interdictor);
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() => Assert.That(beacons.TryJump(ship, Far, out var refusal), Is.True, refusal));
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<FTLComponent>(ship), Is.False, "An interdictor in range breaks the spool.");
            Assert.That(sectors.SectorOfEntity(ship), Is.EqualTo(Home));
        });

        await server.WaitPost(() => UnloadRegion(entMan, map, oldDefault));
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
    public async Task FusionFuelCrateRefuelsTheDrive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var fuel = entMan.System<WFJumpFuelSystem>();

        await server.WaitAssertion(() =>
        {
            var ship = Ship(entMan, map, new MapCoordinates(new Vector2(-3000f, 0f), map.MapId));
            fuel.SetFuel(ship, 0f);
            var drive = entMan.System<SharedShuttleSystem>().TryGetFTLDrive(ship, out var driveUid, out _) ? driveUid!.Value : EntityUid.Invalid;
            var user = entMan.SpawnEntity(null, new EntityCoordinates(ship, 0.5f, 1.5f));
            var crate = entMan.SpawnEntity("TradeGoodFusionFuel", new EntityCoordinates(ship, 0.5f, 1.5f));
            var tank = entMan.GetComponent<WFJumpFuelComponent>(drive);

            var ev = new InteractUsingEvent(user, crate, drive, new EntityCoordinates(ship, 0.5f, 0.5f));
            entMan.EventBus.RaiseLocalEvent(drive, ev);
            Assert.That(ev.Handled);
            Assert.That(fuel.Reserve(ship).Fuel, Is.EqualTo(tank.PerCrate));
            Assert.That(entMan.Deleted(crate) || entMan.IsQueuedForDeletion(crate), "The crate is used up.");
            entMan.DeleteEntity(ship);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FuelIntakeFeedsTheShipsDrive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var fuel = entMan.System<WFJumpFuelSystem>();

        await server.WaitAssertion(() =>
        {
            var ship = Ship(entMan, map, new MapCoordinates(new Vector2(-3000f, 2000f), map.MapId));
            fuel.SetFuel(ship, 0f);
            var intake = entMan.SpawnEntity("WFJumpFuelIntake", new EntityCoordinates(ship, 1.5f, 0.5f));
            var user = entMan.SpawnEntity(null, new EntityCoordinates(ship, 0.5f, 1.5f));
            var crate = entMan.SpawnEntity("TradeGoodFusionFuel", new EntityCoordinates(ship, 0.5f, 1.5f));

            var ev = new InteractUsingEvent(user, crate, intake, new EntityCoordinates(ship, 1.5f, 0.5f));
            entMan.EventBus.RaiseLocalEvent(intake, ev);
            Assert.That(ev.Handled, "The intake takes a fusion fuel crate.");
            Assert.That(fuel.Reserve(ship).Fuel, Is.EqualTo(100f), "One crate fills the drive's tank by a crate's worth.");
            entMan.DeleteEntity(ship);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DockedStationQuotesJumpFuelFromItsStock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sectors = entMan.System<WFSectorTravelSystem>();
        var fuel = entMan.System<WFJumpFuelSystem>();
        var market = entMan.System<WFTradeMarketSystem>();
        var oldDefault = MapId.Nullspace;
        var ship = EntityUid.Invalid;
        var depot = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            oldDefault = LoadRegion(entMan, map);
            depot = FarStation(entMan, sectors);
            market.SetVendorStock(depot, "TradeGoodFusionFuel", 50);
            var at = entMan.System<SharedTransformSystem>().GetMapCoordinates(depot);
            ship = Ship(entMan, map, new MapCoordinates(at.Position + new Vector2(300f, 0f), at.MapId));
            var port = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(ship, 1.5f, 0.5f));
            if (!entMan.GetComponent<TransformComponent>(port).Anchored)
                entMan.System<SharedTransformSystem>().AnchorEntity(port);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(depot.IsValid(), "Far Reach has its depot.");
            var docking = entMan.System<DockingSystem>();
            Entity<DockingComponent>? free = null;
            var docks = entMan.EntityQueryEnumerator<DockingComponent, TransformComponent>();
            while (docks.MoveNext(out var uid, out var dock, out var xform))
            {
                if (xform.GridUid == depot && !dock.Docked)
                    free = (uid, dock);
            }

            var port = docking.GetDocks(ship).First();
            Assert.That(free, Is.Not.Null, "The depot has a free dock.");
            docking.Dock(port, free!.Value);
            Assert.That(port.Comp.Docked, "The ship docks to the depot.");
            fuel.SetFuel(ship, 0f);
            var quote = fuel.ServiceQuote(ship);
            Assert.That(quote, Is.Not.Null, "A docked depot that sells fusion fuel offers jump fuel.");
            Assert.That(quote!.Value.Units, Is.EqualTo(300), "It fills the whole tank from a stocked shelf.");
            Assert.That(quote.Value.Cost, Is.GreaterThan(0));

            var stock = market.Row(depot, "TradeGoodFusionFuel").Stock;
            var buyer = entMan.SpawnEntity(null, new EntityCoordinates(ship, 0.5f, 0.5f));
            Assert.That(fuel.TryBuy(ship, buyer, out var refusal), Is.False, "Nobody without an account can pay.");
            Assert.That(refusal, Is.EqualTo("wf-jump-fuel-service-funds"));
            Assert.That(market.Row(depot, "TradeGoodFusionFuel").Stock, Is.EqualTo(stock), "A refused sale takes nothing off the shelf.");
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(ship);
            UnloadRegion(entMan, map, oldDefault);
        });
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
            Assert.That(far.Comp.Raiders, Is.Not.Null);
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

    /// <summary>The trade station Far Reach loaded.</summary>
    private static EntityUid FarStation(IEntityManager entMan, WFSectorTravelSystem sectors)
    {
        var farMap = entMan.GetComponent<TransformComponent>(sectors.MapOf(Far)!.Value).MapID;
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
    public async Task ALiveHullRetriesABrokenSpoolUntilItCrosses()
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
            home.Comp.Spool = 10f;
            beacon = home.Owner;
            hull = Ship(entMan, map, InZone(entMan, beacon));

            Assert.That(CrossStep(entMan, hull, beacon), Is.EqualTo(WFCrewCrossProgress.Waiting));
            Assert.That(entMan.HasComponent<WFBeaconJumpComponent>(hull), "The hull spools a beacon jump.");

            // Hull damage, an interdictor or the zone breaks the spool: the next step spools again.
            entMan.System<ShuttleSystem>().WfAbortFtlSpool(hull);
            entMan.RemoveComponent<WFBeaconJumpComponent>(hull);
            Assert.That(CrossStep(entMan, hull, beacon), Is.EqualTo(WFCrewCrossProgress.Waiting));
            Assert.That(entMan.HasComponent<WFBeaconJumpComponent>(hull), "A broken spool is spooled again.");
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
            home.Comp.Spool = 30f;
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

    [Test]
    public async Task ADriveBuiltByHandStartsEmpty()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var shipped = entMan.SpawnEntity(Drive, map.GridCoords);
            Assert.That(entMan.GetComponent<WFJumpFuelComponent>(shipped).Fuel, Is.EqualTo(300f), "A drive that comes with a ship starts full.");

            // Construction raises its change event on the new machine before initializing it, as a rebuilt drive is.
            var built = entMan.CreateEntityUninitialized(Drive, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(built, new Content.Server.Construction.ConstructionChangeEntityEvent(built, shipped), broadcast: true);
            entMan.InitializeAndStartEntity(built);
            Assert.That(entMan.GetComponent<WFJumpFuelComponent>(built).Fuel, Is.EqualTo(0f), "A drive built by hand starts empty.");

            entMan.DeleteEntity(shipped);
            entMan.DeleteEntity(built);
        });

        await pair.CleanReturnAsync();
    }
}
