using System.Linq;
using Content.Shared._WG.Construction;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Construction;

/// <summary>The APC recipe refuses a wall face that an APC or APC frame already takes.</summary>
[TestFixture]
[TestOf(typeof(WGApcFaceFree))]
public sealed class ApcFaceFreeTest
{
    private static readonly ProtoId<ConstructionPrototype> Recipe = "APC";

    [Test]
    public async Task SecondApcOnSameFaceIsRefused()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xforms = entMan.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            var recipe = server.ProtoMan.Index(Recipe);
            Assert.That(recipe.Conditions.OfType<WGApcFaceFree>(), Is.Not.Empty, "The APC recipe lost its condition.");

            var condition = new WGApcFaceFree();
            Assert.That(condition.Condition(EntityUid.Invalid, map.GridCoords, Direction.East), Is.True);

            foreach (var proto in new[] { "APCFrame", "APCConstructed", "APCHyperCapacity" })
            {
                var apc = entMan.SpawnEntity(proto, map.GridCoords);
                xforms.SetLocalRotation(apc, Direction.East.ToAngle());

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(apc).Anchored, $"{proto} did not anchor.");
                    Assert.That(condition.Condition(EntityUid.Invalid, map.GridCoords, Direction.East), Is.False,
                        $"A second APC was allowed on the face of a {proto}.");
                    Assert.That(condition.Condition(EntityUid.Invalid, map.GridCoords, Direction.West), Is.True,
                        $"A {proto} blocked the opposite face.");
                });

                entMan.DeleteEntity(apc);
            }
        });

        await pair.CleanReturnAsync();
    }
}
