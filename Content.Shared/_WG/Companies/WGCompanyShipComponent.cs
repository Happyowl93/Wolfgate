using Robust.Shared.GameStates;

namespace Content.Shared._WG.Companies;

/// <summary>A ship flying under a player company, shown on its IFF label unless the hull flies for a faction.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGCompanyShipComponent : Component
{
    [DataField, AutoNetworkedField]
    public int CompanyId;

    [DataField, AutoNetworkedField]
    public string Name = string.Empty;

    [DataField, AutoNetworkedField]
    public string Tag = string.Empty;

    [DataField, AutoNetworkedField]
    public Color Colour = Color.White;

    /// <summary>The IFF colour before the company's was applied, restored when the ship stops flying for it.</summary>
    [DataField]
    public Color? PreviousIffColour;
}
