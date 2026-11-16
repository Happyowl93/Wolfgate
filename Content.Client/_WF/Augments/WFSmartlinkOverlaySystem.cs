using Content.Client._WF.Augments.UI;
using Robust.Client.Graphics;

namespace Content.Client._WF.Augments;

/// <summary>Keeps the smartlink's sight on screen; it draws only while the player holds a gun in a linked hand.</summary>
public sealed partial class WFSmartlinkOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFSmartlinkOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFSmartlinkOverlay>();
    }
}
