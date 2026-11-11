using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.ScratchTickets;
using NUnit.Framework;

namespace Content.Tests._WF.ScratchTickets;

/// <summary>
/// The scratch ticket roll: a winner shows exactly three times, nothing else ever shows three times, and wins come
/// as often as the weights say.
/// </summary>
[TestFixture]
public sealed class WFScratchTicketRulesTest
{
    private const float LoseWeight = 600f;

    private static List<WFScratchSymbol> Symbols()
    {
        return new List<WFScratchSymbol>
        {
            new() { Prize = 1000, Weight = 100 },
            new() { Prize = 500, Weight = 300 },
            new() { Prize = 250, Weight = 0 },
            new(),
            new(),
            new(),
        };
    }

    [Test]
    public void RolledCellsMatchTheOutcome()
    {
        var symbols = Symbols();
        var random = new Random(4);
        var cells = new List<int>();
        var wins = new int[symbols.Count];
        const int rolls = 20000;

        for (var i = 0; i < rolls; i++)
        {
            var winner = WFScratchTicketRules.Roll(symbols, LoseWeight, random, cells);

            Assert.That(cells, Has.Count.EqualTo(WFScratchTicketRules.CellCount));
            Assert.That(WFScratchTicketRules.Winner(cells), Is.EqualTo(winner), "The cells show a different outcome from the roll.");

            foreach (var group in cells.GroupBy(cell => cell))
            {
                Assert.That(group.Count(), group.Key == winner
                    ? Is.EqualTo(WFScratchTicketRules.Match)
                    : Is.LessThan(WFScratchTicketRules.Match));
            }

            if (winner >= 0)
                wins[winner]++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(wins[0] / (float) rolls, Is.EqualTo(0.1f).Within(0.015f));
            Assert.That(wins[1] / (float) rolls, Is.EqualTo(0.3f).Within(0.015f));
            Assert.That(wins.Skip(2).Sum(), Is.Zero, "A symbol with no weight won.");
        });
    }
}
