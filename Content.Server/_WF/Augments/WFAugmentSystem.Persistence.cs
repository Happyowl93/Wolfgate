using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.CryoSleep;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Shared._Shitmed.BodyEffects.Subsystems;
using Content.Shared._WF.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Augments;

/// <summary>
/// Keeps a character's augments in step with its body. When it goes into cryo or the round ends with the player
/// still in it, the cyberware still in the body is written to its profile, so what was installed in the round stays
/// and what was lost is gone. Respawning without cryo leaves the character behind and takes all of it.
/// </summary>
public sealed partial class WFAugmentSystem
{
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private CryoSleepSystem _cryoSleep = default!;
    [Dependency] private MindSystem _mind = default!;

    /// <summary>The character a player spawned as this round: its slot, its mind and the augments it spawned with.</summary>
    internal sealed record PlayedCharacter(int Slot, EntityUid Mind, Dictionary<WFAugmentSlot, WFAugmentChoice> Spawned);

    private readonly Dictionary<NetUserId, PlayedCharacter> _played = new();

    private void InitializePersistence()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<BodyComponent, CryosleepBeforeMindRemovedEvent>(OnCryosleep);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _played.Clear());
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Selection is locked once a player joins, so the selected slot is the one that spawned.
        if (_preferences.GetPreferencesOrNull(args.Player.UserId) is { SelectedCharacter: HumanoidCharacterProfile selected } prefs
            && selected.Name == args.Profile.Name
            && _mind.TryGetMind(args.Mob, out var mind, out _))
            Track(args.Player.UserId, new PlayedCharacter(prefs.SelectedCharacterIndex, mind, new(args.Profile.Augments)));
    }

    internal void Track(NetUserId user, PlayedCharacter character)
    {
        _played[user] = character;
    }

    private void OnCryosleep(EntityUid uid, BodyComponent body, CryosleepBeforeMindRemovedEvent args)
    {
        if (args.User is { } user
            && _played.TryGetValue(user, out var played)
            && _mind.TryGetMind(uid, out var mind, out _)
            && mind == played.Mind)
            SaveBody(user, played, uid);
    }

    private void OnRoundEnded(RoundEndedEvent args)
    {
        foreach (var (user, played) in _played)
        {
            // A player who took a ghost role or a new character is no longer in this one.
            if (_mind.TryGetMind(user, out var mind)
                && mind.Value.Owner == played.Mind
                && mind.Value.Comp.OwnedEntity is { } body
                && HasComp<BodyComponent>(body)
                && HasComp<HumanoidAppearanceComponent>(body))
                SaveBody(user, played, body);
        }
    }

    /// <summary>Writes the cyberware in a body to the profile of the character it belongs to.</summary>
    /// <returns>The profile save, or null if nothing changed.</returns>
    internal Task? SaveBody(NetUserId user, PlayedCharacter played, EntityUid body)
    {
        if (!_players.TryGetSessionById(user, out var session)
            || !_preferences.TryGetCachedPreferences(user, out var prefs)
            || prefs.Characters.GetValueOrDefault(played.Slot) is not HumanoidCharacterProfile profile)
            return null;

        var installed = ReadBody(body, played.Spawned, profile.Augments);
        var augments = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
        foreach (var slot in Enum.GetValues<WFAugmentSlot>())
        {
            var picked = profile.Augments.TryGetValue(slot, out var p) ? p : (WFAugmentChoice?) null;
            var spawned = played.Spawned.TryGetValue(slot, out var s) ? s : (WFAugmentChoice?) null;

            // A slot changed in the creator since the spawn keeps the new pick; it reaches the body next round.
            if (picked != spawned)
            {
                if (picked is { } kept)
                    augments[slot] = kept;
            }
            else if (installed.TryGetValue(slot, out var found))
            {
                augments[slot] = found;
            }
        }

        // What the body carried in the round is owned now; what it lost is not.
        var unlocks = profile.AugmentUnlocks.ToHashSet();
        unlocks.ExceptWith(played.Spawned.Values.Select(c => c.Augment));
        unlocks.UnionWith(installed.Values.Select(c => c.Augment)
            .Where(a => _proto.TryIndex(a, out var augment) && augment.Price > 0));

        if (augments.Count == profile.Augments.Count
            && augments.All(a => profile.Augments.TryGetValue(a.Key, out var c) && c == a.Value)
            && unlocks.SetEquals(profile.AugmentUnlocks))
            return null;

        return Save(session, played.Slot, profile.WithAugments(augments).WithAugmentUnlocks(unlocks));
    }

    /// <summary>
    /// The augments a body carries: each slot's part or organ that is one, and the upgrades in it. A limb keeps the
    /// style it was picked with, else gets the one trait limbs wear. Missing limbs and plating are kept as picked.
    /// </summary>
    internal Dictionary<WFAugmentSlot, WFAugmentChoice> ReadBody(EntityUid uid,
        IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice> spawned,
        IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice> picked)
    {
        var found = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
        if (!TryComp<BodyComponent>(uid, out var bodyComp))
            return found;

        Entity<BodyComponent> body = (uid, bodyComp);
        foreach (var augment in _proto.EnumeratePrototypes<WFAugmentPrototype>().OrderBy(a => a.Order).ThenBy(a => a.ID))
        {
            var slot = augment.Slot;
            if (found.ContainsKey(slot))
                continue;

            var present = augment.Kind switch
            {
                WFAugmentKind.Limb => FindLimb(body, slot) is { } limb
                                      && Prototype(limb.Id) == augment.Part?.Id
                                      && !IsStockChild(limb.Id),
                WFAugmentKind.Organ => OrganIn(body, slot) is { } organ && Prototype(organ) == augment.Organ?.Id,
                WFAugmentKind.Upgrade => Surgeon(augment) is { } surgeon && _autoSurgeon.WfHasUpgrade(uid, surgeon),
                WFAugmentKind.Missing => FindLimb(body, slot) == null && Picked(spawned, slot, augment) != null,
                _ => Picked(spawned, slot, augment) != null,
            };
            if (!present)
                continue;

            var style = Picked(spawned, slot, augment)?.Style ?? Picked(picked, slot, augment)?.Style;
            if (style == null && augment.Styled && _proto.TryIndex(TraitLimbStyle, out var traitStyle)
                && WFAugmentRules.StyleFits(traitStyle, slot))
                style = TraitLimbStyle;
            found[slot] = new WFAugmentChoice(augment.ID, style);
        }

        return found;
    }

    private static WFAugmentChoice? Picked(IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice> choices,
        WFAugmentSlot slot,
        WFAugmentPrototype augment)
    {
        return choices.TryGetValue(slot, out var choice) && choice.Augment == augment.ID ? choice : null;
    }

    private string? Prototype(EntityUid uid) => MetaData(uid).EntityPrototype?.ID;

    /// <summary>Whether a hand or foot is the one its limb comes with, so it isn't an augment of its own.</summary>
    private bool IsStockChild(EntityUid part)
    {
        return _body.GetParentPartOrNull(part) is { } parent
               && TryComp<GenerateChildPartComponent>(parent, out var generate)
               && generate.Id.Id == Prototype(part);
    }

    /// <summary>The organ in an organ slot, if the body has one there.</summary>
    private EntityUid? OrganIn(Entity<BodyComponent> body, WFAugmentSlot slot)
    {
        if (WFAugmentRules.OrganSlot(slot) is not { } organSlot)
            return null;

        var partType = WFAugmentRules.PartSlot(slot) == "head" ? BodyPartType.Head : BodyPartType.Torso;
        if (_body.GetBodyChildrenOfType(body, partType, body.Comp).FirstOrDefault() is not { Id.Valid: true } part
            || !_containers.TryGetContainer(part.Id, SharedBodySystem.GetOrganContainerId(organSlot), out var container))
            return null;

        return container.ContainedEntities.FirstOrDefault() is { Valid: true } organ ? organ : null;
    }

    /// <summary>
    /// A player respawning with <c>ghostrespawn</c> instead of cryo leaves their character behind: it loses its
    /// augments and their unlocks, so priced ones have to be bought again.
    /// </summary>
    /// <returns>The profile save, or null if nothing was taken.</returns>
    public Task? ForfeitAugments(ICommonSession session)
    {
        var user = session.UserId;
        if (_cryoSleep.HasCryosleepingBody(user) || !_played.Remove(user, out var played))
            return null;

        if (_preferences.GetPreferencesOrNull(user)?.Characters.GetValueOrDefault(played.Slot) is not HumanoidCharacterProfile profile
            || profile.Augments.Count == 0 && profile.AugmentUnlocks.Count == 0)
            return null;

        _chat.DispatchServerMessage(session, Loc.GetString("wf-augments-forfeit", ("name", profile.Name)));
        Log.Info($"User {user} respawned without cryo; {profile.Name} lost their augments.");
        return Save(session, played.Slot,
            profile.WithAugments(new Dictionary<WFAugmentSlot, WFAugmentChoice>()).WithAugmentUnlocks([]));
    }

    private async Task Save(ICommonSession session, int slot, HumanoidCharacterProfile profile)
    {
        try
        {
            await _preferences.SetProfile(session.UserId, slot, profile);
            _preferences.ResendPreferences(session.UserId);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save the augments of {session.UserId}'s {profile.Name}: {e}");
        }
    }
}
