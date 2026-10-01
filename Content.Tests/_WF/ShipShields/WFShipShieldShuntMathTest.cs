using System;
using System.Numerics;
using NUnit.Framework;
using Content.Shared._WF.ShipShields;

namespace Content.Tests._WF.ShipShields;

/// <summary>Checks directional shield capacity and collision coverage agree.</summary>
[TestFixture]
public sealed class WFShipShieldShuntMathTest
{
    [Test]
    public void LiveAllocationTakesTheShortWayAndSettlesWithoutOvershoot()
    {
        var direction = 179f * MathF.PI / 180f;
        var target = -179f * MathF.PI / 180f;
        var first = WFShipShieldShuntMath.StepAngle(direction, target, 0.1f);
        Assert.That(WFShipShieldShuntMath.NormalizeAngle(first - direction), Is.InRange(0f, 2f * MathF.PI / 180f));
        var concentration = 0f;
        for (var step = 0; step < 100; step++)
        {
            var next = WFShipShieldShuntMath.Step(concentration, 1f, 0.1f, 0.5f);
            Assert.That(next, Is.InRange(concentration, MathF.Min(1f, concentration + 0.051f)));
            concentration = next;
            direction = WFShipShieldShuntMath.StepAngle(direction, target, 0.1f);
        }
        Assert.That(concentration, Is.EqualTo(1f), "Full diversion must eventually open the unprotected sectors exactly.");
        Assert.That(WFShipShieldShuntMath.NormalizeAngle(direction - target), Is.EqualTo(0f).Within(0.00001f));
        Assert.That(WFShipShieldShuntMath.Step(0.5f, 0f, 0.1f, 0.5f), Is.InRange(0.45f, 0.5f));
        Assert.That(MathF.Abs(WFShipShieldShuntMath.StepAngle(0f, MathF.PI, 0.1f)), Is.LessThanOrEqualTo(MathF.PI / 20f + 0.00001f));
    }

    [TestCase(0f, 90f)]
    [TestCase(90f, 0f)]
    [TestCase(180f, -90f)]
    [TestCase(270f, -180f)]
    public void HelmBearingsAreClockwiseAndRelativeToConsole(float bearing, float gridDegrees)
    {
        const float helmRotation = 0.63f;
        var direction = WFShipShieldHelmAngles.GridDirection(bearing, helmRotation);
        Assert.That(direction, Is.EqualTo(helmRotation + gridDegrees * MathF.PI / 180f).Within(0.0001f));
        var roundTrip = WFShipShieldHelmAngles.Bearing(direction + 4f * MathF.PI, helmRotation);
        Assert.That(MathF.Abs(WFShipShieldShuntMath.NormalizeAngle((roundTrip - bearing) * MathF.PI / 180f)), Is.LessThan(0.0001f));
    }
    [TestCase(0f, 1f, 1f)]
    [TestCase(0.5f, 2.5f, 0.5f)]
    [TestCase(1f, 4f, 0f)]
    public void QuarterArcTransfersPowerAndResetRestoresCoverage(float amount, float reinforced, float remainder)
    {
        Assert.That(WFShipShieldShuntMath.StrengthMultiplier(Vector2.UnitX, Vector2.Zero, 0f, amount, MathF.PI / 2f), Is.EqualTo(reinforced).Within(0.0001f));
        Assert.That(WFShipShieldShuntMath.StrengthMultiplier(-Vector2.UnitX, Vector2.Zero, 0f, amount, MathF.PI / 2f), Is.EqualTo(remainder).Within(0.0001f));
    }

    [TestCase(31f, 30f)]
    [TestCase(179f, 90f)]
    [TestCase(-191f, 120f)]
    [TestCase(721f, 270f)]
    public void ArbitraryDirectionsConserveCapacityAndWrap(float directionDegrees, float arcDegrees)
    {
        var direction = directionDegrees * MathF.PI / 180f;
        var arc = arcDegrees * MathF.PI / 180f;
        const int samples = 3600;
        var total = 0f;
        for (var i = 0; i < samples; i++)
        {
            var angle = (i + 0.37f) * WFShipShieldShuntMath.FullArc / samples;
            var point = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var value = WFShipShieldShuntMath.StrengthMultiplier(point, Vector2.Zero, direction, 0.73f, arc);
            Assert.That(value, Is.EqualTo(WFShipShieldShuntMath.StrengthMultiplier(point, Vector2.Zero, direction + WFShipShieldShuntMath.FullArc, 0.73f, arc)).Within(0.0001f));
            total += value;
        }
        Assert.That(total / samples, Is.EqualTo(1f).Within(0.002f));
    }

    [Test]
    public void FullDiversionSplitsCoverageAtSectorRays()
    {
        var segments = WFShipShieldShuntMath.ProtectedSegments(new Vector2(2f, -4f), new Vector2(2f, 4f), Vector2.Zero, 0f, 1f, MathF.PI / 2f);
        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(Vector2.Distance(segments[0].Start, new Vector2(2f, -2f)), Is.LessThan(0.0001f));
        Assert.That(Vector2.Distance(segments[0].End, new Vector2(2f, 2f)), Is.LessThan(0.0001f));
        Assert.That(WFShipShieldShuntMath.ProtectedSegments(new Vector2(-2f, -4f), new Vector2(-2f, 4f), Vector2.Zero, 0f, 1f, MathF.PI / 2f), Is.Empty);
        Assert.That(WFShipShieldShuntMath.ProtectedSegments(new Vector2(-2f, -4f), new Vector2(-2f, 4f), Vector2.Zero, 0f, 0f, MathF.PI / 2f), Has.Count.EqualTo(1));
    }

    [Test]
    public void RotatedOffOriginHullRetainsItsLocalAllocation()
    {
        var center = new Vector2(103f, -47f);
        var angle = 0.731f;
        Vector2 Rotate(Vector2 point) => new(MathF.Cos(angle) * point.X - MathF.Sin(angle) * point.Y, MathF.Sin(angle) * point.X + MathF.Cos(angle) * point.Y);
        var segments = WFShipShieldShuntMath.ProtectedSegments(center + Rotate(new Vector2(2f, -4f)), center + Rotate(new Vector2(2f, 4f)), center, angle, 1f, MathF.PI / 2f);
        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(Vector2.Distance(segments[0].Start, center + Rotate(new Vector2(2f, -2f))), Is.LessThan(0.0001f));
        Assert.That(Vector2.Distance(segments[0].End, center + Rotate(new Vector2(2f, 2f))), Is.LessThan(0.0001f));
        Assert.That(WFShipShieldShuntMath.StrengthMultiplier(center - Rotate(Vector2.UnitX), center, angle, 1f, MathF.PI / 2f), Is.Zero);
    }
}