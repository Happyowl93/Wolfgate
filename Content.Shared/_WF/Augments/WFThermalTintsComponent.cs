using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Augments;

/// <summary>
/// Thermal eyes that cycle their vision through a set of tints from one action: off, each tint in turn, then off.
/// Drives the body's <see cref="Content.Shared._White.Overlays.ThermalVisionComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFThermalTintsComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public List<WFThermalTint> Tints = new();

    /// <summary>The tint in use, or -1 while the vision is off.</summary>
    [DataField, AutoNetworkedField]
    public int Index = -1;

    [DataField]
    public EntProtoId Action = "WFActionCycleThermalTint";

    [ViewVariables]
    public EntityUid? ActionEntity;

    /// <summary>Played on every switch to a tint.</summary>
    [DataField]
    public SoundSpecifier? SwapSound = new SoundPathSpecifier("/Audio/_White/Items/Goggles/activate.ogg");

    /// <summary>Played over <see cref="SwapSound"/>: the lens settling on the new band.</summary>
    [DataField]
    public SoundSpecifier? ChirpSound = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg",
        AudioParams.Default.WithPitchScale(1.35f).WithVolume(-4f));

    [DataField]
    public SoundSpecifier? OffSound = new SoundPathSpecifier("/Audio/_White/Items/Goggles/deactivate.ogg");
}

/// <summary>One band the eyes can see in.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WFThermalTint
{
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The colour warm bodies are drawn in.</summary>
    [DataField(required: true)]
    public Color Color;

    /// <summary>The colour the rest of the view is washed towards.</summary>
    [DataField(required: true)]
    public Vector3 Tint;
}

public sealed partial class WFCycleThermalTintEvent : InstantActionEvent;

/// <summary>Raised on the body on the client, once, when its eyes switch band; drives the swap glitch.</summary>
[ByRefEvent]
public readonly record struct WFThermalTintSwappedEvent(Color Color);
