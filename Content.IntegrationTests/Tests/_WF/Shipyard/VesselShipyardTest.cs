using System.Collections.Generic;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shipyard;

/// <summary>Checks every vessel grid, loaded into an initialised map as the shipyard spawns it.</summary>
// The name ends in ShipyardTest so the shipyard CI workflow's filter runs it when a grid changes.
[TestFixture]
public sealed class VesselShipyardTest
{
    /// <summary>Every vessel loads as a shuttle, and every hardpoint-only gun on it mounts on a hardpoint.</summary>
    [Test]
    public async Task VesselsLoadAsShuttlesWithMountedGuns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();
        var failures = new List<string>();
        var guns = 0;

        await server.WaitPost(() =>
        {
            foreach (var vessel in prototypes.EnumeratePrototypes<VesselPrototype>())
            {
                maps.CreateMap(out var mapId);
                if (!loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid))
                {
                    failures.Add($"{vessel.ID}: {vessel.ShuttlePath} did not load.");
                    maps.DeleteMap(mapId);
                    continue;
                }

                if (!entities.HasComponent<ShuttleComponent>(grid.Value))
                    failures.Add($"{vessel.ID}: the grid has no Shuttle component.");

                // Fire control refuses a gun that isn't mounted on a hardpoint.
                var query = entities.EntityQueryEnumerator<HardpointAnchorableOnlyComponent, TransformComponent>();
                while (query.MoveNext(out var gun, out var mount, out var xform))
                {
                    if (xform.GridUid != grid.Value.Owner)
                        continue;
                    guns++;
                    if (mount.anchoredTo == null)
                        failures.Add($"{vessel.ID}: {entities.ToPrettyString(gun)} at {xform.LocalPosition} has no compatible hardpoint on its tile.");
                }

                maps.DeleteMap(mapId);
            }
        });

        Assert.That(guns, Is.Positive, "No hardpoint-only guns were found on any vessel.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        await pair.CleanReturnAsync();
    }
}
