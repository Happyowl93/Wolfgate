using System;
using Content.Server._WG.LiveFixes;
using NUnit.Framework;

namespace Content.Tests._WG.LiveFixes;

/// <summary>Checks the cold damage curve keeps its values and gives no NaN or infinity for FixedPoint2 to throw on.</summary>
[TestFixture]
[TestOf(typeof(WGColdDamage))]
public sealed class WGColdDamageTest
{
    private const double Cap = 8;

    [Test]
    public void MatchesTheUpstreamCurve()
    {
        // 260 K threshold, 65 K below it: sqrt(65 * 64 / 260) = 4.
        Assert.That(WGColdDamage.Scale(65, 260, Cap), Is.EqualTo(4).Within(1e-9));
        Assert.That(WGColdDamage.Scale(0, 260, Cap), Is.Zero);
    }

    // The spawn-all flake: a mob with a cold threshold of -150 K cooled below it, and sqrt of a negative was NaN.
    [TestCase(10, -150)]
    [TestCase(0, 0)]
    [TestCase(5, 0)]
    [TestCase(1, double.NaN)]
    public void NonPositiveThresholdDealsNothing(double diff, double threshold)
    {
        Assert.That(WGColdDamage.Scale(diff, threshold, Cap), Is.Zero);
    }

    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NaN)]
    public void NonFiniteDiffDealsNothing(double diff)
    {
        Assert.That(WGColdDamage.Scale(diff, 260, Cap), Is.Zero);
    }

    [Test]
    public void ResultIsAlwaysFinite()
    {
        double[] values = { -1e30, -800, -1, 0, 1e-30, 1, 260, 1e30, double.NegativeInfinity,
            double.PositiveInfinity, double.NaN };
        foreach (var diff in values)
        {
            foreach (var threshold in values)
            {
                var scale = WGColdDamage.Scale(diff, threshold, Cap);
                Assert.That(double.IsFinite(scale), $"diff {diff}, threshold {threshold} gave {scale}");
            }
        }
    }
}
