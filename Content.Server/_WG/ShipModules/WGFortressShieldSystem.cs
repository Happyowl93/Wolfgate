using Content.Server._Crescent.ShipShields;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Runs the fortress shield: while it is on, the ship's raised field is hardened
/// (<see cref="WGShipShieldHardenedComponent"/>), its guns are silent and it builds hard heat. It switches off once the
/// field is down.
/// </summary>
public sealed partial class WGFortressShieldSystem : EntitySystem
{
    [Dependency] private ShipShieldsSystem _shields = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipModuleLockEvent>(OnLock);
        SubscribeLocalEvent<WGShipModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<WGShipModuleDeactivatedEvent>(OnDeactivated);
    }

    private void OnLock(ref WGShipModuleLockEvent args)
    {
        if (args.Lock == WGShipModuleLock.None && args.Module.Effect is WGFortressShieldEffect
            && !_shields.IsWolfgateShipFieldRaised(args.Grid))
            args.Lock = WGShipModuleLock.NoField;
    }

    private void OnActivated(ref WGShipModuleActivatedEvent args)
    {
        if (args.Module.Effect is not WGFortressShieldEffect fortress)
            return;

        var hardened = EnsureComp<WGShipShieldHardenedComponent>(args.Grid);
        hardened.HitFactor = fortress.HitFactor;
        Dirty(args.Grid, hardened);
        EnsureComp<WGFortressShieldActiveComponent>(args.Grid).HeatPerSecond = fortress.HeatPerSecond;
    }

    private void OnDeactivated(ref WGShipModuleDeactivatedEvent args)
    {
        if (args.Module.Effect is not WGFortressShieldEffect)
            return;

        RemCompDeferred<WGShipShieldHardenedComponent>(args.Grid);
        RemCompDeferred<WGFortressShieldActiveComponent>(args.Grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<WGFortressShieldActiveComponent>();
        while (query.MoveNext(out var grid, out var fortress))
        {
            // Past capacity the field overloads, which ends the fortress at the next sweep.
            var (_, capacity) = _shields.GetWolfgateShipHeat(grid);
            _shields.AddWolfgateShipHeat(grid, capacity * fortress.HeatPerSecond * frameTime, hard: true);
        }
    }
}

/// <summary>A fortress shield switched on on a ship's grid.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGFortressShieldActiveComponent : Component
{
    /// <summary>Share of the ship's heat capacity added each second, as hard heat.</summary>
    [ViewVariables]
    public float HeatPerSecond;
}
