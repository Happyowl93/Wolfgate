using System.Numerics;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.SectorTravel;

/// <summary>One sector of the region: a map of its own, joined to the others only by jump beacons.</summary>
[Prototype("wfSector")]
public sealed partial class WFSectorPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>Where the sector sits in the region, in galaxy units; jump fuel and travel time use the distance.</summary>
    [DataField]
    public Vector2 GalaxyPosition;

    /// <summary>The one sector on the game ticker's default map, where players arrive.</summary>
    [DataField]
    public bool Home;

    /// <summary>The points of interest loaded onto the sector's map. The home sector's come from the adventure rule.</summary>
    [DataField]
    public List<WFSectorPoi> Pois = new();

    /// <summary>Whether the server's worldgen config (asteroids, debris) is applied to the sector's map.</summary>
    [DataField]
    public bool Worldgen = true;

    /// <summary>The parallax prototype for the sector's sky; null copies the home sector's.</summary>
    [DataField]
    public string? Parallax;

    /// <summary>The Lattice profile set up on the sector's map; null leaves it to the Lattice's default.</summary>
    [DataField]
    public ProtoId<WFLatticeProfilePrototype>? Lattice;

    /// <summary>Worlds drawn in the sector's space, each with a marker for warps and radar. They have no surface.</summary>
    [DataField]
    public List<WFSectorWorld> Worlds = new();

    /// <summary>The sector's jump beacons, one per linked sector.</summary>
    [DataField]
    public List<WFSectorBeacon> Beacons = new();
}

/// <summary>A world seen from a sector's space: drawn by the star system planet shader, lit from a far, unseen light.</summary>
[DataDefinition]
public sealed partial class WFSectorWorld
{
    [DataField(required: true)]
    public ProtoId<PlanetTypePrototype> Planet;

    /// <summary>The world's name on radar and in the warp list.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Where the world sits, in metres from the map's centre.</summary>
    [DataField]
    public Vector2 Position;

    /// <summary>The star type whose colour and brightness light the world; it is not drawn.</summary>
    [DataField(required: true)]
    public ProtoId<StarTypePrototype> Light;

    /// <summary>Where the light comes from, in metres from the map's centre.</summary>
    [DataField]
    public Vector2 LightPosition = new(-40000f, 30000f);
}

/// <summary>A point of interest a sector loads at round start.</summary>
[DataDefinition]
public sealed partial class WFSectorPoi
{
    /// <summary>The pointOfInterest prototype to load.</summary>
    [DataField(required: true)]
    public string Poi = string.Empty;

    /// <summary>The station's name in place of the POI's own.</summary>
    [DataField]
    public LocId? Name;

    [DataField]
    public float MinDistance = 4000f;

    [DataField]
    public float MaxDistance = 12000f;

    /// <summary>The trade crate destination its station takes, for a cargo depot.</summary>
    [DataField]
    public string? Destination;
}

/// <summary>A jump beacon: its place on the sector's map and the sector it reaches.</summary>
[DataDefinition]
public sealed partial class WFSectorBeacon
{
    /// <summary>The sector this beacon jumps to; that sector's beacon back is the other end of the link.</summary>
    [DataField(required: true)]
    public ProtoId<WFSectorPrototype> Link;

    /// <summary>Where the beacon stands, in metres from the map's centre.</summary>
    [DataField(required: true)]
    public Vector2 Position;

    /// <summary>Whether sector control refuses claims on the beacon's cell.</summary>
    [DataField]
    public bool Protected;

    /// <summary>A blockading faction that moves on the beacon's cell from time to time, or none.</summary>
    [DataField]
    public ProtoId<WFSectorFactionPrototype>? Raiders;

    /// <summary>The mean time between raids on the beacon.</summary>
    [DataField]
    public TimeSpan RaidInterval = TimeSpan.FromMinutes(60);

    /// <summary>How long a raid contests the cell before the raiders take it, unless ships fly it.</summary>
    [DataField]
    public TimeSpan RaidWarning = TimeSpan.FromMinutes(10);
}
