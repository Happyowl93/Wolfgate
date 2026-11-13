using System.Linq;
using Content.Server._WF.PlanetComms;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.CartridgeLoader;
using Content.Server.Chat.Managers;
using Content.Server.Power.Components;
using Content.Server.Radio;
using Content.Shared._WF.NanoChat;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.NanoChat;

/// <summary>
/// The NanoChat app: its window, and sending messages. A message rides the cartridge's radio channel, so it needs
/// a telecom server (or a planet's mass transceiver) where it is sent from, and the receiving PDA must be able to
/// hear that channel, as headsets do. Ghosts read delivered messages in chat unless they are sent subtle; admin
/// ghosts read those too.
/// </summary>
public sealed class NanoChatCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _loader = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private NanoChatSystem _nanoChat = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private WFPlanetCommsSystem _planetComms = default!;

    private static readonly EntProtoId Program = "NanoChatCartridge";

    /// <summary>The shortest gap between two messages from one sender.</summary>
    private static readonly TimeSpan SendCooldown = TimeSpan.FromSeconds(1);

    private const int NotificationMaxLength = 64;
    private const int NotificationTitleMaxLength = 32;

    private readonly Dictionary<EntityUid, TimeSpan> _lastSent = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NanoChatCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<NanoChatCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<WFPdaProgramsEvent>(OnPdaPrograms);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _lastSent.Clear());
    }

    private void OnPdaPrograms(ref WFPdaProgramsEvent ev)
    {
        ev.Programs.Add(Program);
    }

    private void OnUiReady(Entity<NanoChatCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateUI(ent, args.Loader);
    }

    private void OnMessage(Entity<NanoChatCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not NanoChatUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        if (!_nanoChat.TryGetCard(loader, out var card))
            return;

        switch (msg.Type)
        {
            case NanoChatUiMessageType.NewChat:
                HandleNewChat(card.Value, msg);
                break;
            case NanoChatUiMessageType.SelectChat:
                HandleSelectChat(card.Value, msg);
                break;
            case NanoChatUiMessageType.EditChat:
                HandleEditChat(card.Value, msg);
                break;
            case NanoChatUiMessageType.CloseChat:
                _nanoChat.SetCurrentChat(card.Value, null);
                break;
            case NanoChatUiMessageType.ToggleMute:
                _nanoChat.SetNotificationsMuted(card.Value, !card.Value.Comp.NotificationsMuted);
                break;
            case NanoChatUiMessageType.ToggleMuteChat:
                if (msg.RecipientNumber is { } chat)
                    _nanoChat.ToggleChatMuted(card.Value, chat);
                break;
            case NanoChatUiMessageType.DeleteChat:
                HandleDeleteChat(card.Value, msg);
                break;
            case NanoChatUiMessageType.SendMessage:
                HandleSendMessage(ent, card.Value, msg);
                break;
            case NanoChatUiMessageType.ToggleListNumber:
                _nanoChat.SetListNumber(card.Value, !card.Value.Comp.ListNumber);
                break;
        }

        UpdateUI(ent, loader);
    }

    private static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();
        return text.Length > maxLength ? text[..maxLength] : text;
    }

    private void HandleNewChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number
            || number == card.Comp.Number
            || Clean(msg.Content, IdCardConsoleComponent.MaxFullNameLength) is not { } name
            || card.Comp.Recipients.Count >= card.Comp.MaxRecipients && !card.Comp.Recipients.ContainsKey(number))
            return;

        var job = Clean(msg.RecipientJob, IdCardConsoleComponent.MaxJobTitleLength);
        _nanoChat.SetRecipient(card, number, new NanoChatRecipient(number, name, job));
        _nanoChat.EnsureRecipientExists(card, number);

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} created a NanoChat chat with #{SharedNanoChatSystem.FormatNumber(number)} ({name})");
    }

    private void HandleSelectChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number)
            return;

        _nanoChat.SetCurrentChat(card, number);
        if (_nanoChat.GetRecipient(card, number) is { } recipient)
            _nanoChat.SetRecipient(card, number, recipient with { HasUnread = false });
    }

    private void HandleEditChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number
            || _nanoChat.GetRecipient(card, number) is not { } recipient
            || Clean(msg.Content, IdCardConsoleComponent.MaxFullNameLength) is not { } name)
            return;

        recipient.Name = name;
        recipient.JobTitle = Clean(msg.RecipientJob, IdCardConsoleComponent.MaxJobTitleLength);
        _nanoChat.SetRecipient(card, number, recipient);
    }

    private void HandleDeleteChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number || !_nanoChat.TryDeleteChat(card, number, true))
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} deleted the NanoChat chat with #{SharedNanoChatSystem.FormatNumber(number)}");
    }

    private void HandleSendMessage(Entity<NanoChatCartridgeComponent> cartridge,
        Entity<NanoChatCardComponent> card,
        NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number
            || card.Comp.Number is not { } ownNumber
            || number == ownNumber
            || Clean(msg.Content, NanoChatMessage.MaxContentLength) is not { } content)
            return;

        var now = _timing.CurTime;
        if (_lastSent.TryGetValue(msg.Actor, out var last) && now - last < SendCooldown)
        {
            _popup.PopupEntity(Loc.GetString("nano-chat-too-fast"), msg.Actor, msg.Actor, PopupType.SmallCaution);
            return;
        }

        if (!_nanoChat.EnsureRecipientExists(card, number, GetCardInfo(number)))
            return;

        _lastSent[msg.Actor] = now;
        var recipients = FindReachable(cartridge, number);
        var message = new NanoChatMessage(now, content, ownNumber, recipients.Count == 0);
        _nanoChat.AddMessage(card, number, message);

        var names = recipients.Count > 0
            ? string.Join(", ", recipients.Select(r => ToPrettyString(r.Owner)))
            : $"#{SharedNanoChatSystem.FormatNumber(number)}";
        _adminLogger.Add(LogType.Chat, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} sent NanoChat message to {names}: {content}{(recipients.Count == 0 ? " [DELIVERY FAILED]" : "")}{(msg.Subtle ? " [SUBTLE]" : "")}");

        foreach (var recipient in recipients)
        {
            Deliver(ownNumber, recipient, message);
        }

        if (recipients.Count > 0)
            ShowGhosts(msg.Actor, ownNumber, number, content, msg.Subtle);
    }

    /// <summary>Puts a delivered message in ghosts' chat, or only admin ghosts' if it was sent subtle.</summary>
    private void ShowGhosts(EntityUid sender, uint senderNumber, uint recipientNumber, string content, bool subtle)
    {
        var filter = Filter.Empty().AddWhereAttachedEntity(uid => HasComp<GhostComponent>(uid));
        if (subtle)
            filter.RemoveWhere(session => !_admin.IsAdmin(session));

        string NameOf(uint number) => FormattedMessage.EscapeText(GetCardInfo(number)?.Name ?? $"#{SharedNanoChatSystem.FormatNumber(number)}");
        var wrapped = Loc.GetString(subtle ? "nano-chat-ghost-message-subtle" : "nano-chat-ghost-message",
            ("sender", NameOf(senderNumber)),
            ("recipient", NameOf(recipientNumber)),
            ("message", FormattedMessage.EscapeText(content)));

        _chat.ChatMessageToManyFiltered(filter, ChatChannel.Radio, content, wrapped, sender, false, !subtle, null);
    }

    /// <summary>The cards holding a number whose PDA this cartridge's message reaches.</summary>
    private List<Entity<NanoChatCardComponent>> FindReachable(Entity<NanoChatCartridgeComponent> sender, uint number)
    {
        var found = new List<Entity<NanoChatCardComponent>>();
        var channel = _prototype.Index(sender.Comp.RadioChannel);
        if (!CanSend(sender, channel))
            return found;

        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var card))
        {
            if (card.Number != number
                || _nanoChat.GetPda((uid, card)) is not { } pda
                || !_loader.TryGetProgram<NanoChatCartridgeComponent>(pda, out var receiver, out _))
                continue;

            var ev = new RadioReceiveAttemptEvent(channel, sender, receiver.Value);
            RaiseLocalEvent(ref ev);
            RaiseLocalEvent(receiver.Value, ref ev);
            if (!ev.Cancelled)
                found.Add((uid, card));
        }

        return found;
    }

    /// <summary>Whether a message can go out from here: nothing blocks the channel and there is a server to carry it.</summary>
    private bool CanSend(EntityUid sender, RadioChannelPrototype channel)
    {
        var ev = new RadioSendAttemptEvent(channel, sender, channel.Frequency);
        RaiseLocalEvent(ref ev);
        RaiseLocalEvent(sender, ref ev);
        return !ev.Cancelled && HasSignal(sender, channel.ID);
    }

    /// <summary>Whether a telecom server for the channel is up on this map, or a planet's mass transceiver relays.</summary>
    private bool HasSignal(EntityUid uid, string channel)
    {
        if (_planetComms.RelaysTelecom(uid))
            return true;

        var map = Transform(uid).MapID;
        if (map == MapId.Nullspace)
            return false;

        var servers = EntityQueryEnumerator<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (servers.MoveNext(out _, out var keys, out var power, out var xform))
        {
            if (xform.MapID == map && power.Powered && keys.Channels.Contains(channel))
                return true;
        }

        return false;
    }

    private void Deliver(uint senderNumber, Entity<NanoChatCardComponent> recipient, NanoChatMessage message)
    {
        if (!_nanoChat.EnsureRecipientExists(recipient, senderNumber, GetCardInfo(senderNumber)))
            return;

        _nanoChat.AddMessage(recipient, senderNumber, message with { DeliveryFailed = false });
        if (_nanoChat.GetRecipient(recipient, senderNumber) is not { } sender)
            return;

        if (recipient.Comp.CurrentChat != senderNumber)
            _nanoChat.SetRecipient(recipient, senderNumber, sender with { HasUnread = true });

        if (_nanoChat.GetPda(recipient) is not { } pda || !TryComp<CartridgeLoaderComponent>(pda, out var loader))
            return;

        var viewing = recipient.Comp.CurrentChat == senderNumber
            && _ui.IsUiOpen(pda, PdaUiKey.Key)
            && HasComp<NanoChatCartridgeComponent>(loader.ActiveProgram);

        if (!viewing && !recipient.Comp.NotificationsMuted && !recipient.Comp.MutedChats.Contains(senderNumber))
            Notify((pda, loader), sender, message);

        UpdateUIForPda(pda);
    }

    private void Notify(Entity<CartridgeLoaderComponent> loader, NanoChatRecipient sender, NanoChatMessage message)
    {
        var from = string.IsNullOrEmpty(sender.JobTitle)
            ? sender.Name
            : SharedNanoChatSystem.Truncate(
                Loc.GetString("nano-chat-new-message-title-recipient", ("sender", sender.Name), ("jobTitle", sender.JobTitle)),
                NotificationTitleMaxLength,
                " \\[...\\]");

        _loader.SendNotification(loader,
            Loc.GetString("nano-chat-new-message-title", ("sender", from)),
            Loc.GetString("nano-chat-new-message-body",
                ("message", SharedNanoChatSystem.Truncate(message.Content, NotificationMaxLength, " [...]"))),
            loader);
    }

    /// <summary>The name and job on the ID card holding a number.</summary>
    private NanoChatRecipient? GetCardInfo(uint number)
    {
        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var card))
        {
            if (card.Number != number)
                continue;

            if (!TryComp<IdCardComponent>(uid, out var id))
                return new NanoChatRecipient(number, MetaData(uid).EntityName);

            var name = string.IsNullOrWhiteSpace(id.FullName) ? Loc.GetString("nano-chat-unknown-name") : id.FullName;
            return new NanoChatRecipient(number, name, id.LocalizedJobTitle);
        }

        return null;
    }

    /// <summary>Refreshes the app on the PDA a card sits in.</summary>
    public void UpdateUIForCard(Entity<NanoChatCardComponent> card)
    {
        if (_nanoChat.GetPda(card) is { } pda)
            UpdateUIForPda(pda);
    }

    /// <summary>Refreshes a PDA's NanoChat app if someone is looking at it.</summary>
    public void UpdateUIForPda(EntityUid pda)
    {
        if (_loader.TryGetProgram<NanoChatCartridgeComponent>(pda, out var program, out var comp))
            UpdateUI((program.Value, comp), pda);
    }

    private void UpdateUI(Entity<NanoChatCartridgeComponent> ent, EntityUid loader)
    {
        if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
            || loaderComp.ActiveProgram != ent.Owner
            || !_ui.IsUiOpen(loader, loaderComp.UiKey))
            return;

        var contacts = HasSignal(ent, ent.Comp.RadioChannel.Id) ? Directory() : null;
        NanoChatUiState state;
        if (_nanoChat.TryGetCard(loader, out var card))
        {
            var comp = card.Value.Comp;
            var messages = new Dictionary<uint, List<NanoChatMessage>>();
            if (comp.CurrentChat is { } current && comp.Messages.TryGetValue(current, out var chat))
                messages[current] = new List<NanoChatMessage>(chat);

            state = new NanoChatUiState(new Dictionary<uint, NanoChatRecipient>(comp.Recipients),
                messages,
                new HashSet<uint>(comp.MutedChats),
                contacts,
                comp.CurrentChat,
                comp.Number ?? 0,
                comp.MaxRecipients,
                comp.NotificationsMuted,
                comp.ListNumber);
        }
        else
        {
            state = new NanoChatUiState(new Dictionary<uint, NanoChatRecipient>(),
                new Dictionary<uint, List<NanoChatMessage>>(),
                new HashSet<uint>(),
                contacts,
                null,
                0,
                0,
                false,
                false);
        }

        _loader.UpdateCartridgeUiState(loader, state, loader: loaderComp);
    }

    /// <summary>Every listed number on a named ID card that a PDA reads, by name.</summary>
    private List<NanoChatRecipient> Directory()
    {
        var contacts = new List<NanoChatRecipient>();
        var seen = new HashSet<uint>();
        var query = EntityQueryEnumerator<NanoChatCardComponent, IdCardComponent>();
        while (query.MoveNext(out var uid, out var card, out var id))
        {
            if (card.ListNumber
                && card.Number is { } number
                && !string.IsNullOrWhiteSpace(id.FullName)
                && _nanoChat.GetPda((uid, card)) != null
                && seen.Add(number))
                contacts.Add(new NanoChatRecipient(number, id.FullName, id.LocalizedJobTitle));
        }

        contacts.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return contacts;
    }
}
