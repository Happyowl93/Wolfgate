#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks ship heat: how it drains with the field up and down, and what firing through the field costs.</summary>
[TestFixture]
public sealed class WGShipHeatTest
{
    [Test]
    public async Task LoweredFieldShedsHeatFaster()
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
            Assert.That(emitter.UnpoweredBonus, Is.GreaterThan(1f));

            emitter.Damage = 20000f;
            Assert.That(system.GetWolfgateShipHeat(grid).Heat, Is.EqualTo(20000f), "What the generator absorbs is the ship's heat.");
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(hull.Heat, Is.EqualTo(20000f), "The heat is held by the hull.");
            system.Update(1.5f);
            var slow = 20000f - emitter.Damage;
            Assert.That(slow, Is.EqualTo(emitter.HealPerSecond * 1.5f).Within(0.01f), "Under a raised field heat drains at the slow rate.");
            Assert.That(system.GetWolfgateShipHeat(grid).Heat, Is.EqualTo(emitter.Damage));
            Assert.That(hull.Heat, Is.EqualTo(emitter.Damage), "The generator's cooling comes off the hull's heat.");

            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(emitter.Shield, Is.Null);
            var before = emitter.Damage;
            system.Update(1.5f);
            Assert.That(before - emitter.Damage, Is.EqualTo(slow * emitter.UnpoweredBonus).Within(0.01f),
                "A lowered field vents heat at the fast rate.");
            var stats = entities.System<WGShipShieldShuntSystem>().GetState(grid).Stats!;
            Assert.That(stats.Vents, Is.True);
            Assert.That(stats.RechargePerSecond, Is.EqualTo(emitter.HealPerSecond * emitter.UnpoweredBonus));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OutpostGeneratorKeepsItsCoolingRule()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorPOI", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            Assert.That(entities.HasComponent<WGShipShieldCostComponent>(emitterUid), Is.False);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            emitter.Damage = 20000f;
            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(emitter.Shield, Is.Null);
            system.Update(1.5f);
            Assert.That(20000f - emitter.Damage, Is.EqualTo(emitter.HealPerSecond * 1.5f).Within(0.01f),
                "A generator with no ship costs only cools fast in recharge mode.");
            Assert.That(entities.System<WGShipShieldShuntSystem>().GetState(grid).Stats!.Vents, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FiringThroughRaisedFieldHeatsShipAndHoldsGuns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var controls = entities.System<WGShipShieldShuntSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            var cost = entities.GetComponent<WGShipShieldCostComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            Assert.That(entities.HasComponent<FireControllableComponent>(gun), Is.True);
            Assert.That(cost.FireHeat, Is.GreaterThan(0f));
            var (round, damage) = Round(pair.Server.ResolveDependency<IPrototypeManager>(), entities.ComponentFactory);
            var cartridge = entities.SpawnEntity(round, map.GridCoords);

            Fire(entities, gun, user, cartridge);
            Assert.That(emitter.Damage, Is.Zero, "With no field up the guns fire without heating the ship.");

            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Fire(entities, gun, user, cartridge);
            Assert.That(emitter.Damage, Is.EqualTo(damage * cost.FireHeat).Within(0.01f),
                "A shot through the raised field heats the ship by a share of its damage.");
            Assert.That(Refused(entities, gun, user), Is.False);

            var capacity = system.GetWolfgateShipHeat(grid).Capacity;
            emitter.Damage = capacity * WGShipShieldEffects.GunHoldHeat - 1f;
            Fire(entities, gun, user, cartridge);
            Assert.That(emitter.Damage, Is.EqualTo(capacity * WGShipShieldEffects.GunHoldHeat), "A ship's own fire stops short of collapsing its field.");
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(Refused(entities, gun, user), Is.True, "Too hot, the guns hold fire.");
            Assert.That(controls.GetState(grid).GunsHeld, Is.True);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "A heat hold is not the collapse lock.");

            emitter.Damage = capacity * 0.9f;
            Assert.That(Refused(entities, gun, user), Is.True, "The guns stay held until the ship cools to the resume mark.");
            emitter.Damage = capacity * WGShipShieldEffects.GunResumeHeat - 1f;
            Assert.That(Refused(entities, gun, user), Is.False);
            Assert.That(controls.GetState(grid).GunsHeld, Is.False);

