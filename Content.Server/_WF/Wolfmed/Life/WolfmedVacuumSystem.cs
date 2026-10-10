using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Atmos;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// Who is out in hard vacuum with no pressure suit. The life tick drains an exposed brain on
/// wolfmed.brain_vacuum_seconds; the body is told once when it starts.
/// </summary>
// Barotrauma's own rule, read the same way: the containing mixture's pressure through the suit's protection against
// the low pressure hazard line. Its TakingDamage flag is not used, as it stays set once the damage cap is reached.
public sealed class WolfmedVacuumSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private BarotraumaSystem _barotrauma = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TellCooldown = TimeSpan.FromSeconds(30);

    private readonly List<(EntityUid Body, bool Exposed)> _read = new();
    private TimeSpan _nextTick;

    /// <summary>Whether the body felt hazardous low pressure at the last reading.</summary>
    public bool IsExposed(EntityUid body) => TryComp(body, out WolfmedVacuumComponent? comp) && comp.Exposed;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TickInterval;
        _read.Clear();
        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent, BarotraumaComponent>();
        while (query.MoveNext(out var uid, out _, out var barotrauma))
            _read.Add((uid, !_mobState.IsDead(uid) && FeelsVacuum(uid, barotrauma)));

        foreach (var (body, exposed) in _read)
        {
            if (!TerminatingOrDeleted(body))
                Set(body, exposed);
        }

        // A body that lost its BarotraumaComponent while exposed (a zombie's, a trader's) is not read above any more,
        // and would stay marked, and drained, for good.
        var marked = EntityQueryEnumerator<WolfmedVacuumComponent>();
        while (marked.MoveNext(out var uid, out var comp))
        {
            if (comp.Exposed && (!HasComp<BarotraumaComponent>(uid) || !HasComp<WolfmedConsciousnessComponent>(uid)))
                comp.Exposed = false;
        }
    }

    private bool FeelsVacuum(EntityUid body, BarotraumaComponent barotrauma)
    {
        if (barotrauma.HasImmunity)
            return false;

        var pressure = 1f;
        if (_atmosphere.GetContainingMixture(body) is { } mixture)
            pressure = MathF.Max(mixture.Pressure, 1f);

        return pressure <= Atmospherics.WarningLowPressure &&
               _barotrauma.GetFeltLowPressure(body, barotrauma, pressure) <= Atmospherics.HazardLowPressure;
    }

    private void Set(EntityUid body, bool exposed)
    {
        if (!exposed)
        {
            if (TryComp(body, out WolfmedVacuumComponent? clear))
                clear.Exposed = false;
            return;
        }

        var comp = EnsureComp<WolfmedVacuumComponent>(body);
        if (comp.Exposed)
            return;

        comp.Exposed = true;

        // Only a body with a brain to starve is told; a chassis runs no oxygen clock.
        if (_timing.CurTime < comp.NextTell || _cfg.GetCVar(WolfmedCVars.BrainVacuumSeconds) <= 0f ||
            _life.GetBrain(body) == null)
            return;

        comp.NextTell = _timing.CurTime + TellCooldown;
        _popup.PopupEntity(Loc.GetString("wolfmed-vacuum-exposed"), body, body, PopupType.LargeCaution);
    }
}
