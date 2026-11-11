using Content.Server._NF.Bank;
using Content.Server.Administration.Logs;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.ScratchTickets;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.ScratchTickets;

/// <summary>Rolls scratch tickets, records the scratch and pays a winner out at an ATM.</summary>
public sealed partial class WFScratchTicketSystem : EntitySystem
{
    private static readonly ProtoId<StackPrototype> CashStack = "Credit";

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFScratchTicketComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFScratchTicketComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<BankATMComponent, InteractUsingEvent>(OnAtmInteractUsing);

        Subs.BuiEvents<WFScratchTicketComponent>(WFScratchTicketUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WFScratchTicketRevealedMessage>(OnRevealed);
        });
    }

    private void OnMapInit(Entity<WFScratchTicketComponent> ent, ref MapInitEvent args)
    {
        // A ticket saved on a map keeps what it rolled.
        if (ent.Comp.Cells.Count != WFScratchTicketRules.CellCount)
        {
            var winner = WFScratchTicketRules.Roll(ent.Comp.Symbols, ent.Comp.LoseWeight, new System.Random(_random.Next()), ent.Comp.Cells);
            ent.Comp.Prize = winner < 0 ? 0 : ent.Comp.Symbols[winner].Prize;
        }

        UpdateLook(ent);
    }

    private void OnUiOpened(Entity<WFScratchTicketComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnRevealed(Entity<WFScratchTicketComponent> ent, ref WFScratchTicketRevealedMessage args)
    {
        Reveal(ent, args.Actor);
    }

    /// <summary>Marks the ticket scratched and shows what it won.</summary>
    public void Reveal(Entity<WFScratchTicketComponent> ent, EntityUid? user)
    {
        if (ent.Comp.Scratched)
            return;

        ent.Comp.Scratched = true;
        UpdateLook(ent);
        UpdateUi(ent);

        if (ent.Comp.Prize <= 0 || user == null)
            return;

        _audio.PlayEntity(ent.Comp.WinSound, user.Value, ent);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user.Value):actor} scratched {ToPrettyString(ent)} and won {ent.Comp.Prize}");
    }

    private void OnExamined(Entity<WFScratchTicketComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.Scratched)
            args.PushMarkup(Loc.GetString("wf-scratch-ticket-examine-fresh"));
        else if (ent.Comp.Prize > 0)
            args.PushMarkup(Loc.GetString("wf-scratch-ticket-examine-won", ("amount", BankSystemExtensions.ToSpesoString(ent.Comp.Prize))));
        else
            args.PushMarkup(Loc.GetString("wf-scratch-ticket-examine-lost"));
    }

    private void OnAtmInteractUsing(Entity<BankATMComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<WFScratchTicketComponent>(args.Used, out var ticket) || !this.IsPowered(ent, EntityManager))
            return;

        args.Handled = true;

        if (!ticket.Scratched || ticket.Prize <= 0)
        {
            var refusal = ticket.Scratched ? "wf-scratch-ticket-atm-lost" : "wf-scratch-ticket-atm-fresh";
            _popup.PopupEntity(Loc.GetString(refusal), ent, args.User);
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            return;
        }

        // An Ironman's account is frozen: the money comes out as cash.
        var banked = !HasComp<IronmanComponent>(args.User) && _bank.TryBankDeposit(args.User, ticket.Prize, false);
        if (!banked)
            _stack.Spawn(ticket.Prize, CashStack, Transform(ent).Coordinates);

        _popup.PopupEntity(Loc.GetString(banked ? "wf-scratch-ticket-atm-paid" : "wf-scratch-ticket-atm-paid-cash",
            ("amount", BankSystemExtensions.ToSpesoString(ticket.Prize))), ent, args.User);
        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"{ToPrettyString(args.User):actor} redeemed {ToPrettyString(args.Used)} for {ticket.Prize} at {ToPrettyString(ent)}");
        QueueDel(args.Used);
    }

    private void UpdateUi(Entity<WFScratchTicketComponent> ent)
    {
        _ui.SetUiState(ent.Owner, WFScratchTicketUiKey.Key,
            new WFScratchTicketState(ent.Comp.Cells.ToArray(), ent.Comp.Scratched, ent.Comp.Prize));
    }

    private void UpdateLook(Entity<WFScratchTicketComponent> ent)
    {
        var look = !ent.Comp.Scratched ? WFScratchTicketLook.Fresh
            : ent.Comp.Prize > 0 ? WFScratchTicketLook.Won
            : WFScratchTicketLook.Lost;
        _appearance.SetData(ent, WFScratchTicketVisuals.State, look);
    }
}
