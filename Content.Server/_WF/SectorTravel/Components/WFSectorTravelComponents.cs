using System.Numerics;
using Content.Shared._WF.SectorTravel;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorTravel.Components;

/// <summary>A jump beacon: a ship inside its zone hails it, and it opens a portal ahead of the ship to the linked sector.</summary>
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

    /// <summary>Seconds from a confirmed hail to the bolt that opens the portal.</summary>
    [DataField]
    public float Countdown = 10f;

    /// <summary>How long an open portal waits for its ship.</summary>
    [DataField]
    public TimeSpan PortalLifetime = TimeSpan.FromSeconds(60);

    /// <summary>Metres between the ship's nose and the portal it opens.</summary>
    [DataField]
    public float PortalGap = 12f;

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

    /// <summary>Structural damage taken over the countdown that cancels the jump.</summary>
    [DataField]
    public float CancelDamage = 100f;
}

/// <summary>On a ship a beacon is sending through: the countdown, then the open portal it has to cross.</summary>
[RegisterComponent]
public sealed partial class WFBeaconJumpComponent : Component
{
    [DataField]
    public EntityUid Beacon;

    [DataField]
    public ProtoId<WFSectorPrototype> To;

    /// <summary>When the beacon fires the bolt that opens the portal.</summary>
    [DataField]
    public TimeSpan Fires;

    /// <summary>Damage summed over the ship's anchored structure when the countdown began.</summary>
    [DataField]
    public float Damage;

    /// <summary>How many anchored damageable entities the ship had when the countdown began; a loss counts as damage.</summary>
    [DataField]
    public int Structure;

    /// <summary>The open portal, once the bolt has fired.</summary>
    [DataField]
    public EntityUid? Portal;

    /// <summary>The portal's centre and rotation on the map, the way through it, and half its width and depth.</summary>
    [DataField]
    public Vector2 PortalAt;

    [DataField]
    public Angle PortalAngle;

    [DataField]
    public Vector2 Forward;

    [DataField]
    public float HalfWidth;

    [DataField]
    public float HalfDepth;

    /// <summary>When an uncrossed portal closes.</summary>
    [DataField]
    public TimeSpan Expires;

    /// <summary>Set once the ship has crossed and is in the FTL sequence.</summary>
    [DataField]
    public bool Departed;

    /// <summary>An NPC ship's jump: it goes through as soon as the portal opens.</summary>
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

/// <summary>The beacon's traffic control: a hidden shuttle console on the platform that answers hails for the beacon.</summary>
[RegisterComponent]
public sealed partial class WFJumpBeaconConsoleComponent : Component
{
    [DataField]
    public EntityUid Beacon;

    /// <summary>Whether the current caller has had the beacon's answer.</summary>
    [DataField]
    public bool Answered;

    /// <summary>When the beacon closes the channel after answering.</summary>
    [DataField]
    public TimeSpan HangUp;
}
