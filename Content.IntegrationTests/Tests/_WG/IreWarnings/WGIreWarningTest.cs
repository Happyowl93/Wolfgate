#nullable enable
using System.Numerics;
using Content.Server._WG.IreWarnings;
using Content.Server.Ame;
using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.IreWarnings;

/// <summary>The IRE controller reports core stability and its safe limit, and warns once per cooldown below half.</summary>
[TestFixture]
public sealed class WGIreWarningTest
{
    [Test]
    public async Task OverloadedEngineReportsAndWarnsOnCooldown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid ame = default;

        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 3; x++)
            for (var y = 0; y <= 2; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            // A 3x3 block of shielding has one core in its middle; the controller sits against its east face.
            for (var x = 0; x <= 2; x++)
            for (var y = 0; y <= 2; y++)
                em.SpawnEntity("AmeShielding", At(x, y));
            ame = em.SpawnEntity("AmeController", At(3, 1));
        });

        await pair.RunTicksSync(10);

        await pair.Server.WaitAssertion(() =>
        {
            var controllers = em.System<AmeControllerSystem>();
            var warnings = em.System<WGIreWarningSystem>();
            var controller = em.GetComponent<AmeControllerComponent>(ame);
            var now = pair.Server.ResolveDependency<IGameTiming>().CurTime;

            Assert.That(controllers.WGTryGetIreReadout(ame, out var stability, out var limit), Is.True);
            Assert.That(stability, Is.EqualTo(100));
            Assert.That(limit, Is.EqualTo(2), "One core is safe up to an injection of two.");

            controllers.SetInjecting(ame, true, null, controller);
            Assert.That(warnings.TryWarn((ame, controller), now), Is.False, "A healthy engine doesn't warn.");

            // Fuel 60 on one core costs it 58 to 60 integrity: below half, not yet zero.
            var group = em.GetComponent<NodeContainerComponent>(ame).Nodes["ame"].NodeGroup as AmeNodeGroup;
            Assert.That(group, Is.Not.Null);
            group!.InjectFuel(60, out var overloading);
            Assert.That(overloading, Is.True);

            Assert.That(controllers.WGTryGetIreReadout(ame, out stability, out _), Is.True);
            Assert.That(stability, Is.InRange(40, 42));

            Assert.That(warnings.TryWarn((ame, controller), now), Is.True, "Below half, the controller warns.");
            Assert.That(warnings.TryWarn((ame, controller), now + TimeSpan.FromSeconds(1)), Is.False,
                "A second warning waits for the cooldown.");
            Assert.That(warnings.TryWarn((ame, controller), now + WGIreWarningSystem.Cooldown), Is.True,
                "The warning repeats once the cooldown is over.");

            controllers.SetInjecting(ame, false, null, controller);
            Assert.That(warnings.TryWarn((ame, controller), now + WGIreWarningSystem.Cooldown * 3), Is.False,
                "A controller that isn't injecting doesn't warn.");
        });

        await pair.CleanReturnAsync();

        EntityCoordinates At(int x, int y) => new(map.Grid.Owner, new Vector2(x + 0.5f, y + 0.5f));
    }
}
