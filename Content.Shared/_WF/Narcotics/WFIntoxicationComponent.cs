using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Narcotics;

/// <summary>
/// Drug levels on a body, one per profile, with the time spent high and any running crashes. Added on the first dose
/// and removed once everything has worn off.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFIntoxicationComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>Level of each profile, in effective units.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> Levels = new();

    /// <summary>Seconds each profile has spent at or above its first tier this high.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> TimeAbove = new();

    /// <summary>Seconds left on each running crash.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> Crashes = new();

    /// <summary>Damage a <see cref="WFDrugTrait.Damper"/> tier has held off, repaid as Poison during the crash.</summary>
    [DataField]
    public float Debt;

    /// <summary>The phase each profile was last announced in.</summary>
    [DataField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, WFDrugPhase> Phases = new();

    /// <summary>Tier traits whose components this component added, so it removes only those.</summary>
    [DataField]
    public HashSet<WFDrugTrait> Granted = new();
}
