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

    /// <summary>Most heat the hull can hold with no generator; with one, the generator's capacity applies.</summary>
    [DataField]
    public float Capacity = HullCapacity;

    /// <summary>Heat the hull sheds each second with no generator to cool it.</summary>
    [DataField]
    public float Dissipation = 500f;

    /// <summary>The generator cooling the hull; any other generator on it only reads the heat.</summary>
    [ViewVariables]
    public EntityUid? Cooler;
}
