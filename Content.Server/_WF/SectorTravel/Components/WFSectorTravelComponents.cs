using Content.Shared._WF.SectorTravel;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorTravel.Components;

/// <summary>A jump beacon: ships inside its zone spool here and jump to the linked sector's beacon.</summary>
[RegisterComponent]
public sealed partial class WFJumpBeaconComponent : Component
{
    /// <summary>The sector the beacon stands in.</summary>
    [DataField]
    public ProtoId<WFSectorPrototype> Sector;

    /// <summary>The sector it jumps to.</summary>
    [DataField]
    public ProtoId<WFSectorPrototype> Link;

    /// <summary>Whether sector control refuses claims on its cell.</summary>
    [DataField]
    public bool Protected;

    /// <summary>The faction that raids it, its schedule, and when it next moves on the cell.</summary>
    [DataField]
    public ProtoId<Content.Shared._WF.SectorControl.WFSectorFactionPrototype>? Raiders;

    [DataField]
    public TimeSpan RaidInterval = TimeSpan.FromMinutes(60);

    [DataField]
    public TimeSpan RaidWarning = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan NextRaid;

    /// <summary>A ship whose grid centre is within this many metres can jump.</summary>
    [DataField]
    public float ZoneRadius = 1500f;

    /// <summary>Spool time before mass scaling, in seconds.</summary>
    [DataField]
    public float Spool = 20f;

    /// <summary>Travel time is this plus <see cref="TravelPerUnit"/> per galaxy unit, in seconds.</summary>
    [DataField]
    public float TravelBase = 30f;

    [DataField]
    public float TravelPerUnit = 10f;

    /// <summary>Arrivals land on a ring this far from the far beacon, in metres.</summary>
    [DataField]
    public float ArrivalMin = 1000f;

    [DataField]
    public float ArrivalMax = 3000f;

    /// <summary>How long after arriving a ship can't beacon-jump again.</summary>
    [DataField]
    public TimeSpan Lockout = TimeSpan.FromSeconds(120);

    /// <summary>Structural damage taken over the spool that cancels the jump.</summary>
    [DataField]
    public float CancelDamage = 100f;

    /// <summary>Jump fuel per galaxy unit before the ship's mass factor.</summary>
    [DataField]
    public float FuelPerUnit = 40f;
}

/// <summary>On a ship spooling a beacon jump: what to watch until it leaves.</summary>
[RegisterComponent]
public sealed partial class WFBeaconJumpComponent : Component
{
    [DataField]
    public EntityUid Beacon;

    [DataField]
    public ProtoId<WFSectorPrototype> To;

    /// <summary>The fuel the jump spends as the ship enters hyperspace.</summary>
    [DataField]
    public float Cost;

    /// <summary>Damage summed over the ship's anchored structure when the spool began.</summary>
    [DataField]
    public float Damage;

    /// <summary>How many anchored damageable entities the ship had when the spool began; a loss counts as damage.</summary>
    [DataField]
    public int Structure;

    /// <summary>Set once the ship has entered hyperspace.</summary>
    [DataField]
    public bool Departed;

    /// <summary>An NPC ship's jump: no drive or fuel needed.</summary>
    [DataField]
    public bool Npc;
}

/// <summary>On a ship that arrived by beacon: it can't beacon-jump again until <see cref="Until"/>.</summary>
[RegisterComponent]
public sealed partial class WFBeaconLockoutComponent : Component
{
    [DataField]
    public TimeSpan Until;
}

/// <summary>Jump fuel held by an FTL drive. Beacon jumps spend it; Fusion Fuel crates refill it.</summary>
[RegisterComponent]
public sealed partial class WFJumpFuelComponent : Component
{
    [DataField]
    public float Fuel = 300f;

    [DataField]
    public float Capacity = 300f;

    /// <summary>Fuel one crate of <see cref="Good"/> adds.</summary>
    [DataField]
    public float PerCrate = 100f;

    [DataField]
    public EntProtoId Good = "TradeGoodFusionFuel";
}

/// <summary>A ship machine that takes fusion fuel crates and fills the jump fuel tank of the drive the ship jumps with.</summary>
[RegisterComponent]
public sealed partial class WFJumpFuelIntakeComponent : Component;

/// <summary>While powered, cancels any beacon spool within <see cref="Range"/> of it.</summary>
[RegisterComponent]
public sealed partial class WFJumpInterdictorComponent : Component
{
    [DataField]
    public float Range = 1000f;
}
