#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Planets;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WG.Planets;
using Content.Shared.Atmos;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WG.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WG.Planets;

/// <summary>
/// Merak's soft ground digs through to the layer below. The hole keeps the planet's air, and so do the lattice and
/// the floor laid over it: a roof over a dug pit is not a vacuum.
/// </summary>
[TestFixture]
public sealed class DugHoleAtmosphereTest
{
    private static readonly ProtoId<WGPlanetSurfacePrototype> Merak = "WGSurfaceMerak";

    /// <summary>Merak ground whose base turf is space.</summary>
    private const string Sand = "WGFloorMerakDuneSand";

    [Test]
    public async Task LatticeAndFloorOverADugHoleKeepThePlanetsAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableFeature(pair);
        var ground = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>().Index(Merak);
            var network = server.System<WGPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Merak", null);
            ground = entMan.GetComponent<WGPlanetNetworkComponent>(network!.Value).GroundMap;
        });

        // The climate sets the world's outdoor air on its first pass.
        await server.WaitRunTicks(pair.SecondsToTicks(2.5f));

        // A pit three tiles long walled in on the sand, and one more hole out in the open.
        var pit = new[] { new Vector2i(2, 2), new Vector2i(3, 2), new Vector2i(4, 2) };
        var open = new Vector2i(8, 5);
        var holes = pit.Append(open).ToList();

        await server.WaitPost(() =>
        {
            SetTiles(pair, ground, Sand, Area(Vector2i.Zero, new Vector2i(10, 7)));

            foreach (var index in Area(new Vector2i(1, 1), new Vector2i(5, 3)).Except(pit))
            {
                entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, index.X + 0.5f, index.Y + 0.5f));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        // Dug the way a shovel digs: down to the base turf, which on Merak is a hole.
        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var tiles = server.System<TileSystem>();
            foreach (var index in holes)
            {
                Assert.That(tiles.DeconstructTile(maps.GetTileRef(ground, grid, index)), Is.True, $"The sand at {index} could not be dug.");
                Assert.That(maps.GetTileRef(ground, grid, index).Tile.IsEmpty, Is.True, $"Digging the sand at {index} left no hole.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        // Before anything covers them, the open hole and the walled pit already hold the planet's air.
        await server.WaitAssertion(() =>
        {
            var planetAir = atmos.GetTileMixture(null, ground, Vector2i.Zero)!;
            using (Assert.EnterMultipleScope())
            {
                foreach (var index in holes)
                {
                    var air = atmos.GetTileMixture(ground, ground, index);
                    Assert.That(air, Is.Not.Null, $"The dug hole at {index} has no air.");
                    Assert.That(air!.Pressure, Is.EqualTo(planetAir.Pressure).Within(planetAir.Pressure * 0.01f),
                        $"The dug hole at {index} does not share the planet's air.");
                }
            }
        });

        await server.WaitPost(() => SetTiles(pair, ground, "Lattice", holes));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var planetAir = atmos.GetTileMixture(null, ground, Vector2i.Zero)!;
            var lattice = atmos.GetTileMixture(ground, ground, open);
            Assert.That(lattice, Is.Not.Null, "Lattice over a dug hole has no air.");
            Assert.That(lattice!.Pressure, Is.EqualTo(planetAir.Pressure).Within(planetAir.Pressure * 0.01f),
                "Lattice over a dug hole does not share the planet's air.");
        });

        await server.WaitPost(() => SetTiles(pair, ground, "Plating", pit));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var planetAir = atmos.GetTileMixture(null, ground, Vector2i.Zero)!;

            using (Assert.EnterMultipleScope())
            {
                foreach (var index in pit)
                {
                    var air = atmos.GetTileMixture(ground, ground, index);

                    Assert.That(air is { Immutable: false }, $"The floor over the hole at {index} does not hold its own air.");
                    Assert.That(air!.Pressure, Is.EqualTo(planetAir.Pressure).Within(planetAir.Pressure * 0.01f),
                        $"The floor over the hole at {index} did not start with the planet's air.");
                }
            }
        });

        // Whoever stands on the floor, or on the lattice out in the open, breathes the planet's air.
        var mobs = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            mobs.Add(entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, 3.5f, 2.5f)));
            mobs.Add(entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, open.X + 0.5f, open.Y + 0.5f)));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            foreach (var mob in mobs)
            {
                var breathed = atmos.GetContainingMixture(mob);
                Assert.That(breathed?.Pressure ?? 0f, Is.GreaterThan(Atmospherics.WarningLowPressure),
                    $"{entMan.ToPrettyString(mob)} over a dug hole is in a vacuum.");
                Assert.That(entMan.GetComponent<BarotraumaComponent>(mob).TakingDamage, Is.False,
                    $"{entMan.ToPrettyString(mob)} over a dug hole takes pressure damage.");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Vector2i> Area(Vector2i from, Vector2i to)
    {
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            yield return new Vector2i(x, y);
        }
    }

    private static void SetTiles(TestPair pair, EntityUid ground, string tile, IEnumerable<Vector2i> indices)
    {
        var server = pair.Server;
        var id = server.ResolveDependency<ITileDefinitionManager>()[tile].TileId;
        var grid = server.EntMan.GetComponent<MapGridComponent>(ground);

        server.System<SharedMapSystem>().SetTiles(ground, grid, indices.Select(index => (index, new Tile(id))).ToList());
    }
}
