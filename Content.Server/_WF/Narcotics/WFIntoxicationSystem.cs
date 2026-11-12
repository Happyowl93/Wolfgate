using Content.Server._WF.Lattice;
using Content.Shared._WF.Narcotics;
using Content.Shared.EntityEffects;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Narcotics;

/// <summary>
/// Ticks drug levels once a second: reads the blood, decays fed level, applies the tier each reaches, and starts and
/// ends crashes.
/// </summary>
public sealed partial class WFIntoxicationSystem : SharedWFIntoxicationSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private WFDriftEffectsSystem _drift = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Seconds between ticks.</summary>
    public const float TickSeconds = 1f;

    /// <summary>How long a tier's status effects outlast the tick that set them.</summary>
    private static readonly TimeSpan StatusTime = TimeSpan.FromSeconds(TickSeconds * 2.5f);

    private TimeSpan _nextTick;
    private readonly List<ProtoId<WFDrugProfilePrototype>> _profiles = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(TickSeconds);
        Tick(TickSeconds);
    }

    /// <summary>Advances every intoxicated body by <paramref name="seconds"/>.</summary>
    public void Tick(float seconds)
    {
        var query = EntityQueryEnumerator<WFIntoxicationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            TickBody((uid, comp), seconds);
        }
    }

    private void TickBody(Entity<WFIntoxicationComponent> ent, float seconds)
    {
        var (uid, comp) = ent;
        var speedBefore = GetSpeed(comp);
        var args = new EntityEffectBaseArgs(uid, EntityManager);

        var blood = ReadBlood(uid);
        _profiles.Clear();
        _profiles.AddRange(comp.Levels.Keys);
        foreach (var profile in _blood.Keys)
        {
            if (!comp.Levels.ContainsKey(profile))
                _profiles.Add(profile);
        }

        foreach (var profile in _profiles)
        {
            if (!Proto.TryIndex(profile, out var proto))
            {
                comp.Levels.Remove(profile);
                comp.Fed.Remove(profile);
                continue;
            }

            // Tolerance speeds the fed level's decay and blunts the blood's.
            var tolerance = ToleranceFactor(uid, proto.Family);
            var fed = Decayed(proto, comp.Fed.GetValueOrDefault(profile), tolerance * seconds);
            var fromBlood = _blood.GetValueOrDefault(profile) / tolerance;
            var level = fed + fromBlood;
            var tier = proto.TierAt(level);

            if (fed > 0f)
                comp.Fed[profile] = fed;
            else
                comp.Fed.Remove(profile);

            if (blood is { } soln)
                Flush(soln, profile, proto, level, fromBlood, seconds);
            AnnouncePhase(ent, profile, proto, tier);

            if (tier >= 0)
            {
                comp.TimeAbove[profile] = comp.TimeAbove.GetValueOrDefault(profile) + seconds;
                Apply(uid, args, proto.Tiers[tier].Effects, proto.Tiers[tier].StatusEffects);
                Hallucinate(uid, proto.Tiers[tier]);
            }
            else if (comp.TimeAbove.Remove(profile, out var above) && proto.Crash is { } crash && above >= crash.After)
            {
                // A redose paused any crash still running; the new one adds to it.
                var duration = comp.Crashes.GetValueOrDefault(profile) + crash.Duration.TotalSeconds + crash.PerSecondAbove * above;
                comp.Crashes[profile] = (float) Math.Min(duration, crash.MaxDuration.TotalSeconds);
                var ev = new WFDrugCrashEvent(profile, true);
                RaiseLocalEvent(uid, ref ev);
            }

            if (level <= 0f)
                comp.Levels.Remove(profile);
            else
                comp.Levels[profile] = level;
        }

        _profiles.Clear();
        _profiles.AddRange(comp.Crashes.Keys);
        foreach (var profile in _profiles)
        {
            if (IsHigh(comp, profile))
                continue;

            var left = comp.Crashes[profile] - seconds;
            if (left <= 0f || !Proto.TryIndex(profile, out var proto) || proto.Crash == null)
            {
                comp.Crashes.Remove(profile);
                var ev = new WFDrugCrashEvent(profile, false);
                RaiseLocalEvent(uid, ref ev);
                continue;
            }

            comp.Crashes[profile] = left;
            Apply(uid, args, proto.Crash.Effects, proto.Crash.StatusEffects);
        }

        UpdateTraits(ent, seconds);

        if (!MathHelper.CloseTo(GetSpeed(comp), speedBefore))
            Movement.RefreshMovementSpeedModifiers(uid);

        if (comp.Levels.Count == 0 && comp.Crashes.Count == 0 && comp.Debt <= 0f)
        {
            RemCompDeferred<WFIntoxicationComponent>(uid);
            return;
        }

        Dirty(uid, comp);
    }

    /// <summary>The level after <paramref name="seconds"/> of linear decay and half-life.</summary>
    public static float Decayed(WFDrugProfilePrototype proto, float level, float seconds)
    {
        if (proto.HalfLife > 0f)
            level *= MathF.Pow(2f, -seconds / proto.HalfLife);

        return Math.Max(0f, level - proto.Decay * seconds);
    }

    /// <summary>Tells the user when a profile moves into a new phase: light, heavy or overdose.</summary>
    private void AnnouncePhase(Entity<WFIntoxicationComponent> ent, ProtoId<WFDrugProfilePrototype> profile,
        WFDrugProfilePrototype proto, int tier)
    {
        var phase = proto.PhaseOf(tier);
        var before = ent.Comp.Phases.GetValueOrDefault(profile, WFDrugPhase.None);
        if (phase == before)
            return;

        if (phase == WFDrugPhase.None)
            ent.Comp.Phases.Remove(profile);
        else
            ent.Comp.Phases[profile] = phase;

        var line = (phase, phase > before) switch
        {
            (WFDrugPhase.Light, true) => "wf-drug-phase-light",
            (WFDrugPhase.Heavy, true) => "wf-drug-phase-heavy",
            (WFDrugPhase.Overdose, true) => "wf-drug-phase-overdose",
            (WFDrugPhase.Heavy, false) => "wf-drug-phase-easing",
            (WFDrugPhase.Light, false) => "wf-drug-phase-fading",
            _ => null,
        };

        if (line == null)
            return;

        var type = phase == WFDrugPhase.Overdose ? PopupType.LargeCaution
            : phase > before ? PopupType.Medium
            : PopupType.Small;
        _popup.PopupEntity(Loc.GetString(line), ent, ent, type);
    }

    private void Apply(EntityUid uid, EntityEffectBaseArgs args, List<EntityEffect> effects, List<WFDrugStatusEffect> statuses)
    {
        foreach (var effect in effects)
        {
            if (effect.ShouldApply(args, _random))
                effect.Effect(args);
        }

        foreach (var status in statuses)
        {
            if (status.Component != null)
                _status.TryAddStatusEffect(uid, status.Key, StatusTime, true, status.Component);
            else
                _status.TryAddStatusEffect(uid, status.Key, StatusTime, true);
        }
    }

    private void Hallucinate(EntityUid uid, WFDrugTier tier)
    {
        if (tier.Hallucinations.Count == 0
            || !_random.Prob(tier.HallucinationChance)
            || !_players.TryGetSessionByEntity(uid, out var session)
            || IsNeurowareHost(uid))
            return;

        _drift.Show(session, uid, _random.Pick(tier.Hallucinations));
    }
}
