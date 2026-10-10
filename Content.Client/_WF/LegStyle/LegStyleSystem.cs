using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.LegStyle;

/// <summary>
/// Fits clothing to a character's picked legs.
/// </summary>
public sealed partial class LegStyleSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>
    /// The displacement map a clothing slot uses on this wearer, given the one its species would use.
    /// </summary>
    public DisplacementData? GetDisplacement(EntityUid wearer, string slot, DisplacementData? species)
    {
        if (!TryComp<HumanoidAppearanceComponent>(wearer, out var humanoid)
            || humanoid.LegStyle is not { } id
            || !_proto.TryIndex(id, out var style)
            || !style.DisplacementSlots.Contains(slot))
        {
            return species;
        }

        if (humanoid.Sex == Sex.Female && style.FemaleDisplacements.TryGetValue(slot, out var female))
            return female;

        return style.Displacements.GetValueOrDefault(slot);
    }
}
