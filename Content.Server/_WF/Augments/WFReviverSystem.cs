using Content.Server._WF.Wolfmed.Life;
using Content.Shared._WF.Augments;
using Content.Shared.Emp;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Augments;

/// <summary>
/// Shocks a body with a reviver when its heart has stopped. Every shock is one of Wolfmed's: it is refused for what
/// the paddles are refused for, and restarts the heart on the paddles' odds.
/// </summary>
public sealed class WFReviverSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedRevivalSystem _revival = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFReviverComponent, EmpPulseEvent>(OnEmp);
    }

    private void OnEmp(Entity<WFReviverComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        ent.Comp.NextShock = null;
        var until = _timing.CurTime + ent.Comp.EmpLockout;
        if (ent.Comp.ReadyAt < until)
            ent.Comp.ReadyAt = until;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        var query = EntityQueryEnumerator<WFReviverComponent>();
        while (query.MoveNext(out var uid, out var reviver))
        {
            Tick((uid, reviver));
        }
    }

    /// <summary>One look at the body: starts charging when its heart has stopped, and shocks when the charge is up.</summary>
    public void Tick(Entity<WFReviverComponent> ent)
    {
        var reviver = ent.Comp;
        var now = _timing.CurTime;
        if (!_revival.OwnsRevival(ent) || !_life.InArrest(ent) && !_mobState.IsDead(ent))
        {
            reviver.NextShock = null;
            reviver.ShocksGiven = 0;
            return;
        }

        // It doesn't charge for a body no shock would help: no blood to move, too cold, rotten.
        if (now < reviver.ReadyAt || _revival.GetRefusal(ent) != null)
        {
            reviver.NextShock = null;
            return;
        }

        if (reviver.NextShock == null)
        {
            reviver.NextShock = now + (reviver.ShocksGiven == 0 ? reviver.ChargeTime : reviver.RetryDelay);
            if (reviver.ShocksGiven == 0)
            {
                _audio.PlayPvs(reviver.ChargeSound, ent);
                _popup.PopupEntity(Loc.GetString("wf-augment-reviver-charging"), ent, ent, PopupType.Medium);
            }

            return;
        }

        if (now < reviver.NextShock)
            return;

        reviver.NextShock = null;
        reviver.ShocksGiven++;
        var revived = _revival.TryDefibrillate(ent, out _);
        _audio.PlayPvs(reviver.ZapSound, ent);
        _audio.PlayPvs(revived ? reviver.SuccessSound : reviver.FailureSound, ent);
        _popup.PopupEntity(Loc.GetString("wf-augment-reviver-shock", ("target", Identity.Entity(ent, EntityManager))), ent, PopupType.MediumCaution);

        if (revived)
            _revival.OfferReturn(ent);

        if (revived || reviver.ShocksGiven >= reviver.Shocks)
        {
            reviver.ShocksGiven = 0;
            reviver.ReadyAt = now + reviver.Recharge;
        }
    }
}
