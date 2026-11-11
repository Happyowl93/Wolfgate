using System.Collections.Generic;
using Content.Server._WF.Narcotics;
using Content.Shared._WF.Purity;
using Content.Server.Body.Systems;
using Content.Server.EntityEffects.EffectConditions;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._WF.Purity;

/// <summary>
/// Reactions grade their product, metabolism works at a reagent's grade and poisons for the rest, thresholds count
/// effective units, and grades survive serialisation.
/// </summary>
[TestFixture]
[TestOf(typeof(Solution))]
public sealed class PurityTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFPurityTestBeaker
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 100

- type: reagent
  id: WFPurityTestA
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: WFPurityTestB
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: WFPurityTestProduct
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: WFPurityTestC
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: WFPurityTestTuned
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reaction
  id: WFPurityTestPassReaction
  reactants:
    WFPurityTestA:
      amount: 1
    WFPurityTestB:
      amount: 1
  products:
    WFPurityTestProduct: 2

- type: reaction
  id: WFPurityTestTunedReaction
  reactants:
    WFPurityTestC:
      amount: 1
  products:
    WFPurityTestTuned: 1

- type: wfReactionPurity
  id: WFPurityTestTunedReaction
  optimalTemp: 300
  tolerance: 100
  floor: 0.4

- type: reagent
  id: WFPurityTestHeal
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  metabolisms:
    Medicine:
      metabolismRate: 1
      effects:
      - !type:WFClearIntoxication
        family: Stimulant
        amount: 2

- type: wfDrugProfile
  id: WFPurityTestProfile
  family: Stimulant
  decay: 0
  tiers:
  - min: 1000
";

    [Test]
    public async Task ReactionTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();

            Solution Mix(params Solution[] inputs)
            {
                var beaker = server.EntMan.SpawnEntity("WFPurityTestBeaker", map.GridCoords);
                solutions.TryGetSolution(beaker, "beaker", out var ent, out var solution);
                foreach (var input in inputs)
                {
                    solutions.TryAddSolution(ent!.Value, input);
                }

                return solution!;
            }

            Solution Graded(string reagent, float grade, float temperature = 300f)
            {
                var solution = new Solution { Temperature = temperature };
                solution.AddReagent(reagent, FixedPoint2.New(10), grade);
                return solution;
            }

            var passed = Mix(Graded("WFPurityTestA", 0.5f), Graded("WFPurityTestB", 1f));
            Assert.That(passed.GetTotalPrototypeQuantity("WFPurityTestProduct"), Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(passed.GetPurity("WFPurityTestProduct"), Is.EqualTo(0.75f).Within(0.01f),
                "A reaction with no wfReactionPurity should pass the mean input grade through.");

            var optimal = Mix(Graded("WFPurityTestC", 1f, 300f));
            Assert.That(optimal.GetPurity("WFPurityTestTuned"), Is.EqualTo(1f).Within(0.01f));

            var hot = Mix(Graded("WFPurityTestC", 1f, 350f));
            Assert.That(hot.GetPurity("WFPurityTestTuned"), Is.EqualTo(0.7f).Within(0.01f),
                "Off-optimum temperature should lower the product grade.");

            var floor = Mix(Graded("WFPurityTestC", 0.5f, 500f));
            Assert.That(floor.GetPurity("WFPurityTestTuned"), Is.EqualTo(0.2f).Within(0.01f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MetabolismTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid standard = default;
        EntityUid graded = default;
        const string profile = "WFPurityTestProfile";

        // Wolfmed clears small amounts of Poison within seconds, so make the impurity damage large enough to read.
        await server.WaitPost(() => server.CfgMan.SetCVar(WFPurityCVars.ImpurityDamage, 20f));

        await server.WaitPost(() =>
        {
            var bloodstream = server.System<BloodstreamSystem>();
            var intox = server.System<WFIntoxicationSystem>();

            standard = entMan.SpawnEntity("MobHuman", map.GridCoords);
            graded = entMan.SpawnEntity("MobHuman", map.GridCoords);
            intox.AddLevel(standard, profile, 100f);
            intox.AddLevel(graded, profile, 100f);

            var good = new Solution();
            good.AddReagent("WFPurityTestHeal", FixedPoint2.New(2));
            var cut = new Solution();
            cut.AddReagent("WFPurityTestHeal", FixedPoint2.New(2), 0.5f);
            Assert.That(bloodstream.TryAddToChemicals(standard, good), Is.True);
            Assert.That(bloodstream.TryAddToChemicals(graded, cut), Is.True);
        });

        await pair.RunSeconds(12);

        await server.WaitAssertion(() =>
        {
            var intox = server.System<WFIntoxicationSystem>();
            var standardEffect = 100f - intox.GetLevel(standard, profile);
            var gradedEffect = 100f - intox.GetLevel(graded, profile);
            var unitsMetabolised = standardEffect / 2f;
            var standardPoison = entMan.GetComponent<DamageableComponent>(standard).Damage.DamageDict.GetValueOrDefault("Poison").Float();
            var gradedPoison = entMan.GetComponent<DamageableComponent>(graded).Damage.DamageDict.GetValueOrDefault("Poison").Float();

            Assert.Multiple(() =>
            {
                Assert.That(standardEffect, Is.GreaterThan(0f), "The standard dose should take effect.");
                Assert.That(gradedEffect, Is.EqualTo(standardEffect / 2f).Within(0.05f), "A 50% dose should work at half strength.");
                Assert.That(gradedPoison - standardPoison, Is.GreaterThan(unitsMetabolised * 0.5f * 20f * 0.5f),
                    "The impure half of each unit should deal Poison.");
                Assert.That(standardPoison, Is.EqualTo(0f), "Standard grade deals no impurity damage.");
            });
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(WFPurityCVars.ImpurityDamage, 1f));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThresholdTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var protos = server.ResolveDependency<IPrototypeManager>();
            var reagent = protos.Index<ReagentPrototype>("WFPurityTestHeal");
            var target = server.EntMan.SpawnEntity(null, map.GridCoords);
            var threshold = new ReagentThreshold { Min = FixedPoint2.New(5) };

            bool Trips(float grade)
            {
                var solution = new Solution();
                solution.AddReagent(reagent.ID, FixedPoint2.New(8), grade);
                var args = new EntityEffectReagentArgs(target, server.EntMan, null, solution, FixedPoint2.New(1), reagent, null, FixedPoint2.New(1));
                return threshold.Condition(args);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Trips(1f), Is.True, "8 standard units pass a 5 u threshold.");
                Assert.That(Trips(0.5f), Is.False, "8 u at 50% is 4 effective units.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SerializationTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var serialization = server.ResolveDependency<ISerializationManager>();

            var solution = new Solution();
            solution.AddReagent("WFPurityTestA", FixedPoint2.New(10), 0.3f);
            solution.AddReagent("WFPurityTestB", FixedPoint2.New(10));
            var read = serialization.Read<Solution>(serialization.WriteValue(solution, notNullableOverride: true), notNullableOverride: true);
            Assert.That(read.GetPurity("WFPurityTestA"), Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(read.Purity!.ContainsKey("WFPurityTestB"), Is.False);

            var plain = new Solution();
            plain.AddReagent("WFPurityTestB", FixedPoint2.New(10));
            var plainRead = serialization.Read<Solution>(serialization.WriteValue(plain, notNullableOverride: true), notNullableOverride: true);
            Assert.That(plainRead.Purity, Is.Null);
        });

        await pair.CleanReturnAsync();
    }
}
