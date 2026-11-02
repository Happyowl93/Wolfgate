using Robust.Shared.Audio;

namespace Content.Shared._WF.BoardingAction;

/// <summary>
/// A machine that locks onto a contact the pilot picks and stops it from jumping while the lock holds.
/// A ship only ever runs one, however many are anchored on it.
/// </summary>
[RegisterComponent]
public sealed partial class WFFtlDisruptorComponent : Component
{
    /// <summary>How far from the machine the target may be, in metres.</summary>
    [DataField]
    public float Range = 1500f;

    /// <summary>How long the lock takes before the target's FTL goes down.</summary>
    [DataField]
    public TimeSpan LockTime = TimeSpan.FromSeconds(4);

    /// <summary>How far from the cursor a contact may be and still be picked, in metres.</summary>
    [DataField]
    public float CursorReach = 64f;

    /// <summary>Power drawn while on standby.</summary>
    [DataField]
    public float IdleLoad = 500f;

    /// <summary>Power drawn while locking or holding.</summary>
    [DataField]
    public float ActiveLoad = 8000f;

    [DataField]
    public SoundSpecifier LockedSound = new SoundPathSpecifier("/Audio/Machines/vessel_warning.ogg");

    /// <summary>The contact being locked or held.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>When the lock in progress completes.</summary>
    [ViewVariables]
    public TimeSpan LockDone;

    /// <summary>Whether the lock has completed.</summary>
    [ViewVariables]
    public bool Locked;
}
