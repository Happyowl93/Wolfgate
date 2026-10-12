using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// A drydock console that charges materials from its cranes' storage and their linked silos instead of credits.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockMaterialPaymentComponent : Component
{
    /// <summary>Material units charged per credit of work that has no material bill of its own.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, float> Rates = new();
}