            emitter.Damage = capacity * WGShipShieldEffects.GunHoldHeat;
            Assert.That(Refused(entities, gun, user), Is.True);
            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(Refused(entities, gun, user), Is.False, "Lowering the field frees the guns at once.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeflectorFiresFreeAndBareHullHoldsItsOwnHeat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();

            Assert.That(system.GetWolfgateShipHeat(grid), Is.EqualTo((0f, WGShipHeatComponent.HullCapacity)));
            system.AddWolfgateShipHeat(grid, 1500f);
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(system.GetWolfgateShipHeat(grid), Is.EqualTo((1500f, hull.Capacity)), "A hull with no generator keeps its own heat.");
            system.Update(1f);
            // The hull cools once a second on a clock shared with earlier ticks, so up to two seconds may have passed.
            Assert.That(hull.Heat, Is.InRange(1500f - hull.Dissipation * 2f, 1500f - hull.Dissipation));
            system.AddWolfgateShipHeat(grid, hull.Capacity * 2f);
            Assert.That(hull.Heat, Is.EqualTo(hull.Capacity), "A bare hull never holds more than its capacity.");

            var emitterUid = entities.SpawnEntity("WGShieldGeneratorDeflector", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(emitter.Damage, Is.GreaterThan(hull.Capacity / 2f), "A generator installed on a hot hull takes on the hull's heat.");
            Assert.That(system.GetWolfgateShipHeat(grid).Heat, Is.EqualTo(emitter.Damage));
            Assert.That(hull.Heat, Is.EqualTo(emitter.Damage), "Reading the ship's heat carries the generator's cooling to the hull.");
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var (round, _) = Round(pair.Server.ResolveDependency<IPrototypeManager>(), entities.ComponentFactory);
            var before = emitter.Damage;
            Fire(entities, gun, user, entities.SpawnEntity(round, map.GridCoords));
            Assert.That(emitter.Damage, Is.EqualTo(before), "Firing through a deflector field adds no heat.");
            emitter.Damage = system.GetWolfgateShipHeat(grid).Capacity * 0.99f;
            Assert.That(Refused(entities, gun, user), Is.False, "A deflector never holds the guns.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GeneratorsShareTheHullsHeatAndOnlyOneCoolsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var activeUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var active = entities.GetComponent<ShipShieldEmitterComponent>(activeUid);
            entities.GetComponent<ApcPowerReceiverComponent>(activeUid).Powered = true;
            system.Update(1.5f);
            Assert.That(active.Shield, Is.Not.Null);
            var spareUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var spare = entities.GetComponent<ShipShieldEmitterComponent>(spareUid);
            entities.GetComponent<ApcPowerReceiverComponent>(spareUid).Powered = false;

            system.AddWolfgateShipHeat(grid, 20000f);
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(hull.Heat, Is.EqualTo(20000f));
            Assert.That(active.Damage, Is.EqualTo(20000f), "The generator holding the field reads the hull's heat.");
            system.Update(1.5f);
            system.Update(0.01f);
            Assert.That(hull.Heat, Is.EqualTo(20000f - active.HealPerSecond * 1.5f).Within(0.01f),
                "A spare generator is no second heat sink: only the one holding the field cools the hull.");
            Assert.That(active.Damage, Is.EqualTo(hull.Heat));
            Assert.That(spare.Damage, Is.EqualTo(hull.Heat), "Every generator on the hull reads the same heat.");

            entities.System<SharedTransformSystem>().Unanchor(spareUid, entities.GetComponent<TransformComponent>(spareUid));
            system.Update(0.01f);
            Assert.That(spare.Damage, Is.Zero, "A generator taken off the hull leaves the heat behind.");
            Assert.That(hull.Heat, Is.EqualTo(active.Damage));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A plain cartridge and the damage of the round it fires.</summary>
    private static (string Cartridge, float Damage) Round(IPrototypeManager prototypes, IComponentFactory factory)
    {
        foreach (var prototype in prototypes.EnumeratePrototypes<EntityPrototype>().OrderBy(p => p.ID))
        {
            if (prototype.Abstract || !prototype.TryGetComponent<CartridgeAmmoComponent>(out var cartridge, factory) ||
                !prototypes.TryIndex<EntityPrototype>(cartridge.Prototype, out var round) ||
                round.TryGetComponent<ExplosiveComponent>(out _, factory) || round.TryGetComponent<ProjectileSpreadComponent>(out _, factory) ||
                !round.TryGetComponent<ProjectileComponent>(out var projectile, factory))
                continue;
            var damage = (float) projectile.Damage.GetTotal();
            // Heavy enough that one shot's heat shows against a generator's capacity.
            if (damage >= 20f)
                return (prototype.ID, damage);
        }
        throw new InvalidOperationException("No plain cartridge prototype to fire.");
    }

    /// <summary>Reports one shot of a cartridge from a gun, as the gun system does after it fires.</summary>
    private static void Fire(IEntityManager entities, EntityUid gun, EntityUid user, EntityUid cartridge)
    {
        var ammo = new List<(EntityUid? Uid, IShootable Shootable)> { (cartridge, entities.GetComponent<CartridgeAmmoComponent>(cartridge)) };
        var ev = new GunShotEvent(user, ammo, entities.GetComponent<TransformComponent>(gun).Coordinates);
        entities.EventBus.RaiseLocalEvent(gun, ref ev);
    }

    private static bool Refused(IEntityManager entities, EntityUid gun, EntityUid user)
    {
        var ev = new AttemptShootEvent(user, null);
        entities.EventBus.RaiseLocalEvent(gun, ref ev);
        return ev.Cancelled;
    }
}
