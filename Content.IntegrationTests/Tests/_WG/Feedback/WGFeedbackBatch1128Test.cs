#nullable enable
using Content.Server.Silicons.Borgs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Contraband;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Light.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Bug reports from the 2026-11-28 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1128Test
{
    /// <summary>#1577: magboots switched on in hand don't leave the magboots alert on the holder.</summary>
    [Test]
    public async Task HeldMagbootsShowNoAlertTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var mob = em.SpawnEntity("MobHuman", map.GridCoords);
            var boots = em.SpawnEntity("ClothingShoesBootsMag", map.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(mob, boots), Is.True);

            server.System<ItemToggleSystem>().TryActivate(boots, mob, predicted: false);
            Assert.That(server.System<ItemToggleSystem>().IsActivated(boots), Is.True);
            Assert.That(server.System<AlertsSystem>().IsShowingAlert(mob, "Magboots"), Is.False,
                "magboots switched on in hand put their alert on the holder.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1348: a borg chassis with no cell doesn't switch its modules on when a mind enters it.</summary>
    [Test]
    public async Task CelllessBorgStaysOffTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var borg = em.SpawnEntity("BorgChassisGeneric", map.GridCoords);
            server.System<BorgSystem>().BorgActivate(borg, em.GetComponent<BorgChassisComponent>(borg));
            Assert.That(server.System<ItemToggleSystem>().IsActivated(borg), Is.False,
                "a borg with no cell switched on as soon as a mind entered it.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1389: an emptied metamorphic glass drops its last drink's sprite.</summary>
    [Test]
    public async Task EmptiedGlassDropsDrinkSpriteTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var solutions = server.System<SharedSolutionContainerSystem>();
            var appearance = server.System<SharedAppearanceSystem>();
            var glass = em.SpawnEntity("DrinkGlass", map.GridCoords);
            Assert.That(solutions.TryGetSolution(glass, "drink", out var soln), Is.True);

            solutions.TryAddReagent(soln!.Value, "RoyRogers", 10);
            Assert.That(appearance.TryGetData<string>(glass, SolutionContainerVisuals.BaseOverride, out var full), Is.True);
            Assert.That(full, Is.EqualTo("RoyRogers"));

            solutions.RemoveAllSolution(soln.Value);
            Assert.That(appearance.TryGetData<string>(glass, SolutionContainerVisuals.BaseOverride, out var empty), Is.True);
            Assert.That(empty, Is.Empty, "an emptied glass kept the Roy Rogers sprite.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1367: every light that spawns with a lamp spawns one its socket takes.</summary>
    [Test]
    public async Task LightsSpawnWithFittingLampTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var factory = server.EntMan.ComponentFactory;
            Assert.Multiple(() =>
            {
                foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || !proto.TryGetComponent<PoweredLightComponent>(out var light, factory)
                        || light.HasLampOnSpawn is not { } lampId
                        || !server.ProtoMan.Index(lampId).TryGetComponent<LightBulbComponent>(out var lamp, factory))
                        continue;

                    Assert.That(lamp.Type, Is.EqualTo(light.BulbType),
                        $"{proto.ID} spawns a {lampId} ({lamp.Type}) in a {light.BulbType} socket.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Prototype fixes: #1363, #1455, #1558, #1575 and #1364.</summary>
    [Test]
    public async Task PrototypeFixesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan;
            var factory = server.EntMan.ComponentFactory;
            Assert.Multiple(() =>
            {
                Assert.That(proto.Index<EntityPrototype>("ActionSleep").TryGetComponent<InstantActionComponent>(out var sleep, factory)
                    && sleep.CheckConsciousness, Is.True, "#1363: the dead and critical can still fall asleep.");

                Assert.That(proto.Index<EntityPrototype>("ClothingOuterHardsuitCybersunStealth").Components
                    .ContainsKey(factory.GetComponentName<ContrabandComponent>()),
                    Is.True, "#1455: the stealth hardsuit has no contraband value.");

                var biofab = proto.Index<LatheRecipePackPrototype>("MedicalSynthLimbsStatic").Recipes;
                Assert.That(biofab, Does.Contain(new ProtoId<LatheRecipePrototype>("WGSynthKidneys")), "#1558: no kidneys.");
                Assert.That(biofab, Does.Contain(new ProtoId<LatheRecipePrototype>("WGSynthStomach")), "#1558: no stomach.");

                Assert.That(proto.Index<EntityPrototype>("DoorRemoteCommand").TryGetComponent<AccessComponent>(out var remote, factory),
                    Is.True);
                Assert.That(remote?.Groups, Does.Contain(new ProtoId<AccessGroupPrototype>("Caelestinus")),
                    "#1575: the command door remote can't open Caelestinus.");

                var infantry = proto.Index<JobPrototype>("TsfInfantry");
                Assert.That(infantry.AccessGroups, Does.Contain(new ProtoId<AccessGroupPrototype>("GeneralTsfmcAccess")),
                    "#1364: TSF infantry IDs can't open their garrison.");
                Assert.That(infantry.AccessGroups, Does.Not.Contain(new ProtoId<AccessGroupPrototype>("AllAccess")),
                    "#1364: TSF infantry still hold command access.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
