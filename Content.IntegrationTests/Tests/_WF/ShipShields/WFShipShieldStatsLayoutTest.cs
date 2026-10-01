using System.Numerics;
using System.Reflection;
using Content.Client._WF.ShipShields;
using Content.Shared._WF.ShipShields;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks generator specifications fit a compact popup without expanding helm controls.</summary>
[TestFixture]
public sealed class WFShipShieldStatsLayoutTest
{
    [Test]
    public async Task SpecificationsFitSmallPanelAndUnavailableGeneratorsDisableAccess()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        await pair.Client.WaitAssertion(() =>
        {
            var style = pair.Client.ResolveDependency<IUserInterfaceManager>().Stylesheet;
            var stats = new WFShipShieldGeneratorStats("[color=red]Long renamed generator[/color] MS-100", 50000f,
                70000f, 1000f, 6000f, 40000f, 190000f, 60f);
            using var window = new WFShipShieldStatsWindow { Stylesheet = style };
            window.UpdateStats(stats);
            var name = (RichTextLabel) typeof(WFShipShieldStatsWindow).GetField("_name", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.That(name.Text, Is.EqualTo(FormattedMessage.FromUnformatted(Loc.GetString("wf-shield-stats-generator", ("name", stats.Name))).ToMarkup()),
                "Renamed generators must display markup characters literally.");
            foreach (var size in new[] { new Vector2(340f, 300f), new Vector2(440f, 420f) })
            {
                window.SetSize = size;
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(window.IsArrangeValid, Is.True);
                Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X));
                AssertBounds(window);
            }
            using var panel = new WFShipShieldShuntScreen { Stylesheet = style };
            var button = (Button) typeof(WFShipShieldShuntScreen).GetField("_stats", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
            Assert.That(button.Disabled, Is.True);
            panel.UpdateState(new WFShipShieldShuntState(true, true, 1f, 0f, 0f, MathF.PI / 2f) { Stats = stats }, 0f);
            Assert.That(button.Disabled, Is.False);
            var panelSize = new Vector2(760f, 540f);
            panel.Measure(panelSize);
            panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, panelSize));
            Assert.That(panel.DesiredSize.X, Is.LessThanOrEqualTo(panelSize.X));
            AssertBounds(panel);
            panel.UpdateState(null, 0f);
            Assert.That(button.Disabled, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    private static void AssertBounds(Control parent)
    {
        if (parent is Slider)
            return;
        foreach (var child in parent.Children)
        {
            if (!child.Visible)
                continue;
            Assert.That(child.Size.X, Is.LessThanOrEqualTo(parent.Size.X + 1f));
            AssertBounds(child);
        }
    }
}
