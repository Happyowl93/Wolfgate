using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.OldVessels;

/// <summary>
/// The tags that mark the classic ships Monolith#4624 removed and Wolfgate kept.
/// </summary>
public static class ClassicVessels
{
    /// <summary>Every classic ship, including the ones back on sale.</summary>
    public static readonly ProtoId<TagPrototype> ClassicTag = "WFClassicVessel";

    /// <summary>Classic ships no shipyard sells; only admins spawn them.</summary>
    public static readonly ProtoId<TagPrototype> AdminOnlyTag = "WFAdminOnlyVessel";

    public static bool IsClassic(VesselPrototype vessel)
    {
        return vessel.Tags.Contains(ClassicTag);
    }

    public static bool IsAdminOnly(VesselPrototype vessel)
    {
        return vessel.Tags.Contains(AdminOnlyTag);
    }
}
