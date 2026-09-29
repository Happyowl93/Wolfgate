using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Shuttles.Systems;
using Content.Shared._WF.Shuttles;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// The whole-ship view's damage overlay is driven by a server sweep of anchored structures.
/// </summary>
public sealed class ShipStatusTest
{
    [Test]
    public async Task DamagedWallIsReported()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var damageSystem = entManager.System<DamageableSystem>();
        var statusSystem = entManager.System<ShipStatusSystem>();

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(map.Grid);

            var grid = mapManager.CreateGridEntity(map.MapId);

            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);

            var console = entManager.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));

            var wall = entManager.SpawnEntity("WallSolid",
                new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));

            var wallTile = mapSystem.TileIndicesFor(grid.Owner, grid.Comp,
                entManager.GetComponent<TransformComponent>(wall).Coordinates);

            // An undamaged hull should report nothing at all.
            var clean = statusSystem.GetStatus(console);
            Assert.That(clean, Is.Not.Null, "The console should resolve to its own grid.");
            Assert.That(clean!.Tiles.Any(t => (t.Flags & ShipTileFlags.Damaged) != 0), Is.False,
                "An intact hull should report no damaged tiles.");

            // Hit the wall hard enough to be well past the reporting threshold but not destroy it.
            var damage = new DamageSpecifier(protoManager.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(100));
            damageSystem.TryChangeDamage(wall, damage, ignoreResistances: true);

            var damaged = statusSystem.GetStatus(console);
            Assert.That(damaged, Is.Not.Null);

            var reported = damaged!.Tiles.FirstOrDefault(t => t.Index == wallTile);
            Assert.That(reported.Flags & ShipTileFlags.Damaged, Is.Not.EqualTo(ShipTileFlags.None),
                "The damaged wall's tile should be flagged.");
            Assert.That(damaged.Summary.DamagedTiles, Is.GreaterThan(0));
            Assert.That(damaged.Summary.WorstIntegrity, Is.LessThan(1f));
        });

        await pair.CleanReturnAsync();
    }
}
