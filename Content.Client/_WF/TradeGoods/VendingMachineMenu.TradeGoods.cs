using Content.Shared._WF.TradeGoods;

namespace Content.Client.VendingMachines.UI;

public sealed partial class VendingMachineMenu
{
    /// <summary>The vendor this menu lists, for its live trade-good prices.</summary>
    public EntityUid? WfVendor;

    /// <summary>The price a trade vendor will charge for a trade good, or the menu's own price for anything else.</summary>
    private int WfLivePrice(string id, int cost)
    {
        if (_entityManager.TryGetComponent<WFShortagePricedVendorComponent>(WfVendor, out var priced))
            return (int) MathF.Round(cost * priced.Factor);

        return WfVendor is { } vendor
               && _entityManager.TryGetComponent<WFTradeVendorComponent>(vendor, out var live)
               && live.Prices.TryGetValue(id, out var price)
            ? price
            : cost;
    }
}
