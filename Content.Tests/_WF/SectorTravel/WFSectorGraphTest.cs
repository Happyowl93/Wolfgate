using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.SectorTravel;
using NUnit.Framework;

namespace Content.Tests._WF.SectorTravel;

[TestFixture]
[TestOf(typeof(WFSectorGraph))]
public sealed class WFSectorGraphTest
{
    /// <summary>Home links to A and B, and A links to B: two ways to reach A.</summary>
    private static readonly Dictionary<string, string[]> Loop = new()
    {
        ["Home"] = new[] { "A", "B" },
        ["A"] = new[] { "Home", "B" },
        ["B"] = new[] { "Home", "A" },
    };

    private static IEnumerable<string> Links(string sector)
    {
        return Loop.TryGetValue(sector, out var links) ? links : Enumerable.Empty<string>();
    }

    private static bool HomeToAClosed(string a, string b)
    {
        return a == "Home" && b == "A" || a == "A" && b == "Home";
    }

    [Test]
    public void OpenLinksTakeTheShortestWay()
    {
        Assert.That(WFSectorGraph.Hops("Home", "A", Links), Is.EqualTo(1));
        Assert.That(WFSectorGraph.Hops("Home", "Home", Links), Is.EqualTo(0));
    }

    [Test]
    public void AClosedLinkDoesNotHideAnotherWayRound()
    {
        Assert.That(WFSectorGraph.Hops("Home", "A", Links, HomeToAClosed), Is.EqualTo(2),
            "A is still reached through B when the direct link is closed.");
    }

    [Test]
    public void EveryWayClosedCutsASectorOff()
    {
        Assert.That(WFSectorGraph.Hops("Home", "A", Links, (a, b) => a == "A" || b == "A"), Is.Null);
    }
}
