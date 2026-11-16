using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Augments;

/// <summary>An iatric monitor in the head: an action that shows its body its own health analyzer readout.</summary>
[RegisterComponent]
public sealed partial class WFIatricMonitorComponent : Component
{
    /// <summary>
    /// The action, which is also the analyzer: it carries the analyzer's component and window, and raises
    /// <see cref="WFIatricScanEvent"/>.
    /// </summary>
    [DataField]
    public EntProtoId Action = "WFActionIatricScan";

    [ViewVariables]
    public EntityUid? ActionEntity;
}

/// <summary>Marks the action of an iatric monitor, so its scan stops when its window closes.</summary>
[RegisterComponent]
public sealed partial class WFIatricScannerComponent : Component;

public sealed partial class WFIatricScanEvent : InstantActionEvent;
