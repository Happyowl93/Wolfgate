using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Interaction;
using Content.Shared.Localizations;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WolfChem.Narcotics.Delivery;

/// <summary>Keeps anything but the baggie's listed powders out of it.</summary>
public sealed class WFBaggieSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFBaggieComponent, SolutionTransferAttemptEvent>(OnTransferAttempt);
        // Before the spiker, which would empty a pill into it.
        SubscribeLocalEvent<WFBaggieComponent, InteractUsingEvent>(OnInteractUsing,
            before: new[] { typeof(SolutionSpikerSystem) });
    }

    private void OnTransferAttempt(Entity<WFBaggieComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        if (args.To != ent.Owner || !_solution.TryGetDrainableSolution(args.From, out _, out var solution))
            return;

        if (Refused(ent.Comp, solution) is { } reason)
            args.Cancel(reason);
    }

    private void OnInteractUsing(Entity<WFBaggieComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled
            || !TryComp<SolutionSpikerComponent>(args.Used, out var spiker)
            || !_solution.TryGetSolution(args.Used, spiker.SourceSolution, out _, out var solution)
            || Refused(ent.Comp, solution) is not { } reason)
            return;

        _popup.PopupClient(reason, ent, args.User);
        args.Handled = true;
    }

    /// <summary>The refusal message if the solution holds anything the baggie doesn't take, else null.</summary>
    public string? Refused(WFBaggieComponent baggie, Solution solution)
    {
        if (baggie.Reagents is not { } allowed)
            return null;

        var refused = solution.Contents
            .Select(reagent => reagent.Reagent.Prototype)
            .Where(reagent => !allowed.Contains(reagent))
            .Distinct()
            .Select(reagent => _prototype.TryIndex<ReagentPrototype>(reagent, out var proto)
                ? proto.LocalizedName
                : reagent)
            .ToList();

        return refused.Count == 0
            ? null
            : Loc.GetString("wf-baggie-refused", ("reagents", ContentLocalizationManager.FormatList(refused)));
    }
}
