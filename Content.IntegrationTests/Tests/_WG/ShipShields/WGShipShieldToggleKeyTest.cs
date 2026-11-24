#nullable enable
using Content.Server._Crescent.ShipShields;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>The pilot's shield hotkey raises and lowers the flown ship's shields, under the helm switch's checks.</summary>
[TestFixture]
public sealed class WGShipShieldToggleKeyTest
{
    [Test]
    public async Task ThePilotsKeyFlipsTheShields()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            entities.System<ShipShieldsSystem>().Update(1.5f);
            var controls = entities.System<WGShipShieldShuntSystem>();
            Assert.That(controls.GetState(grid).Enabled, Is.True, "Shields start enabled.");

            var helm = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            entities.RemoveComponent<AccessReaderComponent>(helm);
            entities.GetComponent<ApcPowerReceiverComponent>(helm).Powered = true;
            var bystander = entities.SpawnEntity("MobHuman", map.GridCoords);
            var pilot = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<PilotComponent>(pilot);
            entities.System<ShuttleConsoleSystem>().AddPilot(helm, pilot, entities.GetComponent<ShuttleConsoleComponent>(helm));

            Assert.That(controls.TryToggleFromHelm(bystander), Is.False, "Someone not at the helm can't use the key.");
            Assert.That(controls.GetState(grid).Enabled, Is.True);

            Assert.That(controls.TryToggleFromHelm(pilot), Is.True);
            Assert.That(controls.GetState(grid).Enabled, Is.False, "The key lowers raised shields.");
            Assert.That(controls.TryToggleFromHelm(pilot), Is.True);
            Assert.That(controls.GetState(grid).Enabled, Is.True, "And raises them again.");

            entities.GetComponent<ApcPowerReceiverComponent>(helm).Powered = false;
            Assert.That(controls.TryToggleFromHelm(pilot), Is.False, "A dead helm can't work the shields.");
            Assert.That(controls.GetState(grid).Enabled, Is.True);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
