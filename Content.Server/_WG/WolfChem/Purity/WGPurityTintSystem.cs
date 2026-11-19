using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared._WG.WolfChem.Purity;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._WG.WolfChem.Purity;

/// <summary>Keeps <see cref="WGPurityTintVisuals.Tinted"/> in step with the grade of a <see cref="WGPurityTintComponent"/>'s reagent.</summary>
public sealed class WGPurityTintSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPurityTintComponent, MapInitEvent>((ent, ref MapInitEvent _) => Refresh(ent),
            after: new[] { typeof(SolutionContainerSystem), typeof(WGSolutionPuritySystem) });
        SubscribeLocalEvent<WGPurityTintComponent, SolutionContainerChangedEvent>((ent, ref SolutionContainerChangedEvent _) => Refresh(ent));
    }

    /// <summary>Shows the tint if the reagent is present at or above the threshold grade.</summary>
    public void Refresh(Entity<WGPurityTintComponent> ent)
    {
        var tinted = _solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution)
                     && solution.ContainsPrototype(ent.Comp.Reagent)
                     && solution.GetPurity(ent.Comp.Reagent) >= ent.Comp.Threshold;
        _appearance.SetData(ent, WGPurityTintVisuals.Tinted, tinted);
    }
}
