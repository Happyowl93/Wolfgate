#nullable enable
using System.Numerics;
using System.Reflection;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.Detection;
using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipShields;
using Content.Server.Physics.Controllers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Detection;
using Content.Shared._WG.ShipShields;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks what a raised field costs its ship, and that only a field beaten down by damage locks the guns.</summary>
[TestFixture]
public sealed class WGShipShieldCostTest
{
    [Test]
    public async Task RaisedFieldCostsThrustTurningAndStealth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var mover = entities.System<MoverController>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            var cost = entities.GetComponent<WGShipShieldCostComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            var shuttle = entities.EnsureComponent<ShuttleComponent>(grid);
            var body = entities.GetComponent<PhysicsComponent>(grid);
            var transform = entities.GetComponent<TransformComponent>(grid);
            shuttle.AngularThrust = 100f;
            shuttle.LinearThrust[2] = 100f;

            var freeTorque = mover.GetTorque(shuttle);
            var freeThrust = mover.GetDirectionThrust(Vector2.UnitY, shuttle, body, transform).Length();
            Assert.That(freeTorque, Is.GreaterThan(0f));
            Assert.That(freeThrust, Is.GreaterThan(0f));
            Assert.That(Signature(entities, grid), Is.Zero, "A ship with no field up adds nothing to its signature.");

            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(system.GetWolfgateShieldThrustFactor(grid), Is.EqualTo(cost.ThrustFactor));
            Assert.That(system.GetWolfgateShieldTurnFactor(grid), Is.EqualTo(cost.TurnFactor));
            Assert.That(mover.GetTorque(shuttle), Is.EqualTo(freeTorque * cost.TurnFactor).Within(0.001f));
            Assert.That(mover.GetDirectionThrust(Vector2.UnitY, shuttle, body, transform).Length(),
                Is.EqualTo(freeThrust * cost.ThrustFactor).Within(0.001f));
            Assert.That(Signature(entities, grid), Is.EqualTo(cost.Signature));

            var stats = entities.System<WGShipShieldShuntSystem>().GetState(grid).Stats!;
            Assert.That(stats.ThrustFactor, Is.EqualTo(cost.ThrustFactor));
            Assert.That(stats.TurnFactor, Is.EqualTo(cost.TurnFactor));
            Assert.That(stats.GunLockSeconds, Is.EqualTo(cost.GunLockout));

            var heat = entities.EnsureComponent<ThermalSignatureComponent>(grid);
            var stored = heat.StoredHeat;
            typeof(ShipShieldsSystem).GetMethod("AddWolfgateShieldHitHeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(system, new object[] { entities.GetComponent<ShipShieldComponent>(emitter.Shield!.Value), 100f });
            Assert.That(heat.StoredHeat - stored, Is.EqualTo(100f * cost.HitSignature).Within(0.01f), "Absorbed damage flares the signature.");

            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(system.GetWolfgateShieldThrustFactor(grid), Is.EqualTo(1f));
            Assert.That(mover.GetTorque(shuttle), Is.EqualTo(freeTorque));
            Assert.That(mover.GetDirectionThrust(Vector2.UnitY, shuttle, body, transform).Length(), Is.EqualTo(freeThrust).Within(0.001f));
            Assert.That(Signature(entities, grid), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyDamageCollapseLocksGuns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        var grid = map.Grid.Owner;
        var system = entities.System<ShipShieldsSystem>();
        EntityUid gun = default;
        EntityUid user = default;
        await pair.Server.WaitAssertion(() =>
        {
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            var power = entities.GetComponent<ApcPowerReceiverComponent>(emitterUid);
            power.Powered = true;
            gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            user = entities.SpawnEntity(null, map.GridCoords);
            Assert.That(entities.HasComponent<FireControllableComponent>(gun), Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(gun).GridUid, Is.EqualTo(grid));

            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(Refused(entities, gun, user), Is.False);

            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "Lowering the field by hand leaves the guns free.");
            system.SetWolfgateShieldEnabled(grid, true);
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);

            power.Powered = false;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "Losing power leaves the guns free.");
            Assert.That(Sparking(entities, gun), Is.False);
            power.Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);

            emitter.Damage = emitter.DamageLimit + 5000f;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            var locked = system.GetWolfgateGunLockSeconds(grid);
            Assert.That(locked, Is.InRange(1, 6), "A field beaten down by damage locks the guns.");
            Assert.That(entities.System<WGShipShieldShuntSystem>().GetState(grid).GunLockSeconds, Is.EqualTo(locked));
            Assert.That(Refused(entities, gun, user), Is.True);
            Assert.That(Sparking(entities, gun), Is.True, "A locked gun is wreathed in sparks.");
        });
        await pair.RunSeconds(7f);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero);
            Assert.That(Refused(entities, gun, user), Is.False, "The guns come back when the lock runs out.");
            Assert.That(Sparking(entities, gun), Is.False, "The sparks go with the lock.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CollisionCollapseLocksGuns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            emitter.Damage = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp) - 20f;
            var point = entities.System<SharedTransformSystem>().GetWorldPosition(grid);
            system.AbsorbWolfgateCollision(grid, point, 100f, 2f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.InRange(1, 6), "A field a collision uses up locks the guns too.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeflectorFieldIsFree()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("WGShieldGeneratorDeflector", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(system.GetWolfgateShieldThrustFactor(grid), Is.EqualTo(1f));
            Assert.That(system.GetWolfgateShieldTurnFactor(grid), Is.EqualTo(1f));
            Assert.That(Signature(entities, grid), Is.Zero);

            var heat = entities.EnsureComponent<ThermalSignatureComponent>(grid);
            var stored = heat.StoredHeat;
            typeof(ShipShieldsSystem).GetMethod("AddWolfgateShieldHitHeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(system, new object[] { entities.GetComponent<ShipShieldComponent>(emitter.Shield!.Value), 100f });
            Assert.That(heat.StoredHeat, Is.EqualTo(stored));

            emitter.Damage = emitter.DamageLimit + 5000f;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "A deflector beaten down leaves the guns free.");
        });
        await pair.CleanReturnAsync();
    }

    private static float Signature(IEntityManager entities, EntityUid grid)
    {
        var ev = new GetThermalSignatureEvent();
        entities.EventBus.RaiseLocalEvent(grid, ref ev);
        return ev.Signature;
    }

    /// <summary>Whether a gun carries the lock effect.</summary>
    private static bool Sparking(IEntityManager entities, EntityUid gun)
    {
        var children = entities.GetComponent<TransformComponent>(gun).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (entities.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == "WGEffectShieldGunLock")
                return true;
        }
        return false;
    }

    private static bool Refused(IEntityManager entities, EntityUid gun, EntityUid user)
    {
        var ev = new AttemptShootEvent(user, null);
        entities.EventBus.RaiseLocalEvent(gun, ref ev);
        return ev.Cancelled;
    }
}
