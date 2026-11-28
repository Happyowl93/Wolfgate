using Content.Shared.StatusEffectNew;
using Content.Shared.Traits.Assorted;

namespace Content.Server._Shitmed.Medical.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private StatusEffectsSystem _wgStatusEffects = default!;

    /// <summary>Whether the patient feels no pain, from the Numb trait or a numbing drug.</summary>
    private bool WgIsNumb(EntityUid body)
    {
        return HasComp<PainNumbnessComponent>(body)
            || _wgStatusEffects.TryEffectsWithComp<PainNumbnessStatusEffectComponent>(body, out _);
    }
}
