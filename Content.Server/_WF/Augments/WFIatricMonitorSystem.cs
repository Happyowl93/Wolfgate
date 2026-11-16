using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Shared._WF.Augments;
using Content.Shared.Actions;
using Content.Shared.MedicalScanner;
using Robust.Server.GameObjects;

namespace Content.Server._WF.Augments;

/// <summary>
/// Gives a body with an iatric monitor its scan action, which opens the health analyzer's window on the body itself
/// and closes it again.
/// </summary>
public sealed class WFIatricMonitorSystem : EntitySystem
{
    [Dependency] private HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFIatricMonitorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WFIatricMonitorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFIatricMonitorComponent, WFIatricScanEvent>(OnScan);
        SubscribeLocalEvent<WFIatricScannerComponent, BoundUIClosedEvent>(OnClosed);
    }

    private void OnInit(Entity<WFIatricMonitorComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<WFIatricMonitorComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnScan(Entity<WFIatricMonitorComponent> ent, ref WFIatricScanEvent args)
    {
        var action = args.Action.Owner;
        if (action != ent.Comp.ActionEntity || !TryComp<HealthAnalyzerComponent>(action, out var analyzer))
            return;

        args.Handled = true;
        if (_ui.IsUiOpen(action, HealthAnalyzerUiKey.Key, ent.Owner))
        {
            _ui.CloseUi(action, HealthAnalyzerUiKey.Key, ent.Owner);
            return;
        }

        _ui.OpenUi(action, HealthAnalyzerUiKey.Key, ent.Owner);
        _analyzer.BeginAnalyzingEntity((action, analyzer), ent);
    }

    private void OnClosed(Entity<WFIatricScannerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (HealthAnalyzerUiKey.Key.Equals(args.UiKey)
            && TryComp<HealthAnalyzerComponent>(ent, out var analyzer)
            && analyzer.ScannedEntity is { } patient)
            _analyzer.StopAnalyzingEntity((ent, analyzer), patient);
    }
}
