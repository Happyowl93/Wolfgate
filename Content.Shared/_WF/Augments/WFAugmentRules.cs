using System.Linq;
using System.Text;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared._WF.LegStyle;
using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Augments;

/// <summary>Where each slot sits on a body, which augments and styles a character may pick, and how they are stored.</summary>
public static class WFAugmentRules
{
    private const char EntrySeparator = ';';
    private const char FieldSeparator = ':';
    private const string ImplantEntry = "Implant";

    /// <summary>Slots implants were stored under before a part could hold several.</summary>
    private static readonly string[] LegacyImplantSlots = ["LeftArmImplant", "RightArmImplant", "Dermal", "Muscles"];

    /// <summary>The body slot holding the part a limb augment replaces, or the organ an organ augment replaces.</summary>
    public static string PartSlot(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.Head or WFAugmentSlot.Eyes => "head",
        WFAugmentSlot.LeftArm => "left arm",
        WFAugmentSlot.RightArm => "right arm",
        WFAugmentSlot.LeftLeg => "left leg",
        WFAugmentSlot.RightLeg => "right leg",
        WFAugmentSlot.LeftHand => "left hand",
        WFAugmentSlot.RightHand => "right hand",
        WFAugmentSlot.LeftFoot => "left foot",
        WFAugmentSlot.RightFoot => "right foot",
        _ => "torso",
    };

    /// <summary>The share of its price, in percent, an augment taken off in the creator pays back.</summary>
    public const int RefundPercent = 50;

    /// <summary>What an augment taken off in the creator pays back.</summary>
    public static int Refund(WFAugmentPrototype augment) => augment.Price * RefundPercent / 100;

    /// <summary>Implants a part the species was born with holds: two in the torso, one in any other part.</summary>
    public static int NaturalImplantSlots(WFAugmentSlot slot) => slot == WFAugmentSlot.Chest ? 2 : 1;

    /// <summary>The limb a hand or foot sits on, which it comes with unless picked on its own.</summary>
    public static WFAugmentSlot? LimbAbove(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.LeftHand => WFAugmentSlot.LeftArm,
        WFAugmentSlot.RightHand => WFAugmentSlot.RightArm,
        WFAugmentSlot.LeftFoot => WFAugmentSlot.LeftLeg,
        WFAugmentSlot.RightFoot => WFAugmentSlot.RightLeg,
        _ => null,
    };

    /// <summary>The entity an augment puts in the body, which is also what scavengers find of it.</summary>
    public static EntProtoId? Installed(WFAugmentPrototype augment) => augment.Kind switch
    {
        WFAugmentKind.Limb => augment.Part,
        WFAugmentKind.Organ => augment.Organ,
        WFAugmentKind.Upgrade => augment.Autosurgeon,
        _ => null,
    };

    /// <summary>The augment's name: its own, else the installed entity's.</summary>
    public static string Name(WFAugmentPrototype augment, IPrototypeManager proto)
    {
        if (augment.Name is { } name)
            return Loc.GetString(name);
        return Installed(augment) is { } id && proto.TryIndex(id, out var entity) ? entity.Name : augment.ID;
    }

    /// <summary>The augment's description: its own, else the installed entity's.</summary>
    public static string Description(WFAugmentPrototype augment, IPrototypeManager proto)
    {
        if (augment.Description is { } description)
            return Loc.GetString(description);
        return Installed(augment) is { } id && proto.TryIndex(id, out var entity) ? entity.Description : string.Empty;
    }

    /// <summary>How often scavengers find an augment, against the others: the inverse of its price.</summary>
    public static float ScavengeWeight(WFAugmentPrototype augment)
    {
        return augment.Price > 0 && Installed(augment) != null ? 100_000f / augment.Price : 0f;
    }

    /// <summary>The organ slot an organ augment fills, or null for a part.</summary>
    public static string? OrganSlot(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.Eyes => "eyes",
        WFAugmentSlot.Heart => "heart",
        WFAugmentSlot.Lungs => "lungs",
        WFAugmentSlot.Liver => "liver",
        WFAugmentSlot.Stomach => "stomach",
        _ => null,
    };

