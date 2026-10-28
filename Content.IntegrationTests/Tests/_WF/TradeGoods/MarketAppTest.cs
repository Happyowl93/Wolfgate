#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.TradeGoods;
using Content.Shared._WF.TradeGoods;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>The Market PDA app builds every tab on the client from a server state, with live strings.</summary>
[TestFixture]
[TestOf(typeof(MarketUiFragment))]
public sealed class MarketAppTest
{
    [Test]
    public async Task MarketAppShowsPricesAlertsAndHold()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var station = new WFMarketStation { Name = "Halcyon" };
            station.Prices.Add(new WFMarketPrice("medical supplies", 9500, 1.9f, "blockade", 1f, true));
            station.Prices.Add(new WFMarketPrice("metals", 2000, 0.5f, "glutted", 5f, false));
            var rival = new WFMarketStation { Name = "Camelot" };
            rival.Prices.Add(new WFMarketPrice("medical supplies", 6000, 1f, "steady", 1f, false));
            var state = new WFMarketUiState { Ship = "ISV Mule", HoldTonnes = 21f, StoredTonnes = 3f, Thrust = 0.75f };
            state.Stations.Add(station);
            state.Stations.Add(rival);
            state.Alerts.Add(new WFMarketAlert("Freighter Plutomkii inbound to Halcyon with medical supplies", null, false, WFMarketAlertKind.Traffic));
            state.Alerts.Add(new WFMarketAlert("RUSH Halcyon", System.TimeSpan.FromHours(1), true));
            state.Hold.Add(new WFMarketHoldEntry("metals", 5f, "Camelot", 4000));
            state.Hold.Add(new WFMarketHoldEntry("metals", 5f, "Camelot", 4000));
            state.Hold.Add(new WFMarketHoldEntry("express cargo trading crate", 5f, "Cargo Depot A", 9000, true));

            var fragment = new MarketUiFragment();
            fragment.UpdateState(state);
            var texts = Texts(fragment).ToList();

            Assert.That(texts, Has.Some.Contains("Halcyon"));
            Assert.That(texts, Has.Some.EqualTo("9500"));
            Assert.That(texts, Has.Some.Contains("9500/t"), "Prices should show the per-tonne figure.");
            Assert.That(texts, Has.Some.EqualTo("★"), "The best price for a good in the sector should be starred.");
            Assert.That(texts, Has.Some.Contains("best for 1"));
            Assert.That(texts, Has.Some.Contains("Plutomkii"));
            Assert.That(texts, Has.Some.EqualTo("TRAFFIC"), "Alerts should be tagged by kind.");
            Assert.That(texts, Has.Some.Contains("RUSH Halcyon"));
            Assert.That(texts, Has.Some.Contains("min"), "A timed alert should show its countdown.");
            Assert.That(texts, Has.Some.Contains("ISV Mule").And.Contains("25"), "The hold summary should show the thrust cut.");
            Assert.That(texts, Has.Some.Contains("Thrust 75%"));
            Assert.That(texts, Has.Some.Contains("metals").And.Contains("2"), "Identical crates should share a row with a count.");
            Assert.That(texts, Has.Some.Contains("Cargo Depot A").And.Contains("9000"));
            Assert.That(texts, Has.Some.Contains("3t").And.Contains("stored"), "The hold should say how much is stored materials.");
            Assert.That(texts.Any(text => text.Contains('{') || text.Contains("wf-market")), Is.False, "Every string should resolve.");

            // An empty state says so on every tab.
            fragment.UpdateState(new WFMarketUiState());
            texts = Texts(fragment).ToList();
            Assert.That(texts, Has.Some.Contains("No stations"));
            Assert.That(texts, Has.Some.Contains("Nothing happening"));
            Assert.That(texts, Has.Some.Contains("not aboard"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every label's and rich-text label's text under a control.</summary>
    private static IEnumerable<string> Texts(Control control)
    {
        if (control is Label { Text: { } text })
            yield return text;

        if (control is RichTextLabel rich && rich.GetMessage() is { } message)
            yield return message;

        foreach (var child in control.Children)
        {
            foreach (var nested in Texts(child))
            {
                yield return nested;
            }
        }
    }
}
