using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Narcotics;

/// <summary>
/// Raises a drug profile's level by the metabolised units times their grade times <see cref="PerUnit"/>. Does nothing
/// to a body whose brain is non-organic.
/// </summary>
public sealed partial class WFDose : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<WFDrugProfilePrototype> Profile;

    /// <summary>Level added per effective unit; outside a reagent, the whole amount.</summary>
    [DataField]
    public float PerUnit = 1f;

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
        }

        intoxication.AddLevel(args.TargetEntity, Profile, amount);
    }
}
