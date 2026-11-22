using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.ShipShields;

// A ship's heat lives on its hull (WGShipHeatComponent). A generator's damage is that heat as the upstream shield rules
// see it: whatever raises or sets the damage (absorbed hits, other systems) is carried to the hull, and every
// generator on the hull reads the same heat back. A generator sets how much the hull can hold and how fast it cools.
// Heat that came through a ship's shield is hard: it stays while the field is up and taking hits. The rest is soft
// and cools anyway; a ship's own guns add it with every shot, with or without a shield.
public sealed partial class ShipShieldsSystem
{
    [Dependency] private IComponentFactory _wgHeatFactory = default!;

    /// <summary>Shield damage of one shot of each projectile prototype.</summary>
    private readonly Dictionary<string, float> _wgShotDamage = new();

    /// <summary>Ships too hot to fire.</summary>
    private readonly HashSet<EntityUid> _wgHeatHeld = new();

    /// <summary>The hull each generator reads its heat from and the heat it last read, so a later change to its damage can be told apart.</summary>
    private readonly Dictionary<EntityUid, (EntityUid Grid, float Heat)> _wgHeatSinks = new();
    private readonly HashSet<EntityUid> _wgHeatSunkHulls = new();
    private readonly List<EntityUid> _wgHeatGone = new();
    private float _wgHullHeatAccumulator;

    private void InitializeWolfgateShipHeat()
    {
        SubscribeLocalEvent<FireControllableComponent, GunShotEvent>(OnWolfgateShipGunShot);
    }

    /// <summary>
    /// Carries what changed a generator's damage since the last call to its hull, then sets the damage to the hull's
    /// heat. A generator taken off its hull leaves the heat behind.
    /// </summary>
    private void SyncWolfgateGeneratorHeat(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        var transform = Transform(uid);
        if (!transform.Anchored || transform.GridUid is not { } grid)
        {
            if (_wgHeatSinks.Remove(uid))
                emitter.Damage = 0f;
            return;
        }
        if (!TryComp<WGShipHeatComponent>(grid, out var hull))
        {
            if (TerminatingOrDeleted(grid))
                return;
            hull = AddComp<WGShipHeatComponent>(grid);
        }
        var damage = float.IsFinite(emitter.Damage) ? MathF.Max(0f, emitter.Damage) : 0f;
        var ship = HasComp<WGShipShieldCostComponent>(uid);
        if (_wgHeatSinks.TryGetValue(uid, out var sink) && sink.Grid == grid)
        {
            var change = damage - sink.Heat;
            if (change > 0f)
            {
                hull.Heat += change;
                if (ship)
                    hull.Hard = MathF.Min(hull.Hard + change, hull.Heat);
                hull.LastHit = _wgShieldTiming.CurTime;
            }
            else if (change < 0f)
            {
                // Cooling is done on the hull, so the damage was set from outside, maybe on every generator here.
                hull.Heat = MathF.Min(hull.Heat, damage);
                hull.Hard = MathF.Min(hull.Hard, hull.Heat);
            }
        }
        else if (damage > hull.Heat)
        {
            // First seen on this hull: just built, loaded from a save, or carried over by a hull split. It brings its
            // heat without adding to what another generator already brought.
            if (ship)
                hull.Hard += damage - hull.Heat;
            hull.Heat = damage;
        }
        emitter.Damage = hull.Heat;
        _wgHeatSinks[uid] = (grid, hull.Heat);
    }

    /// <summary>
    /// Whether this generator is the one cooling its hull. Only one does, so a second generator is no extra heat sink:
    /// the one with the field up, and after the field drops the same one for as long as it stays installed.
    /// </summary>
    private bool IsWolfgateHullCooler(EntityUid uid, EntityUid grid, WGShipHeatComponent hull)
    {
        if (TryComp<ShipShieldedComponent>(grid, out var shielded) && shielded.Source is { } source && IsWolfgateShieldLive(shielded.Shield))
            hull.Cooler = source;
        else if (hull.Cooler is not { } cooler || Deleted(cooler) || !_wgHeatSinks.TryGetValue(cooler, out var sink) || sink.Grid != grid)
            hull.Cooler = uid;
        return hull.Cooler == uid;
    }

