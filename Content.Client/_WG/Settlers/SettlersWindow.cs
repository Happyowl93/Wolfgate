using System.Numerics;
using Content.Shared._WG.Settlers;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WG.Settlers;

/// <summary>
/// The Settlers of Colossus window: the board on the left; the seats, the local player's hand, moves, contracts,
/// trades and the game log on the right.
/// </summary>
public sealed class SettlersWindow : DefaultWindow
{
    private static readonly Color TurnColor = Color.FromHex("#E8C020");
    private static readonly Color CardColor = Color.FromHex("#1D1D21");
    private static readonly Color CardEdge = Color.FromHex("#3A3A42");
    private static readonly Color SectionColor = Color.FromHex("#9AA0B4");
    private static readonly ResolvedSoundSpecifier DiceSound = new ResolvedPathSpecifier("/Audio/Items/Dice/dice3.ogg");

    private readonly IEntityManager _entMan;
    private readonly IPlayerManager _player;
    private readonly IConfigurationManager _cfg;

    private readonly SettlersBoardControl _board = new();

    private readonly StyleBoxFlat[] _seatStyles = new StyleBoxFlat[SettlersRules.MaxPlayers];
    private readonly Label[] _seatNames = new Label[SettlersRules.MaxPlayers];
    private readonly Label[] _seatStats = new Label[SettlersRules.MaxPlayers];
    private readonly Button[] _seatButtons = new Button[SettlersRules.MaxPlayers];

    private readonly Label _status = new();
    private readonly Label _hint = new();
    private readonly Button _start = new() { Text = Loc.GetString("wg-settlers-start") };
    private readonly Button _newGame = new() { Text = Loc.GetString("wg-settlers-new-game") };

    private readonly BoxContainer _playArea = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
    private readonly Label _points = new();
    private readonly SettlersCounterRow _hand = new(false);

    private readonly Button _roll = new() { Text = Loc.GetString("wg-settlers-roll") };
    private readonly Button _endTurn = new() { Text = Loc.GetString("wg-settlers-end-turn") };
    private readonly Button _endSurvey = new() { Text = Loc.GetString("wg-settlers-end-survey") };
    private readonly Button _buildLane = new() { Text = Loc.GetString("wg-settlers-build-lane"), ToggleMode = true };
    private readonly Button _buildOutpost = new() { Text = Loc.GetString("wg-settlers-build-outpost"), ToggleMode = true };
    private readonly Button _buildColony = new() { Text = Loc.GetString("wg-settlers-build-colony"), ToggleMode = true };
    private readonly Button _buyCard = new() { Text = Loc.GetString("wg-settlers-buy-card") };

    private readonly GridContainer _cards = new() { Columns = 2, HorizontalExpand = true };
    private readonly BoxContainer _choice = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
    private readonly Label _choiceLabel = new();

