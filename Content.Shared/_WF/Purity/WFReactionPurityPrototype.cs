using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Purity;

/// <summary>
/// How well a reaction keeps its product's grade, keyed by the reaction's ID. A reaction with none passes its input
/// grade through.
/// </summary>
[Prototype("wfReactionPurity")]
public sealed partial class WFReactionPurityPrototype : IPrototype
{
    /// <summary>The reaction's ID.</summary>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Temperature, in kelvin, at which the product keeps its full input grade.</summary>
    [DataField(required: true)]
    public float OptimalTemp;

    /// <summary>Kelvin from the optimum at which the factor reaches <see cref="Floor"/>.</summary>
    [DataField(required: true)]
    public float Tolerance;

    /// <summary>The lowest process factor.</summary>
    [DataField]
    public float Floor = 0.4f;

    /// <summary>The process factor at a temperature, falling linearly from 1.0 at the optimum to the floor.</summary>
    public float Factor(float temperature)
    {
        if (Tolerance <= 0f)
            return MathF.Abs(temperature - OptimalTemp) < 0.01f ? 1f : Floor;

        var off = Math.Min(MathF.Abs(temperature - OptimalTemp) / Tolerance, 1f);
        return 1f - (1f - Floor) * off;
    }
}
