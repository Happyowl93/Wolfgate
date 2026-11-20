using Content.Server.Popups;
using Content.Shared._Rat.Overwatch;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._WG.StationAi;

/// <summary>
/// Stops an AI from viewing a camera through an overwatch console. Watching retargets the viewer's eye and drops it
/// back on the brain afterwards, which would strand the AI away from its remote eye.
/// </summary>
public sealed class WGAiOverwatchSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OverwatchConsoleComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
    }

    private void OnMessageAttempt(EntityUid uid, OverwatchConsoleComponent component, BoundUserInterfaceMessageAttempt args)
    {
        if (args.Message is not OverwatchViewCameraMessage || !HasComp<StationAiHeldComponent>(args.Actor))
            return;

        args.Cancel();
        _popup.PopupCursor(Loc.GetString("wg-station-ai-overwatch-no-view"), args.Actor);
    }
}
