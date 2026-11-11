using Content.Shared._WF.Purity;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Purity;

/// <summary>
/// Applies what a reagent's grade does to the body that metabolises it.
/// </summary>
public sealed class WFPuritySystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly ProtoId<DamageTypePrototype> Poison = "Poison";

    private float _impurityDamage;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, WFPurityCVars.ImpurityDamage, v => _impurityDamage = v, true);
    }

    /// <summary>Deals Poison for the impure fraction of a metabolised amount.</summary>
    public void ApplyImpurity(EntityUid body, FixedPoint2 units, float purity)
    {
        var amount = units.Float() * (1f - purity) * _impurityDamage;
        if (amount <= 0f)
            return;

        _damageable.TryChangeDamage(body, new DamageSpecifier { DamageDict = { [Poison.Id] = FixedPoint2.New(amount) } },
            ignoreResistances: true, interruptsDoAfters: false, canSever: false);
    }
}