    /// <summary>The humanoid layers a limb or plating in this slot draws over.</summary>
    public static HumanoidVisualLayers[] Layers(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.Head => [HumanoidVisualLayers.Head],
        WFAugmentSlot.Chest => [HumanoidVisualLayers.Chest],
        WFAugmentSlot.LeftArm => [HumanoidVisualLayers.LArm, HumanoidVisualLayers.LHand],
        WFAugmentSlot.RightArm => [HumanoidVisualLayers.RArm, HumanoidVisualLayers.RHand],
        WFAugmentSlot.LeftLeg => [HumanoidVisualLayers.LLeg, HumanoidVisualLayers.LFoot],
        WFAugmentSlot.RightLeg => [HumanoidVisualLayers.RLeg, HumanoidVisualLayers.RFoot],
        WFAugmentSlot.LeftHand => [HumanoidVisualLayers.LHand],
        WFAugmentSlot.RightHand => [HumanoidVisualLayers.RHand],
        WFAugmentSlot.LeftFoot => [HumanoidVisualLayers.LFoot],
        WFAugmentSlot.RightFoot => [HumanoidVisualLayers.RFoot],
        _ => [],
    };

    /// <summary>Whether the species' body has the part or organ this slot works on.</summary>
    public static bool SlotAvailable(string species, WFAugmentSlot slot, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!proto.TryIndex<SpeciesPrototype>(species, out var speciesProto)
            || !proto.TryIndex(speciesProto.Prototype, out var mob)
            || !mob.TryComp(out BodyComponent? body, factory)
            || body.Prototype is not { } bodyId
            || !proto.TryIndex<BodyPrototype>(bodyId, out var bodyProto)
            || !bodyProto.Slots.TryGetValue(PartSlot(slot), out var part))
            return false;

