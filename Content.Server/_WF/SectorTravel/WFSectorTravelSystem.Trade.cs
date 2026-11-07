using Content.Server._WF.TradeGoods;
using Content.Shared._WF.SectorTravel;
using Content.Shared._WF.TradeGoods;

namespace Content.Server._WF.SectorTravel;

/// <summary>A crate sold in another sector than it was bought in pays a bonus per beacon link between them.</summary>
public sealed partial class WFSectorTravelSystem
{
    /// <summary>The quote bonus per beacon link between where a crate was bought and where it is sold.</summary>
    public const float JumpBonus = 0.15f;

    private void InitializeTrade()
    {
        SubscribeLocalEvent<WFTradeQuoteEvent>(OnQuote);
    }

    private void OnQuote(WFTradeQuoteEvent args)
    {
        if (args.Crate is not { } crate
            || !TryComp<WFTradeOriginComponent>(crate, out var origin)
            || origin.Sector is not { } from
            || SectorOfEntity(args.Chute) is not { } to
            || from == to
            || WFSectorGraph.Hops(_proto, from, to) is not { } hops
            || hops <= 0)
            return;

        args.Modifiers.Add(new WFTradeModifier(1f + JumpBonus * hops, "wf-trade-reason-jumps"));
    }
}
