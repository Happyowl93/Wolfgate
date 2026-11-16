using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Augments;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Goobstation.Autosurgeon;
using Content.Shared._WF.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Emp;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// The tool arm, smartlink, finger lighter and scrubber liver: where they fit, that a body gets them and reads them
/// back, and what each does.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDeployableImplantSystem))]
public sealed class AugmentImplantsTest
{
    private static readonly ProtoId<WFAugmentPrototype>[] Implants =
        ["WFAugmentRightToolArm", "WFAugmentRightSmartlink", "WFAugmentLeftFingerLighter"];

    [Test]
    public async Task ImplantsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var timing = server.ResolveDependency<IGameTiming>();
        var augments = server.System<WFAugmentSystem>();
        var bodies = server.System<SharedBodySystem>();
        var hands = server.System<SharedHandsSystem>();
        var actions = server.System<SharedActionsSystem>();
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var surgeons = server.System<AutoSurgeonSystem>();
        var emp = server.System<SharedEmpSystem>();
        var toxins = server.System<WolfmedToxinSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var noTraits = new ProtoId<TraitPrototype>[0];
            var flesh = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
            var cyber = new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.RightArm] = new("WFAugmentCyberRightArm", "WFAugmentStyleCyborg"),
                [WFAugmentSlot.Liver] = new("WFAugmentScrubberLiver", null),
            };

            // The tool arm and the smartlink need a synthetic part, and a cybernetic arm's own hand is one.
            Assert.That(WFAugmentRules.ValidateImplants("Human", flesh, Implants, noTraits, proto, factory).Select(i => i.Id),
                Is.EquivalentTo(new[] { "WFAugmentLeftFingerLighter" }));
            Assert.That(WFAugmentRules.ValidateImplants("Human", cyber, Implants, noTraits, proto, factory),
                Is.EquivalentTo(Implants));

            // A hand holds one implant, and the smartlink outranks the lighter.
            var oneHand = new ProtoId<WFAugmentPrototype>[] { "WFAugmentRightFingerLighter", "WFAugmentRightSmartlink" };
            Assert.That(WFAugmentRules.ValidateImplants("Human", cyber, oneHand, noTraits, proto, factory).Select(i => i.Id),
                Is.EquivalentTo(new[] { "WFAugmentRightSmartlink" }));

            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            var plain = entities.SpawnEntity("MobHuman", map.GridCoords);
            humanoids.LoadProfile(mob,
                HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(cyber).WithAugmentImplants(Implants));

            EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry side) =>
                bodies.GetBodyChildrenOfType(body, type, symmetry: side).Single().Id;
            Hand HandOf(EntityUid body, HandLocation side) => hands.EnumerateHands(body).Single(h => h.Location == side);
            string Proto(EntityUid? uid) => uid == null ? null : entities.GetComponent<MetaDataComponent>(uid.Value).EntityPrototype?.ID;

            var arm = Part(mob, BodyPartType.Arm, BodyPartSymmetry.Right);
            var rightHand = Part(mob, BodyPartType.Hand, BodyPartSymmetry.Right);
            var leftHand = Part(mob, BodyPartType.Hand, BodyPartSymmetry.Left);
            Assert.That(entities.HasComponent<WFSmartlinkComponent>(rightHand));
            Assert.That(entities.HasComponent<WFSmartlinkUserComponent>(mob));
            Assert.That(entities.HasComponent<WFFingerLighterComponent>(leftHand));
            Assert.That(bodies.GetBodyOrgans(mob).Any(o => Proto(o.Id) == "WFAugmentScrubberLiver"));
            Assert.That(augments.ReadImplants(mob), Is.EquivalentTo(Implants), "The implants put in don't read back.");
            Assert.That(augments.ReadImplants(plain), Is.Empty);

            // The tool arm folds its omnitool out into that side's hand, and back.
            var tool = entities.GetComponent<WFToolArmComponent>(arm);
            Assert.That(tool.ActionEntity, Is.Not.Null, "The tool arm gave its body no action.");
            void Toggle(EntityUid action)
            {
                var comp = entities.GetComponent<InstantActionComponent>(action);
                actions.PerformAction(mob, null, action, comp, comp.BaseEvent, timing.CurTime, false);
            }

            Toggle(tool.ActionEntity.Value);
            Assert.That(Proto(HandOf(mob, HandLocation.Right).HeldEntity), Is.EqualTo("WFAugmentToolArmTool"));
            Assert.That(HandOf(mob, HandLocation.Left).HeldEntity, Is.Null, "The lighter in the other hand answered the tool arm's action.");
            Toggle(tool.ActionEntity.Value);
            Assert.That(HandOf(mob, HandLocation.Right).HeldEntity, Is.Null);

            var lighter = entities.GetComponent<WFFingerLighterComponent>(leftHand);
            Toggle(lighter.ActionEntity!.Value);
            Assert.That(Proto(HandOf(mob, HandLocation.Left).HeldEntity), Is.EqualTo("WFAugmentFingerLighterFlame"));

            // An EMP folds a deployed implant away.
            emp.DoEmpEffects(leftHand, 0f, System.TimeSpan.FromSeconds(30));
            Assert.That(lighter.Deployed, Is.Null);

            // A gun spreads less in the linked hand than in the other.
            var gun = entities.SpawnEntity("WeaponPistolMk58", map.GridCoords);
            var gunComp = entities.GetComponent<GunComponent>(gun);
            Assert.That(hands.TryPickup(plain, gun, HandOf(plain, HandLocation.Right).Name, checkActionBlocker: false));
            var unlinked = gunComp.MaxAngleModified.Theta;
            Assert.That(unlinked, Is.GreaterThan(0));
            Assert.That(hands.TryDrop(plain, gun, checkActionBlocker: false));

            Assert.That(hands.TryPickup(mob, gun, HandOf(mob, HandLocation.Right).Name, checkActionBlocker: false));
            var link = entities.GetComponent<WFSmartlinkComponent>(rightHand);
            Assert.That(gunComp.MaxAngleModified.Theta, Is.EqualTo(unlinked * link.SpreadMultiplier).Within(1e-4));
            Assert.That(hands.TryDrop(mob, gun, checkActionBlocker: false));
            Assert.That(gunComp.MaxAngleModified.Theta, Is.EqualTo(unlinked).Within(1e-4));

            // Put in a flesh hand during a round, it scrambles under an EMP and the gun spreads more than unlinked.
            var surgeon = proto.Index<EntityPrototype>("WFAutosurgeonRightSmartlink");
            Assert.That(surgeon.TryComp(out AutoSurgeonComponent surgeonComp, factory));
            Assert.That(surgeons.WfApplyUpgrade(plain, surgeonComp));
            var fleshHand = Part(plain, BodyPartType.Hand, BodyPartSymmetry.Right);
            var fleshLink = entities.GetComponent<WFSmartlinkComponent>(fleshHand);
            Assert.That(hands.TryPickup(plain, gun, HandOf(plain, HandLocation.Right).Name, checkActionBlocker: false));
            Assert.That(gunComp.MaxAngleModified.Theta, Is.EqualTo(unlinked * fleshLink.SpreadMultiplier).Within(1e-4));

            emp.DoEmpEffects(fleshHand, 0f, System.TimeSpan.FromSeconds(30));
            Assert.That(fleshLink.Scrambled);
            Assert.That(gunComp.MaxAngleModified.Theta, Is.EqualTo(unlinked * fleshLink.ScrambledSpreadMultiplier).Within(1e-4));
            entities.RemoveComponent<EmpDisabledComponent>(fleshHand);
            Assert.That(fleshLink.Scrambled, Is.False);
            Assert.That(gunComp.MaxAngleModified.Theta, Is.EqualTo(unlinked * fleshLink.SpreadMultiplier).Within(1e-4));

            // The scrubber liver clears toxin load three times as fast as a flesh one.
            var clearance = toxins.GetClearanceRate(plain);
            Assert.That(clearance, Is.GreaterThan(0f));
            Assert.That(toxins.GetClearanceRate(mob), Is.EqualTo(clearance * 3f).Within(1e-4));

            entities.DeleteEntity(gun);
            entities.DeleteEntity(mob);
            entities.DeleteEntity(plain);
        });

        await pair.CleanReturnAsync();
    }
}
