using Robust.Shared.GameStates;

namespace Content.Shared._Mono.Shipyard;

/// <summary>
/// Component that tracks which users have guest access to a ship.
/// </summary>
// WOLFGATE(Performance): was AutoGenerateComponentState; WFDeletedRefStateSystem sends the sets, whose entries can be deleted first
[RegisterComponent, NetworkedComponent]
public sealed partial class ShipGuestAccessComponent : Component
{
    /// <summary>
    /// Set of ID card EntityUids that have been granted guest access to this ship.
    /// </summary>
    [DataField] // WOLFGATE(Performance): networked by WFDeletedRefStateSystem
    public HashSet<EntityUid> GuestIdCards = new();

    /// <summary>
    /// Set of cyborg EntityUids that have been granted guest access to this ship.
    /// </summary>
    [DataField] // WOLFGATE(Performance): networked by WFDeletedRefStateSystem
    public HashSet<EntityUid> GuestCyborgs = new();
}
