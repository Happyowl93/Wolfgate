using Robust.Shared.Configuration;

namespace Content.Shared._WF.Purity;

/// <summary>Reagent purity settings.</summary>
[CVarDefs]
public sealed class WFPurityCVars
{
    /// <summary>Poison dealt per metabolised unit of impurity, so 10 u at 60% deals 4 times this.</summary>
    public static readonly CVarDef<float> ImpurityDamage =
        CVarDef.Create("wf.purity.impurity_damage", 1f, CVar.SERVERONLY);
}
