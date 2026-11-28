using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Clothing;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState] // WOLFGATE(Modsuits): runtime-granted magboots (voidboots module) reach the client with their settings
[Access(typeof(SharedMagbootsSystem))]
public sealed partial class MagbootsComponent : Component
{
    [DataField]
    public ProtoId<AlertPrototype> MagbootsAlert = "Magboots";

    /// <summary>
    /// If true, the user must be standing on a grid or planet map to experience the weightlessness-canceling effect
    /// </summary>
    [DataField]
    [AutoNetworkedField] // WOLFGATE(Modsuits): the client predicted weightlessness in space and rubberbanded
    public bool RequiresGrid = true;

    /// <summary>
    /// Goobstation - EquippedPrefix for enabled state
    /// </summary>
    [DataField]
    [AutoNetworkedField] // WOLFGATE(Modsuits)
    public string? EnabledPrefix = "on";
}
