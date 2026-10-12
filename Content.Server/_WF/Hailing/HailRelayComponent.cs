namespace Content.Server._WF.Hailing;

/// <summary>
/// Relayed service windows on a helm, added the first time a service opens on it. Not saved.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class HailRelayComponent : Component
{
    /// <summary>UI keys this system added to the helm.</summary>
    public HashSet<Enum> RelayKeys = new();

    /// <summary>Open relayed UI keys and the service each relays.</summary>
    public Dictionary<Enum, EntityUid> Links = new();

    /// <summary>Last service state mirrored onto the helm, per relayed key.</summary>
    public Dictionary<Enum, BoundUserInterfaceState?> Mirrored = new();
}
