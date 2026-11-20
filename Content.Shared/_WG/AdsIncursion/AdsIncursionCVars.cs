using Robust.Shared.Configuration;

namespace Content.Shared._WG.AdsIncursion;

/// <summary>Settings for the ADS incursion campaign.</summary>
[CVarDefs]
public sealed class AdsIncursionCVars
{
    /// <summary>Admin override: forces the campaign on whether or not its rule runs. It also needs the storyteller and sector control on.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.ads.enabled", false, CVar.SERVERONLY);

    /// <summary>The most spesos ADS bounties pay one player in an hour, an allowance apart from other encounter rewards. The dreadnought is exempt.</summary>
    public static readonly CVarDef<int> PayoutHourlyCap =
        CVarDef.Create("wg.ads.payout_hourly_cap", 450000, CVar.SERVERONLY);
}
