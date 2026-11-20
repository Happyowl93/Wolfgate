using System.Numerics;
using Content.Server._WG.CargoLoad;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Detection;
using Content.Shared._WG.ShipModules;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipModules;

/// <summary>Runs the maneuvering jets module: a short burn along the direction its pilot holds.</summary>
public sealed partial class WGManeuveringJetsSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private WGCargoLoadSystem _cargo = default!;

    /// <summary>Ship-relative push of each linear thruster bucket, indexed as <see cref="ShuttleComponent.LinearThrusters"/>.</summary>
    private static readonly Vector2[] BucketPush = { -Vector2.UnitY, Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX };

    /// <summary>Thrust of a standard thruster; ring size goes with the square root of a thruster's thrust against it.</summary>
    private const float StandardThrust = 150f;

    private const int MaxPuffs = 48;

    private readonly HashSet<EntityUid> _docked = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGManeuveringJetsComponent, WGShipModuleLockEvent>(OnLock);
        SubscribeLocalEvent<WGManeuveringJetsComponent, WGShipModuleActivateAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGManeuveringJetsComponent, WGShipModuleActivatedEvent>(OnActivated);
    }

    private void OnLock(Entity<WGManeuveringJetsComponent> ent, ref WGShipModuleLockEvent args)
    {
        if (args.Lock != WGShipModuleLock.None)
            return;

        if (!TryComp<PhysicsComponent>(args.Grid, out var body) || body.BodyType == BodyType.Static)
        {
            args.Lock = WGShipModuleLock.Anchored;
            return;
        }

        // A burn would drag whatever the ship is docked to.
        _docked.Clear();
        _shuttle.GetAllDockedShuttlesIgnoringFTLLock(args.Grid, _docked);
        if (_docked.Count > 1)
            args.Lock = WGShipModuleLock.Docked;
    }

    private void OnAttempt(Entity<WGManeuveringJetsComponent> ent, ref WGShipModuleActivateAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var held = HeldDirection(args.User);
        if (held == Vector2.Zero)
        {
            args.Cancelled = true;
            args.Reason = "wg-ship-module-jets-no-direction";
            return;
        }

        ent.Comp.BurnDirection = _transform.GetWorldRotation(args.Grid).RotateVec(held);
    }

    private void OnActivated(Entity<WGManeuveringJetsComponent> ent, ref WGShipModuleActivatedEvent args)
    {
        if (!TryComp<PhysicsComponent>(args.Grid, out var body))
            return;

        ent.Comp.BurnLeft = ent.Comp.DeltaV * KickScale(ent.Comp, args.Grid, body);
        ent.Comp.BurnRate = ent.Comp.BurnLeft / MathF.Max(ent.Comp.BurnTime, 0.05f);
        EnsureComp<WGManeuveringJetsBurningComponent>(ent);

        if (TryComp<ThermalSignatureComponent>(args.Grid, out var heat))
            heat.StoredHeat += ent.Comp.Heat;

        Puff(args.Grid, ent.Comp);
    }

    /// <summary>Tells the map's clients which of the ship's thrusters push along the burn, for their plumes and smoke rings.</summary>
    private void Puff(EntityUid grid, WGManeuveringJetsComponent jets)
    {
        if (!TryComp<ShuttleComponent>(grid, out var shuttle))
            return;

        var local = (-_transform.GetWorldRotation(grid)).RotateVec(jets.BurnDirection);
        var ev = new WGManeuveringJetsPuffEvent { BurnTime = jets.BurnTime };
        for (var i = 0; i < BucketPush.Length; i++)
        {
            var strength = Vector2.Dot(local, BucketPush[i]);
            if (strength < 0.05f)
                continue;

            foreach (var thruster in shuttle.LinearThrusters[i])
            {
                if (ev.Puffs.Count >= MaxPuffs)
                    break;

                var thrust = TryComp<ThrusterComponent>(thruster, out var comp) ? comp.Thrust : StandardThrust;
                var size = Math.Clamp(MathF.Sqrt(thrust / StandardThrust), 0.5f, 3f);
                ev.Puffs.Add(new WGJetsPuff(GetNetEntity(thruster), size, strength));
            }
        }

        if (ev.Puffs.Count > 0)
            RaiseNetworkEvent(ev, Filter.BroadcastMap(Transform(grid).MapID));
    }

    /// <summary>Share of the rated kick a hull gets: less for a heavy hull and for a laden one.</summary>
    public float KickScale(WGManeuveringJetsComponent jets, EntityUid grid, PhysicsComponent body)
    {
        var mass = MathF.Max(body.Mass, jets.ReferenceMass);
        var scale = MathF.Max(MathF.Sqrt(jets.ReferenceMass / mass), jets.MinScale);
        return scale * _cargo.GetSpaceFactor(grid);
    }

    /// <summary>The ship-relative direction a pilot holds, normalized; zero with nothing held.</summary>
    private Vector2 HeldDirection(EntityUid? user)
    {
        if (!TryComp<PilotComponent>(user, out var pilot))
            return Vector2.Zero;

        var held = Vector2.Zero;
        if ((pilot.HeldButtons & ShuttleButtons.StrafeLeft) != 0)
            held.X -= 1f;
        if ((pilot.HeldButtons & ShuttleButtons.StrafeRight) != 0)
            held.X += 1f;
        if ((pilot.HeldButtons & ShuttleButtons.StrafeUp) != 0)
            held.Y += 1f;
        if ((pilot.HeldButtons & ShuttleButtons.StrafeDown) != 0)
            held.Y -= 1f;

        return held == Vector2.Zero ? held : Vector2.Normalize(held);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<WGManeuveringJetsBurningComponent, WGManeuveringJetsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var jets, out var xform))
        {
            if (!xform.Anchored
                || xform.GridUid is not { } grid
                || !TryComp<PhysicsComponent>(grid, out var body)
                || body.BodyType == BodyType.Static)
            {
                jets.BurnLeft = 0f;
                RemCompDeferred<WGManeuveringJetsBurningComponent>(uid);
                continue;
            }

            var step = MathF.Min(jets.BurnLeft, jets.BurnRate * frameTime);
            jets.BurnLeft -= step;
            _physics.ApplyLinearImpulse(grid, jets.BurnDirection * step * body.Mass, body: body);
            if (jets.BurnLeft <= 0f)
                RemCompDeferred<WGManeuveringJetsBurningComponent>(uid);
        }
    }
}

/// <summary>Marks a maneuvering jets module with a burn under way.</summary>
[RegisterComponent]
public sealed partial class WGManeuveringJetsBurningComponent : Component;
