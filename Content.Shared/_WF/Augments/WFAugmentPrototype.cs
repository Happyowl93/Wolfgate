using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Augments;

/// <summary>
/// A piece of cyberware a character can carry from round to round: picked or bought in the creator, or installed in
/// a round and kept. Every cybernetic part, organ and autosurgeon upgrade the game has is listed as one.
/// </summary>
[Prototype("wfAugment")]
public sealed partial class WFAugmentPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The name in the creator; the installed entity's name if left out.</summary>
    [DataField]
    public LocId? Name;

    /// <summary>The creator's tooltip; the installed entity's description if left out.</summary>
    [DataField]
    public LocId? Description;

    /// <summary>The creator's icon; the installed entity's sprite if left out.</summary>
    [DataField]
    public SpriteSpecifier? Icon;

    [DataField(required: true)]
    public WFAugmentSlot Slot;

    [DataField(required: true)]
    public WFAugmentKind Kind;

    /// <summary>The limb that replaces the slot's part, for <see cref="WFAugmentKind.Limb"/>.</summary>
    [DataField]
    public EntProtoId? Part;

    /// <summary>The organ that replaces the slot's organ, for <see cref="WFAugmentKind.Organ"/>.</summary>
    [DataField]
    public EntProtoId? Organ;

    /// <summary>The upgrading autosurgeon whose components it adds, for <see cref="WFAugmentKind.Upgrade"/>.</summary>
    [DataField]
    public EntProtoId? Autosurgeon;

    /// <summary>Whether an implant needs a flesh or a synthetic part, for <see cref="WFAugmentKind.Upgrade"/>.</summary>
    [DataField]
    public WFAugmentHost Host;

    /// <summary>How many implants the limb or plating holds, for <see cref="WFAugmentKind.Limb"/> and
    /// <see cref="WFAugmentKind.Plating"/>.</summary>
    [DataField]
    public int ImplantSlots = 1;

    /// <summary>Whether the player picks a style for it. Limbs and plating are styled.</summary>
    [DataField]
    public bool Styled;

    /// <summary>
    /// One-time price in spesos, taken from the character's bank the first time it is saved with this augment.
    /// Zero for augments that give no advantage. Taking it off in the creator refunds
    /// <see cref="WFAugmentRules.RefundPercent"/> of it if it was paid for. Scavenged loot is weighted by its inverse;
    /// free ones never drop.
    /// </summary>
    [DataField]
    public int Price;

    /// <summary>Species that can't take it.</summary>
    [DataField]
    public List<ProtoId<SpeciesPrototype>> SpeciesBlacklist = new();

    /// <summary>Sort order within the slot, lowest first.</summary>
    [DataField]
    public int Order;
}
