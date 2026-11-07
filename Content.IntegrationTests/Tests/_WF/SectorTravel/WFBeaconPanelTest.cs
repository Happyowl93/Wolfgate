using Content.Client._WF.Cockpit;
using Content.Client._WF.SectorTravel;
using Content.Client.Shuttles.UI;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests._WF.SectorTravel;

/// <summary>Every helm layout carries the jump beacon panel, and the panel never hides itself out of its frame updates.</summary>
[TestFixture]
[TestOf(typeof(WFBeaconJumpPanel))]
public sealed class WFBeaconPanelTest
{
    private static bool Within(Control root, Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent == root)
                return true;
        }

        return false;
    }

    [Test]
    public async Task EveryHelmLayoutCarriesTheBeaconPanel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            using var helm = new ShuttleConsoleWindow();
            var map = helm.FindControl<MapScreen>("MapContainer");
            var panel = map.WfBeaconPanel;
            Assert.That(Within(map, panel), "The flight deck's strategic tab carries the beacon panel.");
            Assert.That(panel.Visible, "The panel never hides itself: a hidden control gets no frame updates to show again.");

            var deck = panel.Parent;
            var lease = new WFCockpitLease();
            map.WfCockpitMap(lease);
            ui.WindowRoot.AddChild(map);
            try
            {
                Assert.That(Within(map, panel), "The cockpit's strategic page carries the beacon panel.");
                Assert.That(panel.Parent, Is.Not.SameAs(deck), "The cockpit page takes the panel.");
            }
            finally
            {
                lease.Restore();
            }

            Assert.That(panel.Parent, Is.SameAs(deck), "Leaving the cockpit returns the panel to the flight deck.");
        });
        await pair.CleanReturnAsync();
    }
}
