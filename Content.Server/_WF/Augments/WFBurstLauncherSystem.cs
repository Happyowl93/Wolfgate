using Content.Server.Administration.Logs;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._WF.Augments;
using Content.Shared.Actions;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server._WF.Augments;

/// <summary>
/// Gives a body with a burst launcher its action, fires the ring of pellets when it is used, and brings spent bursts
/// back one at a time.
/// </summary>
public sealed partial class WFBurstLauncherSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subscribe<WFHaloBurstComponent>();
        Subscribe<WFHaloStunComponent>();
    }

    private void Subscribe<T>() where T : WFBurstLauncherComponent
    {
        SubscribeLocalEvent<T, ComponentInit>((uid, comp, _) =>
            _actions.AddAction(uid, ref comp.ActionEntity, comp.Action));
        SubscribeLocalEvent<T, ComponentShutdown>((_, comp, _) =>
        {
            QueueDel(comp.ActionEntity);
            comp.ActionEntity = null;
        });
        SubscribeLocalEvent<T, WFFireBurstLauncherEvent>((uid, comp, args) => OnFire((uid, comp), args));
    }

    private void OnFire(Entity<WFBurstLauncherComponent> ent, WFFireBurstLauncherEvent args)
    {
        // Both models answer the same event; each fires only for its own action.
        if (args.Handled || args.Action.Owner != ent.Comp.ActionEntity)
            return;

        // Left unhandled, the action keeps its charge.
        if (_timing.CurTime < ent.Comp.NextBurst)
        {
            _popup.PopupEntity(Loc.GetString("wf-burst-launcher-cycling"), ent, ent);
            return;
        }

        args.Handled = true;
        ent.Comp.NextBurst = _timing.CurTime + ent.Comp.Spacing;
        Fire(ent);
    }

    /// <summary>Fires one ring of pellets from the body, spread evenly with a little jitter.</summary>
    public void Fire(Entity<WFBurstLauncherComponent> ent)
    {
        var origin = _transform.GetMapCoordinates(ent);
        var segment = 360f / Math.Max(ent.Comp.Pellets, 1);
        for (var i = 0; i < ent.Comp.Pellets; i++)
        {
            var angle = Angle.FromDegrees(segment * (i + _random.NextFloat()));
            var pellet = Spawn(ent.Comp.Pellet, origin);
            // The body is the shooter, so its own pellets pass through it.
            _gun.ShootProjectile(pellet, angle.ToVec(), Vector2.Zero, null, ent, ent.Comp.Speed);
        }

        _audio.PlayPvs(ent.Comp.Sound, ent);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(ent):user} fired a burst launcher ({ent.Comp.Pellet})");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var burst = EntityQueryEnumerator<WFHaloBurstComponent>();
        while (burst.MoveNext(out var comp))
        {
            Recharge(comp, now);
        }

        var stun = EntityQueryEnumerator<WFHaloStunComponent>();
        while (stun.MoveNext(out var comp))
        {
            Recharge(comp, now);
        }
    }

    /// <summary>Brings one spent burst back each <see cref="WFBurstLauncherComponent.RechargeTime"/> until it is full.</summary>
    private void Recharge(WFBurstLauncherComponent comp, TimeSpan now)
    {
        if (!_actions.TryGetActionData(comp.ActionEntity, out var action, logError: false)
            || action.Charges is not { } charges
            || action.MaxCharges is not { } max)
            return;

        if (charges >= max)
        {
            comp.NextCharge = null;
            return;
        }

        if (comp.NextCharge is not { } next)
        {
            comp.NextCharge = now + comp.RechargeTime;
            return;
        }

        if (now < next)
            return;

        _actions.AddCharges(comp.ActionEntity, 1);
        // An empty action switches itself off.
        _actions.SetEnabled(comp.ActionEntity, true);
        comp.NextCharge = charges + 1 < max ? now + comp.RechargeTime : null;
    }
}
