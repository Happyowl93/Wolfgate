using Content.Shared._Onyx.Wounds;

namespace Content.Shared._WF.Augments;

/// <summary>Scales up the pain a body with a reflex booster gains.</summary>
public sealed partial class WFReflexBoosterSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFReflexBoosterComponent, ModifyPainGainEvent>(OnModifyPainGain);
    }

    private void OnModifyPainGain(Entity<WFReflexBoosterComponent> ent, ref ModifyPainGainEvent args)
    {
        args.Multiplier *= ent.Comp.PainMultiplier;
    }
}
