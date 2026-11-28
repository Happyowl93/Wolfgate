using System.Linq;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared._Crescent.DroneControl;
using Content.Shared._Mono.Company;
using Content.Shared._WG.NpcCrew;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WG.Drones;

/// <summary>
/// Keeps console-controlled drones from shooting through their own side: a drone's gun holds fire while the line to
/// its aim point crosses the carrier whose console commands it, another drone of that console, or a ship showing a
/// company of the same faction on its IFF.
/// </summary>
public sealed class WGDroneFireGuardSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DeviceListSystem _deviceList = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How long a gun's clear or fouled line is trusted before it is looked at again.</summary>
    private static readonly TimeSpan LineInterval = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<EntityUid, (TimeSpan Next, MapCoordinates Aim, bool Fouled)> _lines = new();
    private readonly HashSet<EntityUid> _friendly = new();
    private readonly HashSet<string> _companies = new();

    /// <summary>Whether a gun fired by <paramref name="user"/> should hold fire on the aim point; only drones ever do.</summary>
    public bool HoldsFire(EntityUid? user, EntityUid gun, MapCoordinates aim)
    {
        if (user is not { } drone || !HasComp<DroneControlComponent>(drone))
            return false;

        var now = _timing.CurTime;
        // Only the same aim reuses a line: even a small move can cross the carrier's edge.
        if (_lines.TryGetValue(gun, out var line) && now < line.Next && line.Aim.Equals(aim))
            return line.Fouled;

        if (_lines.Count > 512)
            _lines.Clear();

        var fouled = Fouled(drone, _transform.GetMapCoordinates(gun), aim);
        _lines[gun] = (now + LineInterval, aim, fouled);
        return fouled;
    }

    /// <summary>Whether the line from a drone's gun to its aim point crosses a ship of the drone's own side.</summary>
    public bool Fouled(EntityUid drone, MapCoordinates from, MapCoordinates aim)
    {
        if (from.MapId != aim.MapId || Transform(drone).GridUid is not { } own)
            return false;

        var offset = aim.Position - from.Position;
        var length = offset.Length();
        if (length < 1f)
            return false;

        CollectFriends(drone, own);
        var ray = new CollisionRay(from.Position, offset / length, (int) (CollisionGroup.Opaque | CollisionGroup.Impassable));
        return _physics.IntersectRayWithPredicate(from.MapId, ray, (own, this),
            static (hit, state) => !state.Item2.Friendly(state.Item1, hit),
            length, returnOnFirstHit: true).Any();
    }

    /// <summary>Whether a struck entity stands on a ship the drone must spare; its own hull is the native check's.</summary>
    private bool Friendly(EntityUid own, EntityUid hit)
    {
        if (Transform(hit).GridUid is not { } grid || grid == own)
            return false;

        if (_friendly.Contains(grid))
            return true;

        if (TryComp<IFFComponent>(grid, out var iff) && (iff.Flags & (IFFFlags.HideLabel | IFFFlags.Hide)) != 0)
            return false;

        var company = Company(grid);
        foreach (var ours in _companies)
        {
            if (WGCompanyFamily.Same(ours, company))
                return true;
        }

        return false;
    }

    /// <summary>The grids of the consoles commanding a drone and of their other drones, and the companies they fly.</summary>
    private void CollectFriends(EntityUid drone, EntityUid own)
    {
        _friendly.Clear();
        _companies.Clear();
        AddCompany(own);

        if (!TryComp<DeviceNetworkComponent>(drone, out var network))
            return;

        foreach (var console in network.DeviceLists)
        {
            if (!HasComp<DroneControlConsoleComponent>(console) || Transform(console).GridUid is not { } carrier)
                continue;

            _friendly.Add(carrier);
            AddCompany(carrier);
            foreach (var (_, device) in _deviceList.GetDeviceList(console))
            {
                if (HasComp<DroneControlComponent>(device) && Transform(device).GridUid is { } sibling && sibling != own)
                    _friendly.Add(sibling);
            }
        }
    }

    private void AddCompany(EntityUid grid)
    {
        if (Company(grid) is { Length: > 0 } company)
            _companies.Add(company);
    }

    private string? Company(EntityUid uid)
    {
        return CompOrNull<CompanyComponent>(uid)?.CompanyName.Id;
    }
}
