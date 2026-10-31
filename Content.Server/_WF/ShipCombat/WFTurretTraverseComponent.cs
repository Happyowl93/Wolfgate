using Robust.Shared.Map;

namespace Content.Server._WF.ShipCombat;

/// <summary>Limits how fast a rotating ship gun turns; it holds fire until it is laid on its target.</summary>
[RegisterComponent, Access(typeof(WFTurretTraverseSystem))]
public sealed partial class WFTurretTraverseComponent : Component
{
    /// <summary>Turn rate in degrees per second.</summary>
    [DataField]
    public float TurnRate = 90f;

    /// <summary>Degrees off the target bearing at which the gun may still fire.</summary>
    [DataField]
    public float Tolerance = 3f;

    /// <summary>Point the gun is turning toward, if any.</summary>
    [ViewVariables]
    public EntityCoordinates? AimTarget;

    /// <summary>When the gun stops following its last aim point.</summary>
    [ViewVariables]
    public TimeSpan AimUntil;
}
