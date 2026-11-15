using Robust.Shared.Map;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>On a crewman holding an armed grenade: where it goes and when it leaves his hand. See <c>WFCrewGrenadeSystem</c>.</summary>
[RegisterComponent]
public sealed partial class WFCrewArmedGrenadeComponent : Component
{
    [ViewVariables]
    public EntityUid Grenade;

    /// <summary>The spot it is thrown to.</summary>
    [ViewVariables]
    public EntityCoordinates At;

    /// <summary>When he lets go of it, once he has held it for his cook time.</summary>
    [ViewVariables]
    public TimeSpan ThrowTime;
}
