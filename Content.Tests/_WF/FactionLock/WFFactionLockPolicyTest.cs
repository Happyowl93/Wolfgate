using System.Linq;
using Content.Shared._WF.FactionLock;
using NUnit.Framework;

namespace Content.Tests._WF.FactionLock;

/// <summary>Companies group into factions, and a played faction bars every other.</summary>
[TestFixture]
public sealed class WFFactionLockPolicyTest
{
    [TestCase("TSF", "TSF")]
    [TestCase("TSFCivilian", "TSF")]
    [TestCase("PDV", "PDV")]
    [TestCase("PDVCivilian", "PDV")]
    [TestCase("MMC", "MMC")]
    [TestCase("TheViperGroup", "VG")]
    [TestCase("USSP", "USSP")]
    [TestCase("WFUnderground", "Underground")]
    [TestCase("WFAsf", "ASF")]
    [TestCase("Colonial", null)]
    [TestCase("None", null)]
    public void CompaniesGroupIntoFactions(string company, string faction)
    {
        Assert.That(WFFactionLockPolicy.FactionOf(company), Is.EqualTo(faction));
    }

    [Test]
    public void PlayedReadsOnlyFactionFlags()
    {
        var flags = new[] { "some-other-flag", WFFactionLockPolicy.Flag("MMC") };
        Assert.That(WFFactionLockPolicy.Played(flags), Is.EqualTo(new[] { "MMC" }));
    }

    [TestCase("MMC", "TSF", "MMC")]
    [TestCase("PDV", "Underground", "PDV")]
    [TestCase("TSF", "TSF", null)]
    public void PlayedFactionBarsOthers(string played, string wanted, string barring)
    {
        Assert.That(WFFactionLockPolicy.Barring(new[] { played }, wanted), Is.EqualTo(barring));
    }

    [TestCase("Cadet", true)]
    [TestCase("Pirate", true)]
    [TestCase("WFAsfColonist", true)]
    [TestCase("Deputy", false)]
    [TestCase("PirateCaptain", false)]
    public void OnlyGruntJobsStayOpen(string job, bool open)
    {
        Assert.That(WFFactionLockPolicy.IsOpen(job), Is.EqualTo(open));
    }

    [Test]
    public void NothingPlayedBarsNothing()
    {
        Assert.That(WFFactionLockPolicy.Barring(Enumerable.Empty<string>(), "TSF"), Is.Null);
    }
}
