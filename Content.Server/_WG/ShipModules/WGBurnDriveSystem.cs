using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server._WG.CargoLoad;
using Content.Shared._Mono.Detection;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipModules;

/// <summary>Runs the burn drive: drives the hull along its nose for the module's duration, heating it as it goes.</summary>
public sealed partial class WGBurnDriveSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private WGCargoLoadSystem _cargo = default!;
    [Dependency] private WGManeuveringJetsSystem _jets = default!;
    [Dependency] private WGShipModuleSystem _modules = default!;

    /// <summary>Seconds between the exhaust's bursts of plume and smoke rings.</summary>
    private const float PuffInterval = 1f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipModuleLockEvent>(OnLock);
        SubscribeLocalEvent<WGShipModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<WGShipModuleDeactivatedEvent>(OnDeactivated);
    }

    private void OnLock(ref WGShipModuleLockEvent args)
    {
        if (args.Lock == WGShipModuleLock.None && args.Module.Effect is WGBurnDriveEffect)
            args.Lock = _modules.MotionLock(args.Grid, false);
    }

    private void OnActivated(ref WGShipModuleActivatedEvent args)
    {
        if (args.Module.Effect is not WGBurnDriveEffect drive)
            return;

        var burn = EnsureComp<WGBurnDriveActiveComponent>(args.Grid);
        burn.Drive = drive;
        burn.NextPuff = TimeSpan.Zero;
    }

    private void OnDeactivated(ref WGShipModuleDeactivatedEvent args)
    {
        if (args.Module.Effect is WGBurnDriveEffect)
            RemCompDeferred<WGBurnDriveActiveComponent>(args.Grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGBurnDriveActiveComponent, PhysicsComponent>();
        while (query.MoveNext(out var grid, out var burn, out var body))
        {
            if (burn.Drive is not { } drive || body.BodyType == BodyType.Static)
                continue;

            var forward = _transform.GetWorldRotation(grid).RotateVec(Vector2.UnitY);
            var speed = Vector2.Dot(body.LinearVelocity, forward);
            var gain = MathF.Min(drive.Acceleration * Scale(drive, grid, body) * frameTime, drive.TopSpeed - speed);
            if (gain > 0f)
                _physics.ApplyLinearImpulse(grid, forward * gain * body.Mass, body: body);

            var (_, capacity) = _shields.GetWolfgateShipHeat(grid);
            _shields.AddWolfgateShipHeat(grid, capacity * drive.HeatPerSecond * frameTime, WGShipShieldEffects.GunHoldHeat);
            if (TryComp<ThermalSignatureComponent>(grid, out var signature))
                signature.StoredHeat += drive.Signature * frameTime;

            if (now < burn.NextPuff)
                continue;

            burn.NextPuff = now + TimeSpan.FromSeconds(PuffInterval);
            _jets.Puff(grid, forward, PuffInterval + 0.1f);
        }
    }

    /// <summary>Share of the rated push a hull gets: a fixed force, so less for a heavy hull, and less again for a laden one.</summary>
    public float Scale(WGBurnDriveEffect drive, EntityUid grid, PhysicsComponent body)
    {
        var mass = MathF.Max(body.Mass, drive.ReferenceMass);
        return MathF.Max(drive.ReferenceMass / mass, drive.MinScale) * _cargo.GetSpaceFactor(grid);
    }
}

/// <summary>A burn drive running on a ship's grid; the helm's own thrust and turning are cut while it is.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGBurnDriveActiveComponent : Component
{
    [ViewVariables]
    public WGBurnDriveEffect? Drive;

    /// <summary>When the exhaust next flares and puffs.</summary>
    [ViewVariables]
    public TimeSpan NextPuff;
}
