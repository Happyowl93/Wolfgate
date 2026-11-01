using System.Linq;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._WF.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>A station's supply now: how its vendors stand and how many freighters have called this round.</summary>
public readonly record struct WFSupplyReport(string Station, bool Blockaded, bool Short, int Goods, int SoldOut, int Lowest, int Stops);

/// <summary>
/// The supply lines that keep stations stocked between freighters. A blockade cuts their materials, and a blockade or
/// shortage slows what reaches the trade vendors. Stations stay healthy until something disrupts them.
/// </summary>
public sealed partial class WFTradeSupplySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MarketSystem _stock = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;
    [Dependency] private WFTradeShortageSystem _shortages = default!;

    private static readonly TimeSpan ReportInterval = TimeSpan.FromHours(1);

    private TimeSpan _nextPulse;
    private TimeSpan _nextReport;
    private int _pulses;

    /// <summary>Freighter stops at each station this round.</summary>
    private readonly Dictionary<EntityUid, int> _stops = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _nextPulse = TimeSpan.Zero;
        _nextReport = TimeSpan.Zero;
        _pulses = 0;
        _stops.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (_nextReport == TimeSpan.Zero)
            _nextReport = _timing.CurTime + ReportInterval;

        if (_timing.CurTime >= _nextReport)
        {
            _nextReport = _timing.CurTime + ReportInterval;
            foreach (var report in Reports())
            {
                Log.Info(report.ToString());
            }
        }

        var interval = _market.Profile(null).SupplyPulse;
        if (_nextPulse == TimeSpan.Zero)
            _nextPulse = _timing.CurTime + interval;

        if (_timing.CurTime < _nextPulse)
            return;

        _nextPulse = _timing.CurTime + interval;
        Pulse();
    }

    /// <summary>One delivery from the supply lines to every station.</summary>
    public void Pulse()
    {
        _pulses++;
        var vendorGrids = new HashSet<EntityUid>();
        var vendors = EntityQueryEnumerator<WFTradeVendorComponent, TransformComponent>();
        while (vendors.MoveNext(out _, out var xform))
        {
            if (xform.GridUid is { } grid)
                vendorGrids.Add(grid);
        }

        foreach (var grid in vendorGrids)
        {
            // Every second pulse gets through to a disrupted station.
            if (_pulses % 2 == 0 || !_sector.IsBlockaded(grid) && !_shortages.IsShort(grid))
                _market.RestockVendors(grid, 1);
        }

        var markets = EntityQueryEnumerator<CargoMarketDataComponent>();
        while (markets.MoveNext(out var station, out _))
        {
            if (_station.GetLargestGrid(station) is { } grid && !_sector.IsBlockaded(grid))
                _stock.WfRestock(grid);
        }
    }

    /// <summary>Counts a freighter's stop at a station.</summary>
    public void NoteStop(EntityUid grid)
    {
        _stops[grid] = _stops.GetValueOrDefault(grid) + 1;
    }

    /// <summary>How every station with trade vendors stands now, for admins and the log.</summary>
    public List<WFSupplyReport> Reports()
    {
        var grids = new HashSet<EntityUid>();
        var vendors = EntityQueryEnumerator<WFTradeVendorComponent, TransformComponent>();
        while (vendors.MoveNext(out _, out var xform))
        {
            if (xform.GridUid is { } grid)
                grids.Add(grid);
        }

        var reports = new List<WFSupplyReport>();
        foreach (var grid in grids)
        {
            var stocks = _market.StockedGoods(grid).Select(good => _market.VendorStock(grid, good)).ToList();
            if (stocks.Count > 0)
            {
                reports.Add(new WFSupplyReport(Name(grid), _sector.IsBlockaded(grid), _shortages.IsShort(grid), stocks.Count,
                    stocks.Count(stock => stock == 0), stocks.Min(), _stops.GetValueOrDefault(grid)));
            }
        }

        return reports;
    }

    /// <summary>What a fuel shortage or boom does to a trader's refuelling prices.</summary>
    public float RefuelFactor(EntityUid trader)
    {
        return _shortages.Factor(Transform(trader).GridUid, _market.Profile(null).RefuelShortageGroup);
    }
}
