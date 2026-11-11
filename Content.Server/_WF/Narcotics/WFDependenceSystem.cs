using Content.Server.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Narcotics;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Narcotics;

/// <summary>
/// Round-long dependence: doses fill a meter per family, tolerance speeds up the come-down, and a dependent body that
/// goes without for long enough falls into withdrawal until it doses again. Withdrawal never kills.
/// </summary>
public sealed class WFDependenceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    /// <summary>Meter and tolerance lost a second while clean: 0.02 a minute.</summary>
    public const float DecayPerSecond = 0.02f / 60f;

    /// <summary>Tolerance gained per point of dependence gained.</summary>
    public const float ToleranceRatio = 0.5f;

    /// <summary>Withdrawal needs a meter above this.</summary>
    public const float WithdrawalMeter = 0.5f;

    /// <summary>Seconds without a dose or a high before withdrawal starts.</summary>
    public const float WithdrawalDelay = 300f;

    /// <summary>Stamina damage withdrawal keeps a body at, as a share of its crit threshold.</summary>
    public const float StaminaFloor = 0.35f;

    private const int CravingLines = 3;
    private const float PainEvery = 10f;
    private const float VomitChance = 1f / 90f;
    private static readonly TimeSpan StatusTime = TimeSpan.FromSeconds(2.5);

    private TimeSpan _nextTick;
    private readonly List<WFDrugFamily> _families = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFIntoxicationComponent, WFDrugDosedEvent>(OnDosed);
        SubscribeLocalEvent<WFDependenceComponent, WFDrugDecayEvent>(OnDecay);
        SubscribeLocalEvent<WFDependentStimulantComponent, ComponentStartup>((uid, comp, _) => ApplyTrait(uid, comp));
        SubscribeLocalEvent<WFDependentOpioidComponent, ComponentStartup>((uid, comp, _) => ApplyTrait(uid, comp));
        SubscribeLocalEvent<WFDependentNicotineComponent, ComponentStartup>((uid, comp, _) => ApplyTrait(uid, comp));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(1);
        Tick(1f);
    }

    private void ApplyTrait(EntityUid uid, WFDependentTraitComponent trait)
    {
        var dep = EnsureComp<WFDependenceComponent>(uid);
        dep.Meter[trait.Family] = Math.Max(dep.Meter.GetValueOrDefault(trait.Family), trait.Start);
        dep.Floor[trait.Family] = trait.Floor;
        dep.SinceUse.TryAdd(trait.Family, 0f);
    }

    private void OnDosed(Entity<WFIntoxicationComponent> ent, ref WFDrugDosedEvent args)
    {
        var dep = EnsureComp<WFDependenceComponent>(ent);
        var family = args.Family;
        dep.Meter[family] = Math.Clamp(dep.Meter.GetValueOrDefault(family) + args.DependenceGain, 0f, 1f);
        dep.Tolerance[family] = Math.Clamp(dep.Tolerance.GetValueOrDefault(family) + args.DependenceGain * ToleranceRatio, 0f, 1f);
        dep.SinceUse[family] = 0f;

        if (dep.Stage.GetValueOrDefault(family) > 0)
            SetStage(ent, dep, family, 0);
    }

    private void OnDecay(Entity<WFDependenceComponent> ent, ref WFDrugDecayEvent args)
    {
        args.Multiplier *= 1f + ent.Comp.Tolerance.GetValueOrDefault(args.Family);
    }

    /// <summary>The withdrawal stage a meter gives once withdrawal has started.</summary>
    public static int StageFor(float meter)
    {
        return meter >= 0.85f ? 3 : meter >= 0.7f ? 2 : meter > WithdrawalMeter ? 1 : 0;
    }

    /// <summary>Advances every dependent body by <paramref name="seconds"/>.</summary>
    public void Tick(float seconds)
    {
        var query = EntityQueryEnumerator<WFDependenceComponent>();
        while (query.MoveNext(out var uid, out var dep))
        {
            if (_mobState.IsDead(uid))
                continue;

            _families.Clear();
            _families.AddRange(dep.Meter.Keys);
            foreach (var family in _families)
            {
                TickFamily((uid, dep), family, seconds);
            }

            if (dep.Meter.Count == 0)
                RemCompDeferred<WFDependenceComponent>(uid);
        }
    }

    private void TickFamily(Entity<WFDependenceComponent> ent, WFDrugFamily family, float seconds)
    {
        var dep = ent.Comp;
        var high = _intoxication.IsFamilyHigh(ent, family);
        var since = high ? 0f : dep.SinceUse.GetValueOrDefault(family) + seconds;
        dep.SinceUse[family] = since;

        var floor = dep.Floor.GetValueOrDefault(family);
        var meter = dep.Meter[family];
        if (!high)
        {
            meter = Math.Max(floor, meter - DecayPerSecond * seconds);
            dep.Tolerance[family] = Math.Max(0f, dep.Tolerance.GetValueOrDefault(family) - DecayPerSecond * seconds);
        }

        dep.Meter[family] = meter;

        var stage = since >= WithdrawalDelay ? StageFor(meter) : 0;
        if (stage != dep.Stage.GetValueOrDefault(family))
            SetStage(ent, dep, family, stage);

        if (stage > 0)
            Withdraw(ent, dep, family, stage, seconds);

        if (meter <= 0f && floor <= 0f && dep.Tolerance.GetValueOrDefault(family) <= 0f)
        {
            dep.Meter.Remove(family);
            dep.Tolerance.Remove(family);
            dep.SinceUse.Remove(family);
            dep.Stage.Remove(family);
            dep.NextCraving.Remove(family);
        }
    }

    private void SetStage(EntityUid uid, WFDependenceComponent dep, WFDrugFamily family, int stage)
    {
        var old = dep.Stage.GetValueOrDefault(family);
        dep.Stage[family] = stage;
        dep.NextCraving[family] = _random.NextFloat(20f, 40f);

        var suffix = stage == 0 ? "relief" : stage > old ? $"stage-{stage}" : null;
        if (suffix != null)
        {
            _popup.PopupEntity(Loc.GetString(Prefix(uid) + suffix, ("family", FamilyName(family))), uid, uid,
                stage == 0 ? PopupType.Medium : PopupType.MediumCaution);
        }
    }

    private void Withdraw(EntityUid uid, WFDependenceComponent dep, WFDrugFamily family, int stage, float seconds)
    {
        var craving = dep.NextCraving.GetValueOrDefault(family) - seconds;
        if (craving <= 0f)
        {
            craving = _random.NextFloat(45f, 90f);
            var line = $"{Prefix(uid)}craving-{family.ToString().ToLowerInvariant()}-{_random.Next(1, CravingLines + 1)}";
            _popup.PopupEntity(Loc.GetString(line), uid, uid, PopupType.SmallCaution);
        }

        dep.NextCraving[family] = craving;

        if (stage < 2)
            return;

        _jitter.DoJitter(uid, StatusTime, true, 4f, 3f);

        if (TryComp<StaminaComponent>(uid, out var stamina))
        {
            var floor = stamina.CritThreshold * StaminaFloor;
            if (stamina.StaminaDamage < floor)
                _stamina.TakeStaminaDamage(uid, floor - stamina.StaminaDamage, stamina, visual: false);
        }

        if (_random.Prob(seconds / PainEvery) && _body.GetRootPartOrNull(uid) is { } root && TryComp<PainComponent>(root.Entity, out var pain))
            _pain.ChangePain((root.Entity, pain), 3);

        if (stage < 3 || family is not (WFDrugFamily.Opioid or WFDrugFamily.Stimulant))
            return;

        _status.TryAddStatusEffect(uid, "Drowsiness", StatusTime, true, "Drowsiness");
        if (_random.Prob(VomitChance * seconds))
            _vomit.Vomit(uid, -10f, -10f);
    }

    private string Prefix(EntityUid uid)
    {
        return _intoxication.IsNeurowareHost(uid) ? "wf-withdrawal-host-" : "wf-withdrawal-";
    }

    private string FamilyName(WFDrugFamily family)
    {
        return Loc.GetString($"wf-drug-family-{family.ToString().ToLowerInvariant()}");
    }
}
