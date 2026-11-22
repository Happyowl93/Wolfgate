using System.Linq;
using System.Numerics;
using Content.Server._Mono.Detection;
using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipShields;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Detection;
using Content.Shared._Mono.FireControl;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Spawners;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private SharedPopupSystem _wgShieldPopup = default!;
    [Dependency] private SharedUserInterfaceSystem _wgShieldUi = default!;

    /// <summary>Lowest share of thrust or turning a raised field can leave a ship.</summary>
    private const float MinShieldMobility = 0.1f;

    /// <summary>When each ship's guns unlock after its field was beaten down.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _wgGunLocks = new();

    private void InitializeWolfgateShieldCosts()
    {
        SubscribeLocalEvent<ShipShieldedComponent, GetThermalSignatureEvent>(OnWolfgateShieldSignature);
        SubscribeLocalEvent<FireControllableComponent, AttemptShootEvent>(OnWolfgateLockedGunShoot);
        InitializeWolfgateShipHeat();
    }

    /// <summary>The cost component of the generator holding a ship's raised field, if it has one.</summary>
    private WGShipShieldCostComponent? GetWolfgateRaisedCost(EntityUid grid)
    {
        return TryComp<ShipShieldedComponent>(grid, out var shielded) && shielded.Source is { } source &&
            IsWolfgateShieldLive(shielded.Shield) && TryComp<WGShipShieldCostComponent>(source, out var cost)
            ? cost : null;
    }

    /// <summary>Share of linear thrust a ship keeps under its raised field; 1 with the field down.</summary>
    public float GetWolfgateShieldThrustFactor(EntityUid grid)
    {
        return GetWolfgateRaisedCost(grid) is { } cost ? Math.Clamp(cost.ThrustFactor, MinShieldMobility, 1f) : 1f;
    }

    /// <summary>Share of turning thrust a ship keeps under its raised field; 1 with the field down.</summary>
    public float GetWolfgateShieldTurnFactor(EntityUid grid)
    {
        return GetWolfgateRaisedCost(grid) is { } cost ? Math.Clamp(cost.TurnFactor, MinShieldMobility, 1f) : 1f;
    }

    private void OnWolfgateShieldSignature(Entity<ShipShieldedComponent> grid, ref GetThermalSignatureEvent args)
    {
        if (GetWolfgateRaisedCost(grid) is { } cost)
            args.Signature += MathF.Max(0f, cost.Signature);
    }

    /// <summary>Flares the protected ship's thermal signature for damage its field absorbed.</summary>
    private void AddWolfgateShieldHitHeat(ShipShieldComponent shield, float damage)
    {
        if (shield.Source is { } source && TryComp<WGShipShieldCostComponent>(source, out var cost) && cost.HitSignature > 0f &&
            TryComp<ThermalSignatureComponent>(shield.Shielded, out var heat))
            heat.StoredHeat += damage * cost.HitSignature;
    }

    /// <summary>Locks a ship's guns after damage beat its field down, sets them sparking, and tells whoever is at its gunnery consoles.</summary>
    private void LockWolfgateShieldGuns(EntityUid grid, EntityUid emitter)
    {
        if (!TryComp<WGShipShieldCostComponent>(emitter, out var cost) || cost.GunLockout <= 0f)
            return;
        var now = _wgShieldTiming.CurTime;
        foreach (var expired in _wgGunLocks.Where(l => l.Value <= now).Select(l => l.Key).ToArray())
            _wgGunLocks.Remove(expired);
        _wgGunLocks[grid] = now + TimeSpan.FromSeconds(cost.GunLockout);

        if (cost.GunLockEffect is { } effect)
        {
            var guns = EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
            while (guns.MoveNext(out var gun, out _, out var gunTransform))
            {
                if (gunTransform.GridUid != grid || !gunTransform.Anchored)
                    continue;
                var sparks = Spawn(effect, new EntityCoordinates(gun, Vector2.Zero));
                EnsureComp<TimedDespawnComponent>(sparks).Lifetime = cost.GunLockout;
            }
        }

        var message = Loc.GetString("wg-shield-gun-lock-popup", ("seconds", MathF.Round(cost.GunLockout, 1)));
        var consoles = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid != grid)
                continue;
            foreach (var actor in _wgShieldUi.GetActors(uid, FireControlConsoleUiKey.Key))
                _wgShieldPopup.PopupEntity(message, uid, actor, PopupType.LargeCaution);
        }

        // Pilots fire from the helm in the cockpit view, so they are told too.
        var helms = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (helms.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid != grid)
                continue;
            foreach (var actor in _wgShieldUi.GetActors(uid, ShuttleConsoleUiKey.Key))
                _wgShieldPopup.PopupEntity(message, uid, actor, PopupType.LargeCaution);
        }
    }

    /// <summary>Whole seconds until a ship's guns unlock after a field collapse; 0 when they are free.</summary>
    public int GetWolfgateGunLockSeconds(EntityUid grid)
    {
        if (!_wgGunLocks.TryGetValue(grid, out var until))
            return 0;
        var left = until - _wgShieldTiming.CurTime;
        if (left > TimeSpan.Zero)
            return (int) Math.Ceiling(left.TotalSeconds);
        _wgGunLocks.Remove(grid);
        return 0;
    }

    private void OnWolfgateLockedGunShoot(Entity<FireControllableComponent> gun, ref AttemptShootEvent args)
    {
        if (args.Cancelled || Transform(gun).GridUid is not { } grid)
            return;
        if (_wgGunLocks.Count > 0 && GetWolfgateGunLockSeconds(grid) > 0 || IsWolfgateShipHeatHeld(grid))
            args.Cancelled = true;
    }
}
