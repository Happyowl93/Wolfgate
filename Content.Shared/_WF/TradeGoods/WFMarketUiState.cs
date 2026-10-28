using Robust.Shared.Serialization;

namespace Content.Shared._WF.TradeGoods;

/// <summary>What the Market PDA app shows: live chute prices, sector alerts and the holder's hold.</summary>
[Serializable, NetSerializable]
public sealed class WFMarketUiState : BoundUserInterfaceState
{
    public List<WFMarketStation> Stations = new();
    public List<WFMarketAlert> Alerts = new();
    public List<WFMarketHoldEntry> Hold = new();

    /// <summary>Cargo aboard the holder's ship, in tonnes.</summary>
    public float HoldTonnes;

    /// <summary>Tonnes of <see cref="HoldTonnes"/> held as materials in silos, lathes and other material storage.</summary>
    public float StoredTonnes;

    /// <summary>The ship's thrust in space as a fraction of empty.</summary>
    public float Thrust = 1f;

    /// <summary>Name of the holder's ship, or null when not aboard one.</summary>
    public string? Ship;
}

[Serializable, NetSerializable]
public sealed class WFMarketStation
{
    public string Name = string.Empty;
    public List<WFMarketPrice> Prices = new();
}

/// <summary>One good's live quote at a station; <c>Reason</c> is already localized.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMarketPrice(string Good, int Amount, float Factor, string Reason, float Tonnes, bool Highlight);

/// <summary>A sector event worth chasing; <c>Ends</c> is game time, or null for no countdown.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMarketAlert(string Text, TimeSpan? Ends, bool Urgent);

/// <summary>One crate aboard the holder's ship and where it sells best now, or the one place it sells when bound.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMarketHoldEntry(string Good, float Tonnes, string Best, int BestAmount, bool Bound = false);
