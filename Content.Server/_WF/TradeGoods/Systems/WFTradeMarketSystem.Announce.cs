using System.Linq;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Mono.Company;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>The Sector Exchange's voice: market lines on the common channel and private lines to some players.</summary>
public sealed partial class WFTradeMarketSystem
{
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    private static readonly ProtoId<RadioChannelPrototype> ExchangeChannel = "Common";
    private static readonly Color ExchangeColor = Color.FromHex("#d9a441");

    private EntityUid? _exchange;

    /// <summary>Says a market line on the common channel as the Sector Exchange.</summary>
    public void Radio(string message)
    {
        if (Exchange() is { } exchange)
            _radio.SendRadioMessage(exchange, message, ExchangeChannel, exchange);
    }

    /// <summary>Tells the players aboard some grids, as the Sector Exchange.</summary>
    public void TellAboard(IReadOnlyCollection<EntityUid> grids, string message)
    {
        var filter = Filter.Empty();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out _, out var actor, out var xform))
        {
            if (xform.GridUid is { } grid && grids.Contains(grid))
                filter.AddPlayer(actor.PlayerSession);
        }

        Tell(filter, message);
    }

    /// <summary>Tells the players of some companies, as the Sector Exchange.</summary>
    public void TellCompanies(IReadOnlyCollection<ProtoId<CompanyPrototype>> companies, string message)
    {
        var filter = Filter.Empty();
        var players = EntityQueryEnumerator<ActorComponent, CompanyComponent>();
        while (players.MoveNext(out _, out var actor, out var company))
        {
            if (companies.Contains(company.CompanyName))
                filter.AddPlayer(actor.PlayerSession);
        }

        Tell(filter, message);
    }

    private void Tell(Filter filter, string message)
    {
        if (filter.Count > 0)
            _chat.DispatchFilteredAnnouncement(filter, message, sender: Loc.GetString("wf-trade-exchange"), playSound: false, colorOverride: ExchangeColor);
    }

    /// <summary>The Exchange's radio: an entity on the sector map, heard without a telecom server.</summary>
    private EntityUid? Exchange()
    {
        if (_exchange is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        if (!_map.TryGetMap(_ticker.DefaultMap, out var mapUid) || mapUid is not { } uid)
            return null;

        var exchange = Spawn(null, new EntityCoordinates(uid, Vector2.Zero));
        Transform(exchange).GridTraversal = false;
        _meta.SetEntityName(exchange, Loc.GetString("wf-trade-exchange"));
        EnsureComp<TelecomExemptComponent>(exchange);
        _exchange = exchange;
        return exchange;
    }
}
