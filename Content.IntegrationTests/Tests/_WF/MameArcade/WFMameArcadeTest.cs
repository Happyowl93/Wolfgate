#nullable enable
using System.Linq;
using Content.Client._WF.MameArcade;
using Content.Server._WF.MameArcade;
using Content.Shared._WF.MameArcade;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.MameArcade;

/// <summary>The cabinet seats two players only when their ROMs match, and opens its window.</summary>
[TestOf(typeof(WFMameArcadeSystem))]
public sealed class WFMameArcadeTest
{
    private const string Kof94 = "kof94:9036d879";

    [Test]
    public async Task SeatsTwoPlayersWithMatchingRoms()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var system = entMan.System<WFMameArcadeSystem>();
            var uid = entMan.SpawnEntity("WFMameArcade", map.GridCoords);
            var cabinet = new Entity<WFMameArcadeComponent>(uid, entMan.GetComponent<WFMameArcadeComponent>(uid));
            var host = entMan.SpawnEntity(null, map.GridCoords);
            var guest = entMan.SpawnEntity(null, map.GridCoords);
            var other = entMan.SpawnEntity(null, map.GridCoords);

            Assert.That(system.TryHost(cabinet, host, "kof94", Kof94));
            Assert.That(system.TryHost(cabinet, other, "mslug", "mslug:9036d879"), Is.False, "One lobby per cabinet");
            Assert.That(system.TryJoin(cabinet, guest, "kof94:e72943de"), Is.False, "A different BIOS would desync");
            Assert.That(system.TryJoin(cabinet, host, Kof94), Is.False, "The host can't join their own game");
            Assert.That(system.Partner(cabinet.Comp, host), Is.Null, "Nobody plays until player 2 joins");

            Assert.That(system.TryJoin(cabinet, guest, Kof94));
            Assert.That(system.Partner(cabinet.Comp, host), Is.EqualTo(guest));
            Assert.That(system.Partner(cabinet.Comp, guest), Is.EqualTo(host));
            Assert.That(system.Partner(cabinet.Comp, other), Is.Null);
            Assert.That(system.TryJoin(cabinet, other, Kof94), Is.False, "Two seats only");

            system.Leave(cabinet, other);
            Assert.That(cabinet.Comp.Guest, Is.EqualTo(guest), "A bystander leaving changes nothing");

            system.Leave(cabinet, guest);
            Assert.That(cabinet.Comp.Host, Is.Null);
            Assert.That(system.TryHost(cabinet, other, "mslug", "mslug:9036d879"), "The cabinet is free again");

            Assert.That(system.TryHost(cabinet, host, "", Kof94), Is.False);
            entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CabinetOpensWindow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);
            var cabinet = server.EntMan.SpawnEntity("WFMameArcade", map.GridCoords);
            server.System<SharedUserInterfaceSystem>().OpenUi(cabinet, MameArcadeUiKey.Key, player);
        });

        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var window = client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
                .OfType<MameArcadeWindow>()
                .SingleOrDefault();

            Assert.That(window, Is.Not.Null, "The cabinet did not open its window");
            window!.Close();
        });

        await pair.CleanReturnAsync();
    }
}
