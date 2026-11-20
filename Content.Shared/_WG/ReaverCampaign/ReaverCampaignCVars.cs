using Robust.Shared.Configuration;

namespace Content.Shared._WG.ReaverCampaign;

/// <summary>Settings for the Ashfall Reaver campaign.</summary>
[CVarDefs]
public sealed class ReaverCampaignCVars
{
    /// <summary>Admin override: forces the campaign on whether or not the Contested Sector rule runs. It also needs the storyteller and sector control on.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.reavers.enabled", false, CVar.SERVERONLY);

    /// <summary>Admin and test override for the population the campaign scales by; below zero it counts the living players.</summary>
    public static readonly CVarDef<int> PopulationOverride =
        CVarDef.Create("wg.reavers.population_override", -1, CVar.SERVERONLY);

    /// <summary>The most spesos Reaver bounties pay one player in an hour, an allowance apart from other encounter rewards. The citadel is exempt.</summary>
    public static readonly CVarDef<int> PayoutHourlyCap =
        CVarDef.Create("wg.reavers.payout_hourly_cap", 450000, CVar.SERVERONLY);
}
