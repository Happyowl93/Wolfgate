using System.Globalization;

namespace Content.Shared._WG.Banking;

/// <summary>The round's withdrawal limit on a Bank app account, and reading amounts typed into the app.</summary>
public static class WGBankMath
{
    /// <summary>Most an account may send out in a round: the floor, or a share of its opening balance if that is higher.</summary>
    public static int Limit(int opening, int floor, float share)
    {
        var part = Math.Floor(Math.Max(0, opening) * (double) Math.Max(0f, share));
        return Math.Max(Math.Max(0, floor), (int) Math.Min(int.MaxValue, part));
    }

    /// <summary>Most the account can send out right now: its balance, capped by what is left of the limit.</summary>
    public static int Allowed(int balance, int limit, int withdrawn)
    {
        return Math.Max(0, Math.Min(balance, limit - withdrawn));
    }

    /// <summary>Reads a typed amount such as "25000", "25,000", "25k" or "1.5m"; null unless it is a positive amount.</summary>
    public static int? ParseAmount(string text)
    {
        var trimmed = text.Trim().Replace(",", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        var multiplier = 1.0;
        if (trimmed.EndsWith('k'))
            multiplier = 1_000;
        else if (trimmed.EndsWith('m'))
            multiplier = 1_000_000;

        if (multiplier > 1)
            trimmed = trimmed[..^1];

        if (!double.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return null;

        var amount = Math.Floor(value * multiplier);
        if (amount < 1)
            return null;

        return (int) Math.Min(int.MaxValue, amount);
    }
}
