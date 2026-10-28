namespace Content.Server._WF.Planets.Flight;

/// <summary>Raised on a grid when its pooled lift load is weighed; add any virtual cargo mass to Mass.</summary>
[ByRefEvent]
public record struct WFGridLiftLoadEvent(float Mass);

/// <summary>
/// Raised broadcast when the descent warning names low lift; a module that knows how to restore it, such as by
/// dropping cargo, sets <c>Hint</c>.
/// </summary>
[ByRefEvent]
public record struct WFLiftWarningEvent(EntityUid Grid, float Ratio, float FullRatio)
{
    public string? Hint;
}
