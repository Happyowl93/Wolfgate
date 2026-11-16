using System.Threading.Tasks;
using Content.Server._NF.CryoSleep;
using Content.Server.Chat.Managers;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.Augments;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Augments;

public sealed partial class WFAugmentSystem
{
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private CryoSleepSystem _cryoSleep = default!;

    /// <summary>The character slot each player last spawned as this round.</summary>
    private readonly Dictionary<NetUserId, int> _spawnedSlots = new();

    private void InitializeRespawn()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _spawnedSlots.Clear());
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Selection is locked once a player joins, so the selected slot is the one that spawned.
        if (_preferences.GetPreferencesOrNull(args.Player.UserId) is { SelectedCharacter: HumanoidCharacterProfile selected } prefs
            && selected.Name == args.Profile.Name)
            TrackSpawn(args.Player.UserId, prefs.SelectedCharacterIndex);
    }

    internal void TrackSpawn(NetUserId user, int slot)
    {
        _spawnedSlots[user] = slot;
    }

    /// <summary>
    /// A player respawning with <c>ghostrespawn</c> instead of cryo leaving their character behind: that character
    /// loses its augments and their unlocks, so priced ones have to be bought again.
    /// </summary>
    /// <returns>The profile save, or null if nothing was taken.</returns>
    public Task? ForfeitAugments(ICommonSession session)
    {
        var user = session.UserId;
        if (_cryoSleep.HasCryosleepingBody(user)
            || !_spawnedSlots.TryGetValue(user, out var slot)
            || _preferences.GetPreferencesOrNull(user)?.Characters.GetValueOrDefault(slot) is not HumanoidCharacterProfile profile
            || profile.Augments.Count == 0 && profile.AugmentUnlocks.Count == 0)
            return null;

        _spawnedSlots.Remove(user);
        _chat.DispatchServerMessage(session, Loc.GetString("wf-augments-forfeit", ("name", profile.Name)));
        return Forfeit();

        async Task Forfeit()
        {
            try
            {
                await _preferences.SetProfile(user, slot,
                    profile.WithAugments(new Dictionary<WFAugmentSlot, WFAugmentChoice>()).WithAugmentUnlocks([]));
                _preferences.ResendPreferences(user);
                Log.Info($"User {user} respawned without cryo; {profile.Name} lost their augments.");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to take the augments of {user}'s {profile.Name}: {e}");
            }
        }
    }
}
