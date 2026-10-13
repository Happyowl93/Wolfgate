using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Lathe;

/// <summary>
/// Links a machine, such as a lathe, to one parts silo and one chemical silo, apart from its material silo.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedFabricationSiloSystem))]
public sealed partial class FabricationSiloClientComponent : Component
{
    /// <summary>
    /// Linked parts silo.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? PartsSilo;

    /// <summary>
    /// Linked chemical silo.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ChemicalSilo;

    /// <summary>
    /// Parts this machine uses besides its lathe recipes', which parts silos then accept; matched on prototype
    /// components and tags.
    /// </summary>
    [DataField]
    public EntityWhitelist? Parts;
}
