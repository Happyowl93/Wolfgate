using Content.Server._Mono.VendingMachine;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Popups;
using Content.Shared._Crescent.Dispenser;
using Content.Shared.Inventory.VirtualItem;

namespace Content.Server._Crescent.Dispenser;

public sealed partial class DispenserSystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private VendingMachinePurchaseSystem _vendingPurchase = default!;
    [Dependency] private WFTradeMarketSystem _wfMarket = default!;
    [Dependency] private WFTradeSpecialCrateSystem _wfSpecialCrates = default!;

    /// <summary>
    /// Refuses goods bought from a vendor on the dispenser's own grid, or made too close to it, so trade goods only pay
    /// once hauled elsewhere.
    /// </summary>
    private bool RefuseLocalGoods(EntityUid uid, DispenserComponent component, EntityUid used, EntityUid user)
    {
        LocId? reason;
        if (_wfMarket.Shunned(uid, user))
            reason = "wf-trade-refuse-shunned";
        else if (Transform(uid).GridUid is { } grid && _vendingPurchase.WasPurchasedOnGrid(used, grid))
            reason = "wf-trade-chute-local-goods";
        else
            reason = _wfMarket.CheckHaul(uid, used);

        if (reason is not { } refusal)
            return false;

        _audioSystem.PlayPvs(component.DenySound, uid);
        _popup.PopupEntity(Loc.GetString(refusal, ("distance", _wfMarket.HaulKilometres(uid))), uid, user);
        return true;
    }

    /// <summary>Quotes a listed crate and holds the amount for the payout.</summary>
    private void WfQuoteSale(EntityUid uid, EntityUid used, EntityUid user)
    {
        _wfMarket.Sell(uid, used, user);
    }

    /// <summary>Pays a held quote, or prints live prices on the empty-hand paper.</summary>
    private bool WfPayout(EntityUid uid, DispenserComponent component, string itemId)
    {
        return _wfMarket.Payout(uid, component, itemId);
    }

    /// <summary>
    /// Takes a crate priced by who carries it where, such as a consignment or classified cargo, whether or not the
    /// chute lists it. False for any other item.
    /// </summary>
    private bool WfTakeSpecialCrate(EntityUid uid, DispenserComponent component, EntityUid used, EntityUid user, VirtualItemComponent? virtualItem, EntityUid usedHand)
    {
        if (!_wfSpecialCrates.IsSpecial(used))
            return false;

        if (!_wfSpecialCrates.TryPrice(uid, used, user, out var amount))
        {
            _audioSystem.PlayPvs(component.DenySound, uid);
            return true;
        }

        if (RefuseLocalGoods(uid, component, used, user))
            return true;

        _wfMarket.Sell(uid, used, user, amount);
        TryDispenseItem(uid, component, component.DefaultItem);
        if (virtualItem != null)
            _virtualItemSystem.DeleteVirtualItem((usedHand, virtualItem), user);

        QueueDel(used);
        return true;
    }
}
