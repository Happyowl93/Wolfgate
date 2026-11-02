using Content.Server._WF.BoardingAction.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>Whether a ship is held from leaving. Every jump passes here, whatever started it.</summary>
    private bool WfDisruptedDeparture(EntityUid uid) => HasComp<WFFtlDisruptedComponent>(uid);

    /// <summary>Stops a jump still spooling up and leaves the ship where it is, ready to fly.</summary>
    public bool WfAbortFtlSpool(EntityUid uid)
    {
        if (!TryComp<FTLComponent>(uid, out var ftl) || ftl.State != FTLState.Starting || ftl.LinkedShuttle != null)
            return false;

        ftl.StartupStream = _audio.Stop(ftl.StartupStream);
        if (TryComp<ShuttleComponent>(uid, out var shuttle))
            _thruster.DisableLinearThrusters(shuttle);

        RemComp<FTLComponent>(uid);
        _console.RefreshShuttleConsoles(uid);
        return true;
    }
}
