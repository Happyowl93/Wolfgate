using Robust.Shared.Serialization;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// UI key for the drydock console.
/// </summary>
[Serializable, NetSerializable]
public enum WFDrydockConsoleUiKey : byte
{
    Key
}

/// <summary>
/// State of the drydock window: the caller's berthed ship, its damage, repair price, job progress, refit options and booking.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockConsoleState : BoundUserInterfaceState
{
    /// <summary>Name of the berthed ship, or null when none is docked.</summary>
    public string? ShipName;

    /// <summary>Hull tiles missing from the repair snapshot.</summary>
    public int MissingTiles;

    /// <summary>Parts missing from the repair snapshot.</summary>
    public int MissingParts;

    /// <summary>Anchored structures that need healing.</summary>
    public int DamagedParts;

    /// <summary>Total price of the next repair job, including the base fee.</summary>
    public int RepairPrice;

    /// <summary>Whether a job is running.</summary>
    public bool Working;

    /// <summary>Lowest power ratio among the berth's cranes, 0 to 1.</summary>
    public float PowerRatio = 1f;

    /// <summary>Cranes linked to this console on its grid.</summary>
    public int CraneCount;

    /// <summary>Linked cranes whose arm lane is blocked, so they do no work.</summary>
    public int BlockedCranes;

    /// <summary>Whether the cranes are still moving into place over the ship.</summary>
    public bool Deploying;

    /// <summary>Job progress from 0 to 1.</summary>
    public float Progress;

    /// <summary>Seconds until the running job is estimated to finish, 0 when idle.</summary>
    public float RemainingSeconds;

    /// <summary>Refit options offered by the console.</summary>
    public List<WFDrydockUpgradeEntry> Upgrades = new();

    /// <summary>Whether this console books work for a berth instead of starting it.</summary>
    public bool Booking;

    /// <summary>Berth booked for this ship, or null if none.</summary>
    public string? BookedBerth;

    /// <summary>Seconds left on the booking.</summary>
    public float BookingSeconds;

    /// <summary>Localized line explaining why nothing can be booked, or null.</summary>
    public string? Notice;

    /// <summary>Whether this console takes materials from its own storage instead of credits.</summary>
    public bool PaysWithMaterials;

    /// <summary>Bill of the next repair when paying with materials.</summary>
    public Dictionary<string, int> RepairMaterials = new();

    /// <summary>Materials held in the console's storage.</summary>
    public Dictionary<string, int> StoredMaterials = new();
}

/// <summary>
/// One refit line in the console state.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockUpgradeEntry
{
    /// <summary>Upgrade prototype id.</summary>
    public string Id = string.Empty;

    /// <summary>Localized name.</summary>
    public string Name = string.Empty;

    /// <summary>Number of matching parts on the berthed ship.</summary>
    public int Parts;

    /// <summary>Lowest tier among the matching parts; 0 is stock.</summary>
    public int MinTier;

    /// <summary>Highest tier available for this upgrade.</summary>
    public int MaxTier;

    /// <summary>Price per part for the next tier; 0 when maxed or no parts.</summary>
    public int NextPrice;

    /// <summary>Bill for the next tier across all parts when paying with materials.</summary>
    public Dictionary<string, int> NextMaterials = new();
}

/// <summary>
/// Books a repair for the caller's ship.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockRepairMessage : BoundUserInterfaceMessage;

/// <summary>
/// Cancels the booking held for the caller's ship.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockCancelBookingMessage : BoundUserInterfaceMessage;

/// <summary>
/// Books a refit of all parts for one upgrade line, one tier up.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDrydockUpgradeMessage(string upgrade) : BoundUserInterfaceMessage
{
    /// <summary>Upgrade prototype id to refit.</summary>
    public string Upgrade = upgrade;
}
