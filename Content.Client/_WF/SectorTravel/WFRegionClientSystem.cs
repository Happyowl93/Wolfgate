using Content.Shared._WF.SectorTravel;
using Robust.Client.Player;

namespace Content.Client._WF.SectorTravel;

/// <summary>Keeps the server's last word on who holds each jump beacon, and says which sector the player is in.</summary>
public sealed class WFRegionClientSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>Every beacon end, with its holder, as the server last sent.</summary>
    public IReadOnlyList<WFRegionBeaconState> Beacons => _state.Beacons;

    /// <summary>Sectors with no open link path to home.</summary>
    public IReadOnlyList<string> CutOff => _state.CutOff;

    private WFRegionStateEvent _state = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFRegionStateEvent>(OnState);
    }

    private void OnState(WFRegionStateEvent ev)
    {
        _state = ev;
    }

    /// <summary>The sector the local player's map belongs to, or null.</summary>
    public string? CurrentSector()
    {
        return _player.LocalEntity is { } local
               && TryComp<WFSectorMapComponent>(Transform(local).MapUid, out var stamp)
            ? stamp.Sector.Id
            : null;
    }
}
