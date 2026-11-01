using Robust.Shared.GameStates;

namespace Content.Shared._WF.TradeGoods;

/// <summary>A vendor whose whole stock costs more while its station is short of a goods group, or the sector booms for it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), UnsavedComponent]
public sealed partial class WFShortagePricedVendorComponent : Component
{
    /// <summary>The goods group it follows.</summary>
    [DataField(required: true)]
    public string Group = string.Empty;

    /// <summary>What it charges now, as a multiple of its price; kept by the server so the menu shows it.</summary>
    [DataField, AutoNetworkedField]
    public float Factor = 1f;
}
