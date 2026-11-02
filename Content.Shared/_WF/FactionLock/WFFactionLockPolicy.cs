using System.Linq;

namespace Content.Shared._WF.FactionLock;

/// <summary>Which companies are factions, how a character's played factions are stored, and which factions bar each other.</summary>
public static class WFFactionLockPolicy
{
    private const string FlagPrefix = "wf-faction:";

    /// <summary>The faction a job's company belongs to; null for neutral companies such as Colonial.</summary>
    public static string? FactionOf(string company) => company switch
    {
        "TSF" or "TSFCivilian" or "TSFHighComm" => "TSF",
        "PDV" or "PDVCivilian" or "PDVHighComm" => "PDV",
        "MMC" => "MMC",
        "TheViperGroup" => "VG",
        "USSP" => "USSP",
        "WFUnderground" => "Underground",
        "WFAsf" => "ASF",
        _ => null,
    };

    /// <summary>The persistent profile flag recording that a character has played a faction.</summary>
    public static string Flag(string faction) => FlagPrefix + faction;

    /// <summary>The factions a character's profile flags say they have played.</summary>
    public static IEnumerable<string> Played(IEnumerable<string> flags) =>
        flags.Where(flag => flag.StartsWith(FlagPrefix, StringComparison.Ordinal)).Select(flag => flag.Substring(FlagPrefix.Length));

    /// <summary>A character's profile flags with their played factions taken out.</summary>
    public static IEnumerable<string> WithoutFactions(IEnumerable<string> flags) =>
        flags.Where(flag => !flag.StartsWith(FlagPrefix, StringComparison.Ordinal));

    /// <summary>Whether having played one faction bars another. Every faction opposes every other for now.</summary>
    public static bool Opposes(string played, string wanted) => played != wanted;

    /// <summary>The played faction that bars the wanted one, or null if none does.</summary>
    public static string? Barring(IEnumerable<string> played, string wanted) =>
        played.FirstOrDefault(faction => Opposes(faction, wanted));
}
