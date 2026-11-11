using Content.Server._WF.Narcotics;
using Content.Shared._WF.Narcotics;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Narcotics;

/// <summary>A heavy stimulant high rushes speech and a heavy opioid high trails it off; a light one changes nothing.</summary>
[TestFixture]
[TestOf(typeof(WFDrugSpeechSystem))]
public sealed class DrugSpeechTest
{
    [Test]
    public async Task SpeechTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var speech = server.System<WFDrugSpeechSystem>();
            var intoxication = server.System<WFIntoxicationSystem>();

            for (var i = 0; i < 20; i++)
            {
                var rushed = speech.Rush("We need to go now. Right now.", false);
                Assert.That(rushed, Does.EndWith("now!"), rushed);
                Assert.That(rushed, Does.Not.Contain("."), rushed);

                var trailed = speech.Trail("I feel fine, really fine.", false);
                Assert.That(trailed, Does.EndWith("fine..."), trailed);
                Assert.That(trailed, Does.StartWith("I"), trailed);
            }

            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entMan.EnsureComponent<WFIntoxicationComponent>(mob);
            comp.Levels["WFCocaine"] = 2f;
            Assert.That(intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant), Is.EqualTo(WFDrugPhase.Light));
            comp.Levels["WFCocaine"] = 10f;
            Assert.That(intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant), Is.EqualTo(WFDrugPhase.Heavy));
            comp.Levels["WFCrack"] = 13f;
            Assert.That(intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant), Is.EqualTo(WFDrugPhase.Overdose),
                "The furthest profile of the family sets the phase.");
            Assert.That(intoxication.FamilyPhase(comp, WFDrugFamily.Opioid), Is.EqualTo(WFDrugPhase.None));
        });

        await pair.CleanReturnAsync();
    }
}
