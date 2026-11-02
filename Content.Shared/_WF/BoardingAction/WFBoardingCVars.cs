using Robust.Shared.Configuration;

namespace Content.Shared._WF.BoardingAction;

/// <summary>Tuning for FTL disruption.</summary>
[CVarDefs]
public sealed class WFBoardingCVars
{
    /// <summary>
    /// Whether any powered shuttle console within the old 1536 m radius blocks a jump, as it did before hacks and NPC holds.
    /// Also restores the expedition console's 256 m check.
    /// </summary>
    public static readonly CVarDef<bool> ProximityBlock =
        CVarDef.Create("wf.ftl_proximity_block", false, CVar.SERVERONLY);

    /// <summary>How far an NPC ship's disruption reaches from the ship it is shooting at, in metres.</summary>
    public static readonly CVarDef<float> NpcDisruptRange =
        CVarDef.Create("wf.npc_disrupt_range", 1500f, CVar.SERVERONLY);
}
