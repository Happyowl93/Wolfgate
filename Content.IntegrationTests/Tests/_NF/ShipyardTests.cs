using System.Collections.Generic; // WOLFGATE: expected-value appraisal
using System.Linq;
using Content.Server.Cargo.Systems;
using Content.Server.Storage.Components; // WOLFGATE: expected-value appraisal
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Containers; // WOLFGATE: expected-value appraisal
using Content.Shared.EntityTable; // WOLFGATE: expected-value appraisal
using Content.Shared.Storage; // WOLFGATE: expected-value appraisal
using Content.Shared.Storage.Components; // WOLFGATE: expected-value appraisal
using Robust.Server.GameObjects;
using Robust.Shared.Containers; // WOLFGATE: expected-value appraisal
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random; // WOLFGATE: expected-value appraisal

namespace Content.IntegrationTests.Tests._NF;

[TestFixture]
public sealed class ShipyardTest
{
    [Test]
    public async Task CheckAllShuttleGrids()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var map = entManager.System<MapSystem>();

        await server.WaitPost(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var vessel in protoManager.EnumeratePrototypes<VesselPrototype>())
                {
                    map.CreateMap(out var mapId);

                    try
                    {
                        Assert.That(mapLoader.TryLoadGrid(mapId, vessel.ShuttlePath, out var shuttle));
                        Assert.That(shuttle.HasValue, Is.True);
                        Assert.That(entManager.HasComponent<MapGridComponent>(shuttle.Value), Is.True);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to load shuttle {vessel.ShuttlePath}", ex);
                    }

                    try
                    {
                        map.DeleteMap(mapId);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to delete map {vessel.ShuttlePath}", ex);
                    }
                }
            });
        });
        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NoShipyardShipArbitrage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapLoader = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<MapLoaderSystem>();
        var map = entManager.System<MapSystem>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var pricing = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<PricingSystem>();
        var random = server.ResolveDependency<IRobustRandom>(); // WOLFGATE: seeded so the appraisal is reproducible

        await server.WaitAssertion(() =>
        {
            // WOLFGATE START: scratch map where fill items are spawned to be priced
            var appraiser = new ExpectedAppraiser(entManager, pricing,
                new EntityCoordinates(map.CreateMap(out var scratchMapId), default));
            // WOLFGATE END

            Assert.Multiple(() =>
            {
                foreach (var vessel in protoManager.EnumeratePrototypes<VesselPrototype>())
                {
                    random.SetSeed(ArbitrageSeed); // WOLFGATE: same rolls on every run
                    map.CreateMap(out var mapId, runMapInit: false); // WOLFGATE: mapped fill contents are recorded before map init
                    double appraisePrice = 0;

                    Assert.That(mapLoader.TryLoadGrid(mapId, vessel.ShuttlePath, out var shuttle));
                    Assert.That(entManager.HasComponent<MapGridComponent>(shuttle.Value), Is.True);

                    // WOLFGATE START: random container fills count at their expected value, not this load's roll
                    // A 0.5% ResearchDisk35000 (7000) in a Guppy maintenance closet failed CI.
                    var mapped = appraiser.MappedFillContents(shuttle.Value);
                    map.InitializeMap(mapId);
                    appraisePrice = appraiser.AppraiseGrid(shuttle.Value, mapped);
                    /*
                    pricing.AppraiseGrid(shuttle.Value, null, (uid, price) =>
                    {
                        appraisePrice += price;
                    });
                    */
                    // WOLFGATE END

                    var idealMinPrice = appraisePrice * vessel.MinPriceMarkup;

                    Assert.That(vessel.Price, Is.AtLeast(idealMinPrice),
                        $"Arbitrage possible on {vessel.ID}. Minimal price should be {idealMinPrice}, {(vessel.MinPriceMarkup - 1.0f) * 100}% over the appraise price ({appraisePrice}).");
                    
                    map.DeleteMap(mapId);
                }
            });

            // WOLFGATE START: drop the scratch map and unseed the pooled server
            map.DeleteMap(scratchMapId);
            random.SetSeed(new System.Random().Next());
            // WOLFGATE END
        });

        await pair.CleanReturnAsync();
    }

    // WOLFGATE START: appraisal that counts random container fills at their expected value
    private const int ArbitrageSeed = 1;
    private const int FillSamples = 1000;

    /// <summary>
    /// Appraises a grid like <see cref="PricingSystem.AppraiseGrid"/>, but prices each random container fill
    /// at its mean over <see cref="FillSamples"/> rolls instead of what the load happened to roll.
    /// </summary>
    private sealed class ExpectedAppraiser(IEntityManager entMan, PricingSystem pricing, EntityCoordinates scratch)
    {
        private readonly EntityTableSystem _tables = entMan.System<EntityTableSystem>();
        private readonly SharedContainerSystem _containers = entMan.System<SharedContainerSystem>();
        private readonly Dictionary<string, double> _protoValues = new();

        /// <summary>
        /// Items the map file placed in fill containers. Call before map init; they keep their real price.
        /// </summary>
        public HashSet<EntityUid> MappedFillContents(EntityUid root)
        {
            var mapped = new HashSet<EntityUid>();
            CollectMapped(root, mapped);
            return mapped;
        }

        public double AppraiseGrid(EntityUid grid, HashSet<EntityUid> mapped)
        {
            var price = 0.0;
            var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
            while (children.MoveNext(out var child))
                price += Appraise(child, mapped);
            return price;
        }

        private void CollectMapped(EntityUid uid, HashSet<EntityUid> mapped)
        {
            foreach (var (container, _) in Fills(uid))
                mapped.UnionWith(container.ContainedEntities);

            var children = entMan.GetComponent<TransformComponent>(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
                CollectMapped(child, mapped);
        }

        /// <summary>
        /// Mirrors <see cref="PricingSystem.GetPriceConditional"/>, swapping rolled fill contents for the fill's mean.
        /// </summary>
        private double Appraise(EntityUid uid, HashSet<EntityUid> mapped)
        {
            var price = pricing.GetPrice(uid, false);
            var fills = Fills(uid).ToList();
            foreach (var (_, roll) in fills)
                price += Mean(roll);

            if (!entMan.TryGetComponent<ContainerManagerComponent>(uid, out var manager))
                return price;

            foreach (var container in manager.Containers.Values)
            {
                var filled = fills.Any(f => f.Container == container);
                foreach (var ent in container.ContainedEntities)
                {
                    if (!filled || mapped.Contains(ent))
                        price += Appraise(ent, mapped);
                }
            }

            return price;
        }

        /// <summary>
        /// Each container this entity fills at map init, with a roll of the prototypes it fills it with.
        /// </summary>
        private IEnumerable<(BaseContainer Container, Func<System.Random, IEnumerable<string>> Roll)> Fills(EntityUid uid)
        {
            if (entMan.TryGetComponent<EntityTableContainerFillComponent>(uid, out var tableFill))
            {
                foreach (var (id, table) in tableFill.Containers)
                {
                    if (_containers.TryGetContainer(uid, id, out var container))
                        yield return (container, rand => _tables.GetSpawns(table, rand).Select(p => p.Id));
                }
            }

            if (!entMan.TryGetComponent<StorageFillComponent>(uid, out var storageFill))
                yield break;

            Func<System.Random, IEnumerable<string>> storageRoll =
                rand => EntitySpawnCollection.GetSpawns(storageFill.Contents, rand).OfType<string>();
            if (entMan.TryGetComponent<StorageComponent>(uid, out var storage))
                yield return (storage.Container, storageRoll);
            else if (entMan.TryGetComponent<EntityStorageComponent>(uid, out var entityStorage))
                yield return (entityStorage.Contents, storageRoll);
        }

        private double Mean(Func<System.Random, IEnumerable<string>> roll)
        {
            var rand = new System.Random(ArbitrageSeed);
            var sum = 0.0;
            for (var i = 0; i < FillSamples; i++)
            {
                foreach (var proto in roll(rand))
                    sum += ProtoValue(proto);
            }

            return sum / FillSamples;
        }

        private double ProtoValue(string proto)
        {
            if (_protoValues.TryGetValue(proto, out var value))
                return value;

            _protoValues[proto] = 0; // A fill that can contain itself counts its nested copies as free.
            var ent = entMan.SpawnEntity(proto, scratch);
            if (entMan.Deleted(ent))
                return _protoValues[proto] = 0;

            value = Appraise(ent, []);
            entMan.DeleteEntity(ent);
            return _protoValues[proto] = value;
        }
    }
    // WOLFGATE END
}
