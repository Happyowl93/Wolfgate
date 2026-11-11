using Content.Shared._WF.Narcotics;
using Robust.Client.Graphics;

namespace Content.Client._WF.Narcotics;

/// <summary>
/// Client half of intoxication: keeps predicted movement speed in step with the networked levels and draws the
/// screen overlay.
/// </summary>
public sealed partial class WFIntoxicationSystem : SharedWFIntoxicationSystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    private WFIntoxicationOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new WFIntoxicationOverlay(this);
        _overlays.AddOverlay(_overlay);
        SubscribeLocalEvent<WFIntoxicationComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFIntoxicationComponent, ComponentRemove>(OnRemove);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
    }

    private void OnState(Entity<WFIntoxicationComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRemove(Entity<WFIntoxicationComponent> ent, ref ComponentRemove args)
    {
        ent.Comp.Levels.Clear();
        ent.Comp.Crashes.Clear();
        if (!TerminatingOrDeleted(ent))
            Movement.RefreshMovementSpeedModifiers(ent);
    }
}
