using Content.Server._WG.IreWarnings;
using Content.Shared.Ame.Components;

namespace Content.Server.Ame.EntitySystems;

public sealed partial class AmeControllerSystem
{
    /// <summary>Reads the engine's mean core stability and its safe injection limit, if the controller is in an engine.</summary>
    public bool WGTryGetIreReadout(EntityUid uid, out int stability, out int safeLimit)
    {
        stability = 100;
        safeLimit = 0;
        if (!TryGetAMENodeGroup(uid, out var group))
            return false;

        stability = group.GetTotalStability();
        safeLimit = WGIreWarningSystem.SafeInjectionLimit(group.CoreCount);
        return true;
    }

    /// <summary>Adds core stability and the safe injection limit to the controller window's state.</summary>
    private void WGFillIreState(EntityUid uid, AmeControllerBoundUserInterfaceState state)
    {
        if (!WGTryGetIreReadout(uid, out var stability, out var safeLimit))
            return;

        state.WGStability = stability;
        state.WGSafeInjectionLimit = safeLimit;
    }
}
