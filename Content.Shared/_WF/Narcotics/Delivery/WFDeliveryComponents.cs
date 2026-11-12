using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Narcotics.Delivery;

/// <summary>A powder baggie. Used on a table, it lays out a line. Takes only the listed reagents.</summary>
[RegisterComponent]
public sealed partial class WFBaggieComponent : Component
{
    [DataField]
    public string Solution = "baggie";

    /// <summary>Units in one line.</summary>
    [DataField]
    public FixedPoint2 LineAmount = FixedPoint2.New(2.5);

    [DataField]
    public EntProtoId Line = "WFPowderLine";

    /// <summary>The only reagents it takes; null takes anything.</summary>
    [DataField]
    public HashSet<ProtoId<ReagentPrototype>>? Reagents;
}

/// <summary>A line of powder on a surface. Snorting it moves its solution into the bloodstream.</summary>
[RegisterComponent]
public sealed partial class WFPowderLineComponent : Component
{
    [DataField]
    public string Solution = "line";

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(1.5);
}

/// <summary>
/// A water pipe that works from the hand. Light the loaded bowl, then each use takes one hit from whatever is in it,
/// as long as the reservoir holds enough water.
/// </summary>
[RegisterComponent]
public sealed partial class WFBongComponent : Component
{
    public const string BowlSlotId = "bowl";

    [DataField(required: true)]
    public ItemSlot Bowl = new();

    [DataField]
    public string Water = "water";

    /// <summary>Water the reservoir needs before it draws.</summary>
    [DataField]
    public FixedPoint2 MinWater = FixedPoint2.New(5);

    /// <summary>Units drawn from the bowl per hit.</summary>
    [DataField]
    public FixedPoint2 HitAmount = FixedPoint2.New(3);

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(2);

    [DataField]
    public EntProtoId Puff = "Smoke";

    [DataField]
    public bool Lit;
}

[Serializable, NetSerializable]
public sealed partial class WFSnortLineDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WFBongHitDoAfterEvent : SimpleDoAfterEvent;