    /// <summary>
    /// Brings a hull's heat up to date and returns the generator that sets its capacity: the one with the field up,
    /// else the largest installed.
    /// </summary>
    private EntityUid? SyncWolfgateShipHeat(EntityUid grid)
    {
        if (TryComp<ShipShieldedComponent>(grid, out var shielded) && shielded.Source is { } source &&
            TryComp<ShipShieldEmitterComponent>(source, out var active))
        {
            SyncWolfgateGeneratorHeat(source, active);
            return source;
        }
        EntityUid? largest = null;
        var most = -1f;
        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var transform))
        {
            if (transform.GridUid != grid || !transform.Anchored)
                continue;
            SyncWolfgateGeneratorHeat(uid, emitter);
            var capacity = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
            if (capacity <= most)
                continue;
            largest = uid;
            most = capacity;
        }
        return largest;
    }

    /// <summary>A ship's heat and the most it can hold: its generator's capacity, or the bare hull's.</summary>
    public (float Heat, float Capacity) GetWolfgateShipHeat(EntityUid grid)
    {
        var generator = SyncWolfgateShipHeat(grid);
        var hull = CompOrNull<WGShipHeatComponent>(grid);
        if (generator is not { } uid)
            return (hull?.Heat ?? 0f, hull?.Capacity ?? WGShipHeatComponent.HullCapacity);
        var emitter = Comp<ShipShieldEmitterComponent>(uid);
        return (hull?.Heat ?? 0f,
            WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp));
    }

    /// <summary>The part of a ship's heat that came through its shield and stays while the field is up.</summary>
    public float GetWolfgateShipHardHeat(EntityUid grid)
    {
        SyncWolfgateShipHeat(grid);
        return CompOrNull<WGShipHeatComponent>(grid)?.Hard ?? 0f;
    }

    /// <summary>
    /// Heats a ship's hull. With <paramref name="limit"/> below 1 the heat stops at that share of capacity, so it can't
    /// collapse the field; a hull with no generator never goes past its capacity.
    /// </summary>
    public void AddWolfgateShipHeat(EntityUid grid, float amount, float limit = float.PositiveInfinity, bool hard = false)
    {
        if (!float.IsFinite(amount) || amount <= 0f || TerminatingOrDeleted(grid))
            return;
        var generator = SyncWolfgateShipHeat(grid);
        var hull = EnsureComp<WGShipHeatComponent>(grid);
        var most = float.PositiveInfinity;
        if (generator is not { } uid)
            most = hull.Capacity * MathF.Min(limit, 1f);
        else if (float.IsFinite(limit) && TryComp<ShipShieldEmitterComponent>(uid, out var emitter))
            most = limit * WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
        var heated = MathF.Max(hull.Heat, MathF.Min(hull.Heat + amount, most));
        if (hard)
            hull.Hard = MathF.Min(hull.Hard + heated - hull.Heat, heated);
        hull.Heat = heated;
        // The generators read the new heat at once, so the field's rules and the panels see it this tick.
        SyncWolfgateShipHeat(grid);
    }

    /// <summary>
    /// Does one generator tick of cooling on the hull and returns what the upstream rule may still take from the
    /// generator itself: nothing while it reads a hull. An outpost generator cools its hull by the upstream amount.
    /// </summary>
    private float CoolWolfgateHull(EntityUid uid, ShipShieldEmitterComponent emitter, float healed)
    {
        SyncWolfgateGeneratorHeat(uid, emitter);
        if (!_wgHeatSinks.TryGetValue(uid, out var sink) || !TryComp<WGShipHeatComponent>(sink.Grid, out var hull))
            return healed;
        if (IsWolfgateHullCooler(uid, sink.Grid, hull))
        {
            if (HasComp<WGShipShieldCostComponent>(uid))
                CoolWolfgateShip(sink.Grid, hull, emitter.HealPerSecond * EmitterUpdateRate, emitter.UnpoweredBonus);
            else
                CoolWolfgateShip(sink.Grid, hull, healed, 1f, false);
        }
        emitter.Damage = hull.Heat;
        _wgHeatSinks[uid] = (sink.Grid, hull.Heat);
        return 0f;
    }

    /// <summary>
    /// Cools a hull by a ship's rules: under a raised field that was hit lately only soft heat goes, otherwise all of
    /// it does, and a vent multiplies the rate until no heat is left.
    /// </summary>
    private void CoolWolfgateShip(EntityUid grid, WGShipHeatComponent hull, float amount, float ventBonus, bool keepsHard = true)
    {
        if (hull.Venting)
            amount *= WolfgateVentBonus(ventBonus);
        var most = keepsHard && _wgShieldTiming.CurTime - hull.LastHit < TimeSpan.FromSeconds(WGShipShieldEffects.HardHeatHold) &&
            TryComp<ShipShieldedComponent>(grid, out var shielded) && IsWolfgateShieldLive(shielded.Shield)
            ? hull.Heat - hull.Hard : hull.Heat;
        if (float.IsFinite(amount) && amount > 0f && most > 0f)
            hull.Heat = MathF.Max(0f, hull.Heat - MathF.Min(amount, most));
        hull.Hard = Math.Clamp(hull.Hard, 0f, hull.Heat);
        if (hull.Venting && hull.Heat <= 0f)
            StopWolfgateVent(grid, hull);
    }

    /// <summary>Whether a hull's guns heat it and hold when it is hot: every hull but one an outpost generator rules.</summary>
    private bool HasWolfgateShipHeatRules(EntityUid grid)
    {
        return SyncWolfgateShipHeat(grid) is not { } generator || HasComp<WGShipShieldCostComponent>(generator);
    }

    /// <summary>Whether a ship is too hot to fire, with or without a shield. It holds from the hold mark down to the resume mark.</summary>
    public bool IsWolfgateShipHeatHeld(EntityUid grid)
    {
        // A hull nothing has heated yet has no heat to hold for.
        if (!HasComp<WGShipHeatComponent>(grid) || !HasWolfgateShipHeatRules(grid))
        {
            _wgHeatHeld.Remove(grid);
            return false;
        }
        var (heat, capacity) = GetWolfgateShipHeat(grid);
        if (capacity <= 0f || heat < capacity * WGShipShieldEffects.GunResumeHeat)
            _wgHeatHeld.Remove(grid);
        else if (heat >= capacity * WGShipShieldEffects.GunHoldHeat)
            _wgHeatHeld.Add(grid);
        return _wgHeatHeld.Contains(grid);
    }

    /// <summary>A ship gun heats its hull by a share of each shot's damage, whether or not the ship has a shield.</summary>
    private void OnWolfgateShipGunShot(Entity<FireControllableComponent> gun, ref GunShotEvent args)
    {
        var transform = Transform(gun);
        if (!transform.Anchored || transform.GridUid is not { } grid || TerminatingOrDeleted(grid) || !HasWolfgateShipHeatRules(grid))
            return;
        var hull = EnsureComp<WGShipHeatComponent>(grid);
        if (hull.FireHeat <= 0f)
            return;
        var damage = 0f;
        foreach (var (uid, shootable) in args.Ammo)
            damage += WolfgateShotDamage(gun, uid, shootable);
        // It stops at the hold mark, so a ship's own fire never collapses its field.
        AddWolfgateShipHeat(grid, damage * hull.FireHeat, WGShipShieldEffects.GunHoldHeat);
    }

    /// <summary>What one shot would cost a shield that absorbed it.</summary>
    private float WolfgateShotDamage(EntityUid gun, EntityUid? uid, IShootable shootable)
    {
        if (shootable is CartridgeAmmoComponent cartridge)
            return WolfgatePrototypeShotDamage(cartridge.Prototype);
        // A beam's entity is deleted before the shot is reported, so its damage is read from the gun's battery.
        if (shootable is HitscanAmmoComponent)
        {
            return TryComp<HitscanBatteryAmmoProviderComponent>(gun, out var battery)
                ? WolfgatePrototypeShotDamage(battery.HitscanEntityProto) * _wgHitscanDamage.UniversalHitscanDamageModifier : 0f;
        }
        return uid is { } shot && !Deleted(shot) && MetaData(shot).EntityPrototype is { } prototype
            ? WolfgatePrototypeShotDamage(prototype.ID) : 0f;
    }

    /// <summary>Projectile, beam and explosive damage of one shot of a prototype, times its pellets.</summary>
    private float WolfgatePrototypeShotDamage(string id)
    {
        if (_wgShotDamage.TryGetValue(id, out var cached))
            return cached;
        var damage = 0f;
        if (_prototypeManager.TryIndex<EntityPrototype>(id, out var prototype))
        {
            if (prototype.TryGetComponent<ProjectileComponent>(out var projectile, _wgHeatFactory))
                damage += (float) projectile.Damage.GetTotal();
            if (prototype.TryGetComponent<HitscanBasicDamageComponent>(out var beam, _wgHeatFactory))
                damage += (float) beam.Damage.GetTotal();
            if (prototype.TryGetComponent<ExplosiveComponent>(out var explosive, _wgHeatFactory) &&
                _prototypeManager.TryIndex(explosive.ExplosionType, out var type))
                damage += explosive.TotalIntensity * (float) type.DamagePerIntensity.GetTotal();
            if (prototype.TryGetComponent<ProjectileSpreadComponent>(out var spread, _wgHeatFactory))
                damage *= Math.Max(1, spread.Count);
        }
        damage = float.IsFinite(damage) ? MathF.Max(0f, damage) : 0f;
        _wgShotDamage[id] = damage;
        return damage;
    }

    /// <summary>Cools hulls with no generator at their own rate; a generator does the cooling for the hull it sits on.</summary>
    private void UpdateWolfgateHullHeat(float frameTime)
    {
        _wgHullHeatAccumulator += frameTime;
        if (_wgHullHeatAccumulator < 1f)
            return;
        var elapsed = _wgHullHeatAccumulator;
        _wgHullHeatAccumulator = 0f;
        _wgHeatSunkHulls.Clear();
        foreach (var (uid, sink) in _wgHeatSinks)
        {
            if (Deleted(uid))
                _wgHeatGone.Add(uid);
            else
                _wgHeatSunkHulls.Add(sink.Grid);
        }
        foreach (var uid in _wgHeatGone)
            _wgHeatSinks.Remove(uid);
        _wgHeatGone.Clear();
        var query = EntityQueryEnumerator<WGShipHeatComponent>();
        while (query.MoveNext(out var grid, out var hull))
        {
            if ((hull.Heat > 0f || hull.Venting) && !_wgHeatSunkHulls.Contains(grid))
                CoolWolfgateShip(grid, hull, hull.Dissipation * elapsed, hull.VentBonus);
        }
        UpdateWolfgateNpcVents();
    }
}
