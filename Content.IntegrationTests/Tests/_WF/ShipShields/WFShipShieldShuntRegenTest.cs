using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks a shunted field regenerates no faster than an even one, and recharge mode is untouched.</summary>
[TestFixture]
public sealed class WFShipShieldShuntRegenTest
{
    [Test]
    public async Task ShuntDividesOnlineRegenByPeakStrength()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var shields = entities.System<ShipShieldsSystem>();
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(generator);
            shields.Update(1.5f);
            Assert.That(emitter.Shielded, Is.EqualTo(map.Grid.Owner), "The field must be up before measuring regen.");

            float Heal(bool recharging)
            {
                emitter.Recharging = recharging;
                emitter.Damage = 10000f;
                emitter.Accumulator = 0f;
                shields.Update(1.5f);
                return 10000f - emitter.Damage;
            }

            var even = Heal(false);
            Assert.That(even, Is.EqualTo(emitter.HealPerSecond * 1.5f).Within(0.01f));
            Assert.That(shields.SetWolfgateShieldShunt(map.Grid.Owner, 0f, 1f, MathF.PI / 2f), Is.True);
            Assert.That(Heal(false), Is.EqualTo(even / WFShipShieldShuntMath.PeakStrength(1f, MathF.PI / 2f)).Within(0.01f));
            Assert.That(Heal(true), Is.EqualTo(even * emitter.UnpoweredBonus).Within(0.01f),
                "Recharge mode recovers at the full rate whatever the allocation.");
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
