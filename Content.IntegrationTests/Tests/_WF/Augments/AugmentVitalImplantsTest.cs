using System.Collections.Generic;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Augments;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Medical.Components;
using Content.Shared._WF.Augments;
using Content.Shared.Actions;
using Content.Shared.Emp;
using Content.Shared.Humanoid;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// The nutriment pump, reviver and iatric monitor: where they fit, that a body gets them and reads them back, and
/// what each does.
/// </summary>
[TestFixture]
[TestOf(typeof(WFReviverSystem))]
public sealed class AugmentVitalImplantsTest
{
    private static readonly ProtoId<WFAugmentPrototype>[] Vital =
        ["WFAugmentNutrimentPump", "WFAugmentReviver", "WFAugmentIatricMonitor"];

    [Test]
    public async Task VitalImplantsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var timing = server.ResolveDependency<IGameTiming>();
        var augments = server.System<WFAugmentSystem>();
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var actions = server.System<SharedActionsSystem>();
        var ui = server.System<UserInterfaceSystem>();
        var hungers = server.System<HungerSystem>();
        var pumps = server.System<WFNutrimentPumpSystem>();
        var revivers = server.System<WFReviverSystem>();
        var life = server.System<WolfmedLifeSystem>();
        var revival = server.System<WolfmedRevivalSystem>();
        var mobState = server.System<MobStateSystem>();
        var emp = server.System<SharedEmpSystem>();
        var map = await WolfmedGameTest.CreateTestMap(pair);

        var mob = EntityUid.Invalid;
        var scanner = EntityUid.Invalid;
        void Scan()
        {
            var comp = entities.GetComponent<InstantActionComponent>(scanner);
            actions.PerformAction(mob, null, scanner, comp, comp.BaseEvent, timing.CurTime, false);
        }

        await server.WaitAssertion(() =>
        {
            var noTraits = new ProtoId<TraitPrototype>[0];
            var flesh = new Dictionary<WFAugmentSlot, WFAugmentChoice>();

            // The pump and the reviver need flesh; the monitor fits any head.
            Assert.That(WFAugmentRules.ValidateImplants("Human", flesh, Vital, noTraits, proto, factory), Is.EquivalentTo(Vital));
            Assert.That(WFAugmentRules.ValidateImplants("IPC", flesh, Vital, noTraits, proto, factory), Is.EquivalentTo(new[] { Vital[2] }));

            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            humanoids.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugmentImplants(Vital));
            Assert.That(augments.ReadImplants(mob), Is.EquivalentTo(Vital), "The implants put in don't read back.");

            // The pump leaves a fed body alone and takes a starving one back over the line, no further.
            var pump = entities.GetComponent<WFNutrimentPumpComponent>(mob);
            var hunger = entities.GetComponent<HungerComponent>(mob);
            Assert.That(hungers.GetHungerThreshold(hunger), Is.GreaterThan(HungerThreshold.Starving));
            Assert.That(pumps.Feed((mob, pump), hunger), Is.False);
            hungers.SetHunger(mob, 1f, hunger);
            Assert.That(hungers.GetHungerThreshold(hunger), Is.EqualTo(HungerThreshold.Starving));
            Assert.That(pumps.Feed((mob, pump), hunger));
            Assert.That(hungers.GetHunger(hunger), Is.EqualTo(1f + pump.Amount).Within(0.1f));
            for (var i = 0; i < 20 && pumps.Feed((mob, pump), hunger); i++)
            {
            }

            Assert.That(hungers.GetHungerThreshold(hunger), Is.EqualTo(HungerThreshold.Peckish));

            // The monitor's action opens the analyzer's window on its own body.
            var monitor = entities.GetComponent<WFIatricMonitorComponent>(mob);
            Assert.That(monitor.ActionEntity, Is.Not.Null, "The monitor gave its body no action.");
            scanner = monitor.ActionEntity.Value;
            Scan();
            Assert.That(ui.IsUiOpen(scanner, HealthAnalyzerUiKey.Key, mob));
            Assert.That(entities.GetComponent<HealthAnalyzerComponent>(scanner).ScannedEntity, Is.EqualTo(mob));
        });

        // It stays open: the action is in range of the body that holds it.
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.GetComponent<HealthAnalyzerComponent>(scanner);
            Assert.That(ui.IsUiOpen(scanner, HealthAnalyzerUiKey.Key, mob), "The readout closed by itself.");
            Assert.That(analyzer.ScannedEntity, Is.EqualTo(mob));
            Scan();
            Assert.That(ui.IsUiOpen(scanner, HealthAnalyzerUiKey.Key, mob), Is.False);
            Assert.That(analyzer.ScannedEntity, Is.Null, "The scan went on after its window closed.");

            // The reviver charges when the heart stops, and an EMP knocks the charge out and locks it.
            var reviver = entities.GetComponent<WFReviverComponent>(mob);
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Null, "The reviver charged for a beating heart.");
            Assert.That(life.StartArrest(mob, "test"));
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.EqualTo(timing.CurTime + reviver.ChargeTime));

            emp.DoEmpEffects(mob, 0f, System.TimeSpan.FromSeconds(30));
            Assert.That(reviver.NextShock, Is.Null);
            Assert.That(reviver.ReadyAt, Is.EqualTo(timing.CurTime + reviver.EmpLockout));
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Null, "The reviver charged while an EMP had it locked.");

            // Three shocks that don't take, and it has to recharge.
            reviver.ReadyAt = System.TimeSpan.Zero;
            revival.ForcedRoll = 1f;
            for (var i = 0; i < reviver.Shocks; i++)
            {
                Assert.That(reviver.ReadyAt, Is.EqualTo(System.TimeSpan.Zero));
                revivers.Tick((mob, reviver));
                Assert.That(reviver.NextShock, Is.Not.Null);
                reviver.NextShock = timing.CurTime;
                revivers.Tick((mob, reviver));
            }

            Assert.That(life.InArrest(mob));
            Assert.That(reviver.ReadyAt, Is.EqualTo(timing.CurTime + reviver.Recharge));
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Null, "The reviver charged while it was recharging.");

            // A shock that takes restarts the heart, and it recharges again.
            reviver.ReadyAt = System.TimeSpan.Zero;
            revival.ForcedRoll = 0f;
            revivers.Tick((mob, reviver));
            reviver.NextShock = timing.CurTime;
            revivers.Tick((mob, reviver));
            revival.ForcedRoll = null;
            Assert.That(life.InArrest(mob), Is.False, "The reviver's shock did not restart the heart.");
            Assert.That(mobState.IsDead(mob), Is.False);
            Assert.That(reviver.ReadyAt, Is.EqualTo(timing.CurTime + reviver.Recharge));
        });

        await pair.CleanReturnAsync();
    }
}
