using System.Globalization;
using System.Linq;
using Content.Server._Mono.MonoCoins;
using Content.Server._NF.Bank;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Administration.Logs;
using Content.Server.CartridgeLoader;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.Stack;
using Content.Shared._Mono.CCVar;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Bank.Events;
using Content.Shared._WG.Banking;
using Content.Shared._WG.CCVar;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Stacks;
using Content.Shared.Traits;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Banking;

/// <summary>
/// The Bank PDA app: signs into the account of the ID card in the PDA, whoever holds it, to withdraw cash, transfer and
/// deposit. Each account may send out at most the floor or a share of its round-start balance per round, whichever is
/// higher, and take in at most a fixed amount of cash.
/// </summary>
public sealed partial class WGBankSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private MonoCoinsManager _coins = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public static readonly EntProtoId BankCartridge = "WGBankCartridge";

    private static readonly ProtoId<StackPrototype> Cash = "Credit";
    private static readonly ProtoId<TraitPrototype> Ironman = "Ironman";
    private static readonly SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
    private static readonly SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
    private const int HistoryLength = 50;

    private readonly Dictionary<(NetUserId User, int Slot), AccountRound> _rounds = new();
    private TimeSpan _nextRefresh;

    /// <summary>Set while the app moves money itself, which notes its own activity.</summary>
    private bool _own;

    /// <summary>An account's round: its opening balance, what it has sent out and taken in, and its activity.</summary>
    private sealed class AccountRound
    {
        public int Opening;
        public int Withdrawn;
        public int Deposited;

        /// <summary>The balance at the last change seen, to tell what the next one moved.</summary>
        public int Seen;

        public readonly List<WGBankEntry> History = new();
    }

    /// <summary>A card's account, resolved through its holder's connected session.</summary>
    private sealed record Account(WGBankCardComponent Card, ICommonSession Session, PlayerPreferences Prefs, HumanoidCharacterProfile Profile)
    {
        public (NetUserId, int) Key => (Card.User, Card.Slot);
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<BalanceChangedEvent>(OnBalanceChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<WGPdaProgramsEvent>(OnPdaPrograms);
        SubscribeLocalEvent<WGBankCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<WGBankCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _rounds.Clear();
    }

    private void OnPdaPrograms(ref WGPdaProgramsEvent ev)
    {
        ev.Programs.Add(BankCartridge);
    }

    /// <summary>Issues the ID card a player spawns with to their character's account.</summary>
    private void OnSpawn(PlayerSpawnCompleteEvent ev)
    {
        if (ev.Player is not { } player
            || !_prefs.TryGetCachedPreferences(player.UserId, out var prefs)
            || !_idCard.TryFindIdCard(ev.Mob, out var id))
            return;

        var slot = prefs.IndexOfCharacter(ev.Profile);
        Link(id, player.UserId, slot >= 0 ? slot : prefs.SelectedCharacterIndex, ev.Profile.Name, ev.Profile.BankBalance);
    }

    /// <summary>Issues an ID card to a character's account, and notes the account's opening balance if it's new this round.</summary>
    public void Link(EntityUid card, NetUserId user, int slot, string name, int balance)
    {
        var link = EnsureComp<WGBankCardComponent>(card);
        link.User = user;
        link.Slot = slot;
        link.Name = name;
        Round((user, slot), balance);
    }

    private AccountRound Round((NetUserId, int) key, int balance)
    {
        if (!_rounds.TryGetValue(key, out var round))
            _rounds[key] = round = new AccountRound { Opening = balance, Seen = balance };
        return round;
    }

    /// <summary>Notes every change to an account's balance the app didn't make: ATMs, vendors, wages, fines.</summary>
    private void OnBalanceChanged(BalanceChangedEvent ev)
    {
        var user = ev.Session.UserId;
        if (!_prefs.TryGetCachedPreferences(user, out var prefs))
            return;

        foreach (var (key, round) in _rounds)
        {
            if (key.User != user
                || !prefs.Characters.TryGetValue(key.Slot, out var character)
                || character is not HumanoidCharacterProfile profile)
                continue;

            var moved = profile.BankBalance - round.Seen;
            round.Seen = profile.BankBalance;
            if (moved == 0 || _own)
                continue;

            var (kind, party) = Source(ev.Session, moved);
            Note(round, kind, Math.Abs(moved), party);
        }
    }

    /// <summary>What moved money in or out, from the UI its holder has open: an ATM, a vendor or a console.</summary>
    private (WGBankEntryKind Kind, string Party) Source(ICommonSession session, int moved)
    {
        if (session.AttachedEntity is { } actor)
        {
            foreach (var (ui, _) in _ui.GetActorUis(actor))
            {
                if (HasComp<BankATMComponent>(ui))
                    return (moved < 0 ? WGBankEntryKind.AtmWithdrawal : WGBankEntryKind.AtmDeposit, string.Empty);
                if (moved < 0 && !HasComp<PdaComponent>(ui))
                    return (WGBankEntryKind.Purchase, Name(ui));
            }
        }

        return (moved < 0 ? WGBankEntryKind.Charge : WGBankEntryKind.Payment, string.Empty);
    }

    /// <summary>Runs one of the app's own transactions, which it notes itself.</summary>
    private bool Own(Func<bool> transaction)
    {
        _own = true;
        try
        {
            return transaction();
        }
        finally
        {
            _own = false;
        }
    }

    private int Limit(AccountRound round)
    {
        return WGBankMath.Limit(round.Opening, _cfg.GetCVar(BankingCVars.WithdrawFloor), _cfg.GetCVar(BankingCVars.WithdrawShare));
    }

    private WGBankStatus Resolve(EntityUid? card, out Account? account)
    {
        account = null;
        if (card == null)
            return WGBankStatus.NoId;

        if (!TryComp<WGBankCardComponent>(card, out var link))
            return WGBankStatus.Unlinked;

        if (!_players.TryGetSessionById(link.User, out var session) || !_prefs.TryGetCachedPreferences(link.User, out var prefs))
            return WGBankStatus.Offline;

        if (!prefs.Characters.TryGetValue(link.Slot, out var character)
            || character is not HumanoidCharacterProfile profile
            || profile.Name != link.Name)
            return WGBankStatus.Unlinked;

        // Saving a balance selects its slot, so only the character the holder is playing can be reached.
        if (prefs.SelectedCharacterIndex != link.Slot)
            return WGBankStatus.Offline;

        account = new Account(link, session, prefs, profile);
        return profile.TraitPreferences.Contains(Ironman) || Body(account) is { } body && HasComp<IronmanComponent>(body)
            ? WGBankStatus.Locked
            : WGBankStatus.Ready;
    }

    /// <summary>The account holder's body, when it carries this account, so its balance updates at once.</summary>
    private EntityUid? Body(Account account)
    {
        return account.Session.AttachedEntity is { } body
               && account.Prefs.SelectedCharacterIndex == account.Card.Slot
               && HasComp<BankAccountComponent>(body)
            ? body
            : null;
    }

    private bool Take(Account account, int amount)
    {
        return Own(() => Body(account) is { } body
            ? _bank.TryBankWithdraw(body, amount)
            : _bank.TryBankWithdraw(account.Session, account.Prefs, account.Profile, amount, out _));
    }

    private bool Give(Account account, int amount, bool tax)
    {
        if (Body(account) is { } body)
            return Own(() => _bank.TryBankDeposit(body, amount, tax));

        // The profile deposit has no tax of its own, so it's taken here as the body deposit takes it.
        var taxed = 0;
        if (tax)
            _bank.GetTaxedDepositAmount(amount, account.Profile.BankBalance, out amount, out taxed);

        if (amount > 0 && !Own(() => _bank.TryBankDeposit(account.Session, account.Prefs, account.Profile, amount, out _)))
            return false;

        if (taxed > 0)
            _ = _coins.AddMonoCoinsAsync(account.Session.UserId, taxed);
        return true;
    }

    private EntityUid? CardIn(EntityUid loader)
    {
        return CompOrNull<PdaComponent>(loader)?.ContainedId;
    }

    /// <summary>The thalers in someone's active hand.</summary>
    private Entity<StackComponent>? HandCash(EntityUid user)
    {
        if (_hands.GetActiveItem(user) is not { } held
            || !TryComp<StackComponent>(held, out var stack)
            || stack.StackTypeId != Cash.Id)
            return null;

        return new Entity<StackComponent>(held, stack);
    }

    /// <summary>Why an amount can't leave a card's account now, or null with the account and its round.</summary>
    private string? Check(EntityUid? card, int amount, out Account? account, out AccountRound? round)
    {
        round = null;
        var status = Resolve(card, out account);
        if (status != WGBankStatus.Ready || account == null)
            return StatusError(status);

        round = Round(account.Key, account.Profile.BankBalance);
        var left = Math.Max(0, Limit(round) - round.Withdrawn);
        if (amount <= 0)
            return Loc.GetString("wg-bank-error-amount");
        if (amount > account.Profile.BankBalance)
            return Loc.GetString("wg-bank-error-funds");
        if (amount > left)
            return Loc.GetString("wg-bank-error-limit", ("left", Format(left)));
        return null;
    }

    private string StatusError(WGBankStatus status)
    {
        return Loc.GetString($"wg-bank-error-{status.ToString().ToLowerInvariant()}");
    }

    /// <summary>Withdraws from a card's account as cash in the actor's hands. Returns why it failed, or null.</summary>
    public string? Withdraw(EntityUid actor, EntityUid? card, int amount)
    {
        if (Check(card, amount, out var account, out var round) is { } error)
            return error;

        if (!Take(account!, amount))
            return Loc.GetString("wg-bank-error-declined");

        round!.Withdrawn += amount;
        Note(round, WGBankEntryKind.Withdrawal, amount);
        var cash = _stack.Spawn(amount, _proto.Index(Cash), Transform(actor).Coordinates);
        _hands.PickupOrDrop(actor, cash);

        Alert(actor, account!, Loc.GetString("wg-bank-alert-withdrawal", ("amount", Format(amount))));
        _adminLog.Add(LogType.ATMUsage, Impact(actor, account!),
            $"{ToPrettyString(actor):actor} withdrew {amount} from {account!.Card.Name}'s account with the Bank app");
        return null;
    }

    /// <summary>Transfers from a card's account to a connected account holder. Returns why it failed, or null.</summary>
    public string? Transfer(EntityUid actor, EntityUid? card, EntityUid recipient, int amount)
    {
        if (!_cfg.GetCVar(MonoCVars.DepositEnabled))
            return Loc.GetString("wg-bank-error-transfers-off");

        if (Check(card, amount, out var account, out var round) is { } error)
            return error;

        if (!IsRecipient(recipient, account!, out var session))
            return Loc.GetString("wg-bank-error-recipient");

        if (!Take(account!, amount))
            return Loc.GetString("wg-bank-error-declined");

        // The recipient's round starts before the money lands, so the transfer isn't also noted as a payment.
        AccountRound? theirs = null;
        if (_prefs.TryGetCachedPreferences(session!.UserId, out var prefs))
        {
            _bank.TryGetBalance(recipient, out var before);
            theirs = Round((session.UserId, prefs.SelectedCharacterIndex), before);
        }

        if (!Own(() => _bank.TryBankDeposit(recipient, amount, tax: false)))
        {
            // The account changed with the withdrawal, so it's resolved again to put the money back.
            Resolve(card, out var refund);
            if (refund != null)
                Give(refund, amount, tax: false);
            return Loc.GetString("wg-bank-error-declined");
        }

        var to = Name(recipient);
        round!.Withdrawn += amount;
        Note(round, WGBankEntryKind.TransferOut, amount, to);
        if (theirs != null)
            Note(theirs, WGBankEntryKind.TransferIn, amount, account!.Card.Name);

        _chat.DispatchServerMessage(session,Loc.GetString("wg-bank-alert-received", ("amount", Format(amount)), ("from", account!.Card.Name)));
        Alert(actor, account, Loc.GetString("wg-bank-alert-transfer", ("amount", Format(amount)), ("to", to)));
        _adminLog.Add(LogType.ATMUsage, Impact(actor, account),
            $"{ToPrettyString(actor):actor} transferred {amount} from {account.Card.Name}'s account to {ToPrettyString(recipient):target} with the Bank app");
        return null;
    }

    /// <summary>
    /// Deposits the thalers in the actor's active hand into a card's account, up to what is left of the round's deposit
    /// limit; the rest stays in hand. Returns why it failed, or null.
    /// </summary>
    public string? Deposit(EntityUid actor, EntityUid? card, out int amount)
    {
        amount = 0;
        if (!_cfg.GetCVar(MonoCVars.DepositEnabled))
            return Loc.GetString("wg-bank-error-deposits-off");

        var status = Resolve(card, out var account);
        if (status != WGBankStatus.Ready || account == null)
            return StatusError(status);

        if (HandCash(actor) is not { } cash)
            return Loc.GetString("wg-bank-error-no-cash");

        var round = Round(account.Key, account.Profile.BankBalance);
        var left = Math.Max(0, _cfg.GetCVar(BankingCVars.DepositLimit) - round.Deposited);
        if (left == 0)
            return Loc.GetString("wg-bank-error-deposit-limit");

        var count = cash.Comp.Count;
        amount = Math.Min(count, left);
        if (!Give(account, amount, tax: true))
        {
            amount = 0;
            return Loc.GetString("wg-bank-error-declined");
        }

        if (amount == count)
            Del(cash);
        else
            _stack.SetCount(cash, count - amount, cash.Comp);

        round.Deposited += amount;
        Note(round, WGBankEntryKind.Deposit, amount);
        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"{ToPrettyString(actor):actor} deposited {amount} into {account.Card.Name}'s account with the Bank app");
        return null;
    }

    /// <summary>Whether a body can take a transfer from an account: a connected account holder other than the account's own.</summary>
    private bool IsRecipient(EntityUid recipient, Account account, out ICommonSession? session)
    {
        session = null;
        return Exists(recipient)
               && recipient != Body(account)
               && HasComp<BankAccountComponent>(recipient)
               && !HasComp<IronmanComponent>(recipient)
               && _players.TryGetSessionByEntity(recipient, out session);
    }

    /// <summary>
    /// Transfers to the connected account holder whose character has exactly this name. The app never lists account
    /// holders; the sender has to know who they're paying.
    /// </summary>
    public string? Transfer(EntityUid actor, EntityUid? card, string recipient, int amount)
    {
        var name = recipient.Trim();
        EntityUid? found = null;
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } body
                || !string.Equals(Name(body), name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (found != null)
                return Loc.GetString("wg-bank-error-recipient-ambiguous");
            found = body;
        }

        return found is { } target
            ? Transfer(actor, card, target, amount)
            : Loc.GetString("wg-bank-error-recipient-unknown");
    }

    private void Note(AccountRound round, WGBankEntryKind kind, int amount, string party = "")
    {
        round.History.Insert(0, new WGBankEntry { Kind = kind, Amount = amount, Party = party, Time = _ticker.RoundDuration() });
        if (round.History.Count > HistoryLength)
            round.History.RemoveAt(round.History.Count - 1);
    }

    /// <summary>Tells the account holder when someone else moves money out of their account.</summary>
    private void Alert(EntityUid actor, Account account, string message)
    {
        if (actor != account.Session.AttachedEntity)
            _chat.DispatchServerMessage(account.Session, message);
    }

    private static LogImpact Impact(EntityUid actor, Account account)
    {
        return actor == account.Session.AttachedEntity ? LogImpact.Low : LogImpact.Medium;
    }

    public static string Format(int amount)
    {
        return amount.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>The last four digits of an account number, fixed for a character's slot.</summary>
    private static string Number(WGBankCardComponent card)
    {
        var bytes = card.User.UserId.ToByteArray();
        var hash = BitConverter.ToUInt32(bytes, 0) ^ BitConverter.ToUInt32(bytes, 12) ^ (uint) (card.Slot * 7919);
        return (hash % 10000).ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>What a viewer sees of the account on a card.</summary>
    public WGBankUiState State(EntityUid viewer, EntityUid? card)
    {
        var state = new WGBankUiState
        {
            DepositEnabled = _cfg.GetCVar(MonoCVars.DepositEnabled),
            HandCash = HandCash(viewer)?.Comp.Count ?? 0,
            Status = Resolve(card, out var account),
        };

        if (account == null)
            return state;

        var round = Round(account.Key, account.Profile.BankBalance);
        state.Holder = account.Card.Name;
        state.Job = CompOrNull<IdCardComponent>(card)?.LocalizedJobTitle ?? string.Empty;
        state.Number = Number(account.Card);
        state.Balance = account.Profile.BankBalance;
        state.Limit = Limit(round);
        state.Withdrawn = round.Withdrawn;
        state.DepositLimit = _cfg.GetCVar(BankingCVars.DepositLimit);
        state.Deposited = round.Deposited;
        state.History = round.History.ToList();
        return state;
    }

    private void OnUiReady(Entity<WGBankCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        Push(args.Loader);
    }

    private void OnMessage(Entity<WGBankCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not WGBankCartridgeMessage message)
            return;

        var loader = GetEntity(args.LoaderUid);
        var actor = args.Actor;
        // Only the PDA's carrier may use it.
        if (Transform(loader).ParentUid != actor)
            return;

        var card = CardIn(loader);
        string? error;
        string? done = null;
        switch (message.Action)
        {
            case WGBankWithdrawAction withdraw:
                error = Withdraw(actor, card, withdraw.Amount);
                done = Loc.GetString("wg-bank-done-withdrawal", ("amount", Format(withdraw.Amount)));
                break;
            case WGBankTransferAction transfer:
                error = Transfer(actor, card, transfer.Recipient, transfer.Amount);
                done = Loc.GetString("wg-bank-done-transfer", ("amount", Format(transfer.Amount)), ("to", transfer.Recipient.Trim()));
                break;
            case WGBankDepositAction:
                error = Deposit(actor, card, out var deposited);
                done = Loc.GetString("wg-bank-done-deposit", ("amount", Format(deposited)));
                break;
            default:
                return;
        }

        _popup.PopupEntity(error ?? done, actor, actor);
        _audio.PlayEntity(error == null ? ConfirmSound : ErrorSound, actor, loader);
        Push(loader);
    }

    private void Push(EntityUid loader)
    {
        var holder = Transform(loader).ParentUid;
        if (!TryComp<ActorComponent>(holder, out var actor))
            return;

        _cartridgeLoader.UpdateCartridgeUiState(loader, State(holder, CardIn(loader)), actor.PlayerSession);
    }

    /// <summary>Keeps every open Bank app current: cards go in and out, and balances change elsewhere.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;
        var query = EntityQueryEnumerator<WGBankCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out _, out var cartridge))
        {
            if (cartridge.LoaderUid is { } loader
                && TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
                && loaderComp.ActiveProgram == uid
                && _ui.IsUiOpen(loader, loaderComp.UiKey))
                Push(loader);
        }
    }
}