    private readonly BoxContainer _steal = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };

    private readonly BoxContainer _discardBox = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly Label _discardLabel = new();
    private readonly SettlersCounterRow _discard = new(true);
    private readonly Button _discardButton = new() { Text = Loc.GetString("wg-settlers-discard") };

    private readonly OptionButton _bankGive = new() { HorizontalExpand = true };
    private readonly OptionButton _bankGet = new() { HorizontalExpand = true };
    private readonly Button _bankTrade = new();
    private readonly SettlersCounterRow _offerGive = new(true);
    private readonly SettlersCounterRow _offerWant = new(true);
    private readonly Button _postOffer = new() { Text = Loc.GetString("wg-settlers-post-offer") };
    private readonly Button _withdrawOffer = new() { Text = Loc.GetString("wg-settlers-withdraw-offer") };
    private readonly BoxContainer _offers = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };

    private readonly BoxContainer _log = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };

    private SettlersStateMessage? _message;
    private int? _seat;
    private SettlersPick _buildPick;

    /// <summary>A windfall or embargo waiting for the resources it names, and the windfall's first pick.</summary>
    private SettlersCard? _pendingCard;
    private int? _firstPick;
    private int _rollCount = -1;

    public event Action<int>? OnSeat;
    public event Action? OnStart;
    public event Action? OnNewGame;
    public event Action<SettlersAction>? OnAction;

    public SettlersWindow()
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        _player = IoCManager.Resolve<IPlayerManager>();
        _cfg = IoCManager.Resolve<IConfigurationManager>();

        Title = Loc.GetString("wg-settlers-window-title");
        SetSize = new Vector2(1040f, 720f);
        MinSize = new Vector2(900f, 600f);

        var side = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        for (var seat = 0; seat < SettlersRules.MaxPlayers; seat++)
        {
            side.AddChild(MakeSeat(seat));
        }

        _status.StyleClasses.Add("LabelHeading");
        _hint.StyleClasses.Add("LabelSubText");
        side.AddChild(_status);
        side.AddChild(_hint);
        side.AddChild(Row(_start, _newGame));

        BuildPlayArea();
        side.AddChild(_playArea);

        side.AddChild(Section("wg-settlers-section-log"));
        side.AddChild(_log);

        var scroll = new ScrollContainer { HScrollEnabled = false, MinWidth = 440f, VerticalExpand = true };
        scroll.AddChild(side);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        root.AddChild(_board);
        root.AddChild(scroll);
        Contents.AddChild(root);

        _board.OnPicked += OnPicked;
        _start.OnPressed += _ => OnStart?.Invoke();
        _newGame.OnPressed += _ => OnNewGame?.Invoke();
    }

    private void BuildPlayArea()
    {
        _playArea.AddChild(Row(Section("wg-settlers-section-hand"), _points));
        _playArea.AddChild(_hand);

        _playArea.AddChild(Section("wg-settlers-section-turn"));
        _playArea.AddChild(Row(_roll, _endTurn, _endSurvey));
        _playArea.AddChild(Row(_buildLane, _buildOutpost, _buildColony, _buyCard));
        _buildLane.ToolTip = Cost(SettlersRules.LaneCost);
        _buildOutpost.ToolTip = Cost(SettlersRules.OutpostCost);
        _buildColony.ToolTip = Cost(SettlersRules.ColonyCost);
        _buyCard.ToolTip = Cost(SettlersRules.CardCost);

        _roll.OnPressed += _ => Act(SettlersActionKind.Roll);
        _endTurn.OnPressed += _ => Act(SettlersActionKind.EndTurn);
        _endSurvey.OnPressed += _ => Act(SettlersActionKind.EndSurvey);
        _buyCard.OnPressed += _ => Act(SettlersActionKind.BuyCard);
        _buildLane.OnToggled += args => ToggleBuild(SettlersPick.Lane, args.Pressed);
        _buildOutpost.OnToggled += args => ToggleBuild(SettlersPick.Outpost, args.Pressed);
        _buildColony.OnToggled += args => ToggleBuild(SettlersPick.Colony, args.Pressed);

        _steal.Visible = false;
        _playArea.AddChild(_steal);

        _discardBox.AddChild(_discardLabel);
        _discardBox.AddChild(_discard);
        _discardBox.AddChild(_discardButton);
        _discard.OnChanged += Refresh;
        _discardButton.OnPressed += _ => SendCounts(SettlersActionKind.Discard, _discard.Counts, Array.Empty<int>());
        _playArea.AddChild(_discardBox);

        _playArea.AddChild(Section("wg-settlers-section-contracts"));
        _playArea.AddChild(_cards);
        _choice.AddChild(_choiceLabel);
        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            var index = resource;
            var button = new Button { ToolTip = SettlersArt.ResourceName(resource) };
            button.AddChild(new TextureRect
            {
                Texture = SettlersArt.ResourceTexture(resource),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                MinSize = new Vector2(24f, 24f),
            });
            button.OnPressed += _ => PickResource(index);
            _choice.AddChild(button);
        }

        var cancel = new Button { Text = Loc.GetString("wg-settlers-cancel") };
        cancel.OnPressed += _ => CancelCard();
        _choice.AddChild(cancel);
        _choice.Visible = false;
        _playArea.AddChild(_choice);

        _playArea.AddChild(Section("wg-settlers-section-trade"));
        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            _bankGive.AddItem(SettlersArt.ResourceName(resource), resource);
            _bankGet.AddItem(SettlersArt.ResourceName(resource), resource);
        }

        _bankGet.SelectId(1);
        _bankGive.OnItemSelected += args =>
        {
            _bankGive.SelectId(args.Id);
            Refresh();
        };
        _bankGet.OnItemSelected += args =>
        {
            _bankGet.SelectId(args.Id);
            Refresh();
        };
        _bankTrade.OnPressed += _ => OnAction?.Invoke(new SettlersAction(SettlersActionKind.BankTrade, 0, _bankGive.SelectedId, _bankGet.SelectedId));
        _playArea.AddChild(Row(new Label { Text = Loc.GetString("wg-settlers-bank-give") }, _bankGive,
            new Label { Text = Loc.GetString("wg-settlers-bank-get") }, _bankGet, _bankTrade));

        _playArea.AddChild(new Label { Text = Loc.GetString("wg-settlers-offer-give"), FontColorOverride = SectionColor });
        _playArea.AddChild(_offerGive);
        _playArea.AddChild(new Label { Text = Loc.GetString("wg-settlers-offer-want"), FontColorOverride = SectionColor });
        _playArea.AddChild(_offerWant);
        _playArea.AddChild(Row(_postOffer, _withdrawOffer));
        _offerGive.OnChanged += Refresh;
        _offerWant.OnChanged += Refresh;
        _postOffer.OnPressed += _ => SendCounts(SettlersActionKind.Offer, _offerGive.Counts, _offerWant.Counts);
        _withdrawOffer.OnPressed += _ => SendCounts(SettlersActionKind.Offer, new int[SettlersRules.ResourceCount], new int[SettlersRules.ResourceCount]);
        _playArea.AddChild(_offers);
    }

    private static Label Section(string loc)
    {
        return new Label { Text = Loc.GetString(loc), FontColorOverride = SectionColor, HorizontalExpand = true };
    }

    private static BoxContainer Row(params Control[] children)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        foreach (var child in children)
        {
            row.AddChild(child);
        }

        return row;
    }

    private static string Cost(int[] cost)
    {
        return Loc.GetString("wg-settlers-cost", ("cost", SettlersArt.Counts(cost)));
    }

    /// <summary>A seat's card: the player's colour, name and public numbers, and the button to join or leave.</summary>
    private PanelContainer MakeSeat(int seat)
    {
        var style = new StyleBoxFlat { BackgroundColor = CardColor, BorderColor = CardEdge, BorderThickness = new Thickness(2) };
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, 3);
        _seatStyles[seat] = style;

        var swatch = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = SettlersArt.SeatColors[seat] },
            MinSize = new Vector2(12f, 28f),
            VerticalAlignment = VAlignment.Center,
        };

        _seatNames[seat] = new Label { ClipText = true };
        _seatStats[seat] = new Label { ClipText = true };
        _seatStats[seat].StyleClasses.Add("LabelSubText");
        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        text.AddChild(_seatNames[seat]);
        text.AddChild(_seatStats[seat]);

        _seatButtons[seat] = new Button { VerticalAlignment = VAlignment.Center };
        _seatButtons[seat].OnPressed += _ => OnSeat?.Invoke(_seat == seat ? -1 : seat);

        var box = Row(swatch, text, _seatButtons[seat]);
        box.SeparationOverride = 8;
        var card = new PanelContainer { PanelOverride = style, HorizontalExpand = true };
        card.AddChild(box);
        return card;
    }

    public void UpdateState(SettlersStateMessage message)
    {
        _message = message;
        _seat = null;
        if (_player.LocalEntity is { } local && _entMan.TryGetNetEntity(local, out var net))
        {
            for (var seat = 0; seat < message.Seats.Length; seat++)
            {
                if (message.Seats[seat] == net)
                    _seat = seat;
            }
        }

        var state = message.State;
        if (_rollCount >= 0 && state.RollCount > _rollCount)
            PlayDice();

        _rollCount = state.RollCount;

        if (_seat is not { } mine || state.Phase != SettlersPhase.Discard || state.Discard[mine] == 0)
            _discard.Clear();

        if (state.Phase != SettlersPhase.Main || _seat != state.Turn)
            CancelCard();

        Refresh();
    }

    private void PlayDice()
    {
        var gain = _cfg.GetCVar(CCVars.InterfaceVolume);
        if (gain <= 0f)
            return;

        _entMan.System<AudioSystem>().PlayGlobal(DiceSound, Filter.Local(), false,
            AudioParams.Default.WithVolume(SharedAudioSystem.GainToVolume(gain)));
    }

    private string SeatName(int seat)
    {
        if (_message?.SeatNames is { } names && seat < names.Length && names[seat] is { } name)
            return name;

        return Loc.GetString("wg-settlers-seat", ("number", seat + 1));
    }

    private void Refresh()
    {
        if (_message is not { } message)
            return;

        var state = message.State;
        var mine = _seat;
        var myTurn = mine != null && mine == state.Turn && state.Running;

        RefreshSeats(state, message);
        RefreshStatus(state, mine, myTurn);

        _start.Visible = state.Phase == SettlersPhase.Lobby;
        _start.Disabled = mine == null;
        _newGame.Visible = state.Phase != SettlersPhase.Lobby;
        _newGame.Disabled = mine == null || state.Running && SeatedCount(message) > 1;

        _playArea.Visible = mine != null && state.Players[mine.Value].InGame;
        if (mine is not { } seat || !_playArea.Visible)
        {
            _board.State = state;
            _board.Seat = null;
            _board.Pick = SettlersPick.None;
            RefreshLog(state);
            return;
        }

        var player = state.Players[seat];
        var main = myTurn && state.Phase == SettlersPhase.Main;
        _hand.Show(player.Hand);
        var hidden = player.HiddenPoints;
        _points.Text = hidden > 0
            ? Loc.GetString("wg-settlers-points-hidden", ("points", state.Points(seat)), ("hidden", hidden))
            : Loc.GetString("wg-settlers-points", ("points", state.Points(seat)));

        _roll.Disabled = !(myTurn && state.Phase == SettlersPhase.Roll);
        _endTurn.Disabled = !main;
        _endSurvey.Visible = myTurn && state.Phase == SettlersPhase.Survey;

        SetBuild(_buildLane, SettlersPick.Lane, main && state.CanAfford(seat, SettlersRules.LaneCost) && state.Lanes(seat) < SettlersRules.MaxLanes);
        SetBuild(_buildOutpost, SettlersPick.Outpost, main && state.CanAfford(seat, SettlersRules.OutpostCost) && state.Count(seat, false) < SettlersRules.MaxOutposts);
        SetBuild(_buildColony, SettlersPick.Colony, main && state.CanAfford(seat, SettlersRules.ColonyCost) && state.Count(seat, true) < SettlersRules.MaxColonies);
        _buyCard.Disabled = !main || state.DeckCount == 0 || !state.CanAfford(seat, SettlersRules.CardCost);
        _buyCard.Text = Loc.GetString("wg-settlers-buy-card-left", ("count", state.DeckCount));

        _board.State = state;
        _board.Seat = seat;
        _board.Pick = myTurn ? state.Phase switch
        {
            SettlersPhase.SetupOutpost => SettlersPick.Outpost,
            SettlersPhase.SetupLane or SettlersPhase.Survey => SettlersPick.Lane,
            SettlersPhase.MovePirates => SettlersPick.Pirates,
            SettlersPhase.Main => _buildPick,
            _ => SettlersPick.None,
        } : SettlersPick.None;

        RefreshCards(state, seat, myTurn);
        RefreshSteal(state, seat, myTurn);
        RefreshDiscard(state, seat);
        RefreshTrade(state, seat, main);
        RefreshLog(state);
    }

    private static int SeatedCount(SettlersStateMessage message)
    {
        var count = 0;
        foreach (var seat in message.Seats)
        {
            if (seat != null)
                count++;
        }

        return count;
    }

    private void RefreshSeats(SettlersState state, SettlersStateMessage message)
    {
        for (var seat = 0; seat < SettlersRules.MaxPlayers; seat++)
        {
            var player = state.Players[seat];
            var taken = seat < message.Seats.Length && message.Seats[seat] != null;
            var lit = state.Phase == SettlersPhase.Finished ? state.Winner == seat : state.Running && state.Turn == seat;
            _seatStyles[seat].BorderColor = lit ? TurnColor : CardEdge;
            _seatNames[seat].FontColorOverride = lit ? TurnColor : null;

            if (taken)
                _seatNames[seat].Text = SeatName(seat);
            else
                _seatNames[seat].Text = Loc.GetString(player.InGame ? "wg-settlers-seat-vacant" : "wg-settlers-seat-open", ("number", seat + 1));

            if (player.InGame)
            {
                var points = seat == _seat ? state.Points(seat) : state.PublicPoints(seat);
                var stats = Loc.GetString("wg-settlers-seat-stats", ("points", points), ("cards", state.HandSize(seat)),
                    ("contracts", player.CardCount), ("escorts", player.Escorts), ("route", player.Route));
                if (state.RouteHolder == seat)
                    stats += " " + Loc.GetString("wg-settlers-badge-route");
                if (state.FleetHolder == seat)
                    stats += " " + Loc.GetString("wg-settlers-badge-fleet");
                if (state.Discard[seat] > 0)
                    stats += " " + Loc.GetString("wg-settlers-badge-discarding", ("count", state.Discard[seat]));

                _seatStats[seat].Text = stats;
            }
            else
            {
                _seatStats[seat].Text = Loc.GetString(state.Running ? "wg-settlers-seat-not-playing" : "wg-settlers-seat-waiting");
            }

            var canJoin = _seat == null && !taken && (!state.Running || player.InGame) && state.Phase != SettlersPhase.Finished;
            _seatButtons[seat].Text = Loc.GetString(_seat == seat ? "wg-settlers-leave" : "wg-settlers-join");
            _seatButtons[seat].Visible = _seat == seat || canJoin;
        }
    }

    private void RefreshStatus(SettlersState state, int? mine, bool myTurn)
    {
        var turnName = SeatName(state.Turn);
        _status.Text = state.Phase switch
        {
            SettlersPhase.Lobby => Loc.GetString("wg-settlers-status-lobby"),
            SettlersPhase.Finished => Loc.GetString("wg-settlers-status-won", ("player", SeatName(state.Winner))),
            _ => Loc.GetString($"wg-settlers-status-{state.Phase.ToString().ToLowerInvariant()}", ("player", turnName), ("count", state.FreeLanes)),
        };

        string hint;
        if (mine == null)
            hint = Loc.GetString("wg-settlers-hint-spectator");
        else if (state.Phase == SettlersPhase.Lobby)
            hint = Loc.GetString("wg-settlers-hint-lobby", ("min", SettlersRules.MinPlayers), ("max", SettlersRules.MaxPlayers));
        else if (state.Phase == SettlersPhase.Finished)
            hint = Loc.GetString("wg-settlers-hint-finished");
        else if (state.Phase == SettlersPhase.Discard && state.Discard[mine.Value] > 0)
            hint = Loc.GetString("wg-settlers-hint-discard", ("count", state.Discard[mine.Value]));
        else if (state.Phase == SettlersPhase.Discard)
            hint = Loc.GetString("wg-settlers-hint-others-discarding");
        else if (!myTurn)
            hint = Loc.GetString("wg-settlers-hint-waiting", ("player", turnName));
        else
            hint = Loc.GetString($"wg-settlers-hint-{state.Phase.ToString().ToLowerInvariant()}", ("count", state.FreeLanes));

        _hint.Text = hint;
    }

    private void SetBuild(Button button, SettlersPick pick, bool enabled)
    {
        button.Disabled = !enabled;
        if (!enabled && _buildPick == pick)
            _buildPick = SettlersPick.None;

        button.Pressed = _buildPick == pick;
    }

    private void ToggleBuild(SettlersPick pick, bool pressed)
    {
        _buildPick = pressed ? pick : SettlersPick.None;
        Refresh();
    }

    private void RefreshCards(SettlersState state, int seat, bool myTurn)
    {
        _cards.RemoveAllChildren();
        var player = state.Players[seat];
        var canPlay = myTurn && state.Phase is SettlersPhase.Roll or SettlersPhase.Main && !state.PlayedCard;

        for (var card = 0; card < SettlersRules.CardCounts.Length; card++)
        {
            var kind = (SettlersCard) card;
            var held = Count(player.Cards, kind);
            var fresh = Count(player.NewCards, kind);
            if (held + fresh == 0)
                continue;

            var button = new Button
            {
                Text = Loc.GetString("wg-settlers-card-button", ("card", SettlersArt.CardName(kind)), ("count", held + fresh)),
                ToolTip = Loc.GetString($"wg-settlers-card-{kind.ToString().ToLowerInvariant()}-desc"),
                HorizontalExpand = true,
                Disabled = kind == SettlersCard.Charter || held == 0 || !canPlay,
            };

            if (kind != SettlersCard.Charter && held == 0)
                button.ToolTip += "\n" + Loc.GetString("wg-settlers-card-new");

            button.OnPressed += _ => PlayCard(kind);
            _cards.AddChild(button);
        }

        if (_cards.ChildCount == 0)
            _cards.AddChild(new Label { Text = Loc.GetString("wg-settlers-no-cards"), FontColorOverride = SectionColor });
    }

    private static int Count(List<SettlersCard> cards, SettlersCard kind)
    {
        var count = 0;
        foreach (var card in cards)
        {
            if (card == kind)
                count++;
        }

        return count;
    }

    private void PlayCard(SettlersCard card)
    {
        if (card is SettlersCard.Windfall or SettlersCard.Embargo)
        {
            _pendingCard = card;
            _firstPick = null;
            _choiceLabel.Text = Loc.GetString(card == SettlersCard.Windfall ? "wg-settlers-choose-windfall" : "wg-settlers-choose-embargo");
            _choice.Visible = true;
            return;
        }

        OnAction?.Invoke(new SettlersAction(SettlersActionKind.PlayCard, (int) card));
    }

    private void PickResource(int resource)
    {
        if (_pendingCard is not { } card)
            return;

        if (card == SettlersCard.Windfall && _firstPick == null)
        {
            _firstPick = resource;
            _choiceLabel.Text = Loc.GetString("wg-settlers-choose-windfall-second", ("first", SettlersArt.ResourceName(resource)));
            return;
        }

        OnAction?.Invoke(new SettlersAction(SettlersActionKind.PlayCard, (int) card, _firstPick ?? resource, resource));
        CancelCard();
    }

    private void CancelCard()
    {
        _pendingCard = null;
        _firstPick = null;
        _choice.Visible = false;
    }

    private void RefreshSteal(SettlersState state, int seat, bool myTurn)
    {
        _steal.RemoveAllChildren();
        _steal.Visible = myTurn && state.Phase == SettlersPhase.Steal;
        if (!_steal.Visible)
            return;

        _steal.AddChild(new Label { Text = Loc.GetString("wg-settlers-rob") });
        foreach (var victim in state.Victims(seat, state.Pirates))
        {
            var target = victim;
            var button = new Button { Text = SeatName(victim) };
            button.OnPressed += _ => OnAction?.Invoke(new SettlersAction(SettlersActionKind.Steal, target));
            _steal.AddChild(button);
        }
    }

    private void RefreshDiscard(SettlersState state, int seat)
    {
        var owed = state.Phase == SettlersPhase.Discard ? state.Discard[seat] : 0;
        _discardBox.Visible = owed > 0;
        if (owed == 0)
            return;

        _discard.SetLimits(state.Players[seat].Hand);
        var chosen = SettlersRules.Total(_discard.Counts);
        _discardLabel.Text = Loc.GetString("wg-settlers-discard-count", ("count", owed), ("chosen", chosen));
        _discardButton.Disabled = chosen != owed;
    }

    private void RefreshTrade(SettlersState state, int seat, bool main)
    {
        var hand = state.Players[seat].Hand;
        var give = _bankGive.SelectedId;
        var get = _bankGet.SelectedId;
        var ratio = state.TradeRatio(seat, give);
        _bankTrade.Text = Loc.GetString("wg-settlers-bank-trade", ("ratio", ratio));
        _bankTrade.Disabled = !main || give == get || hand[give] < ratio || state.Bank[get] < 1;

        var trading = state.Phase == SettlersPhase.Main;
        _offerGive.SetLimits(hand);
        _postOffer.Disabled = !trading || SettlersRules.Total(_offerGive.Counts) == 0 || SettlersRules.Total(_offerWant.Counts) == 0;
        _withdrawOffer.Visible = state.Offers[seat] != null;

        _offers.RemoveAllChildren();
        for (var offerer = 0; offerer < state.Offers.Length; offerer++)
        {
            if (state.Offers[offerer] is not { } offer)
                continue;

            var text = Loc.GetString("wg-settlers-offer-line", ("player", SeatName(offerer)),
                ("give", SettlersArt.Counts(offer.Give)), ("want", SettlersArt.Counts(offer.Want)));
            var label = new Label { Text = text, HorizontalExpand = true, ClipText = true, ToolTip = text };
            label.StyleClasses.Add("LabelSubText");

            var row = Row(label);
            var mayAccept = offerer != seat && (offerer == state.Turn || seat == state.Turn) && state.CanAfford(seat, offer.Want);
            if (offerer != seat && trading)
            {
                var from = offerer;
                var accept = new Button { Text = Loc.GetString("wg-settlers-accept"), Disabled = !mayAccept };
                accept.OnPressed += _ => OnAction?.Invoke(new SettlersAction(SettlersActionKind.Accept, from));
                row.AddChild(accept);
            }

            _offers.AddChild(row);
        }
    }

    private void RefreshLog(SettlersState state)
    {
        _log.RemoveAllChildren();
        for (var i = state.Log.Count - 1; i >= 0; i--)
        {
            var entry = state.Log[i];
            var label = new Label { Text = LogLine(entry), ClipText = true };
            label.StyleClasses.Add("LabelSubText");
            _log.AddChild(label);
        }
    }

    private string LogLine(SettlersLogEntry entry)
    {
        var player = SeatName(entry.Seat);
        var other = entry.Other >= 0 && entry.Other < SettlersRules.MaxPlayers ? SeatName(entry.Other) : string.Empty;
        return entry.Kind switch
        {
            SettlersLogKind.Built => Loc.GetString(entry.Value == 0 ? "wg-settlers-log-lane" : "wg-settlers-log-outpost", ("player", player)),
            SettlersLogKind.PlayedCard => Loc.GetString("wg-settlers-log-playedcard", ("player", player), ("card", SettlersArt.CardName((SettlersCard) entry.Value))),
            SettlersLogKind.BankTrade => Loc.GetString("wg-settlers-log-banktrade", ("player", player),
                ("give", SettlersArt.ResourceName(entry.Other)), ("get", SettlersArt.ResourceName(entry.Value))),
            _ => Loc.GetString($"wg-settlers-log-{entry.Kind.ToString().ToLowerInvariant()}", ("player", player), ("other", other), ("value", entry.Value)),
        };
    }

    private void OnPicked(SettlersPick pick, int target)
    {
        var kind = pick switch
        {
            SettlersPick.Lane => SettlersActionKind.BuildLane,
            SettlersPick.Outpost => SettlersActionKind.BuildOutpost,
            SettlersPick.Colony => SettlersActionKind.BuildColony,
            _ => SettlersActionKind.MovePirates,
        };

        OnAction?.Invoke(new SettlersAction(kind, target));
    }

    private void Act(SettlersActionKind kind)
    {
        OnAction?.Invoke(new SettlersAction(kind));
    }

    private void SendCounts(SettlersActionKind kind, int[] give, int[] want)
    {
        OnAction?.Invoke(new SettlersAction(kind)
        {
            Give = (int[]) give.Clone(),
            Want = (int[]) want.Clone(),
        });

        if (kind == SettlersActionKind.Offer)
        {
            _offerGive.Clear();
            _offerWant.Clear();
        }
    }
}
