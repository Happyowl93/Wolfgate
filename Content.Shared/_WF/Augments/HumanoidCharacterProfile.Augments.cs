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

    /// <summary>The unlocks kept from a round instead of paid for, which sell back for nothing. Only the server adds to it.</summary>
    [DataField]
    public HashSet<ProtoId<WFAugmentPrototype>> AugmentFound { get; set; } = new();

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

    /// <summary>Copy of this profile with the given unlocks; found ones no longer among them are dropped.</summary>
    public HumanoidCharacterProfile WithAugmentUnlocks(IEnumerable<ProtoId<WFAugmentPrototype>> unlocks)
    {
        var kept = new HashSet<ProtoId<WFAugmentPrototype>>(unlocks);
        return new(this) { AugmentUnlocks = kept, AugmentFound = new(AugmentFound.Where(kept.Contains)) };
    }

    /// <summary>Copy of this profile with those of its unlocks marked as found, not paid for.</summary>
    public HumanoidCharacterProfile WithAugmentFound(IEnumerable<ProtoId<WFAugmentPrototype>> found)
    {
        return new(this) { AugmentFound = new(found.Where(AugmentUnlocks.Contains)) };
    }

    private bool AugmentsEqual(HumanoidCharacterProfile other)
    {
        return Augments.Count == other.Augments.Count
               && Augments.All(a => other.Augments.TryGetValue(a.Key, out var o) && o == a.Value)
               && AugmentImplants.SetEquals(other.AugmentImplants)
               && AugmentUnlocks.SetEquals(other.AugmentUnlocks)
               && AugmentFound.SetEquals(other.AugmentFound);
    }

    private void EnsureAugmentsValid(IPrototypeManager proto, IComponentFactory factory)
    {
        // Crafted network data can deliver null.
        Augments = WFAugmentRules.Validate(Species, Augments, TraitPreferences, proto, factory);
        AugmentImplants = WFAugmentRules.ValidateImplants(Species, Augments, AugmentImplants, TraitPreferences, proto, factory);
        AugmentUnlocks = new((AugmentUnlocks ?? new()).Where(u => proto.HasIndex(u)));
        AugmentFound = new((AugmentFound ?? new()).Where(AugmentUnlocks.Contains));
    }
}
