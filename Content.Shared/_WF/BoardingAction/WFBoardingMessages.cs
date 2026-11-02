using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.BoardingAction;

/// <summary>The pilot pressed the ship module key with the cursor at this point.</summary>
[Serializable, NetSerializable]
public sealed class WFFtlDisruptorToggleEvent(MapCoordinates cursor) : EntityEventArgs
{
    public MapCoordinates Cursor = cursor;
}

/// <summary>Helm request to let go of the ship's hacking cable.</summary>
[Serializable, NetSerializable]
public sealed class WFBoardingReleaseCableMessage : BoundUserInterfaceMessage;

/// <summary>What the helm banner shows about hacks and disruption on this ship.</summary>
[Serializable, NetSerializable]
public sealed class WFBoardingHelmState
{
    /// <summary>Ships with a hacking cable in this hull.</summary>
    public string[] Hackers = Array.Empty<string>();

    /// <summary>Whether a hack has finished and this ship is locked down.</summary>
    public bool Lockdown;

    /// <summary>Ships disrupting this one's FTL from range.</summary>
    public string[] Disruptors = Array.Empty<string>();

    /// <summary>The ship this one's hacking cable is latched to.</summary>
    public string? CableTarget;

    /// <summary>The ship this one's FTL disruptor is on.</summary>
    public string? DisruptorTarget;

    /// <summary>Whether the disruptor has finished locking on.</summary>
    public bool DisruptorLocked;

    /// <summary>Whether there is nothing to show.</summary>
    public bool Empty => Hackers.Length == 0 && Disruptors.Length == 0 && CableTarget == null && DisruptorTarget == null;
}

/// <summary>Appearance key for the FTL disruptor's lights.</summary>
[Serializable, NetSerializable]
public enum WFFtlDisruptorVisuals : byte
{
    Status,
}

/// <summary>What the FTL disruptor's lights show.</summary>
[Serializable, NetSerializable]
public enum WFFtlDisruptorStatus : byte
{
    Off,
    Idle,
    Locking,
    Locked,
}

/// <summary>Sprite layers of the FTL disruptor.</summary>
[Serializable, NetSerializable]
public enum WFFtlDisruptorVisualLayers : byte
{
    Status,
}
