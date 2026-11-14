using Content.Shared._WF.Banking;
using NUnit.Framework;

namespace Content.Tests._WF.Banking;

[TestFixture]
[TestOf(typeof(WFBankMath))]
public sealed class WFBankMathTest
{
    [TestCase(0, 250_000)]
    [TestCase(100_000, 250_000)]
    [TestCase(2_500_000, 250_000)]
    [TestCase(3_000_000, 300_000)]
    [TestCase(10_000_000, 1_000_000)]
    [TestCase(-50_000, 250_000)]
    public void LimitIsTheHigherOfFloorAndShare(int opening, int expected)
    {
        Assert.That(WFBankMath.Limit(opening, 250_000, 0.1f), Is.EqualTo(expected));
    }

    [Test]
    public void AllowedIsCappedByBalanceAndWhatIsLeft()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFBankMath.Allowed(100_000, 250_000, 0), Is.EqualTo(100_000), "A poor account can send everything.");
            Assert.That(WFBankMath.Allowed(5_000_000, 500_000, 200_000), Is.EqualTo(300_000));
            Assert.That(WFBankMath.Allowed(5_000_000, 500_000, 600_000), Is.EqualTo(0));
        });
    }

    [TestCase("25000", 25_000)]
    [TestCase("25,000", 25_000)]
    [TestCase(" 25k ", 25_000)]
    [TestCase("1.5m", 1_500_000)]
    [TestCase("2.5K", 2_500)]
    [TestCase("99999999999", int.MaxValue)]
    public void ParseReadsAmounts(string text, int expected)
    {
        Assert.That(WFBankMath.ParseAmount(text), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("0")]
    [TestCase("0.4")]
    [TestCase("-5")]
    [TestCase("abc")]
    [TestCase("k")]
    public void ParseRejectsNonsense(string text)
    {
        Assert.That(WFBankMath.ParseAmount(text), Is.Null);
    }
}
