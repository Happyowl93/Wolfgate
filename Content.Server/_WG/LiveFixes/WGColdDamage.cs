namespace Content.Server._WG.LiveFixes;

/// <summary>The cold damage curve of <c>TemperatureSystem</c>, with no NaN or infinity for it to throw on.</summary>
public static class WGColdDamage
{
    /// <summary>
    /// Damage multiplier for a body <paramref name="diff"/> kelvin below its cold threshold:
    /// <c>sqrt(diff * cap² / threshold)</c>. A threshold at or below 0 K means no cold damage, and a body pushed to
    /// minus infinity or NaN takes none either; the raw curve gave NaN there and <c>FixedPoint2</c> threw.
    /// </summary>
    public static double Scale(double diff, double threshold, double cap)
    {
        if (!(threshold > 0))
            return 0;

        var scale = Math.Sqrt(diff * (cap * cap / threshold));
        return double.IsFinite(scale) ? scale : 0;
    }
}
