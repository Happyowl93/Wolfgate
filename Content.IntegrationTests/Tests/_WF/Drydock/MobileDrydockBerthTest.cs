using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Drydock;
using Content.Shared._WF.Drydock;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Checks a ship parked in a mobile drydock's berth stays still, keeps its console UI open, and is not re-sent or re-created.</summary>
[TestFixture]
public sealed class MobileDrydockBerthTest
{
    private const int WarmupTicks = 20;
    private const int MeasuredTicks = 300;
    private const float MaxJitter = 0.01f;

    /// <summary>Per-tick samples of one entity: its world position, velocity, and parent.</summary>
    private sealed class Track
    {
        public readonly List<Vector2> Positions = new();
        public readonly List<Vector2> Velocities = new();
        public readonly List<EntityUid> Parents = new();
        public readonly List<uint> GridTicks = new();
        public readonly List<uint> MetaTicks = new();
        public readonly List<int> ComponentCounts = new();
        public readonly List<string> ComponentSets = new();
        public readonly List<uint> CraneTicks = new();
    }

    /// <summary>Spawns a host grid with a mobile crane and console, and a ship parked over the berth.</summary>
    private static (EntityUid Host, EntityUid Crane, EntityUid Console, EntityUid Ship) SpawnScene(TestPair pair, MapId map, bool againstCrane)
    {
        var host = DrydockTest.CreateShip(pair, map, new Vector2(-300f, 0f));
        DrydockTest.SetFloor(pair, host, 6);
        var crane = DrydockTest.SpawnAnchored(pair, host, "WFMobileDrydockCrane", new Vector2i(0, 5));
        for (var x = 0; x <= 4; x++)
        {
            DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(x, 5));
            DrydockTest.SpawnAnchored(pair, host, "CableMV", new Vector2i(x, 5));
        }

        DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(4, 4));
        DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(4, 3));
        DrydockTest.SpawnAnchored(pair, host, "DebugGenerator", new Vector2i(4, 3));
        DrydockTest.SpawnAnchored(pair, host, "DebugSubstation", new Vector2i(4, 4));
        DrydockTest.SpawnAnchored(pair, host, "CableMV", new Vector2i(4, 4));
        var console = DrydockTest.SpawnAnchored(pair, host, "WFComputerMobileDrydock", new Vector2i(5, 0));
        DrydockTest.LinkCrane(pair, console, crane);

        var comp = pair.Server.EntMan.GetComponent<WFDrydockConsoleComponent>(console);
        pair.Server.System<WFDrydockSystem>().TryGetBerth(console, comp, out var berth);

        var ship = DrydockTest.CreateDamagedShip(pair, map);
        DrydockTest.PlaceOver(pair, ship, host, berth.Area.Center);
        if (againstCrane)
        {
            // Places the ship's centre a metre from the crane's, toward the bay, so its hull overlaps the crane body.
            var xforms = pair.Server.System<SharedTransformSystem>();
            var craneWorld = xforms.GetWorldPosition(crane);
            var bayWorld = xforms.ToWorldPosition(new EntityCoordinates(host, berth.Area.Center));
            var toBay = Vector2.Normalize(bayWorld - craneWorld);
            var target = craneWorld + toBay;
            xforms.SetWorldPosition(ship, target - pair.Server.EntMan.GetComponent<MapGridComponent>(ship).LocalAABB.Center);
        }

        return (host, crane, console, ship);
    }

    /// <summary>Samples one entity's transform, physics, meta data and components into a track.</summary>
    private static void Sample(TestPair pair, Track track, EntityUid uid)
    {
        var em = pair.Server.EntMan;
        var xforms = pair.Server.System<SharedTransformSystem>();
        track.Positions.Add(xforms.GetWorldPosition(uid));
        track.Parents.Add(em.GetComponent<TransformComponent>(uid).ParentUid);
        track.Velocities.Add(em.TryGetComponent(uid, out PhysicsComponent? body) ? body.LinearVelocity : Vector2.Zero);
        track.MetaTicks.Add(em.GetComponent<MetaDataComponent>(uid).EntityLastModifiedTick.Value);
        if (em.TryGetComponent(uid, out MapGridComponent? grid))
            track.GridTicks.Add(grid.LastModifiedTick.Value);
        track.ComponentCounts.Add(em.ComponentCount(uid));
        track.ComponentSets.Add(string.Join(",", em.GetComponents(uid).Select(c => c.GetType().Name).OrderBy(n => n)));
    }

    /// <summary>The largest distance any sample sits from the first one.</summary>
    private static float MaxDrift(IReadOnlyList<Vector2> positions)
    {
        var first = positions[0];
        return positions.Max(p => Vector2.Distance(p, first));
    }

    /// <summary>How many consecutive samples differ from the one before.</summary>
    private static int Changes<T>(IReadOnlyList<T> values) where T : notnull
    {
        var changes = 0;
        for (var i = 1; i < values.Count; i++)
        {
            if (!values[i].Equals(values[i - 1]))
                changes++;
        }

        return changes;
    }

    /// <summary>Runs the scene with a ship parked in the berth and checks it stays put, its UI open, and its grids not churned.</summary>
    [TestCase(false, TestName = "ParkedShipInMobileBerthStaysStable")]
    [TestCase(true, TestName = "ShipTouchingMobileCraneStaysStable")]
    public async Task ParkedShipInMobileBerthStaysStable(bool againstCrane)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var ui = server.System<SharedUserInterfaceSystem>();
        var xforms = server.System<SharedTransformSystem>();

        EntityUid host = default, crane = default, console = default, ship = default, mob = default;
        await server.WaitAssertion(() =>
        {
            (host, crane, console, ship) = SpawnScene(pair, map.MapId, againstCrane);
            var consoleXform = em.GetComponent<TransformComponent>(console);
            mob = em.SpawnEntity("MobHuman", new EntityCoordinates(host, 4.5f, 0.5f));
            Assert.That(ui.TryOpenUi(console, WFDrydockConsoleUiKey.Key, mob), Is.True, "The mob should open the console.");
            Assert.That(consoleXform.Anchored, Is.True, "The console should be anchored.");
        });

        // Let the ship and the cranes settle before measuring.
        await server.WaitRunTicks(WarmupTicks);

        var craneTrack = new Track();
        var consoleTrack = new Track();
        var shipTrack = new Track();
        var hostTrack = new Track();
        var physics = server.System<SharedPhysicsSystem>();
        var uiClosedTicks = 0;
        var craneContactTicks = 0;
        var shipContactTotal = 0;

        for (var i = 0; i < MeasuredTicks; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                if (!ui.IsUiOpen(console, WFDrydockConsoleUiKey.Key, mob))
                    uiClosedTicks++;

                Sample(pair, craneTrack, crane);
                Sample(pair, consoleTrack, console);
                Sample(pair, shipTrack, ship);
                Sample(pair, hostTrack, host);
                if (physics.GetContactingEntities(crane).Count > 0)
                    craneContactTicks++;
                shipContactTotal += physics.GetContactingEntities(ship).Count;
                craneTrack.CraneTicks.Add(em.GetComponent<WFDrydockCraneComponent>(crane).LastModifiedTick.Value);
            });
        }

        var craneSets = craneTrack.ComponentSets.Distinct().Count();
        var shipSets = shipTrack.ComponentSets.Distinct().Count();
        var consoleSets = consoleTrack.ComponentSets.Distinct().Count();
        var summary =
            $"UI closed ticks: {uiClosedTicks}/{MeasuredTicks}, crane contact ticks: {craneContactTicks}, ship contacts summed: {shipContactTotal}\n" +
            $"  ship: drift {MaxDrift(shipTrack.Positions):F4} m, velocity max {shipTrack.Velocities.Max(v => v.Length()):F4} m/s, " +
            $"parent changes {Changes(shipTrack.Parents)}, grid dirty ticks {Changes(shipTrack.GridTicks)}, " +
            $"meta dirty ticks {Changes(shipTrack.MetaTicks)}, component sets {shipSets}, component-count changes {Changes(shipTrack.ComponentCounts)}\n" +
            $"  host: drift {MaxDrift(hostTrack.Positions):F4} m, grid dirty ticks {Changes(hostTrack.GridTicks)}, meta dirty ticks {Changes(hostTrack.MetaTicks)}\n" +
            $"  console: drift {MaxDrift(consoleTrack.Positions):F4} m, component sets {consoleSets}, component-count changes {Changes(consoleTrack.ComponentCounts)}, " +
            $"meta dirty ticks {Changes(consoleTrack.MetaTicks)}\n" +
            $"  crane: drift {MaxDrift(craneTrack.Positions):F4} m, parent changes {Changes(craneTrack.Parents)}, " +
            $"component sets {craneSets}, crane dirty ticks {Changes(craneTrack.CraneTicks)}, meta dirty ticks {Changes(craneTrack.MetaTicks)}";
        TestContext.Out.WriteLine(summary);

        Assert.Multiple(() =>
        {
            Assert.That(uiClosedTicks, Is.EqualTo(0), "The console UI should stay open while a ship sits in the berth.");
            Assert.That(MaxDrift(shipTrack.Positions), Is.LessThanOrEqualTo(MaxJitter), "The parked ship should not jitter.");
            Assert.That(MaxDrift(hostTrack.Positions), Is.LessThanOrEqualTo(MaxJitter), "The host grid should not jitter.");
            Assert.That(MaxDrift(craneTrack.Positions), Is.LessThanOrEqualTo(MaxJitter), "The crane should not be pushed by the parked ship.");
            Assert.That(Changes(shipTrack.Parents), Is.EqualTo(0), "The parked ship should keep its parent.");
            Assert.That(Changes(craneTrack.Parents), Is.EqualTo(0), "The crane should keep its parent.");
            Assert.That(Changes(shipTrack.GridTicks), Is.EqualTo(0), "The parked ship's grid should not be dirtied every tick.");
            Assert.That(Changes(consoleTrack.ComponentCounts), Is.EqualTo(0), "The console should not gain or lose components while a ship sits in the berth.");
            Assert.That(shipSets, Is.EqualTo(1), "The parked ship should not gain or lose components while sitting in the berth.");
            Assert.That(craneSets, Is.EqualTo(1), "The crane should not gain or lose components while a ship sits in the berth.");
            Assert.That(Changes(craneTrack.CraneTicks), Is.EqualTo(0), "The crane component should not be dirtied every tick.");
        });

        await pair.CleanReturnAsync();
    }
}
