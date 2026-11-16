namespace Content.Shared._WF.Augments;

/// <summary>
/// Where on the body an augment goes; a character holds at most one augment per slot. Augments are applied in this
/// order, so a limb comes before the hand or foot on it and before an implant in it. Stored by name.
/// </summary>
public enum WFAugmentSlot : byte
{
    Head,
    Chest,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
    Eyes,
    Heart,
    Lungs,
    Liver,
    Stomach,
    LeftHand,
    RightHand,
    LeftFoot,
    RightFoot,
    LeftArmImplant,
    RightArmImplant,
    Dermal,
    Muscles,
}

/// <summary>What an augment does to its slot.</summary>
public enum WFAugmentKind : byte
{
    /// <summary>Plating drawn over the slot in a style; the part itself is unchanged (head, chest).</summary>
    Plating,

    /// <summary>The limb is replaced with <see cref="WFAugmentPrototype.Part"/>.</summary>
    Limb,

    /// <summary>The limb is taken off and nothing replaces it.</summary>
    Missing,

    /// <summary>The organ in the slot is replaced with <see cref="WFAugmentPrototype.Organ"/>.</summary>
    Organ,

    /// <summary>The part or organ takes the upgrade <see cref="WFAugmentPrototype.Autosurgeon"/> installs.</summary>
    Upgrade,
}
