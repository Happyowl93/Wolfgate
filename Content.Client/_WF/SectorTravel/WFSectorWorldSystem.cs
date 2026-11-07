using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.SectorTravel;

/// <summary>Adds the overlay that draws each sector's worlds.</summary>
public sealed class WFSectorWorldSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFSectorWorldOverlay(EntityManager, _proto));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFSectorWorldOverlay>();
    }
}
