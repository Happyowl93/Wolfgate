using Content.Server._WF.Narcotics;
using Content.Server._WF.Neuroware;
using Content.Server._WF.Synthetics;
using Content.Server.Body.Components;
using Content.Shared._WF.Narcotics;
using Content.Shared._WF.Neuroware;
using Content.Shared.Body.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Neuroware;

/// <summary>
/// A stick loads only into a host and feeds its profile at the stick's integrity, copies lose integrity, and a panel
/// reboot clears what is running.
/// </summary>
[TestFixture]
[TestOf(typeof(WFNeurowareSystem))]
public sealed class NeurowareTest
{
    private const string Profile = "WFCocaine";

    [Test]
    public async Task LoadCopyRebootTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid host = default;

        await server.WaitAssertion(() =>
        {
            var neuro = server.System<WFNeurowareSystem>();
            var intox = server.System<WFIntoxicationSystem>();
            var body = server.System<SharedBodySystem>();

            Entity<WFNeurowareStickComponent> Stick(string proto)
            {
                var uid = entMan.SpawnEntity(proto, map.GridCoords);
                return (uid, entMan.GetComponent<WFNeurowareStickComponent>(uid));
            }

            var organic = entMan.SpawnEntity("MobHuman", map.GridCoords);
            host = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WFNeurowareHostComponent>(body.GetBodyOrganEntityComps<BrainComponent>(host)[0].Owner);

            var stick = Stick("WFNeurowareStickWhiteline");
            Assert.That(neuro.Load(stick, organic, organic), Is.False, "An organic brain has no port.");
            Assert.That(neuro.Load(stick, host, host), Is.True);
            Assert.That(stick.Comp.Loads, Is.EqualTo(2));

            neuro.Tick(1f);
            Assert.That(intox.GetLevel(host, Profile), Is.EqualTo(stick.Comp.Rate * stick.Comp.Integrity).Within(0.001f),
                "A program feeds its rate each second.");

            var copy = Stick("WFNeurowareStick");
            var copyOfCopy = Stick("WFNeurowareStick");
            Assert.That(neuro.TryCopy(stick, copy, null), Is.True);
            Assert.That(neuro.TryCopy(copy, copyOfCopy, null), Is.True);
            Assert.That(neuro.TryCopy(stick, copy, null), Is.False, "Only a blank takes a copy.");
            Assert.Multiple(() =>
            {
                Assert.That(copy.Comp.Integrity, Is.EqualTo(0.85f).Within(0.001f));
                Assert.That(copyOfCopy.Comp.Integrity, Is.EqualTo(0.7225f).Within(0.001f), "Each generation loses 15%.");
            });

            server.System<WFChassisPanelSystem>().FinishReboot(host);
            Assert.That(intox.GetLevel(host, Profile), Is.EqualTo(0f), "A reboot clears the levels.");
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFNeurowareComponent>(host), Is.False, "A reboot stops running programs.");
        });

        await pair.CleanReturnAsync();
    }
}
