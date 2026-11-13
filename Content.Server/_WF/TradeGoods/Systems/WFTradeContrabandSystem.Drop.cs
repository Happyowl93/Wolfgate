using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Robust.Shared.Random;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// The contraband drop-point: one open station at a time buys smuggling crates, and the rest refuse them. It moves every
/// <c>contrabandDropInterval</c>; the Underground's companies hear where at once and see it in the Market app, and
/// security is tipped off <c>contrabandDropLeak</c> later.
/// </summary>
public sealed partial class WFTradeContrabandSystem
{
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFTradeSectorSystem _sector = default!;

    private static readonly TimeSpan DropCheckInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _nextDropCheck;
    private TimeSpan _dropLeaks;
    private bool _dropLeaked;

    /// <summary>The one open station buying contraband now, or null when there is none to pick or the rotation is off.</summary>
    public EntityUid? DropPoint { get; private set; }

    /// <summary>When the drop moves on.</summary>
    public TimeSpan DropEnds { get; private set; }

    /// <summary>Whether security has been tipped off about the current drop.</summary>
    public bool DropLeaked => _dropLeaked;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextDropCheck)
            return;

        _nextDropCheck = _timing.CurTime + DropCheckInterval;
        TickDrop();
    }

    /// <summary>Moves the drop when it is due or its station is gone, and tips off security once the leak delay has passed.</summary>
    public void TickDrop()
    {
        if (_market.Profile(null).ContrabandDropInterval <= TimeSpan.Zero)
        {
            ClearDrop();
            return;
        }

        if (DropPoint is not { } drop || TerminatingOrDeleted(drop) || _timing.CurTime >= DropEnds)
        {
            Rotate();
            return;
        }

        if (!_dropLeaked && _timing.CurTime >= _dropLeaks)
            Leak();
    }

    /// <summary>
    /// Moves the drop to another open station that buys contraband, never the same one twice running when there is a
    /// choice, and tells the Underground. Returns the new drop, or null if no station can take it.
    /// </summary>
    public EntityUid? Rotate()
    {
        var profile = _market.Profile(null);
        var candidates = DropCandidates();
        if (candidates.Count > 1 && DropPoint is { } previous)
            candidates.Remove(previous);

        if (profile.ContrabandDropInterval <= TimeSpan.Zero || candidates.Count == 0)
        {
            ClearDrop();
            return null;
        }

        var drop = _random.Pick(candidates);
        var now = _timing.CurTime;
        DropPoint = drop;
        DropEnds = now + profile.ContrabandDropInterval;
        _dropLeaks = now + profile.ContrabandDropLeak;
        _dropLeaked = false;

        _market.TellCompanies(profile.UndergroundCompanies, Loc.GetString("wf-trade-contraband-drop",
            ("station", Name(drop)), ("minutes", (int) profile.ContrabandDropInterval.TotalMinutes)));
        return drop;
    }

    /// <summary>Tips security off on where the drop is, once per drop.</summary>
    public void Leak()
    {
        if (DropPoint is not { } drop || _dropLeaked || TerminatingOrDeleted(drop))
            return;

        _dropLeaked = true;
        var profile = _market.Profile(null);
        _market.Radio(Loc.GetString("wf-trade-contraband-drop-leak", ("station", Name(drop))), profile.ContrabandAlertChannel);
    }

    /// <summary>Whether contraband sells at the drop alone right now.</summary>
    public bool DropActive => DropPoint is { } drop && !TerminatingOrDeleted(drop)
                              && _market.Profile(null).ContrabandDropInterval > TimeSpan.Zero;

    /// <summary>Why a chute refuses a crate because it is contraband and the chute isn't at the drop, or null if it takes it.</summary>
    public LocId? CheckDrop(EntityUid chute, EntityUid crate)
    {
        if (!DropActive
            || MetaData(crate).EntityPrototype?.ID is not { } good
            || !_market.Profile(null).ContrabandGoods.Contains(good)
            || Transform(chute).GridUid == DropPoint
            || _market.IsHiddenChute(chute))
            return null;

        return "wf-trade-refuse-drop";
    }

    /// <summary>Whether a good is contraband that sells at the drop alone right now.</summary>
    public bool AtDropOnly(string good)
    {
        return DropActive && _market.Profile(null).ContrabandGoods.Contains(good);
    }

    /// <summary>
    /// The stations the drop may move to: those with an open chute listing contraband, but not a wandering merchant, a
    /// hidden market or a blockaded station.
    /// </summary>
    private List<EntityUid> DropCandidates()
    {
        var profile = _market.Profile(null);
        var blockaded = _sector.Blockaded().ToHashSet();
        var chutes = _market.Chutes();
        var hidden = chutes.Where(chute => _market.IsHiddenChute(chute.Chute)).Select(chute => chute.Grid).ToHashSet();
        return chutes
            .Where(chute => !blockaded.Contains(chute.Grid)
                            && !hidden.Contains(chute.Grid)
                            && !HasComp<WFEncounterGridComponent>(chute.Grid)
                            && profile.ContrabandGoods.Any(good => chute.Dispenser.Inventory.ContainsKey(good.Id)))
            .Select(chute => chute.Grid)
            .Distinct()
            .ToList();
    }

    private void ClearDrop()
    {
        DropPoint = null;
        DropEnds = TimeSpan.Zero;
        _dropLeaks = TimeSpan.Zero;
        _dropLeaked = false;
    }
}
