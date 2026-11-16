using Content.Client._WF.Augments.UI;
using Content.Shared._White.Overlays;
using Content.Shared._WF.Augments;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._WF.Augments;

/// <summary>Keeps the thermal wash in step with the eyes' tint and plays the swap glitch for the local player.</summary>
public sealed partial class WFThermalTintsVisualsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;

    private WFThermalGlitchOverlay _glitch = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFThermalTintsComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFThermalTintsComponent, WFThermalTintSwappedEvent>(OnSwapped);

        _glitch = new WFThermalGlitchOverlay();
        _overlays.AddOverlay(_glitch);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_glitch);
    }

    private void OnState(Entity<WFThermalTintsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<ThermalVisionComponent>(ent, out var thermal))
            WFThermalTintsSystem.ApplyTint(ent.Comp, thermal);
    }

    private void OnSwapped(Entity<WFThermalTintsComponent> ent, ref WFThermalTintSwappedEvent args)
    {
        if (ent.Owner == _player.LocalEntity)
            _glitch.Start(args.Color);
    }
}
