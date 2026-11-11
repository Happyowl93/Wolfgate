namespace Content.Shared._WF.ScratchTickets;

/// <summary>The scratch ticket's layout and how a ticket is rolled.</summary>
public static class WFScratchTicketRules
{
    public const int Columns = 3;
    public const int CellCount = Columns * Columns;

    /// <summary>How many of one symbol win.</summary>
    public const int Match = 3;

    /// <summary>The share of the foil that has to come off before the ticket counts as scratched.</summary>
    public const float RevealFraction = 0.9f;

    /// <summary>
    /// Fills <paramref name="cells"/> and returns the winning symbol, or -1. A winner shows exactly three times;
    /// every other symbol shows at most twice, so no ticket wins by accident.
    /// </summary>
    public static int Roll(IReadOnlyList<WFScratchSymbol> symbols, float loseWeight, System.Random random, List<int> cells)
    {
        cells.Clear();

        var total = loseWeight;
        foreach (var symbol in symbols)
        {
            total += symbol.Weight;
        }

        var winner = -1;
        var roll = random.NextDouble() * total - loseWeight;
        for (var i = 0; i < symbols.Count && roll >= 0 && winner < 0; i++)
        {
            roll -= symbols[i].Weight;
            if (roll < 0 && symbols[i].Weight > 0)
                winner = i;
        }

        var filler = new List<int>();
        for (var i = 0; i < symbols.Count; i++)
        {
            if (i == winner)
                continue;

            for (var copy = 0; copy < Match - 1; copy++)
            {
                filler.Add(i);
            }
        }

        if (winner >= 0)
        {
            for (var i = 0; i < Match; i++)
            {
                cells.Add(winner);
            }
        }

        Shuffle(filler, random);
        for (var i = 0; cells.Count < CellCount && i < filler.Count; i++)
        {
            cells.Add(filler[i]);
        }

        Shuffle(cells, random);
        return winner;
    }

    /// <summary>The symbol that shows at least three times, or -1.</summary>
    public static int Winner(IReadOnlyList<int> cells)
    {
        foreach (var candidate in cells)
        {
            var count = 0;
            foreach (var cell in cells)
            {
                if (cell == candidate)
                    count++;
            }

            if (count >= Match)
                return candidate;
        }

        return -1;
    }

    private static void Shuffle(List<int> list, System.Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
