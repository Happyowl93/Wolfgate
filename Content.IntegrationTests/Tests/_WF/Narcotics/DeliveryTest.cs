using Content.Server._WF.Narcotics;
using Content.Server.Body.Components;
using Content.Shared._WF.Narcotics.Delivery;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Narcotics;

/// <summary>A snorted line and a bong hit each move their drug into the bloodstream.</summary>
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
}
