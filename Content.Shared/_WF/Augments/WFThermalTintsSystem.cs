using Content.Shared._White.Overlays;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Augments;

/// <summary>Cycles <see cref="WFThermalTintsComponent"/> eyes through their tints and the body's thermal vision with them.</summary>
public sealed partial class WFThermalTintsSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>The colour of the swap glitch when the eyes switch off.</summary>
    private static readonly Color OffColor = Color.FromHex("#9aa3a8");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFThermalTintsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFThermalTintsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFThermalTintsComponent, WFCycleThermalTintEvent>(OnCycle);
    }

    private void OnMapInit(Entity<WFThermalTintsComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<WFThermalTintsComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnCycle(Entity<WFThermalTintsComponent> ent, ref WFCycleThermalTintEvent args)
    {
        if (args.Handled || !TryComp<ThermalVisionComponent>(ent, out var thermal))
            return;

        args.Handled = true;
        var next = ent.Comp.Index + 1 < ent.Comp.Tints.Count ? ent.Comp.Index + 1 : -1;
        SetTint(ent, next, thermal);

        if (!_timing.IsFirstTimePredicted)
            return;

        if (next < 0)
        {
            _audio.PlayLocal(ent.Comp.OffSound, ent, ent);
            _popup.PopupClient(Loc.GetString("wf-thermal-tints-off"), ent, ent);
        }
        else
        {
            _audio.PlayLocal(ent.Comp.SwapSound, ent, ent);
            _audio.PlayLocal(ent.Comp.ChirpSound, ent, ent);
            _popup.PopupClient(Loc.GetString("wf-thermal-tints-on", ("tint", Loc.GetString(ent.Comp.Tints[next].Name))), ent, ent);
        }

        var swapped = new WFThermalTintSwappedEvent(next < 0 ? OffColor : ent.Comp.Tints[next].Color);
        RaiseLocalEvent(ent, ref swapped);
    }

    /// <summary>Sets the eyes to a tint, or off for -1, and switches the thermal vision to match.</summary>
    public void SetTint(Entity<WFThermalTintsComponent> ent, int index, ThermalVisionComponent thermal)
    {
        ent.Comp.Index = index;
        Dirty(ent);
        ApplyTint(ent.Comp, thermal);

        var active = index >= 0;
        if (thermal.IsActive == active)
        {
            Dirty(ent, thermal);
            return;
        }

        thermal.IsActive = active;
        Dirty(ent, thermal);
        // The thermal vision's own toggle raises this; the client redraws its overlays on it.
        var toggled = new SwitchableOverlayToggledEvent(ent, active);
        RaiseLocalEvent(ent, ref toggled);
    }

    /// <summary>Copies the current tint onto the thermal vision. The wash tint isn't networked, so the client calls this on state.</summary>
    public static void ApplyTint(WFThermalTintsComponent comp, ThermalVisionComponent thermal)
    {
        if (comp.Index < 0 || comp.Index >= comp.Tints.Count)
            return;

        var tint = comp.Tints[comp.Index];
        thermal.Color = tint.Color;
        thermal.Tint = tint.Tint;
    }
}
