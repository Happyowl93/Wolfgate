using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Ship module that kicks the hull sideways, forward or back: each use adds <see cref="DeltaV"/> along the direction
/// the pilot holds, spread over <see cref="BurnTime"/>.
/// </summary>
[RegisterComponent]
public sealed partial class WGManeuveringJetsComponent : Component
{
    /// <summary>Speed one burn adds to a hull of <see cref="ReferenceMass"/> or less, in metres per second.</summary>
    [DataField]
    public float DeltaV = 20f;

    /// <summary>Seconds a burn lasts.</summary>
    [DataField]
    public float BurnTime = 0.4f;

    /// <summary>Hull mass up to which a burn gives its full kick; heavier hulls get the square root of the ratio.</summary>
    [DataField]
    public float ReferenceMass = 100f;

    /// <summary>Smallest share of <see cref="DeltaV"/> a heavy hull still gets.</summary>
    [DataField]
    public float MinScale = 0.25f;

    /// <summary>Heat one burn adds to the ship's thermal signature.</summary>
    [DataField]
    public float Heat = 1000000f;

    /// <summary>World direction of the burn under way.</summary>
    [ViewVariables]
    public Vector2 BurnDirection;

    /// <summary>Speed the burn under way still has to add.</summary>
    [ViewVariables]
    public float BurnLeft;

    /// <summary>Speed the burn under way adds per second.</summary>
    [ViewVariables]
    public float BurnRate;
}

/// <summary>A jets burn started: the ship's thrusters that push along it flare and puff smoke rings.</summary>
[Serializable, NetSerializable]
public sealed class WGManeuveringJetsPuffEvent : EntityEventArgs
{
    public List<WGJetsPuff> Puffs = new();
    public float BurnTime;
}

/// <summary>One thruster's share of a burn: its size against a standard thruster and how squarely it pushes along it.</summary>
[Serializable, NetSerializable]
public record struct WGJetsPuff(NetEntity Thruster, float Size, float Strength);
