using System.Linq;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.TradeGoods;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.TradeGoods;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    [Dependency] private WFTradeMarketSystem _market = default!;

    /// <summary>Banks a funding sale toward the next payout, and pays it out if one is due.</summary>
    private void OnTradeSale(WFTradeSaleEvent args)
    {
        if (!Running
            || Campaign is not { FundingCrates: > 0 } campaign
            || !FundsReavers(args)
            || State(true) is not { Finished: false } state)
            return;

        state.Funding = Math.Min(state.Funding + 1, FundingChest(campaign));
        TickFunding(campaign, state);
    }

    /// <summary>The most crates the war chest banks: <see cref="WFReaverCampaignPrototype.FundingBank"/> payouts' worth.</summary>
    public static int FundingChest(WFReaverCampaignPrototype campaign)
    {
        return campaign.FundingCrates * Math.Max(1, campaign.FundingBank);
    }

    /// <summary>Whether a sale funds the Reavers: smuggling crates at open chutes, and freight taken off NPC haulers fenced at the Underground.</summary>
    private bool FundsReavers(WFTradeSaleEvent args)
    {
        if (_market.Profile(args.Grid).ContrabandGoods.Contains(args.Good))
            return !_market.IsHiddenMarket(args.Grid);

        return _market.IsSealedOnly(args.Chute)
               && TryComp<WFTradeOriginComponent>(args.Crate, out var origin)
               && origin.Run != null;
    }

    /// <summary>Spends one payout from the war chest once the campaign isn't held, a stronghold stands to spend it and the last payout has cooled down.</summary>
    private void TickFunding(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        if (campaign.FundingCrates <= 0 || state.Funding < campaign.FundingCrates || Held || state.Finished ||
            _timing.CurTime < state.NextFunding)
            return;

        if (Strongholds().Count == 0)
            return;

        state.Funding -= campaign.FundingCrates;
        state.NextFunding = _timing.CurTime + campaign.FundingCooldown;
        Fund(campaign, state);
    }

    /// <summary>
    /// Spends one payout: every stronghold's next reinforcement and the next spread come sooner by
    /// <see cref="WFReaverCampaignPrototype.FundingShare"/> of their intervals, an extra patrol goes out if one may, and
    /// the sector hears of it on the radio.
    /// </summary>
    public void Fund(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        var reinforcementCut = ReinforcementInterval(campaign) * campaign.FundingShare;
        foreach (var stronghold in Strongholds())
        {
            stronghold.Comp1.NextReinforcement = MaxTime(now, stronghold.Comp1.NextReinforcement - reinforcementCut);
        }

        if (state.NextSpread is { } next)
            state.NextSpread = MaxTime(now, next - campaign.SpreadInterval * campaign.FundingShare);

        FundPatrol(campaign);
        state.Funded++;
        AnnounceRadio(Loc.GetString("wf-reaver-funded"));
    }

    /// <summary>
    /// Sends a patrol from the stronghold with the fewest out, while the patrols out stay within the usual cap plus
    /// <see cref="WFReaverCampaignPrototype.FundingPatrols"/>. It leaves the stronghold's own patrol schedule as it was.
    /// </summary>
    private bool FundPatrol(WFReaverCampaignPrototype campaign)
    {
        if (campaign.FundingPatrols <= 0)
            return false;

        var patrols = Patrols();
        if (patrols.Count >= PatrolCap() + campaign.FundingPatrols)
            return false;

        foreach (var stronghold in Strongholds().OrderBy(entry => patrols.Count(patrol => patrol.Comp1.Stronghold == entry.Owner)))
        {
            var next = stronghold.Comp1.NextPatrol;
            if (!TryLaunchPatrol(stronghold.Owner))
                continue;

            stronghold.Comp1.NextPatrol = next;
            return true;
        }

        return false;
    }
}
