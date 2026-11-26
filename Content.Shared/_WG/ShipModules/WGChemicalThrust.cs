using Content.Shared.Atmos;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Chemical fuel conversion: while fitted, the ship's thrusters push and turn harder but burn <see cref="Gas"/> from
/// canisters anchored aboard while they fire. With none left they fall to <see cref="EmptyFactor"/> of their usual thrust.
/// </summary>
public sealed partial class WGChemicalThrustEffect : WGShipModuleEffect
{
    /// <summary>Multiplier on linear thrust while fuelled.</summary>
    [DataField]
    public float ThrustFactor = 1.5f;

    /// <summary>Multiplier on turning thrust while fuelled.</summary>
    [DataField]
    public float TurnFactor = 1.35f;

    /// <summary>Multiplier on all thrust once the fuel is gone.</summary>
    [DataField]
    public float EmptyFactor = 0.25f;

    /// <summary>The gas the thrusters burn.</summary>
    [DataField]
    public Gas Gas = Gas.Frezon;

    /// <summary>Moles burnt per second for each point of thrust firing, so a standard 150 thruster burns 0.375 mol/s.</summary>
    [DataField]
    public float MolesPerThrust = 0.0025f;

    /// <summary>Played through the hull when the fuel runs out.</summary>
    [DataField]
    public SoundSpecifier? FlameoutSound;

    /// <summary>Played through the hull when fuel is found again.</summary>
    [DataField]
    public SoundSpecifier? RelightSound;
}

/// <summary>On a ship's grid while chemical fuel conversion is fitted; clients tint its plumes by it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WGChemicalThrustComponent : Component
{
    /// <summary>Whether the thrusters have fuel to burn.</summary>
    [DataField, AutoNetworkedField]
    public bool Fueled = true;

    /// <summary>Share of the fuel aboard against a full canister's worth, for the helm; may pass 1.</summary>
    [ViewVariables]
    public float Reserve;

    [ViewVariables]
    public float ThrustFactor = 1.5f;

    [ViewVariables]
    public float TurnFactor = 1.35f;

    [ViewVariables]
    public float EmptyFactor = 0.25f;

    [ViewVariables]
    public Gas Gas = Gas.Frezon;

    [ViewVariables]
    public float MolesPerThrust = 0.0025f;

    [ViewVariables]
    public SoundSpecifier? FlameoutSound;

    [ViewVariables]
    public SoundSpecifier? RelightSound;
}
