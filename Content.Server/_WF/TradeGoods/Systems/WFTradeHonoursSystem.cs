using System.Linq;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>Round-end lines for the round's best traders: the top hauler, blockade runner, rush champion, courier and escort.</summary>
public sealed class WFTradeHonoursSystem : EntitySystem
{
    [Dependency] private WFTradeSectorSystem _sector = default!;
    [Dependency] private WFTradeRushSystem _rush = default!;
    [Dependency] private WFTradeSpecialCrateSystem _special = default!;
    [Dependency] private WFTradeEscortSystem _escort = default!;

    /// <summary>Spesos and crates delivered per ship (or seller, with no ship) this round.</summary>
    private readonly Dictionary<string, (int Spesos, int Crates)> _hauled = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _hauled.Clear();
    }

    private void OnSale(WFTradeSaleEvent args)
    {
        var seller = args.Ship is { } ship ? Name(ship) : Name(args.Seller);
        var (spesos, crates) = _hauled.GetValueOrDefault(seller);
        _hauled[seller] = (spesos + args.Amount, crates + 1);
    }

    private void OnRoundEndText(RoundEndTextAppendEvent args)
    {
        if (_hauled.Count == 0)
            return;

        var top = _hauled.OrderByDescending(entry => entry.Value.Spesos).First();
        args.AddLine(Loc.GetString("wf-trade-honours-header"));
        args.AddLine(Loc.GetString("wf-trade-honours-hauler", ("ship", top.Key), ("spesos", top.Value.Spesos), ("crates", top.Value.Crates)));

        if (Top(_sector.BlockadesBroken.Where(entry => !TerminatingOrDeleted(entry.Key)).ToDictionary(entry => Name(entry.Key), entry => entry.Value)) is { } runner)
            args.AddLine(Loc.GetString("wf-trade-honours-runner", ("ship", runner.Key), ("count", runner.Value)));

        if (Top(_rush.Delivered) is { } rush)
            args.AddLine(Loc.GetString("wf-trade-honours-rush", ("ship", rush.Key), ("count", rush.Value)));

        if (Top(_special.Couriers) is { } courier)
            args.AddLine(Loc.GetString("wf-trade-honours-courier", ("ship", courier.Key), ("count", courier.Value)));

        if (Top(_escort.Paid) is { } escort)
            args.AddLine(Loc.GetString("wf-trade-honours-escort", ("ship", escort.Key), ("spesos", escort.Value)));
    }

    private static KeyValuePair<string, int>? Top(IReadOnlyDictionary<string, int> tally)
    {
        return tally.Count == 0 ? null : tally.OrderByDescending(entry => entry.Value).First();
    }
}
