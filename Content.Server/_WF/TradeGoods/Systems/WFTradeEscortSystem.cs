using System.Linq;
using System.Numerics;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.GameTicking;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Escort pay: crewed ships that fly beside a laden trader share a bonus when it sells a big haul. Paid on top of the sale,
/// never taken from the trader.
/// </summary>
public sealed class WFTradeEscortSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFSectorActivitySystem _activity = default!;
    [Dependency] private WFEncounterRewardSystem _rewards = default!;
    [Dependency] private WFCargoLoadSystem _cargo = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;

    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>How close an escort must keep, in metres.</summary>
    private const float Range = 1500f;

    /// <summary>How far an escort must have moved between samples to count, so parked ships don't.</summary>
    private const float MinMove = 50f;

    /// <summary>Share of samples an escort must be present for.</summary>
    private const float Presence = 0.6f;

    /// <summary>Sales a trader must make in the window for its escorts to be paid.</summary>
    private const int Threshold = 25000;

    /// <summary>Escort share of those sales.</summary>
    private const float Share = 0.1f;

    private TimeSpan _nextSample;

    /// <summary>Per laden ship: when each sample was taken and which escorts were beside it.</summary>
    private readonly Dictionary<EntityUid, List<(TimeSpan At, HashSet<EntityUid> Escorts)>> _samples = new();

    /// <summary>Per trader ship: its recent sales.</summary>
    private readonly Dictionary<EntityUid, List<(TimeSpan At, int Amount)>> _sales = new();

    private readonly Dictionary<EntityUid, Vector2> _lastPositions = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _samples.Clear();
        _sales.Clear();
        _lastPositions.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextSample)
            return;

        _nextSample = _timing.CurTime + SampleInterval;
        Sample();
    }

    private void Sample()
    {
        var now = _timing.CurTime;
        var map = _ticker.DefaultMap;
        var crewed = _activity.CrewedShips(map).Where(HasComp<ShuttleDeedComponent>).ToList();
        var positions = crewed.ToDictionary(ship => ship, ship => _transform.GetWorldPosition(ship));

        var moved = new HashSet<EntityUid>();
        foreach (var (ship, position) in positions)
        {
            if (_lastPositions.TryGetValue(ship, out var last) && (position - last).Length() >= MinMove)
                moved.Add(ship);

            _lastPositions[ship] = position;
        }

        foreach (var trader in crewed)
        {
            var set = _cargo.GetCargoSet(trader);
            if (_cargo.GetSetLoad(set) <= 0f)
                continue;

            var owner = CompOrNull<ShuttleDeedComponent>(trader)?.ShuttleOwner;
            var escorts = new HashSet<EntityUid>();
            foreach (var other in moved)
            {
                if (other == trader || set.Contains(other)
                    || owner != null && CompOrNull<ShuttleDeedComponent>(other)?.ShuttleOwner == owner
                    || (positions[other] - positions[trader]).Length() > Range)
                    continue;

                escorts.Add(other);
            }

            if (!_samples.TryGetValue(trader, out var samples))
                _samples[trader] = samples = new List<(TimeSpan, HashSet<EntityUid>)>();

            samples.Add((now, escorts));
            samples.RemoveAll(sample => now - sample.At > Window);
        }

        foreach (var ship in _samples.Keys.Where(ship => TerminatingOrDeleted(ship)).ToList())
        {
            _samples.Remove(ship);
            _sales.Remove(ship);
        }
    }

    /// <summary>Ships beside a laden trader at its latest sample.</summary>
    public IReadOnlyCollection<EntityUid> EscortsOf(EntityUid trader)
    {
        return _samples.TryGetValue(trader, out var samples) && samples.Count > 0
            ? samples[^1].Escorts
            : Array.Empty<EntityUid>();
    }

    /// <summary>Laden traders a ship was beside at their latest sample.</summary>
    public IEnumerable<EntityUid> EscortedBy(EntityUid escort)
    {
        foreach (var (trader, samples) in _samples)
        {
            if (samples.Count > 0 && samples[^1].Escorts.Contains(escort) && !TerminatingOrDeleted(trader))
                yield return trader;
        }
    }

    /// <summary>A trader's sales in the window, and the total at which its escorts are paid.</summary>
    public (int Sold, int Threshold) Progress(EntityUid trader)
    {
        var now = _timing.CurTime;
        var sold = _sales.TryGetValue(trader, out var sales) ? sales.Where(sale => now - sale.At <= Window).Sum(sale => sale.Amount) : 0;
        return (sold, Threshold);
    }

    /// <summary>Escort share of a big haul, as a percentage.</summary>
    public static int SharePercent => (int) (Share * 100f);

    private void OnSale(WFTradeSaleEvent args)
    {
        if (args.Ship is not { } ship || !_market.WasHauled(args.Chute, args.Crate))
            return;

        var now = _timing.CurTime;
        if (!_sales.TryGetValue(ship, out var sales))
            _sales[ship] = sales = new List<(TimeSpan, int)>();

        sales.Add((now, args.Amount));
        sales.RemoveAll(sale => now - sale.At > Window);
        var total = sales.Sum(sale => sale.Amount);
        if (total < Threshold || !_samples.TryGetValue(ship, out var samples) || samples.Count == 0)
            return;

        var counts = new Dictionary<EntityUid, int>();
        foreach (var (_, escorts) in samples)
        {
            foreach (var escort in escorts)
            {
                counts[escort] = counts.GetValueOrDefault(escort) + 1;
            }
        }

        var paid = counts
            .Where(entry => entry.Value >= samples.Count * Presence && !TerminatingOrDeleted(entry.Key))
            .Select(entry => entry.Key)
            .ToList();

        sales.Clear();
        if (paid.Count == 0)
            return;

        var bonus = (int) (total * Share);
        // Nobody aboard to pay means nothing earned.
        if (_rewards.PayCrews(Loc.GetString("wf-trade-escort-name", ("ship", Name(ship))), $"escort of {ToPrettyString(ship)}", bonus, paid, "escorting") == 0)
            return;

        _market.TellAboard(paid.Append(ship).ToList(), Loc.GetString("wf-trade-escort-paid",
            ("ships", string.Join(", ", paid.Select(escort => Name(escort)))), ("amount", bonus), ("trader", Name(ship))));
    }
}
