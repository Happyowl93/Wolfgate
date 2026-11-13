using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.NanoChat;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Kitchen;
using Content.Shared.Kitchen.Components;
using Content.Shared.PDA;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server._WF.NanoChat;

/// <summary>
/// Gives NanoChat cards their numbers, keeps a character's number from round to round, tracks which PDA a card is in
/// and scrambles a microwaved card.
/// </summary>
public sealed class NanoChatSystem : SharedNanoChatSystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private NanoChatCartridgeSystem _cartridge = default!;

    private const uint MinNumber = 1;
    private const uint MaxNumber = 9999;

    /// <summary>Numbers saved for characters, kept off the cards that are not theirs.</summary>
    private readonly HashSet<uint> _reserved = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NanoChatCardComponent, MapInitEvent>(OnCardInit);
        SubscribeLocalEvent<NanoChatCardComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<NanoChatCardComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<NanoChatCardComponent, BeingMicrowavedEvent>(OnMicrowaved, after: [typeof(IdCardSystem)]);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);

        LoadReserved();
    }

    private async void LoadReserved()
    {
        try
        {
            foreach (var number in await _db.GetNanoChatNumbersAsync())
            {
                _reserved.Add((uint) number);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Reading NanoChat numbers threw: {e}");
        }
    }

    private void OnCardInit(Entity<NanoChatCardComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Number == null)
            SetNumber(ent, PickFreeNumber());
    }

    private void OnInserted(Entity<NanoChatCardComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != PdaComponent.PdaIdSlotId)
            return;

        SetPda(ent, args.Container.Owner);
        _cartridge.UpdateUIForPda(args.Container.Owner);
    }

    private void OnRemoved(Entity<NanoChatCardComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.ID != PdaComponent.PdaIdSlotId)
            return;

        SetPda(ent, null);
        _cartridge.UpdateUIForPda(args.Container.Owner);
    }

    /// <summary>A number no card holds and no character owns, or a random one if every number is taken.</summary>
    private uint PickFreeNumber()
    {
        var live = LiveNumbers();
        for (var i = 0; i < 200; i++)
        {
            var number = (uint) _random.Next((int) MinNumber, (int) MaxNumber + 1);
            if (!live.Contains(number) && !_reserved.Contains(number))
                return number;
        }

        for (var number = MinNumber; number <= MaxNumber; number++)
        {
            if (!live.Contains(number))
                return number;
        }

        return (uint) _random.Next((int) MinNumber, (int) MaxNumber + 1);
    }

    private HashSet<uint> LiveNumbers()
    {
        var live = new HashSet<uint>();
        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var card))
        {
            if (card.Number is { } number)
                live.Add(number);
        }

        return live;
    }

    /// <summary>Gives a spawned character's ID card the number saved for that character, claiming one on first spawn.</summary>
    private async void OnPlayerSpawn(PlayerSpawnCompleteEvent args)
    {
        var mob = args.Mob;
        var user = args.Player.UserId;
        if (!_preferences.TryGetCachedPreferences(user, out var preferences)
            || !preferences.TryIndexOfCharacter(args.Profile, out var slot))
            return;

        int? number = null;
        try
        {
            var profileIds = await _db.GetProfileIdsAsync(user.UserId);
            if (!profileIds.TryGetValue(slot, out var profileId))
                return;

            for (var attempt = 0; attempt < 5 && number == null; attempt++)
            {
                var candidate = PickFreeNumber();
                number = await _db.ClaimNanoChatNumberAsync(profileId, (int) candidate);
                _reserved.Add(candidate);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Claiming a NanoChat number for {user} threw: {e}");
            return;
        }

        if (number is not { } claimed || TerminatingOrDeleted(mob))
            return;

        _reserved.Add((uint) claimed);
        if (!_idCard.TryFindIdCard(mob, out var id) || !TryComp<NanoChatCardComponent>(id, out var card))
            return;

        AssignNumber((id, card), (uint) claimed);
    }

    /// <summary>Gives a card a number, moving any other card holding it onto a free one.</summary>
    public void AssignNumber(Entity<NanoChatCardComponent> card, uint number)
    {
        if (card.Comp.Number == number)
            return;

        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var other))
        {
            if (uid != card.Owner && other.Number == number)
                SetNumber((uid, other), PickFreeNumber());
        }

        SetNumber(card, number);
        _cartridge.UpdateUIForCard(card);
    }

    private void OnMicrowaved(Entity<NanoChatCardComponent> ent, ref BeingMicrowavedEvent args)
    {
        if (TerminatingOrDeleted(ent) || !TryComp<MicrowaveComponent>(args.Microwave, out var micro) || micro.Broken)
            return;

        if (_random.Prob(0.1f))
        {
            RewriteMessages(ent, messages => messages.Clear());
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.Microwave)} erased all NanoChat messages on {ToPrettyString(ent)}");
        }
        else
        {
            RewriteMessages(ent, messages => Scramble(messages, ent.Comp.Recipients.Keys.ToList()));
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.Microwave)} scrambled NanoChat messages on {ToPrettyString(ent)}");
        }

        _cartridge.UpdateUIForCard(ent);
    }

    /// <summary>Shuffles the letters of half the messages and moves a quarter of the chats to another contact.</summary>
    private void Scramble(Dictionary<uint, List<NanoChatMessage>> chats, List<uint> contacts)
    {
        foreach (var (number, messages) in chats.ToList())
        {
            for (var i = 0; i < messages.Count; i++)
            {
                if (!_random.Prob(0.5f))
                    continue;

                var message = messages[i];
                var chars = message.Content.ToCharArray();
                _random.Shuffle(chars.AsSpan());
                message.Content = new string(chars);
                messages[i] = message;
            }

            if (contacts.Count == 0 || !_random.Prob(0.25f))
                continue;

            var target = _random.Pick(contacts);
            if (target == number)
                continue;

            if (!chats.TryGetValue(target, out var targetMessages))
            {
                targetMessages = new List<NanoChatMessage>();
                chats[target] = targetMessages;
            }

            targetMessages.AddRange(messages);
            messages.Clear();
        }
    }
}
