using Content.Server.Shuttles.Components;
using Content.Shared._WG.ShipModules;
using Content.Shared.Atmos.Piping.Unary.Components;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Runs chemical fuel conversion: the hull's thrusters push harder (<see cref="WGShipModuleThrustSystem"/>) and burn
/// gas from canisters anchored aboard while they fire. With none left they flame out to a fraction of their thrust.
/// </summary>
public sealed partial class WGChemicalThrustSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;

    /// <summary>Moles a full standard canister holds, for the helm's reading.</summary>
    public const float FullCanister = 2769.36f;

    private static readonly TimeSpan BurnInterval = TimeSpan.FromSeconds(0.25);

    private readonly List<GasCanisterComponent> _tanks = new();
    private TimeSpan _nextBurn;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<WGShipModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<WGShipModuleGaugeEvent>(OnGauge);
    }

    private void OnActivated(ref WGShipModuleActivatedEvent args)
    {
        if (args.Module.Effect is not WGChemicalThrustEffect effect)
            return;

        var comp = EnsureComp<WGChemicalThrustComponent>(args.Grid);
        comp.ThrustFactor = effect.ThrustFactor;
        comp.TurnFactor = effect.TurnFactor;
        comp.EmptyFactor = effect.EmptyFactor;
        comp.Gas = effect.Gas;
        comp.MolesPerThrust = effect.MolesPerThrust;
        comp.FlameoutSound = effect.FlameoutSound;
        comp.RelightSound = effect.RelightSound;
        // A freshly loaded ship shows its fuel at once rather than flaming out and relighting.
        Refuel(args.Grid, comp, 0f, false);
    }

    private void OnDeactivated(ref WGShipModuleDeactivatedEvent args)
    {
        if (args.Module.Effect is WGChemicalThrustEffect)
            RemCompDeferred<WGChemicalThrustComponent>(args.Grid);
    }

    private void OnGauge(ref WGShipModuleGaugeEvent args)
    {
        if (args.Module.Effect is WGChemicalThrustEffect && TryComp<WGChemicalThrustComponent>(args.Grid, out var comp))
            args.Gauge = comp.Reserve;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextBurn)
            return;

        _nextBurn = now + BurnInterval;
        var seconds = (float) BurnInterval.TotalSeconds;
        var query = EntityQueryEnumerator<WGChemicalThrustComponent, ShuttleComponent>();
        while (query.MoveNext(out var grid, out var comp, out var shuttle))
        {
            Refuel(grid, comp, FiringThrust(shuttle) * comp.MolesPerThrust * seconds, true);
        }
    }

    /// <summary>Burns <paramref name="moles"/> of fuel from the hull's canisters and updates whether it has any left.</summary>
    public void Refuel(EntityUid grid, WGChemicalThrustComponent comp, float moles, bool audible)
    {
        GatherTanks(grid);
        var available = 0f;
        foreach (var tank in _tanks)
        {
            available += tank.Air.GetMoles(comp.Gas);
        }

        if (moles > 0f && available > 0f)
        {
            // Each canister gives its share, so they run dry together.
            var share = MathF.Min(1f, moles / available);
            foreach (var tank in _tanks)
            {
                tank.Air.AdjustMoles(comp.Gas, -tank.Air.GetMoles(comp.Gas) * share);
            }

            available = MathF.Max(0f, available - moles);
        }

        var capacity = _tanks.Count * FullCanister;
        comp.Reserve = capacity > 0f ? Math.Clamp(available / capacity, 0f, 1f) : 0f;
        var fueled = available > 0.01f;
        if (fueled == comp.Fueled)
            return;

        comp.Fueled = fueled;
        Dirty(grid, comp);
        if (audible)
            _audio.PlayGlobal(fueled ? comp.RelightSound : comp.FlameoutSound, Filter.BroadcastGrid(grid), true);
    }

    /// <summary>Thrust of the hull's thrusters firing now, linear and turning.</summary>
    private float FiringThrust(ShuttleComponent shuttle)
    {
        var total = 0f;
        foreach (var bucket in shuttle.LinearThrusters)
        {
            foreach (var uid in bucket)
            {
                total += Firing(uid);
            }
        }

        foreach (var uid in shuttle.AngularThrusters)
        {
            total += Firing(uid);
        }

        return total;
    }

    private float Firing(EntityUid uid)
    {
        return TryComp<ThrusterComponent>(uid, out var thruster) && thruster.IsOn && thruster.Firing
            ? MathF.Max(thruster.Thrust, 0f)
            : 0f;
    }

    /// <summary>Lists the canisters anchored on one hull.</summary>
    private void GatherTanks(EntityUid grid)
    {
        _tanks.Clear();
        var canisters = EntityQueryEnumerator<GasCanisterComponent, TransformComponent>();
        while (canisters.MoveNext(out _, out var canister, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            _tanks.Add(canister);
        }
    }
}
