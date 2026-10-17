using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>Factions supported by crew deployment; existing jobs remain the source of identity and equipment.</summary>
[Serializable, NetSerializable]
public enum WFCrewFaction : byte { None, TSF, PDV }

/// <summary>Explicit command tiers, independent of job allocation weights and custom titles.</summary>
public static class WFFactionCrewPolicy
{
    /// <summary>Regular ranks and specialist postings supported by faction crews.</summary>
    public static readonly string[] TsfJobs = { "Cadet", "Deputy", "Brigmedic", "TsfEngineer", "NFDetective",
        "PublicAffairsLiaison", "SeniorOfficer", "Bailiff", "Sheriff" };
    /// <summary>Regular PDV ranks and specialist postings.</summary>
    public static readonly string[] PdvJobs = { "Pirate", "PDVInfiltrator", "PDVDenasvar", "PirateFirstMate", "PirateCaptain" };

    /// <summary>Only explicitly supported jobs can establish a faction or recruitment ceiling.</summary>
    public static int Rank(WFCrewFaction faction, string? job) => (faction, job) switch
    {
        (WFCrewFaction.TSF, "Cadet") => 0,
        (WFCrewFaction.TSF, "Deputy" or "Brigmedic" or "TsfEngineer" or "NFDetective" or "PublicAffairsLiaison") => 1,
        (WFCrewFaction.TSF, "SeniorOfficer") => 2,
        (WFCrewFaction.TSF, "Bailiff") => 3,
        (WFCrewFaction.TSF, "Sheriff") => 4,
        (WFCrewFaction.PDV, "Pirate" or "PDVInfiltrator") => 1,
        (WFCrewFaction.PDV, "PDVDenasvar") => 2,
        (WFCrewFaction.PDV, "PirateFirstMate") => 3,
        (WFCrewFaction.PDV, "PirateCaptain") => 4,
        _ => -1,
    };

    /// <summary>Specialist eligibility is checked separately; a title never grants rank.</summary>
    public static bool CanRecruit(WFCrewFaction faction, string? hostJob, string? recruitJob) =>
        Rank(faction, recruitJob) is var rank && rank >= 0 && rank <= Rank(faction, hostJob);

    /// <summary>Returns supported jobs without exposing special deployment roles.</summary>
    public static string[] Jobs(WFCrewFaction faction) => faction switch
    {
        WFCrewFaction.TSF => TsfJobs,
        WFCrewFaction.PDV => PdvJobs,
        _ => Array.Empty<string>(),
    };
}
