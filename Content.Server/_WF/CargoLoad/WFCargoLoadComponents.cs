namespace Content.Server._WF.CargoLoad;

/// <summary>A grid's cargo: its own load and the thrust factor its whole docked set flies with in space.</summary>
[RegisterComponent]
public sealed partial class WFCargoLoadComponent : Component
{
    /// <summary>Tonnes of cargo aboard this grid alone.</summary>
    [ViewVariables]
    public float OwnLoad;

    /// <summary>Tonnes aboard the grid's cargo set.</summary>
    [ViewVariables]
    public float SetLoad;

    /// <summary>Share of thrust the set keeps in space.</summary>
    [ViewVariables]
    public float SpaceFactor = 1f;
}

/// <summary>A helm on a loaded ship: its examine line, and which load warnings its pilots have had.</summary>
[RegisterComponent]
public sealed partial class WFCargoHelmComponent : Component
{
    /// <summary>Bands whose warning has gone out and not yet re-armed.</summary>
    [ViewVariables]
    public readonly HashSet<int> Told = new();

    /// <summary>Pilots already told the current load.</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Pilots = new();
}
