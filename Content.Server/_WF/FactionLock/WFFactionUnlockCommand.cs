using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._WF.FactionLock;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._WF.FactionLock;

/// <summary>Lists a player's characters and their faction locks, or clears one character's lock.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFFactionUnlockCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private WFFactionLockSystem _locks = default!;

    public override string Command => "factionunlock";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Help);
            return;
        }
        if (await _locator.LookupIdByNameOrIdAsync(args[0]) is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-factionunlock-no-player", ("player", args[0])));
            return;
        }
        if (await _locks.GetPreferences(player.UserId) is not { } preferences)
        {
            shell.WriteError(Loc.GetString("cmd-factionunlock-no-characters", ("player", player.Username)));
            return;
        }
        if (args.Length == 1)
        {
            WriteCharacters(shell, preferences);
            return;
        }
        if (FindSlot(preferences, args[1]) is not { } slot)
        {
            shell.WriteError(Loc.GetString("cmd-factionunlock-no-character", ("character", args[1])));
            WriteCharacters(shell, preferences);
            return;
        }
        var name = preferences.Characters[slot].Name;
        if (!await _locks.Unlock(player.UserId, slot))
        {
            shell.WriteError(Loc.GetString("cmd-factionunlock-not-locked", ("character", name)));
            return;
        }
        shell.WriteLine(Loc.GetString("cmd-factionunlock-done", ("character", name), ("player", player.Username)));
        _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{shell.Player?.Name ?? "Server"} cleared the faction lock on {name} (slot {slot}) of {player.Username} ({player.UserId})");
    }

    /// <summary>A slot number, or a character name that only one of the player's characters has.</summary>
    private static int? FindSlot(PlayerPreferences preferences, string query)
    {
        if (int.TryParse(query, out var slot) && preferences.Characters.ContainsKey(slot))
            return slot;
        var named = preferences.Characters.Where(pair => string.Equals(pair.Value.Name, query, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key).ToArray();
        return named.Length == 1 ? named[0] : null;
    }

    private void WriteCharacters(IConsoleShell shell, PlayerPreferences preferences)
    {
        foreach (var (slot, profile) in preferences.Characters.OrderBy(pair => pair.Key))
        {
            var played = profile is HumanoidCharacterProfile humanoid ? WFFactionLockPolicy.Played(humanoid.Flags).ToArray() : Array.Empty<string>();
            shell.WriteLine(Loc.GetString("cmd-factionunlock-character", ("slot", slot), ("character", profile.Name),
                ("factions", played.Length > 0 ? string.Join(", ", played) : Loc.GetString("cmd-factionunlock-free"))));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), Loc.GetString("cmd-factionunlock-hint-player"));
        if (args.Length != 2 || !_players.TryGetUserId(args[0], out var user) || _locks.GetCachedPreferences(user) is not { } preferences)
            return CompletionResult.Empty;
        return CompletionResult.FromHintOptions(preferences.Characters.OrderBy(pair => pair.Key)
            .Select(pair => new CompletionOption(pair.Key.ToString(), pair.Value.Name)), Loc.GetString("cmd-factionunlock-hint-character"));
    }
}
