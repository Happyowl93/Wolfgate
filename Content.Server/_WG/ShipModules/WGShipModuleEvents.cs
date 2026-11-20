using Content.Shared._WG.ShipModules;

namespace Content.Server._WG.ShipModules;

/// <summary>Raised on a powered module so its own system can lock it; power, FTL and shields are already checked.</summary>
[ByRefEvent]
public record struct WGShipModuleLockEvent(EntityUid Grid)
{
    public WGShipModuleLock Lock = WGShipModuleLock.None;
}

/// <summary>Raised on a ready module before a charge is spent; cancelling refuses the use and shows <see cref="Reason"/>.</summary>
[ByRefEvent]
public record struct WGShipModuleActivateAttemptEvent(EntityUid Grid, EntityUid? User)
{
    public bool Cancelled;
    public LocId? Reason;
}

/// <summary>Raised on a module once a charge is spent: do the effect here.</summary>
[ByRefEvent]
public readonly record struct WGShipModuleActivatedEvent(EntityUid Grid, EntityUid? User);
