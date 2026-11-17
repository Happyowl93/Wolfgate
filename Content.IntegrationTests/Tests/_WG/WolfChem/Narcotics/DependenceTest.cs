using System.Collections.Generic;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared._WG.WolfChem.Narcotics;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// Doses build dependence and tolerance, the meter fades while clean, withdrawal starts after going without and only
/// a high ends it, and the habit traits start the round dependent.
/// </summary>
[TestFixture]
[TestOf(typeof(WGDependenceSystem))]
public sealed class DependenceTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wgDrugProfile
  id: WGDependenceTestProfile
  family: Opioid
  decay: 1
  dependenceGain: 0.1
  tiers:
  - min: 2
";

    private const string Profile = "WGDependenceTestProfile";

    [Test]
    public async Task GainDecayWithdrawalTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var intox = server.System<WGIntoxicationSystem>();
            var dependence = server.System<WGDependenceSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            intox.AddLevel(mob, Profile, 4f);
            var dep = entMan.GetComponent<WGDependenceComponent>(mob);
            Assert.Multiple(() =>
            {
                Assert.That(dep.Meter[WGDrugFamily.Opioid], Is.EqualTo(0.4f).Within(0.001f), "Gain is dependenceGain per level.");
                Assert.That(dep.Tolerance[WGDrugFamily.Opioid], Is.EqualTo(0.2f).Within(0.001f));
            });

            var patient = entMan.SpawnEntity("MobHuman", map.GridCoords);
            intox.RegisterDose(patient, Profile, 4f, false);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<WGIntoxicationComponent>(patient), Is.False, "A dose that gives no level tracks no high.");
                Assert.That(entMan.GetComponent<WGDependenceComponent>(patient).Meter[WGDrugFamily.Opioid], Is.EqualTo(0.4f).Within(0.001f));
            });

            intox.Tick(1f);
            Assert.That(intox.GetLevel(mob, Profile), Is.EqualTo(2.8f).Within(0.001f), "Tolerance speeds up decay.");

            for (var i = 0; i < 5; i++)
            {
                intox.Tick(1f);
            }

            dependence.Tick(60f);
            Assert.That(dep.Meter[WGDrugFamily.Opioid], Is.EqualTo(0.38f).Within(0.001f), "The meter loses 0.02 a minute clean.");
            Assert.That(dep.Stage.GetValueOrDefault(WGDrugFamily.Opioid), Is.EqualTo(0), "0.38 is below withdrawal.");

            dep.Meter[WGDrugFamily.Opioid] = 0.9f;
            dependence.Tick(WGDependenceSystem.WithdrawalDelay - 61f);
            Assert.That(dep.Stage.GetValueOrDefault(WGDrugFamily.Opioid), Is.EqualTo(0), "Withdrawal waits five minutes.");

            dep.Meter[WGDrugFamily.Opioid] = 0.9f;
            dependence.Tick(2f);
            Assert.That(dep.Stage[WGDrugFamily.Opioid], Is.EqualTo(3), "A meter of 0.9 is the top stage.");

            intox.AddLevel(mob, Profile, 0.5f);
            dependence.Tick(1f);
            Assert.That(dep.Stage[WGDrugFamily.Opioid], Is.EqualTo(3), "A taste below the first tier doesn't end withdrawal.");

            intox.AddLevel(mob, Profile, 4f);
            dependence.Tick(1f);
            Assert.Multiple(() =>
            {
                Assert.That(dep.Stage[WGDrugFamily.Opioid], Is.EqualTo(0), "Reaching the first tier does.");
                Assert.That(dep.Satisfied, Does.Contain(WGDrugFamily.Opioid), "A dependent body that is high is satisfied.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TraitTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var dependence = server.System<WGDependenceSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGDependentStimulantComponent>(mob);
            entMan.AddComponent<WGDependentNicotineComponent>(mob);

            var dep = entMan.GetComponent<WGDependenceComponent>(mob);
            Assert.Multiple(() =>
            {
                Assert.That(dep.Meter[WGDrugFamily.Stimulant], Is.EqualTo(0.8f).Within(0.001f));
                Assert.That(dep.Meter[WGDrugFamily.Nicotine], Is.EqualTo(0.8f).Within(0.001f), "Traits stack.");
            });

            dependence.Tick(100000f);
            Assert.That(dep.Meter[WGDrugFamily.Stimulant], Is.EqualTo(0.55f).Within(0.001f), "A trait keeps the meter at 0.55.");
            Assert.That(WGDependenceSystem.StageFor(WGDrugFamily.Stimulant, dep.Meter[WGDrugFamily.Stimulant]), Is.EqualTo(1), "A clean trait eases to cravings.");
            Assert.Multiple(() =>
            {
                Assert.That(WGDependenceSystem.StageFor(WGDrugFamily.Stimulant, 0.9f), Is.EqualTo(3));
                Assert.That(WGDependenceSystem.StageFor(WGDrugFamily.Nicotine, 0.9f), Is.EqualTo(1), "Nicotine stops at cravings.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
