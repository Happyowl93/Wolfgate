#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.Augments;
using Content.Shared._WG.Augments;
using Content.Shared._WG.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// A severed part carries off the paid-for augments counted in its body, its hand's and organs' included, and
/// nothing counted in another body; the severed and destroyed warnings exist.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAugmentSystem))]
public sealed class AugmentSeveredTest
{
    [Test]
    public async Task SeveredMarksTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var augments = server.System<WGAugmentSystem>();
        var body = server.System<SharedBodySystem>();
        var detach = server.System<WolfmedBodySystem>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var map = await pair.CreateTestMap();
        ProtoId<WGAugmentPrototype> dex = "WGAugmentDexLeftHand";
        ProtoId<WGAugmentPrototype> eyes = "WGAugmentCyberEyes";

        await server.WaitAssertion(() =>
        {
            Assert.That(loc.HasString("wg-augments-severed"));
            Assert.That(loc.HasString("wg-augments-destroyed"));

            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            var other = entities.SpawnEntity("MobHuman", map.GridCoords);
            var owner = new WGAugmentOwner(new NetUserId(Guid.NewGuid()), 0);
            var stranger = new WGAugmentOwner(new NetUserId(Guid.NewGuid()), 1);

            var arm = body.GetBodyChildrenOfType(mob, BodyPartType.Arm, symmetry: BodyPartSymmetry.Right).First();
            var hand = body.GetBodyChildrenOfType(mob, BodyPartType.Hand, symmetry: BodyPartSymmetry.Right).First();
            var paid = entities.EnsureComponent<WGAugmentPaidComponent>(hand.Id);
            paid.Augments[dex] = new WGAugmentMark(owner, mob);
            paid.Augments[eyes] = new WGAugmentMark(stranger, other);

            var marks = augments.SeveredMarks(mob, (arm.Id, arm.Component));
            Assert.That(marks.Keys, Is.EquivalentTo(new[] { owner }), "A mark counted in another body reads as lost from this one.");
            Assert.That(marks[owner], Is.EquivalentTo(new[] { dex }), "The hand's mark doesn't leave with its arm.");

            var leftArm = body.GetBodyChildrenOfType(mob, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left).First();
            Assert.That(augments.SeveredMarks(mob, (leftArm.Id, leftArm.Component)), Is.Empty);

            // A head carries its organs' marks.
            var head = body.GetBodyChildrenOfType(mob, BodyPartType.Head).First();
            var organ = body.GetPartOrgans(head.Id, head.Component).First().Id;
            entities.EnsureComponent<WGAugmentPaidComponent>(organ).Augments[eyes] = new WGAugmentMark(owner, mob);
            Assert.That(augments.SeveredMarks(mob, (head.Id, head.Component))[owner], Is.EquivalentTo(new[] { eyes }));

            // Severing the arm with nobody online to warn goes through.
            Assert.That(detach.TryDetachPart(arm.Id), "The arm didn't come off.");
            Assert.That(entities.GetComponent<BodyPartComponent>(arm.Id).Body, Is.Null);

            entities.DeleteEntity(mob);
            entities.DeleteEntity(other);
            entities.DeleteEntity(arm.Id);
        });

        await pair.CleanReturnAsync();
    }
}
