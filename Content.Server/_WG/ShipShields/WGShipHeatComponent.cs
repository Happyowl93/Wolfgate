using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipShields;

/// <summary>A ship's heat. Shield generators on the hull read their damage from it and do its cooling.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGShipHeatComponent : Component
{
    /// <summary>Heat a hull with no generator holds unless its grid says otherwise.</summary>
    public const float HullCapacity = 20000f;

    /// <summary>Heat the hull holds now.</summary>
    [DataField]
    public float Heat;

    /// <summary>The part of the heat that came through the shield. It stays while the field is up and taking hits.</summary>
    [DataField]
    public float Hard;

    /// <summary>Most heat the hull can hold with no generator; with one, the generator's capacity applies.</summary>
    [DataField]
    public float Capacity = HullCapacity;

    /// <summary>Heat the hull sheds each second with no generator to cool it.</summary>
    [DataField]
    public float Dissipation = 500f;

    /// <summary>How much faster a hull with no generator cools while venting; a generator uses its own bonus.</summary>
    [DataField]
    public float VentBonus = 3f;

    /// <summary>Thermal signature a venting hull adds for long-range sensors.</summary>
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

    /// <summary>When the field last took a hit.</summary>
    [ViewVariables]
    public TimeSpan LastHit;

    /// <summary>When the next plumes leave the hull.</summary>
    [ViewVariables]
    public TimeSpan NextPlume;

    /// <summary>Where plumes can leave the hull and which way they drift, in grid space; found when a vent starts.</summary>
    public readonly List<(Vector2 Position, Angle Facing)> Plumes = new();
}