        return OrganSlot(slot) is not { } organ || part.Organs.ContainsKey(organ);
    }

    /// <summary>The sprite a style draws on a layer, the digitigrade one on digitigrade legs.</summary>
    public static ProtoId<HumanoidSpeciesSpriteLayer>? Sprite(WFAugmentStylePrototype style, HumanoidVisualLayers layer, bool digitigrade)
    {
        if (digitigrade && style.Digitigrade.TryGetValue(layer, out var digi))
            return digi;
        return style.Sprites.TryGetValue(layer, out var sprite) ? sprite : (ProtoId<HumanoidSpeciesSpriteLayer>?) null;
    }

    /// <summary>
    /// The sprite a style draws on a layer over a body whose own limb there is <paramref name="under"/>: the style
    /// fitted to that limb's shape when it differs from the human's, else the human-shaped one.
    /// </summary>
    public static ProtoId<HumanoidSpeciesSpriteLayer>? Sprite(WFAugmentStylePrototype style,
        HumanoidVisualLayers layer,
        bool digitigrade,
        string? under,
        IPrototypeManager proto)
    {
        if (under != null && style.Fitted.Count > 0)
        {
            foreach (var shape in proto.EnumeratePrototypes<WFAugmentShapePrototype>())
            {
                if (style.Fitted.TryGetValue(shape.ID, out var fitted) && shape.BaseSprites.Contains(under))
                    return fitted;
            }
        }

        return Sprite(style, layer, digitigrade);
    }

    /// <summary>The base sprite a body draws on a layer without augments: its leg style's, else its species'.</summary>
    public static string? Underlying(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer, IPrototypeManager proto)
    {
        if (humanoid.LegStyle is { } legStyleId
            && proto.TryIndex(legStyleId, out var legStyle)
            && legStyle.Sprites.TryGetValue(layer, out var legSprite))
            return legSprite;

        if (proto.TryIndex<SpeciesPrototype>(humanoid.Species, out var species)
            && proto.TryIndex<HumanoidSpeciesBaseSpritesPrototype>(species.SpriteSet, out var set)
            && set.Sprites.TryGetValue(layer, out var sprite))
            return sprite;

        return null;
    }

    /// <summary>Whether a style draws every layer of a slot.</summary>
    public static bool StyleFits(WFAugmentStylePrototype style, WFAugmentSlot slot)
    {
        return Layers(slot).All(style.Sprites.ContainsKey);
    }

    /// <summary>The styles that fit a slot, ordered by prototype id.</summary>
    public static List<WFAugmentStylePrototype> StylesFor(WFAugmentSlot slot, IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<WFAugmentStylePrototype>()
            .Where(s => StyleFits(s, slot))
            .OrderBy(s => s.ID)
            .ToList();
    }

    /// <summary>The augments a species may take in a slot, in order.</summary>
    public static List<WFAugmentPrototype> AugmentsFor(string species, WFAugmentSlot slot, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!SlotAvailable(species, slot, proto, factory))
            return new List<WFAugmentPrototype>();

        return proto.EnumeratePrototypes<WFAugmentPrototype>()
            .Where(a => a.Slot == slot && a.Kind != WFAugmentKind.Upgrade && !a.SpeciesBlacklist.Contains(species))
            .OrderBy(a => a.Order)
            .ThenBy(a => a.ID)
            .ToList();
    }

    /// <summary>The implants a species may take in a slot's part, in order.</summary>
    public static List<WFAugmentPrototype> ImplantsFor(string species, WFAugmentSlot slot, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!SlotAvailable(species, slot, proto, factory))
            return new List<WFAugmentPrototype>();

        return proto.EnumeratePrototypes<WFAugmentPrototype>()
            .Where(a => a.Slot == slot && a.Kind == WFAugmentKind.Upgrade && !a.SpeciesBlacklist.Contains(species))
            .OrderBy(a => a.Order)
            .ThenBy(a => a.ID)
            .ToList();
    }

    /// <summary>
    /// How many implants the slot's part holds with these choices: the picked limb's count, none for a missing one
    /// or a hand or foot whose limb is missing, one in a trait's limb, else the natural part's.
    /// </summary>
    public static int ImplantSlots(WFAugmentSlot slot,
        IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice> choices,
        IReadOnlyDictionary<WFAugmentSlot, ProtoId<TraitPrototype>> claimed,
        IPrototypeManager proto)
    {
        if (claimed.ContainsKey(slot))
            return 1;
        if (choices.TryGetValue(slot, out var choice) && proto.TryIndex(choice.Augment, out var augment))
        {
            if (augment.Kind == WFAugmentKind.Limb)
                return augment.ImplantSlots;
            if (augment.Kind == WFAugmentKind.Missing)
                return 0;
        }
        else if (LimbAbove(slot) is { } above
                 && choices.TryGetValue(above, out var limb)
                 && proto.TryIndex(limb.Augment, out var limbAugment)
                 && limbAugment.Kind == WFAugmentKind.Missing)
        {
            return 0;
        }

        return NaturalImplantSlots(slot);
    }

    /// <summary>
    /// Whether the slot's part is synthetic with these choices: a picked or trait limb is, a hand or foot not picked
    /// on its own is what its limb is, and a natural part is if its prototype is cybernetic or not built of flesh
    /// (an IPC's).
    /// </summary>
    public static bool Synthetic(string species,
        WFAugmentSlot slot,
        IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice> choices,
        IReadOnlyDictionary<WFAugmentSlot, ProtoId<TraitPrototype>> claimed,
        IPrototypeManager proto,
        IComponentFactory factory)
    {
        if (claimed.ContainsKey(slot))
            return true;
        if (choices.TryGetValue(slot, out var choice)
            && proto.TryIndex(choice.Augment, out var augment)
            && augment.Kind == WFAugmentKind.Limb)
            return augment.Part is not { } part || SyntheticPart(part, proto, factory);

        if (LimbAbove(slot) is { } above && (claimed.ContainsKey(above) || choices.ContainsKey(above)))
            return Synthetic(species, above, choices, claimed, proto, factory);

        return NaturalPart(species, slot, proto, factory) is { } natural && SyntheticPart(natural, proto, factory);
    }

    private static bool SyntheticPart(EntProtoId part, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!proto.TryIndex(part, out var entity))
            return false;
        if (entity.TryComp(out CyberneticsComponent? _, factory))
            return true;
        return entity.TryComp(out DamageableComponent? damageable, factory)
               && damageable.DamageContainerID is { } container
               && container.Id != "Biological"
               && container.Id != "OrganicPart";
    }

    /// <summary>The part prototype the species' body has in the slot.</summary>
    private static EntProtoId? NaturalPart(string species, WFAugmentSlot slot, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!proto.TryIndex<SpeciesPrototype>(species, out var speciesProto)
            || !proto.TryIndex(speciesProto.Prototype, out var mob)
            || !mob.TryComp(out BodyComponent? body, factory)
            || body.Prototype is not { } bodyId
            || !proto.TryIndex<BodyPrototype>(bodyId, out var bodyProto)
            || !bodyProto.Slots.TryGetValue(PartSlot(slot), out var part)
            || part.Part is not { } partId)
            return null;

        return partId;
    }

    /// <summary>Whether an implant goes in a part that is or isn't synthetic.</summary>
    public static bool HostFits(WFAugmentPrototype implant, bool synthetic) => implant.Host switch
    {
        WFAugmentHost.Organic => !synthetic,
        WFAugmentHost.Synthetic => synthetic,
        _ => true,
    };

    /// <summary>
    /// Drops implants the species can't take, that don't fit the part the choices leave in their slot, and those past
    /// what the part holds, keeping the first by order.
    /// </summary>
    public static HashSet<ProtoId<WFAugmentPrototype>> ValidateImplants(string species,
        IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice> choices,
        IEnumerable<ProtoId<WFAugmentPrototype>>? implants,
        IEnumerable<ProtoId<TraitPrototype>> traits,
        IPrototypeManager proto,
        IComponentFactory factory)
    {
        var valid = new HashSet<ProtoId<WFAugmentPrototype>>();
        if (implants == null)
            return valid;

        var claimed = ClaimedSlots(traits, proto);
        var used = new Dictionary<WFAugmentSlot, int>();
        var picked = implants
            .Select(id => proto.TryIndex(id, out var augment) ? augment : null)
            .OfType<WFAugmentPrototype>()
            .Where(a => a.Kind == WFAugmentKind.Upgrade)
            .Distinct()
            .OrderBy(a => a.Order)
            .ThenBy(a => a.ID);
        foreach (var implant in picked)
        {
            var slot = implant.Slot;
            if (implant.SpeciesBlacklist.Contains(species)
                || !SlotAvailable(species, slot, proto, factory)
                || !HostFits(implant, Synthetic(species, slot, choices, claimed, proto, factory))
                || used.GetValueOrDefault(slot) >= ImplantSlots(slot, choices, claimed, proto))
                continue;

            used[slot] = used.GetValueOrDefault(slot) + 1;
            valid.Add(implant.ID);
        }

        return valid;
    }

    /// <summary>Whether the profile's legs are digitigrade.</summary>
    public static bool Digitigrade(HumanoidCharacterProfile profile, IPrototypeManager proto)
    {
        return LegStyleRules.IsDigitigrade(profile.Species, profile.LegStance, proto);
    }

    /// <summary>The slots the given traits put their own parts in, with the trait that claims each.</summary>
    public static Dictionary<WFAugmentSlot, ProtoId<TraitPrototype>> ClaimedSlots(IEnumerable<ProtoId<TraitPrototype>> traits,
        IPrototypeManager proto)
    {
        var claimed = new Dictionary<WFAugmentSlot, ProtoId<TraitPrototype>>();
        foreach (var trait in traits)
        {
            if (!proto.TryIndex<WFAugmentTraitSlotsPrototype>(trait.Id, out var slots))
                continue;
            foreach (var slot in slots.Slots)
                claimed.TryAdd(slot, trait);
        }

        return claimed;
    }

    /// <summary>
    /// Drops choices the species can't take, slots a trait claims, augments that don't belong to their slot and
    /// styles that don't fit, and gives a styled augment without a style the first one that fits.
    /// </summary>
    public static Dictionary<WFAugmentSlot, WFAugmentChoice> Validate(string species,
        IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice>? choices,
        IEnumerable<ProtoId<TraitPrototype>> traits,
        IPrototypeManager proto,
        IComponentFactory factory)
    {
        var claimed = ClaimedSlots(traits, proto);
        var valid = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
        if (choices == null)
            return valid;

        foreach (var (slot, choice) in choices)
        {
            if (!Enum.IsDefined(typeof(WFAugmentSlot), slot)
                || claimed.ContainsKey(slot)
                || !proto.TryIndex(choice.Augment, out var augment)
                || augment.Slot != slot
                || augment.Kind == WFAugmentKind.Upgrade
                || augment.SpeciesBlacklist.Contains(species)
                || !SlotAvailable(species, slot, proto, factory))
                continue;

            ProtoId<WFAugmentStylePrototype>? style = null;
            if (augment.Styled)
            {
                if (choice.Style is { } picked && proto.TryIndex(picked, out var pickedStyle) && StyleFits(pickedStyle, slot))
                    style = picked;
                else if (StylesFor(slot, proto).FirstOrDefault() is { } first)
                    style = first.ID;
                else
                    continue;
            }

            valid[slot] = new WFAugmentChoice(augment.ID, style);
        }

        return valid;
    }

    /// <summary>Every augment the profile picked: its slot choices and its implants.</summary>
    public static IEnumerable<ProtoId<WFAugmentPrototype>> Picked(HumanoidCharacterProfile profile)
    {
        return profile.Augments.Values.Select(c => c.Augment).Concat(profile.AugmentImplants);
    }

    /// <summary>The price due for priced augments the profile picked but hasn't unlocked.</summary>
    public static int Due(HumanoidCharacterProfile profile, IPrototypeManager proto)
    {
        var due = 0;
        foreach (var id in Picked(profile).Distinct())
        {
            if (proto.TryIndex(id, out var augment) && augment.Price > 0 && !profile.AugmentUnlocks.Contains(id))
                due += augment.Price;
        }

        return due;
    }

    /// <summary>The unlocked augments the profile no longer picks, which are sold back when it is saved.</summary>
    public static List<WFAugmentPrototype> Sold(HumanoidCharacterProfile profile, IPrototypeManager proto)
    {
        var picked = Picked(profile).ToHashSet();
        return profile.AugmentUnlocks
            .Where(u => !picked.Contains(u))
            .Select(u => proto.TryIndex(u, out var augment) ? augment : null)
            .OfType<WFAugmentPrototype>()
            .OrderBy(a => a.ID)
            .ToList();
    }

    /// <summary>Choices and implants as stored in the database: slot:augment[:style] and Implant:augment entries.</summary>
    public static string ToStored(IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice> choices,
        IEnumerable<ProtoId<WFAugmentPrototype>> implants)
    {
        var builder = new StringBuilder();
        foreach (var (slot, choice) in choices.OrderBy(c => c.Key))
        {
            if (builder.Length > 0)
                builder.Append(EntrySeparator);
            builder.Append(slot).Append(FieldSeparator).Append(choice.Augment.Id);
            if (choice.Style is { } style)
                builder.Append(FieldSeparator).Append(style.Id);
        }

        foreach (var implant in implants.Select(i => i.Id).Order())
        {
            if (builder.Length > 0)
                builder.Append(EntrySeparator);
            builder.Append(ImplantEntry).Append(FieldSeparator).Append(implant);
        }

        return builder.ToString();
    }

    /// <summary>Reads the slot choices of <see cref="ToStored(IReadOnlyDictionary{WFAugmentSlot, WFAugmentChoice}, IEnumerable{ProtoId{WFAugmentPrototype}})"/> text. Entries it can't read are dropped.</summary>
    public static Dictionary<WFAugmentSlot, WFAugmentChoice> FromStored(string? stored)
    {
        var choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
        if (string.IsNullOrEmpty(stored))
            return choices;

        foreach (var entry in stored.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = entry.Split(FieldSeparator);
            if (fields.Length < 2 || !Enum.TryParse<WFAugmentSlot>(fields[0], out var slot))
                continue;

            choices[slot] = new WFAugmentChoice(fields[1], fields.Length > 2 ? fields[2] : null);
        }

        return choices;
    }

    /// <summary>Reads the implants of stored choices, including those saved in the old one-per-slot implant slots.</summary>
    public static HashSet<ProtoId<WFAugmentPrototype>> ImplantsFromStored(string? stored)
    {
        var implants = new HashSet<ProtoId<WFAugmentPrototype>>();
        if (string.IsNullOrEmpty(stored))
            return implants;

        foreach (var entry in stored.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = entry.Split(FieldSeparator);
            if (fields.Length >= 2 && (fields[0] == ImplantEntry || LegacyImplantSlots.Contains(fields[0])))
                implants.Add(fields[1]);
        }

        return implants;
    }

    /// <summary>Unlocked augments as stored in the database, separated by semicolons.</summary>
    public static string ToStored(IEnumerable<ProtoId<WFAugmentPrototype>> unlocks)
    {
        return string.Join(EntrySeparator, unlocks.Select(u => u.Id).Order());
    }

    /// <summary>Reads stored unlocks.</summary>
    public static HashSet<ProtoId<WFAugmentPrototype>> UnlocksFromStored(string? stored)
    {
        var unlocks = new HashSet<ProtoId<WFAugmentPrototype>>();
        if (string.IsNullOrEmpty(stored))
            return unlocks;

        foreach (var id in stored.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            unlocks.Add(id);
        return unlocks;
    }
}
