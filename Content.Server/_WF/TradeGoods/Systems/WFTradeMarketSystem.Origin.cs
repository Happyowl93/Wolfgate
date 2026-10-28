using Content.Server._WF.Traders;
using Content.Server.Shuttles.Events;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.TradeGoods;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Provenance: every trade crate is stamped where it first appears on a market map, and a chute refuses one stamped on
/// its own grid or within the haul distance of it. An unknown origin always counts as local.
/// </summary>
public sealed partial class WFTradeMarketSystem
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>Crates not yet stamped, checked every sweep.</summary>
    private readonly HashSet<EntityUid> _pending = new();

    private TimeSpan _nextSweep;

    private void InitializeOrigin()
    {
        SubscribeLocalEvent<WFTradeOriginComponent, ComponentStartup>(OnOriginStartup);
        SubscribeLocalEvent<WFTradeOriginComponent, MapInitEvent>(OnOriginMapInit);
        SubscribeLocalEvent<WFTradeOriginComponent, ComponentShutdown>(OnOriginShutdown);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
    }

    private void OnOriginStartup(Entity<WFTradeOriginComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Pending)
            _pending.Add(ent);
    }

    private void OnOriginMapInit(Entity<WFTradeOriginComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Pending)
            TryStamp(ent);
    }

    private void OnOriginShutdown(Entity<WFTradeOriginComponent> ent, ref ComponentShutdown args)
    {
        _pending.Remove(ent);
    }

    /// <summary>Re-stamps a jumping ship's crates where it arrives when they were pending or stamped on another map.</summary>
    private void OnFtlCompleted(ref FTLCompletedEvent ev)
    {
        if (!TryComp(ev.MapUid, out TransformComponent? mapXform) || !IsMarketMap(mapXform.MapID))
            return;

        var query = EntityQueryEnumerator<WFTradeOriginComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var origin, out var xform))
        {
            if (xform.GridUid == ev.Entity && (origin.Pending || origin.Point.MapId != xform.MapID))
                Stamp((uid, origin));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;

        // Ships placed by teleport (shipyard docking, z-level moves) raise no FTL event; the sweep catches their crates.
        foreach (var uid in new List<EntityUid>(_pending))
        {
            if (TryComp<WFTradeOriginComponent>(uid, out var origin))
                TryStamp((uid, origin));
            else
                _pending.Remove(uid);
        }

        var query = EntityQueryEnumerator<WFTradeOriginComponent, TransformComponent>();
        while (query.MoveNext(out _, out var origin, out var xform))
        {
            if (xform.GridUid is { } grid && HasComp<ShuttleDeedComponent>(grid))
                origin.Carrier = grid;
        }
    }

    /// <summary>Stamps a pending crate if it is on a market map now.</summary>
    private void TryStamp(Entity<WFTradeOriginComponent> crate)
    {
        var xform = Transform(crate);
        if (!crate.Comp.Pending || xform.MapUid is not { } map || Paused(map) || !IsMarketMap(xform.MapID))
            return;

        Stamp(crate);
    }

    /// <summary>Records where a crate is now as its origin.</summary>
    public void Stamp(Entity<WFTradeOriginComponent> crate)
    {
        crate.Comp.Point = _transform.GetMapCoordinates(crate);
        crate.Comp.Grid = Transform(crate).GridUid;
        crate.Comp.Pending = false;
        _pending.Remove(crate);
    }

    /// <summary>
    /// Why a chute refuses a crate for its origin, or null if it was hauled far enough. A crate whose origin is unknown
    /// or on another map is stamped at the chute and refused.
    /// </summary>
    public LocId? CheckHaul(EntityUid chute, EntityUid crate)
    {
        if (!TryComp<WFTradeOriginComponent>(crate, out var origin))
            return null;

        var chuteXform = Transform(chute);
        if (origin.Pending || origin.Point.MapId != chuteXform.MapID)
        {
            Stamp((crate, origin));
            return "wf-trade-refuse-unknown";
        }

        if (origin.Grid is { } grid && grid == chuteXform.GridUid)
            return "wf-trade-refuse-made-here";

        var distance = (origin.Point.Position - _transform.GetWorldPosition(chuteXform)).Length();
        return distance < Profile(chuteXform.GridUid).MinHaulDistance ? "wf-trade-refuse-too-close" : null;
    }

    /// <summary>Whether a crate passed the haul test at a chute, for rewards that require a real haul.</summary>
    public bool WasHauled(EntityUid chute, EntityUid crate)
    {
        if (!TryComp<WFTradeOriginComponent>(crate, out var origin) || origin.Pending)
            return false;

        var chuteXform = Transform(chute);
        return origin.Point.MapId == chuteXform.MapID
               && origin.Grid != chuteXform.GridUid
               && (origin.Point.Position - _transform.GetWorldPosition(chuteXform)).Length()
               >= Profile(chuteXform.GridUid).MinHaulDistance;
    }

    /// <summary>Whether a trader aboard the chute's grid refuses to deal with someone who hurt them.</summary>
    public bool Shunned(EntityUid chute, EntityUid user)
    {
        var grid = Transform(chute).GridUid;
        var query = EntityQueryEnumerator<TraderFleeComponent, TransformComponent>();
        while (query.MoveNext(out _, out var flee, out var xform))
        {
            if (xform.GridUid == grid && flee.Attackers.Contains(user))
                return true;
        }

        return false;
    }

    /// <summary>The haul distance players are told, in kilometres.</summary>
    public float HaulKilometres(EntityUid chute)
    {
        return Profile(Transform(chute).GridUid).MinHaulDistance / 1000f;
    }
}
