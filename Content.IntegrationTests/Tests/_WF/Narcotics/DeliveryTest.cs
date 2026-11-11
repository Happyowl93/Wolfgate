using Content.Server._WF.Narcotics;
using Content.Server.Body.Components;
using Content.Shared._WF.Narcotics.Delivery;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Narcotics;

/// <summary>
/// A snorted line and a bong hit each move their drug into the bloodstream, and a baggie takes only powder and
/// solid drugs.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDeliverySystem))]
public sealed class DeliveryTest
{
    [Test]
    public async Task LineAndBongTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var delivery = server.System<WFDeliverySystem>();
            var solutions = server.System<SharedSolutionContainerSystem>();
            var slots = server.System<ItemSlotsSystem>();

            FixedPoint2 InBlood(EntityUid mob, string reagent)
            {
                solutions.TryGetSolution(mob, BloodstreamComponent.DefaultChemicalsSolutionName, out _, out var chemicals);
                return chemicals!.GetTotalPrototypeQuantity(reagent);
            }

            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var baggie = entMan.SpawnEntity("WFBaggieCocaine", map.GridCoords);
            var line = delivery.TryPlaceLine((baggie, entMan.GetComponent<WFBaggieComponent>(baggie)), user, map.GridCoords);
            Assert.That(line, Is.Not.Null);
            Assert.That(delivery.Snort((line!.Value, entMan.GetComponent<WFPowderLineComponent>(line.Value)), user), Is.True);
            Assert.That(InBlood(user, "WFCocaine"), Is.EqualTo(FixedPoint2.New(5)), "A line is 5 u.");

            var smoker = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var bong = entMan.SpawnEntity("WFBongFilled", map.GridCoords);
            var rock = entMan.SpawnEntity("WFCrackRock", map.GridCoords);
            var bongComp = entMan.GetComponent<WFBongComponent>(bong);
            Assert.That(delivery.Hit((bong, bongComp), smoker), Is.False, "An empty, unlit bong does nothing.");
            Assert.That(slots.TryInsert(bong, bongComp.Bowl, rock, null), Is.True, "Crack rocks fit the bowl.");
            Assert.That(delivery.TryLight((bong, bongComp), smoker), Is.True);
            Assert.That(delivery.Hit((bong, bongComp), smoker), Is.True);
            Assert.That(InBlood(smoker, "WFCrack"), Is.EqualTo(FixedPoint2.New(3)), "A hit draws 3 u.");

            Assert.That(delivery.Hit((bong, bongComp), smoker), Is.True);
            Assert.That(InBlood(smoker, "WFCrack"), Is.EqualTo(FixedPoint2.New(5)), "The last hit takes what's left.");
            Assert.That(bongComp.Lit, Is.False, "A spent bowl goes out.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BaggieWhitelistTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            var transfer = server.System<SolutionTransferSystem>();
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);

            FixedPoint2 Pour(string reagent)
            {
                var beaker = entMan.SpawnEntity("Beaker", map.GridCoords);
                var baggie = entMan.SpawnEntity("WFBaggie", map.GridCoords);
                solutions.TryGetSolution(beaker, "beaker", out var from, out _);
                solutions.TryAddReagent(from!.Value, reagent, FixedPoint2.New(10));
                solutions.TryGetSolution(baggie, "baggie", out var to, out _);
                return transfer.Transfer(user, beaker, from.Value, baggie, to!.Value, FixedPoint2.New(5));
            }

            Assert.That(Pour("WFCocaine"), Is.EqualTo(FixedPoint2.New(5)), "Cocaine pours into a baggie.");
            Assert.That(Pour("Water"), Is.EqualTo(FixedPoint2.Zero), "Water doesn't.");

            var storage = server.System<SharedStorageSystem>();
            var bag = entMan.SpawnEntity("WFBaggie", map.GridCoords);
            foreach (var solid in new[] { "WFCrackRock", "WFHashBrick", "GroundCannabis", "FentanylSolidified" })
            {
                Assert.That(storage.CanInsert(bag, entMan.SpawnEntity(solid, map.GridCoords), out _), Is.True,
                    $"{solid} fits a baggie.");
            }
            Assert.That(storage.CanInsert(bag, entMan.SpawnEntity("WFBaggieCocaine", map.GridCoords), out _), Is.False,
                "A baggie doesn't take another baggie.");
        });

        await pair.CleanReturnAsync();
    }
}
