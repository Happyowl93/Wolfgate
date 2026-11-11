using Content.Shared._WF.Purity;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;

namespace Content.Shared.Chemistry.Reaction;

public sealed partial class ChemicalReactionSystem
{
    /// <summary>The grade of the reaction being performed, for reaction effects that spawn graded solids.</summary>
    public float WFLastReactionGrade { get; private set; } = 1f;

    /// <summary>
    /// The grade of a reaction's product: the mean grade of the consumed reactants times the reaction's process factor
    /// at the solution's temperature.
    /// </summary>
    public float WFReactionGrade(Solution solution, ReactionPrototype reaction)
    {
        var input = 1f;
        if (solution.Purity != null)
        {
            var total = 0f;
            var weighted = 0f;
            foreach (var (reagent, reactant) in reaction.Reactants)
            {
                if (reactant.Catalyst)
                    continue;

                var amount = reactant.Amount.Float();
                total += amount;
                weighted += amount * solution.GetPurity(reagent);
            }

            if (total > 0f)
                input = weighted / total;
        }

        WFLastReactionGrade = _prototypeManager.TryIndex<WFReactionPurityPrototype>(reaction.ID, out var purity)
            ? input * purity.Factor(solution.Temperature)
            : input;
        return WFLastReactionGrade;
    }

    /// <summary>Re-grades a product that was just added at standard grade.</summary>
    private static void WFGradeProduct(Solution solution, string product, FixedPoint2 added, float grade)
    {
        if (grade < Solution.StandardPurityThreshold)
            solution.RegradeAdded(product, added, grade);
    }
}
