namespace Content.Server._NF.Market.Components;

public sealed partial class CargoMarketDataComponent
{
    /// <summary>Sold materials on their way to the console.</summary>
    public List<WFArrivingMaterial> Arriving = new();
}

/// <summary>A sold lot of a material and how long until it reaches the console.</summary>
public sealed class WFArrivingMaterial(string good, int count, TimeSpan left)
{
    public string Good = good;
    public int Count = count;
    public TimeSpan Left = left;
}
