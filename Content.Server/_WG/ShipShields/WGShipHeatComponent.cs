using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipShields;

/// <summary>A ship's heat. Shield generators on the hull read their damage from it and add to what it holds and sheds.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGShipHeatComponent : Component
{
    /// <summary>Heat a hull holds before its tiles are counted in.</summary>
    public const float HullCapacity = 8000f;

    /// <summary>Heat the hull holds now.</summary>
    [DataField]
    public float Heat;

    /// <summary>The part of the heat that came through the shield. It stays while the field is up and taking hits.</summary>
    [DataField]
    public float Hard;

    /// <summary>Heat a hull holds whatever its size.</summary>
    [DataField]
    public float BaseCapacity = HullCapacity;

    /// <summary>Heat each hull tile adds to what the hull holds.</summary>
    [DataField]
    public float CapacityPerTile = 30f;

    /// <summary>Most heat a hull holds by its size, however large.</summary>
    [DataField]
    public float MaxCapacity = 50000f;

    /// <summary>Seconds a hull with no generator takes to shed a full load of heat. No field traps it, so it beats any generator.</summary>
    [DataField]
    public float CoolSeconds = 40f;

    /// <summary>Share of its own cooling a hull keeps under a ship generator, on top of the generator's rate.</summary>
    [DataField]
    public float GeneratorCooling = 0.25f;

    /// <summary>Heat the hull holds by its size. A ship generator adds its capacity to this; an outpost generator's replaces it.</summary>
    [ViewVariables]
    public float Capacity = HullCapacity;

    /// <summary>Heat the hull sheds each second with no generator to cool it, from its size.</summary>
    [ViewVariables]
    public float Dissipation = HullCapacity / 40f;

    /// <summary>Whether capacity and cooling match the hull's tiles; cleared when the hull gains or loses one.</summary>
    [ViewVariables]
    public bool Sized;

    /// <summary>Share of each shot's damage the ship's own guns add to its heat.</summary>
    [DataField]
    public float FireHeat = 0.15f;

    /// <summary>How much faster a hull with no generator cools while venting; a generator uses its own bonus.</summary>
    [DataField]
    public float VentBonus = 1.5f;

    /// <summary>Thermal signature each point of heat the hull holds adds for long-range sensors.</summary>
    [DataField]
    public float HeatSignature = 25f;

    /// <summary>Thermal signature a venting hull adds for long-range sensors, on top of its heat's.</summary>
    [DataField]
    public float VentSignature = 1500000f;

    /// <summary>Played across the hull when a vent starts.</summary>
    [DataField]
    public SoundSpecifier VentSound = new SoundPathSpecifier("/Audio/_WG/ShipShields/vent.ogg");

    /// <summary>Spawned along the hull's edge while it vents.</summary>
    [DataField]
    public EntProtoId? VentEffect = "WGEffectShipHeatVent";

    /// <summary>The generator cooling the hull; any other generator on it only reads the heat.</summary>
    [ViewVariables]
    public EntityUid? Cooler;

    /// <summary>Whether the hull is venting: field down, guns silent, cooling at the fast rate until no heat is left.</summary>
    [ViewVariables]
    public bool Venting;

    /// <summary>Whether damage beat the field down and an NPC crew has yet to make its call on it, once the overload ends.</summary>
    [ViewVariables]
    public bool Collapsed;

    /// <summary>When the field last took a hit.</summary>
    [ViewVariables]
    public TimeSpan LastHit;

    /// <summary>When the next plumes leave the hull.</summary>
    [ViewVariables]
    public TimeSpan NextPlume;

    /// <summary>Where plumes can leave the hull and which way they drift, in grid space; found when a vent starts.</summary>
    public readonly List<(Vector2 Position, Angle Facing)> Plumes = new();
}
