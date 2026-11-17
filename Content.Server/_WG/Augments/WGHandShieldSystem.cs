using System.Numerics;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._WG.Augments;

/// <summary>
/// Marks a body as shielded while a hand shield is raised, stops shots and blows that come at it from in front, and
/// recharges lowered shields. Only one shield goes up at a time.
/// </summary>
public sealed class WGHandShieldSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGDeployableImplantSystem _deployables = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGHandShieldComponent, WGDeployAttemptEvent>(OnDeployAttempt);
        SubscribeLocalEvent<WGHandShieldUserComponent, ProjectileReflectAttemptEvent>(OnProjectile);
        SubscribeLocalEvent<WGHandShieldUserComponent, HitScanReflectAttemptEvent>(OnHitscan);
        // Ahead of Wolfmed's routing, which takes over the body's damage and cancels the event itself.
        SubscribeLocalEvent<WGHandShieldUserComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            before: [typeof(WoundDamageRoutingSystem)]);
    }

    private void OnDeployAttempt(Entity<WGHandShieldComponent> ent, ref WGDeployAttemptEvent args)
    {
        if (ent.Comp.Collapsed)
        {
            _popup.PopupEntity(Loc.GetString("wg-hand-shield-recharging"), args.Body, args.Body);
            args.Cancelled = true;
        }
        else if (TryComp<WGHandShieldUserComponent>(args.Body, out var user) && user.Part != ent.Owner)
        {
            _popup.PopupEntity(Loc.GetString("wg-hand-shield-other"), args.Body, args.Body);
            args.Cancelled = true;
        }
    }

    private void OnProjectile(Entity<WGHandShieldUserComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var travel = _physics.GetMapLinearVelocity(args.ProjUid) - _physics.GetMapLinearVelocity(ent);
        if (!TryBlock(ent, -travel, args.Component.Damage.GetTotal().Float(), true, out _))
            return;

        args.Cancelled = true;
        QueueDel(args.ProjUid);
    }

    private void OnHitscan(Entity<WGHandShieldUserComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected
            || !TryBlock(ent, -args.Direction, args.Damage?.GetTotal().Float() ?? 0f, true, out var shield))
            return;

        var spread = _random.NextAngle(-shield.DeflectSpread / 2, shield.DeflectSpread / 2);
        args.Direction = -spread.RotateVec(args.Direction);
        args.Reflected = true;
    }

    private void OnBeforeDamage(Entity<WGHandShieldUserComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // Only blows: a weapon swung or a fist thrown by someone else, not a bullet that already got past.
        if (args.Cancelled
            || args.Origin is not { } attacker
            || attacker == ent.Owner
            || args.Tool is not { } tool
            || !HasComp<MeleeWeaponComponent>(tool)
            || HasComp<ProjectileComponent>(tool))
            return;

        var damage = args.Damage.GetTotal().Float();
        var from = _transform.GetWorldPosition(attacker) - _transform.GetWorldPosition(ent);
        if (damage > 0 && TryBlock(ent, from, damage, false, out _))
            args.Cancelled = true;
    }

    /// <summary>
    /// Soaks a hit coming at the body from the given direction if the raised shield covers it, collapsing the shield
    /// if that drains it.
    /// </summary>
    private bool TryBlock(Entity<WGHandShieldUserComponent> ent, Vector2 from, float damage, bool shot,
        out WGHandShieldComponent shield)
    {
        shield = default!;
        if (!TryComp<WGHandShieldComponent>(ent.Comp.Part, out var found)
            || !Exists(found.Deployed)
            || from.LengthSquared() < 1e-6f)
            return false;

        shield = found;
        var facing = _transform.GetWorldRotation(ent).ToWorldVec();
        if (Vector2.Dot(facing, Vector2.Normalize(from)) < Math.Cos(shield.Arc.Theta / 2))
            return false;

        _audio.PlayPvs(shot ? shield.ShotSound : shield.BlowSound, ent, AudioParams.Default.WithVariation(0.05f));
        shield.Charge -= damage;
        if (shield.Charge <= 0)
            Collapse(ent.Comp.Part, shield, ent);

        return true;
    }

    private void Collapse(EntityUid part, WGHandShieldComponent shield, EntityUid body)
    {
        shield.Charge = 0;
        shield.Collapsed = true;
        _deployables.Stow(part, shield);
        Lower(part, shield);
        _audio.PlayPvs(shield.CollapseSound, body);
        _popup.PopupEntity(Loc.GetString("wg-hand-shield-collapse"), body, body, PopupType.MediumCaution);
        if (shield.ActionEntity is { } action)
            _actions.SetCooldown(action, TimeSpan.FromSeconds(shield.MaxCharge / shield.RechargeRate));
    }

    private void Raise(EntityUid part, WGHandShieldComponent shield, EntityUid body)
    {
        EnsureComp<WGHandShieldUserComponent>(body).Part = part;
        shield.RaisedBy = body;
    }

    private void Lower(EntityUid part, WGHandShieldComponent shield)
    {
        if (shield.RaisedBy is { } body
            && TryComp<WGHandShieldUserComponent>(body, out var user)
            && user.Part == part)
            RemCompDeferred(body, user);

        shield.RaisedBy = null;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WGHandShieldComponent, BodyPartComponent>();
        while (query.MoveNext(out var uid, out var shield, out var part))
        {
            var raisedBy = Exists(shield.Deployed) ? part.Body : null;
            if (raisedBy != shield.RaisedBy)
            {
                Lower(uid, shield);
                if (raisedBy is { } body)
                    Raise(uid, shield, body);
            }

            if (raisedBy != null || shield.Charge >= shield.MaxCharge)
                continue;

            shield.Charge = Math.Min(shield.MaxCharge, shield.Charge + shield.RechargeRate * frameTime);
            if (shield.Charge >= shield.MaxCharge)
                shield.Collapsed = false;
        }
    }
}
