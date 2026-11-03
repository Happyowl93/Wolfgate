using Content.Shared._WF.MameArcade;
using Content.Shared.IdentityManagement;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.MameArcade;

/// <summary>
/// Seats two players at a MAME cabinet and passes each one's controls to the other. The games run on the clients;
/// the server never sees ROM data, only which set and BIOS each copy uses.
/// </summary>
public sealed partial class WFMameArcadeSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    // Neo Geo frames, about 59.19 a second.
    private const float FrameMilliseconds = 1000f / 59.19f;

    // Beyond ping, an input waits up to a server tick to be relayed and a client frame to be read.
    private const float RelayMilliseconds = 50f;

    // Frames of slack on top of the trip between the two clients; the clients raise it if they still stall.
    private const int DelaySlack = 1;
    private const int MinDelay = 2;
    private const int MaxDelay = 20;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFMameArcadeComponent, PowerChangedEvent>(OnPowerChanged);

        // Server systems start before the server takes connections, so the message is known when clients join.
        _net.RegisterNetMessage<MsgMameArcadeInput>(OnInput);

        Subs.BuiEvents<WFMameArcadeComponent>(MameArcadeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<BoundUIClosedEvent>(OnClosed);
            subs.Event<MameArcadeHostMessage>(OnHost);
            subs.Event<MameArcadeJoinMessage>(OnJoin);
            subs.Event<MameArcadeLeaveMessage>(OnLeave);
        });
    }

    /// <summary>Offers a 2-player game at the cabinet. False if one is already open.</summary>
    public bool TryHost(Entity<WFMameArcadeComponent> cabinet, EntityUid player, string game, string fingerprint)
    {
        var comp = cabinet.Comp;
        if (comp.Host != null
            || game.Length is 0 or > MameArcadeProtocol.MaxGameName
            || fingerprint.Length is 0 or > MameArcadeProtocol.MaxFingerprint)
        {
            return false;
        }

        comp.Host = player;
        comp.Guest = null;
        comp.Game = game;
        comp.Fingerprint = fingerprint;
        UpdateState(cabinet);
        return true;
    }

    /// <summary>Joins the open game as player 2 and starts it for both. False unless the ROMs match.</summary>
    public bool TryJoin(Entity<WFMameArcadeComponent> cabinet, EntityUid player, string fingerprint)
    {
        var comp = cabinet.Comp;
        if (comp.Host is not { } host || comp.Guest != null || host == player || fingerprint != comp.Fingerprint)
            return false;

        comp.Guest = player;
        var delay = InputDelay(host, player);
        _ui.ServerSendUiMessage(cabinet.Owner, MameArcadeUiKey.Key, new MameArcadeStartMessage(0, delay, Name(player)), host);
        _ui.ServerSendUiMessage(cabinet.Owner, MameArcadeUiKey.Key, new MameArcadeStartMessage(1, delay, Name(host)), player);
        UpdateState(cabinet);
        return true;
    }

    /// <summary>Takes a player out of the lobby or game; the partner plays on alone.</summary>
    public void Leave(Entity<WFMameArcadeComponent> cabinet, EntityUid player)
    {
        var comp = cabinet.Comp;
        if (player != comp.Host && player != comp.Guest)
            return;

        var partner = player == comp.Host ? comp.Guest : comp.Host;
        if (partner != null)
            _ui.ServerSendUiMessage(cabinet.Owner, MameArcadeUiKey.Key, new MameArcadePartnerLeftMessage(), partner.Value);

        Reset(cabinet);
    }

    /// <summary>The other player of a running 2-player game, if <paramref name="player"/> is in one.</summary>
    public EntityUid? Partner(WFMameArcadeComponent comp, EntityUid player)
    {
        if (comp.Host == null || comp.Guest == null)
            return null;

        if (player == comp.Host)
            return comp.Guest;

        return player == comp.Guest ? comp.Host : null;
    }

    private void OnOpened(Entity<WFMameArcadeComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateState(ent);
    }

    private void OnClosed(Entity<WFMameArcadeComponent> ent, ref BoundUIClosedEvent args)
    {
        Leave(ent, args.Actor);
    }

    private void OnHost(Entity<WFMameArcadeComponent> ent, ref MameArcadeHostMessage args)
    {
        TryHost(ent, args.Actor, args.Game, args.Fingerprint);
    }

    private void OnJoin(Entity<WFMameArcadeComponent> ent, ref MameArcadeJoinMessage args)
    {
        TryJoin(ent, args.Actor, args.Fingerprint);
    }

    private void OnLeave(Entity<WFMameArcadeComponent> ent, ref MameArcadeLeaveMessage args)
    {
        Leave(ent, args.Actor);
    }

    /// <summary>Passes a seated player's inputs straight on to their partner.</summary>
    private void OnInput(MsgMameArcadeInput message)
    {
        if (!_players.TryGetSessionByChannel(message.MsgChannel, out var session)
            || session.AttachedEntity is not { } player
            || !TryGetEntity(message.Cabinet, out var cabinet)
            || !TryComp<WFMameArcadeComponent>(cabinet, out var comp)
            || Partner(comp, player) is not { } partner
            || !TryComp<ActorComponent>(partner, out var actor)
            || message.Inputs.Length == 0
            || message.FirstFrame < 0)
        {
            return;
        }

        _net.ServerSendMessage(new MsgMameArcadeInput
        {
            Cabinet = message.Cabinet,
            FirstFrame = message.FirstFrame,
            Inputs = message.Inputs,
            ChecksumFrame = message.ChecksumFrame,
            Checksum = message.Checksum,
            Margin = message.Margin,
            Delay = message.Delay,
        }, actor.PlayerSession.Channel);
    }

    private void OnPowerChanged(Entity<WFMameArcadeComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered)
            return;

        _ui.CloseUi(ent.Owner, MameArcadeUiKey.Key);
        Reset(ent);
    }

    private void Reset(Entity<WFMameArcadeComponent> cabinet)
    {
        var comp = cabinet.Comp;
        comp.Host = null;
        comp.Guest = null;
        comp.Game = string.Empty;
        comp.Fingerprint = string.Empty;
        UpdateState(cabinet);
    }

    private void UpdateState(Entity<WFMameArcadeComponent> cabinet)
    {
        var comp = cabinet.Comp;
        var state = new MameArcadeBuiState(
            GetNetEntity(comp.Host),
            comp.Host is { } host ? Name(host) : string.Empty,
            comp.Game,
            comp.Fingerprint,
            GetNetEntity(comp.Guest),
            comp.Guest is { } guest ? Name(guest) : string.Empty);

        _ui.SetUiState(cabinet.Owner, MameArcadeUiKey.Key, state);
    }

    /// <summary>Enough frames for one player's input to reach the other through the server.</summary>
    private int InputDelay(EntityUid first, EntityUid second)
    {
        var trip = (Ping(first) + Ping(second)) / 2f + RelayMilliseconds;
        return Math.Clamp((int) MathF.Ceiling(trip / FrameMilliseconds) + DelaySlack, MinDelay, MaxDelay);
    }

    private int Ping(EntityUid player)
    {
        return TryComp<ActorComponent>(player, out var actor) ? actor.PlayerSession.Channel.Ping : 0;
    }

    private string Name(EntityUid player) => Identity.Name(player, EntityManager);
}
