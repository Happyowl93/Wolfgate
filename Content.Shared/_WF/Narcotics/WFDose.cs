using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Narcotics;

/// <summary>
/// Doses a drug profile. With <see cref="InBlood"/>, the level follows the reagent left in the blood, which the
/// intoxication system reads each tick, and this only counts the metabolised units towards dependence. Without it,
/// the metabolised units times their grade times <see cref="PerUnit"/> are fed to the level, which then decays on its
/// own. Does nothing to a body whose brain is non-organic.
/// </summary>
public sealed partial class WFDose : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<WFDrugProfilePrototype> Profile;

    /// <summary>Level per effective unit; outside a reagent, the whole amount.</summary>
    [DataField]
    public float PerUnit = 1f;

    /// <summary>The level follows the units in the blood, so the high lasts as long as the drug does.</summary>
    [DataField]
    public bool InBlood;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wf-dose", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var intoxication = args.EntityManager.System<SharedWFIntoxicationSystem>();
        if (intoxication.IsNeurowareHost(args.TargetEntity))
            return;

        var amount = PerUnit;
        if (args is EntityEffectReagentArgs reagent)
        {
            var grade = reagent.Source != null && reagent.Reagent != null ? reagent.Source.GetPurity(reagent.Reagent.ID) : 1f;
            amount *= reagent.Quantity.Float() * grade;

            if (InBlood)
            {
                intoxication.RegisterDose(args.TargetEntity, Profile, amount);
                return;
            }
        }

        intoxication.AddLevel(args.TargetEntity, Profile, amount);
    }
}
