using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>Stored choices round-trip, validation drops what doesn't fit, and every style draws legs of both kinds.</summary>
[TestFixture]
[TestOf(typeof(WFAugmentRules))]
public sealed class AugmentRulesTest
{
    [Test]
    public async Task RulesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleXion"),
                [WFAugmentSlot.Heart] = new("WFAugmentCyberHeart", null),
            };
            var implants = new HashSet<ProtoId<WFAugmentPrototype>> { "WFAugmentDermalArmor" };
            var stored = WFAugmentRules.ToStored(choices, implants);
            Assert.That(WFAugmentRules.FromStored(stored), Is.EquivalentTo(choices));
            Assert.That(WFAugmentRules.ImplantsFromStored(stored), Is.EquivalentTo(implants));
            Assert.That(WFAugmentRules.FromStored("garbage;Nope:Thing;LeftArm"), Is.Empty);

            // Implants saved one per slot before a part held several still read, and not as slot choices.
            const string legacy = "LeftArmImplant:WFAugmentLeftMantisBlade;Muscles:WFAugmentMuscleFibers";
            Assert.That(WFAugmentRules.ImplantsFromStored(legacy).Select(i => i.Id),
                Is.EquivalentTo(new[] { "WFAugmentLeftMantisBlade", "WFAugmentMuscleFibers" }));
            Assert.That(WFAugmentRules.FromStored(legacy), Is.Empty);

            // Unlocks round-trip with which of them were found; one stored before that was tracked was paid for.
            var unlocks = new HashSet<ProtoId<WFAugmentPrototype>> { "WFAugmentDermalArmor", "WFAugmentCyberEyes" };
            var found = new HashSet<ProtoId<WFAugmentPrototype>> { "WFAugmentCyberEyes" };
            var storedUnlocks = WFAugmentRules.ToStored(unlocks, found);
            Assert.That(WFAugmentRules.UnlocksFromStored(storedUnlocks), Is.EquivalentTo(unlocks));
            Assert.That(WFAugmentRules.FoundFromStored(storedUnlocks), Is.EquivalentTo(found));
            Assert.That(WFAugmentRules.UnlocksFromStored("WFAugmentDermalArmor;WFAugmentCyberEyes"), Is.EquivalentTo(unlocks));
            Assert.That(WFAugmentRules.FoundFromStored("WFAugmentDermalArmor;WFAugmentCyberEyes"), Is.Empty);

            var valid = WFAugmentRules.Validate("Human", new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                // Wrong slot for the augment.
                [WFAugmentSlot.RightArm] = new("WFAugmentCyberLeftArm", null),
                // Unknown augment.
                [WFAugmentSlot.Heart] = new("WFAugmentNotReal", null),
                // A legs-only style can't plate an arm; the first style that fits takes its place.
                [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleZhenkov"),
            }, new ProtoId<TraitPrototype>[0], proto, factory);
            Assert.That(valid.Keys, Is.EquivalentTo(new[] { WFAugmentSlot.LeftArm }));
            Assert.That(valid[WFAugmentSlot.LeftArm].Style?.Id, Is.Not.EqualTo("WFAugmentStyleZhenkov").And.Not.Null);

            // Prybar Prosthetics and Bionic Legs fit their own limbs at spawn, so augments there are dropped.
            var all = new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleCyborg"),
                [WFAugmentSlot.RightLeg] = new("WFAugmentCyberRightLeg", "WFAugmentStyleCyborg"),
            };
            Assert.That(WFAugmentRules.Validate("Human", all, new ProtoId<TraitPrototype>[] { "PrybarProsthetics" }, proto, factory).Keys,
                Is.EquivalentTo(new[] { WFAugmentSlot.RightLeg }));
            Assert.That(WFAugmentRules.Validate("Human", all, new ProtoId<TraitPrototype>[] { "BionicLegs" }, proto, factory).Keys,
                Is.EquivalentTo(new[] { WFAugmentSlot.LeftArm }));

            // A mantis blade needs a synthetic arm: a cybernetic one, a trait's, or an IPC's own.
            var none = new ProtoId<TraitPrototype>[0];
            var unclaimed = new Dictionary<WFAugmentSlot, ProtoId<TraitPrototype>>();
            var natural = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
            var cyberArm = new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleCyborg"),
            };
            var jawsArm = new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.LeftArm] = new("WFAugmentJawsOfLifeLeftArm", "WFAugmentStyleCyborg"),
            };
            var wanted = new ProtoId<WFAugmentPrototype>[] { "WFAugmentLeftMantisBlade", "WFAugmentDermalArmor", "WFAugmentMuscleFibers" };
            Assert.That(WFAugmentRules.ValidateImplants("Human", natural, wanted, none, proto, factory).Select(i => i.Id),
                Is.EquivalentTo(new[] { "WFAugmentDermalArmor", "WFAugmentMuscleFibers" }));
            Assert.That(WFAugmentRules.ValidateImplants("Human", cyberArm, wanted, none, proto, factory), Has.Count.EqualTo(3));
            Assert.That(WFAugmentRules.Synthetic("Human", WFAugmentSlot.LeftArm, natural, unclaimed, proto, factory), Is.False);
            Assert.That(WFAugmentRules.Synthetic("Human", WFAugmentSlot.LeftArm, natural,
                WFAugmentRules.ClaimedSlots(new ProtoId<TraitPrototype>[] { "PrybarProsthetics" }, proto), proto, factory));
            Assert.That(WFAugmentRules.Synthetic("IPC", WFAugmentSlot.LeftArm, natural, unclaimed, proto, factory));

            // Better arms hold more implants; a missing one none.
            Assert.That(WFAugmentRules.ImplantSlots(WFAugmentSlot.LeftArm, natural, unclaimed, proto), Is.EqualTo(1));
            Assert.That(WFAugmentRules.ImplantSlots(WFAugmentSlot.LeftArm, cyberArm, unclaimed, proto), Is.EqualTo(1));
            Assert.That(WFAugmentRules.ImplantSlots(WFAugmentSlot.LeftArm, jawsArm, unclaimed, proto), Is.EqualTo(2));
            Assert.That(WFAugmentRules.ImplantSlots(WFAugmentSlot.Chest, natural, unclaimed, proto), Is.EqualTo(2));

            foreach (var claim in proto.EnumeratePrototypes<WFAugmentTraitSlotsPrototype>())
                Assert.That(proto.HasIndex<TraitPrototype>(claim.ID), $"{claim.ID} claims slots for a trait that doesn't exist.");

            foreach (var style in proto.EnumeratePrototypes<WFAugmentStylePrototype>())
            {
                foreach (var layer in new[] { HumanoidVisualLayers.LLeg, HumanoidVisualLayers.RLeg, HumanoidVisualLayers.LFoot, HumanoidVisualLayers.RFoot })
                {
                    Assert.That(style.Sprites.ContainsKey(layer), $"{style.ID} has no {layer}.");
                    Assert.That(style.Digitigrade.ContainsKey(layer), $"{style.ID} has no digitigrade {layer}.");
                }
            }

            foreach (var augment in proto.EnumeratePrototypes<WFAugmentPrototype>())
            {
                if (augment.Part is { } part)
                    Assert.That(proto.HasIndex(part), $"{augment.ID} names a missing part.");
                if (augment.Organ is { } organ)
                    Assert.That(proto.HasIndex(organ), $"{augment.ID} names a missing organ.");
                Assert.That(augment.Kind != WFAugmentKind.Limb || augment.Part != null, $"{augment.ID} is a limb without a part.");
                Assert.That(augment.Kind != WFAugmentKind.Organ || augment.Organ != null, $"{augment.ID} is an organ without one.");
                Assert.That(!augment.Styled || WFAugmentRules.StylesFor(augment.Slot, proto).Count > 0, $"{augment.ID} has no style to wear.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
