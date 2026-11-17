using System.Collections.Generic;
using Content.Shared._WF.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// The cyber chest: a third chest implant, no room for implants that need living tissue, and its style drawn on the
/// chest.
/// </summary>
[TestFixture]
[TestOf(typeof(WFAugmentRules))]
public sealed class AugmentCyberChestTest
{
    private static readonly ProtoId<WFAugmentPrototype> Chest = "WFAugmentCyberChest";
    private static readonly ProtoId<WFAugmentStylePrototype> Style = "WFAugmentStyleBishop";

    [Test]
    public async Task CyberChestTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var natural = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
            var cyber = new Dictionary<WFAugmentSlot, WFAugmentChoice> { [WFAugmentSlot.Chest] = new(Chest, Style) };
            var noTraits = new ProtoId<TraitPrototype>[0];
            ProtoId<WFAugmentPrototype>[] three = ["WFAugmentHaloBurst", "WFAugmentMuscleFibers", "WFAugmentHoloProjector"];
            ProtoId<WFAugmentPrototype>[] organic = ["WFAugmentAdrenalBooster"];

            Assert.That(WFAugmentRules.ValidateImplants("Human", natural, three, noTraits, proto, factory), Has.Count.EqualTo(2));
            Assert.That(WFAugmentRules.ValidateImplants("Human", cyber, three, noTraits, proto, factory), Has.Count.EqualTo(3),
                "The cyber chest didn't hold a third implant.");
            Assert.That(WFAugmentRules.ValidateImplants("Human", natural, organic, noTraits, proto, factory), Has.Count.EqualTo(1));
            Assert.That(WFAugmentRules.ValidateImplants("Human", cyber, organic, noTraits, proto, factory), Is.Empty,
                "An implant that needs living tissue fit the cyber chest.");

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            server.System<SharedHumanoidAppearanceSystem>().LoadProfile(body,
                HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(cyber));
            var humanoid = entities.GetComponent<HumanoidAppearanceComponent>(body);
            var sprite = WFAugmentRules.Sprite(proto.Index(Style), HumanoidVisualLayers.Chest, false);
            Assert.That(sprite, Is.Not.Null);
            Assert.That(humanoid.CustomBaseLayers.TryGetValue(HumanoidVisualLayers.Chest, out var chest)
                        && chest.Id == sprite, "The chest wasn't drawn in its style.");
            entities.DeleteEntity(body);
        });
        await pair.CleanReturnAsync();
    }
}
