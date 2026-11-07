using System.Linq;
using Content.Server._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.SectorTravel.Components;
using Content.Server.Chat.Systems;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.SectorTravel;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorTravel;

/// <summary>Who holds each beacon, which links that closes, and which sectors are cut off from home.</summary>
public sealed partial class WFSectorTravelSystem
{
    [Dependency] private WFSectorTerritorySystem _territory = default!;
    [Dependency] private ChatSystem _chat = default!;

    private static readonly TimeSpan RegionInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RaidCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly Color RegionColor = Color.FromHex("#7fd4ff");
    private TimeSpan _nextRaidCheck;

    private readonly HashSet<string> _cutOff = new();
    private WFRegionStateEvent _region = new();
    private TimeSpan _nextRegion;

    private void InitializeRegion()
    {
        SubscribeLocalEvent<WFSectorClaimAttemptEvent>(OnClaimAttempt);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void ClearRegion()
    {
        _cutOff.Clear();
        _region = new WFRegionStateEvent();
        _nextRegion = TimeSpan.Zero;
    }

    private void UpdateRegion()
    {
        UpdateRaids();
        if (_timing.CurTime < _nextRegion)
            return;

        _nextRegion = _timing.CurTime + RegionInterval;
        RefreshRegion();
    }

    /// <summary>Re-reads the beacon holders now, and tells clients if anything changed.</summary>
    public void RefreshRegion()
    {
        var state = new WFRegionStateEvent();
        var query = EntityQueryEnumerator<WFJumpBeaconComponent>();
        while (query.MoveNext(out var uid, out var beacon))
        {
            state.Beacons.Add(new WFRegionBeaconState(beacon.Sector, beacon.Link, Holder(uid)?.Id, Closes(uid)));
        }

        state.Beacons.Sort((a, b) => string.CompareOrdinal(a.Sector + a.Link, b.Sector + b.Link));
        _cutOff.Clear();
        if (WFSectorGraph.Home(_proto) is { } home)
        {
            foreach (var sector in _proto.EnumeratePrototypes<WFSectorPrototype>())
            {
                if (!sector.Home && WFSectorGraph.Hops(_proto, home.ID, sector.ID, LinkClosed) == null)
                    _cutOff.Add(sector.ID);
            }
        }

        state.CutOff = _cutOff.OrderBy(sector => sector).ToList();
        if (state.Beacons.SequenceEqual(_region.Beacons) && state.CutOff.SequenceEqual(_region.CutOff))
            return;

        foreach (var sector in state.CutOff.Except(_region.CutOff))
        {
            Announce(Loc.GetString("wf-sector-cut-off", ("sector", SectorName(sector))));
        }

        foreach (var sector in _region.CutOff.Except(state.CutOff))
        {
            Announce(Loc.GetString("wf-sector-reopened", ("sector", SectorName(sector))));
        }

        _region = state;
        RaiseNetworkEvent(state, Filter.Broadcast());
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        RaiseNetworkEvent(_region, args.Player);
    }

    /// <summary>The faction holding a beacon's cell, if sector control runs on its map.</summary>
    public ProtoId<WFSectorFactionPrototype>? Holder(EntityUid beacon)
    {
        if (!_territory.Enabled)
            return null;

        var coordinates = _transform.GetMapCoordinates(beacon);
        return _territory.Owner(coordinates.MapId, _territory.CellOf(coordinates));
    }

    /// <summary>Whether a beacon's holder closes its link to freight.</summary>
    public bool Closes(EntityUid beacon)
    {
        return Holder(beacon) is { } holder && _proto.TryIndex(holder, out var faction) && faction.Blockades;
    }

    /// <summary>Whether a link between two sectors is closed by a blockading holder at either end.</summary>
    public bool LinkClosed(string a, string b)
    {
        return FindBeacon(a, b) is { } there && Closes(there) || FindBeacon(b, a) is { } back && Closes(back);
    }

    /// <summary>Whether a sector has no open path of beacon links to the home sector.</summary>
    public bool IsCutOff(string sector)
    {
        return _cutOff.Contains(sector);
    }

    /// <summary>Whether a station grid is in a sector cut off from home, which blockades it.</summary>
    public bool IsCutOffGrid(EntityUid grid)
    {
        return SectorOfEntity(grid) is { } sector && _cutOff.Contains(sector);
    }

    /// <summary>When a beacon's raiders next move on its cell, from now.</summary>
    private TimeSpan NextRaidIn(WFJumpBeaconComponent beacon)
    {
        return beacon.RaidInterval * _random.NextFloat(0.75f, 1.25f);
    }

    /// <summary>Starts the raids that are due on beacons that have raiders.</summary>
    private void UpdateRaids()
    {
        if (!_territory.Enabled || _timing.CurTime < _nextRaidCheck)
            return;

        _nextRaidCheck = _timing.CurTime + RaidCheckInterval;
        var query = EntityQueryEnumerator<WFJumpBeaconComponent>();
        while (query.MoveNext(out var uid, out var beacon))
        {
            if (beacon.Raiders == null || _timing.CurTime < beacon.NextRaid)
                continue;

            beacon.NextRaid = _timing.CurTime + NextRaidIn(beacon);
            TryRaid((uid, beacon));
        }
    }

    /// <summary>
    /// The beacon's raiders contest its cell: unless ships fly it before the warning runs out, they take it and close
    /// the link. False if the cell is held, contested or protected, or the beacon has no raiders.
    /// </summary>
    public bool TryRaid(Entity<WFJumpBeaconComponent> beacon)
    {
        if (beacon.Comp.Raiders is not { } raiders)
            return false;

        var (map, cell) = BeaconCell(beacon);
        if (_territory.Owner(map, cell) != null || _territory.ContestOf(map, cell) != null
            || !_territory.Contest(map, cell, raiders, _timing.CurTime + beacon.Comp.RaidWarning))
            return false;

        var faction = _proto.TryIndex(raiders, out var proto) ? Loc.GetString(proto.Name) : raiders.Id;
        Announce(Loc.GetString("wf-sector-beacon-raid", ("faction", faction), ("sector", SectorName(beacon.Comp.Sector)),
            ("link", SectorName(beacon.Comp.Link)), ("callsign", WFSectorHex.Callsign(cell)),
            ("minutes", (int) Math.Ceiling(beacon.Comp.RaidWarning.TotalMinutes))));
        return true;
    }

    private string SectorName(string sector)
    {
        return _proto.TryIndex<WFSectorPrototype>(sector, out var proto) ? Loc.GetString(proto.Name) : sector;
    }

    private void Announce(string message)
    {
        _chat.DispatchGlobalAnnouncement(message, Loc.GetString("wf-sector-travel-sender"), true, null, RegionColor);
    }

    /// <summary>A protected beacon's cell can't be claimed.</summary>
    private void OnClaimAttempt(ref WFSectorClaimAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var query = EntityQueryEnumerator<WFJumpBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beacon, out var xform))
        {
            if (beacon.Protected && xform.MapID == args.Map && _territory.CellOf(_transform.GetMapCoordinates(uid, xform)) == args.Cell)
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    /// <summary>The cell a beacon stands in, on its map.</summary>
    public (MapId Map, WFSectorCell Cell) BeaconCell(EntityUid beacon)
    {
        var coordinates = _transform.GetMapCoordinates(beacon);
        return (coordinates.MapId, _territory.CellOf(coordinates));
    }
}
