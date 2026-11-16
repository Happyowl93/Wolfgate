using System.Linq;
using Content.Shared._WF.Augments;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>The augment picked for each slot; a slot left out keeps the species' own part.</summary>
    [DataField]
    public Dictionary<WFAugmentSlot, WFAugmentChoice> Augments { get; set; } = new();

    /// <summary>Implants picked for the parts their slots name, up to what each part holds.</summary>
    [DataField]
    public HashSet<ProtoId<WFAugmentPrototype>> AugmentImplants { get; set; } = new();

    /// <summary>
    /// Priced augments this character owns: paid for, or kept from a round. Only the server adds to it; one the
    /// character no longer picks is sold back on the next creator save.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<WFAugmentPrototype>> AugmentUnlocks { get; set; } = new();

    /// <summary>Copy of this profile with the given augment choices.</summary>
    public HumanoidCharacterProfile WithAugments(Dictionary<WFAugmentSlot, WFAugmentChoice> augments)
    {
        return new(this) { Augments = new(augments) };
    }

    /// <summary>Copy of this profile with the given implants.</summary>
    public HumanoidCharacterProfile WithAugmentImplants(IEnumerable<ProtoId<WFAugmentPrototype>> implants)
    {
        return new(this) { AugmentImplants = new(implants) };
    }

    /// <summary>Copy of this profile with the given unlocks.</summary>
    public HumanoidCharacterProfile WithAugmentUnlocks(IEnumerable<ProtoId<WFAugmentPrototype>> unlocks)
    {
        return new(this) { AugmentUnlocks = new(unlocks) };
    }

    private bool AugmentsEqual(HumanoidCharacterProfile other)
    {
        return Augments.Count == other.Augments.Count
               && Augments.All(a => other.Augments.TryGetValue(a.Key, out var o) && o == a.Value)
               && AugmentImplants.SetEquals(other.AugmentImplants)
               && AugmentUnlocks.SetEquals(other.AugmentUnlocks);
    }

    private void EnsureAugmentsValid(IPrototypeManager proto, IComponentFactory factory)
    {
        // Crafted network data can deliver null.
        Augments = WFAugmentRules.Validate(Species, Augments, TraitPreferences, proto, factory);
        AugmentImplants = WFAugmentRules.ValidateImplants(Species, Augments, AugmentImplants, TraitPreferences, proto, factory);
        AugmentUnlocks = new((AugmentUnlocks ?? new()).Where(u => proto.HasIndex(u)));
    }
}
