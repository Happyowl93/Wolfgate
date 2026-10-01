using System.Numerics; // WOLFGATE(Species)
using Content.Shared.Actions;
// WOLFGATE(Species) START: unused after the switch to circle-based squeezing
// using DrawDepth = Content.Shared.DrawDepth.DrawDepth;
// WOLFGATE END
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._DV.Abilities;

// WOLFGATE(Species) START: HardLight balance. The Delta-V original dropped the mob under tables instead.
/// <summary>
/// Lets a mob toggle sneaking: it moves slower and its circle fixtures shrink, so it can squeeze past mobs and
/// furniture, and it is drawn under tables it has climbed onto. Walking through tables stays blocked.
/// See <see cref="SharedCrawlUnderObjectsSystem"/>.
/// </summary>
// WOLFGATE END
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrawlUnderObjectsComponent : Component
{
    [DataField]
    public EntityUid? ToggleHideAction;

    [DataField]
    public EntProtoId? ActionProto;

    // WOLFGATE(Species) START: sneak state is networked; HardLight circle squeezing replaces the fixture mask list
    // upstream original, kept for merges:
    // [DataField]
    // public bool Enabled = false;
    //
    // /// <summary>
    // ///     List of fixtures that had their collision mask changed.
    // ///     Required for re-adding the collision mask.
    // /// </summary>
    // [DataField, AutoNetworkedField]
    // public List<(string key, int originalMask)> ChangedFixtures = new();

    [DataField, AutoNetworkedField]
    public bool Enabled;
    // WOLFGATE END

    // WOLFGATE(Species): documents the client-only draw depth field
    /// <summary>Client only: the draw depth the sprite had before it was dropped under the tables.</summary>
    [DataField]
    public int? OriginalDrawDepth;

    [DataField]
    public float SneakSpeedModifier = 0.7f;

    // WOLFGATE(Species) START: HardLight circle squeeze fields
    /// <summary>
    /// Circle fixture radius multiplier while sneaking, relative to the unsqueezed radius.
    /// </summary>
    [DataField]
    public float SqueezeRadiusScale = 1f;

    /// <summary>
    /// Circle fixture radius multiplier applied once at startup, so a species can be bulkier than its prototype
    /// radius while standing.
    /// </summary>
    [DataField]
    public float UnsqueezedRadiusScale = 1f;

    /// <summary>
    /// Circle geometry captured when sneaking starts and restored when it ends, so cycles never drift.
    /// </summary>
    public List<(string key, Vector2 position, float radius)> ChangedCircles = new();

    /// <summary>
    /// Guards the unsqueezed baseline inflation so it is only applied once.
    /// </summary>
    public bool BaselineInflationApplied;

    /// <summary>
    /// Geometry captured while downed, which uses the squeeze scale too.
    /// </summary>
    public List<(string key, Vector2 position, float radius)> DownedCircles = new();

    public bool DownedScaleApplied;
    // WOLFGATE END
}

[Serializable, NetSerializable]
public enum SneakMode : byte
{
    Enabled
}

public sealed partial class ToggleCrawlingStateEvent : InstantActionEvent { }

// WOLFGATE(Species) START: unused once sneak state moved onto the networked component fields directly
// [Serializable, NetSerializable]
// public sealed partial class CrawlingUpdatedEvent(bool enabled = false) : EventArgs
// {
//     public readonly bool Enabled = enabled;
// }
// WOLFGATE END
