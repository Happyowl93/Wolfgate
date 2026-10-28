using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.TradeGoods.Components;

/// <summary>A priority consignment: pays well at its destination before its deadline, less after, little elsewhere.</summary>
[RegisterComponent]
public sealed partial class WFConsignmentComponent : Component
{
    [DataField]
    public int Value = 20000;

    [DataField]
    public int OnTimeBonus = 10000;

    [DataField]
    public int LatePenalty = 10000;

    /// <summary>What any other chute pays for it.</summary>
    [DataField]
    public int Elsewhere = 2000;

    [DataField]
    public TimeSpan Window = TimeSpan.FromMinutes(25);

    [ViewVariables]
    public EntityUid? Origin;

    [ViewVariables]
    public EntityUid? Destination;

    [ViewVariables]
    public TimeSpan Deadline;

    /// <summary>The last ship announced as carrying it.</summary>
    [ViewVariables]
    public EntityUid? Announced;
}

/// <summary>
/// A trade crate carrying a TSF transponder. Once it leaves the station it was bought at, its pings tell PDV and the
/// Underground roughly where it is, and whoever delivers it to their own drop-off is paid well.
/// </summary>
[RegisterComponent]
public sealed partial class WFClassifiedCargoComponent : Component
{
    [ViewVariables]
    public bool Awake;

    /// <summary>The grid it was bought or loaded on; it wakes on leaving it.</summary>
    [ViewVariables]
    public EntityUid? Home;

    /// <summary>The grid it was last announced on, or null while adrift.</summary>
    [ViewVariables]
    public EntityUid? Aboard;

    [ViewVariables]
    public bool Adrift;

    [ViewVariables]
    public WFSectorCell? LastCell;

    [ViewVariables]
    public TimeSpan LastPing;

    [ViewVariables]
    public TimeSpan JammedUntil;

    /// <summary>Set when a chute takes it, so its deletion is not reported as destruction.</summary>
    [ViewVariables]
    public bool Delivered;
}

/// <summary>A one-use device that silences a classified crate's transponder for a while.</summary>
[RegisterComponent]
public sealed partial class WFTransponderJammerComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(5);
}
