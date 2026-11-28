using Robust.Shared.Serialization;

namespace Content.Shared._WG.DockLock;

/// <summary>Asks the helm to lock or unlock one of its ship's docking ports.</summary>
[Serializable, NetSerializable]
public sealed class WGDockLockToggleMessage : BoundUserInterfaceMessage
{
    public NetEntity Dock;
    public bool Locked;

    public WGDockLockToggleMessage(NetEntity dock, bool locked)
    {
        Dock = dock;
        Locked = locked;
    }
}
