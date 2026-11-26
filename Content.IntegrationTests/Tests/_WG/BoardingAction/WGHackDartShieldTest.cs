#nullable enable
using Content.Server._Crescent.ShipShields;
using Content.Server._WG.BoardingAction;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.BoardingAction;

/// <summary>A hacking dart slips through the field of a ship at least half full of heat, and nothing else does.</summary>
[TestFixture]
public sealed class WGHackDartShieldTest
{
    [Test]
    public async Task HotShieldsLetTheDartThrough()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var shields = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var dart = entities.SpawnEntity("WGHackDart", map.GridCoords);
            var harpoon = entities.SpawnEntity("WGShipHarpoon", map.GridCoords);

            bool Passes(EntityUid shot)
            {
                var ev = new WGShipShieldInterceptAttemptEvent(grid, shot, null, null);
                entities.EventBus.RaiseEvent(EventSource.Local, ref ev);
                return ev.Cancelled;
            }

            var capacity = shields.GetWolfgateShipHeat(grid).Capacity;
            Assert.That(Passes(dart), Is.False, "A cool field stops the dart.");
            SetHeat(entities, generator, capacity * (WGHackHarpoonSystem.ShieldPierceHeat - 0.02f));
            Assert.That(Passes(dart), Is.False, "Just under half heat still stops it.");
            SetHeat(entities, generator, capacity * (WGHackHarpoonSystem.ShieldPierceHeat + 0.02f));
            Assert.That(Passes(dart), Is.True, "Half full of heat, the field lets the dart through.");
            Assert.That(Passes(harpoon), Is.False, "Only the hacking dart slips through.");
        });
        await pair.CleanReturnAsync();
    }

    private static void SetHeat(IEntityManager entities, EntityUid uid, float heat)
    {
        var emitter = entities.GetComponent<ShipShieldEmitterComponent>(uid);
        emitter.Damage = heat * entities.System<ShipShieldsSystem>().WolfgateGeneratorShare(uid, emitter);
    }
}
