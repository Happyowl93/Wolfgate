using Content.Shared._WF.Narcotics;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Stunnable;
using Content.Shared.Traits.Assorted;

namespace Content.Server._WF.Narcotics;

public sealed partial class WFIntoxicationSystem
{
    [Dependency] private DamageableSystem _damageable = default!;

    /// <summary>Share of incoming Brute and Burn a <see cref="WFDrugTrait.Damper"/> tier holds off.</summary>
    public const float DamperReduction = 0.3f;

    /// <summary>Share of held-off damage repaid as Poison.</summary>
    public const float DamperRepay = 0.4f;

    /// <summary>Shortest time debt is repaid over when no crash is running.</summary>
    private const float MinRepaySeconds = 5f;

    /// <summary>Slowest repayment, in Poison a second, so a small debt doesn't linger.</summary>
    private const float MinRepayRate = 0.5f;

    private static readonly string[] DamperGroups = { "Brute", "Burn" };
    private static readonly WFDrugTrait[] ComponentTraits =
    {
        WFDrugTrait.IgnoreDamageSlowdown, WFDrugTrait.StaminaCritResistance, WFDrugTrait.HideDamage,
    };

    private readonly HashSet<WFDrugTrait> _activeTraits = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFIntoxicationComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<WFIntoxicationComponent, ComponentShutdown>(OnShutdown);
    }

    private void UpdateTraits(Entity<WFIntoxicationComponent> ent, float seconds)
    {
        var (uid, comp) = ent;
        _activeTraits.Clear();
        foreach (var (profile, level) in comp.Levels)
        {
            if (!Proto.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            if (tier >= 0)
                _activeTraits.UnionWith(proto.Tiers[tier].Traits);
        }

        foreach (var trait in ComponentTraits)
        {
            if (_activeTraits.Contains(trait))
                Grant(ent, trait);
            else if (comp.Granted.Remove(trait))
                RemoveTraitComponent(uid, trait);
        }

        if (_activeTraits.Contains(WFDrugTrait.StunResistance))
        {
            _status.TryRemoveStatusEffect(uid, "Stun");
            _status.TryRemoveStatusEffect(uid, "KnockedDown");
        }

        if (comp.Debt > 0f && !_activeTraits.Contains(WFDrugTrait.Damper))
            Repay(ent, seconds);
    }

    private void Grant(Entity<WFIntoxicationComponent> ent, WFDrugTrait trait)
    {
        var uid = ent.Owner;
        var added = trait switch
        {
            WFDrugTrait.IgnoreDamageSlowdown => !HasComp<IgnoreSlowOnDamageComponent>(uid) && AddComp<IgnoreSlowOnDamageComponent>(uid) != null,
            WFDrugTrait.StaminaCritResistance => !HasComp<StamcritResistComponent>(uid) && AddComp<StamcritResistComponent>(uid) != null,
            WFDrugTrait.HideDamage => !HasComp<PainNumbnessComponent>(uid) && AddComp<PainNumbnessComponent>(uid) != null,
            _ => false,
        };

        if (added)
            ent.Comp.Granted.Add(trait);
    }

    private void RemoveTraitComponent(EntityUid uid, WFDrugTrait trait)
    {
        switch (trait)
        {
            case WFDrugTrait.IgnoreDamageSlowdown:
                RemComp<IgnoreSlowOnDamageComponent>(uid);
                break;
            case WFDrugTrait.StaminaCritResistance:
                RemComp<StamcritResistComponent>(uid);
                break;
            case WFDrugTrait.HideDamage:
                RemComp<PainNumbnessComponent>(uid);
                break;
        }
    }

    private void OnShutdown(Entity<WFIntoxicationComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        foreach (var trait in ent.Comp.Granted)
        {
            RemoveTraitComponent(ent, trait);
        }

        ent.Comp.Granted.Clear();
    }

    private void OnDamageModify(Entity<WFIntoxicationComponent> ent, ref DamageModifyEvent args)
    {
        if (!HasTrait(ent, WFDrugTrait.Damper))
            return;

        var damage = new DamageSpecifier(args.Damage);
        var avoided = FixedPoint2.Zero;
        foreach (var group in DamperGroups)
        {
            if (!Proto.TryIndex<DamageGroupPrototype>(group, out var proto))
                continue;

            foreach (var type in proto.DamageTypes)
            {
                if (!damage.DamageDict.TryGetValue(type, out var value) || value <= 0)
                    continue;

                var cut = value * DamperReduction;
                damage.DamageDict[type] = value - cut;
                avoided += cut;
            }
        }

        if (avoided <= 0)
            return;

        args.Damage = damage;
        ent.Comp.Debt += avoided.Float() * DamperRepay;
    }

    /// <summary>Repays held-off damage as Poison, spread over whatever crash is left.</summary>
    private void Repay(Entity<WFIntoxicationComponent> ent, float seconds)
    {
        var left = MinRepaySeconds;
        foreach (var crash in ent.Comp.Crashes.Values)
        {
            left = Math.Max(left, crash);
        }

        var amount = Math.Min(ent.Comp.Debt, Math.Max(ent.Comp.Debt * seconds / left, MinRepayRate * seconds));
        ent.Comp.Debt -= amount;

        var poison = new DamageSpecifier();
        poison.DamageDict["Poison"] = FixedPoint2.New(amount);
        _damageable.TryChangeDamage(ent, poison, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
    }
}
