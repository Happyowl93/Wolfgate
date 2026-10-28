using Content.Client.UserInterface.Fragments;
using Content.Shared._WF.TradeGoods;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.TradeGoods;

/// <summary>The Market PDA app: live chute prices, sector alerts and the holder's hold.</summary>
public sealed partial class MarketUi : UIFragment
{
    private MarketUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new MarketUiFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WFMarketUiState cast)
            _fragment?.UpdateState(cast);
    }
}

/// <summary>Three tabs: prices by station, alerts with countdowns, and what's in the hold.</summary>
public sealed class MarketUiFragment : BoxContainer
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Color Up = Color.FromHex("#80d080");
    private static readonly Color Down = Color.FromHex("#d08080");
    private static readonly Color Hot = Color.FromHex("#e0b050");
    private static readonly Color Dim = Color.FromHex("#9aa7b1");

    private readonly BoxContainer _prices;
    private readonly BoxContainer _alerts;
    private readonly BoxContainer _hold;

    public MarketUiFragment()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        HorizontalExpand = true;
        Margin = new Thickness(5);

        var tabs = new TabContainer { VerticalExpand = true, HorizontalExpand = true };
        AddChild(tabs);
        _prices = AddTab(tabs, Loc.GetString("wf-market-tab-prices"));
        _alerts = AddTab(tabs, Loc.GetString("wf-market-tab-alerts"));
        _hold = AddTab(tabs, Loc.GetString("wf-market-tab-hold"));
    }

    private static BoxContainer AddTab(TabContainer tabs, string title)
    {
        var list = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(4) };
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(list);
        tabs.AddChild(scroll);
        tabs.SetTabTitle(tabs.ChildCount - 1, title);
        return list;
    }

    public void UpdateState(WFMarketUiState state)
    {
        _prices.RemoveAllChildren();
        foreach (var station in state.Stations)
        {
            _prices.AddChild(new Label { Text = station.Name, StyleClasses = { "LabelHeading" }, Margin = new Thickness(0, 6, 0, 2) });
            foreach (var price in station.Prices)
            {
                var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
                row.AddChild(new Label { Text = price.Good, HorizontalExpand = true, ClipText = true });
                row.AddChild(new Label
                {
                    Text = Loc.GetString("wf-market-price", ("amount", price.Amount), ("tonnes", price.Tonnes),
                        ("perTonne", price.Tonnes > 0 ? (int) (price.Amount / price.Tonnes) : 0)),
                    FontColorOverride = price.Highlight ? Hot : null,
                });
                row.AddChild(new Label
                {
                    Text = $" {Trend(price.Factor)} {price.Reason}",
                    MinWidth = 110,
                    FontColorOverride = price.Factor >= 1.02f ? Up : price.Factor <= 0.98f ? Down : Dim,
                });
                _prices.AddChild(row);
            }
        }

        if (state.Stations.Count == 0)
            _prices.AddChild(new Label { Text = Loc.GetString("wf-market-no-stations"), FontColorOverride = Dim });

        _alerts.RemoveAllChildren();
        foreach (var alert in state.Alerts)
        {
            var text = alert.Text;
            if (alert.Ends is { } ends)
            {
                var minutes = Math.Max(0, (int) Math.Ceiling((ends - _timing.CurTime).TotalMinutes));
                text = Loc.GetString("wf-market-alert-timed", ("text", text), ("minutes", minutes));
            }

            _alerts.AddChild(new Label { Text = text, FontColorOverride = alert.Urgent ? Hot : null, Margin = new Thickness(0, 2) });
        }

        if (state.Alerts.Count == 0)
            _alerts.AddChild(new Label { Text = Loc.GetString("wf-market-no-alerts"), FontColorOverride = Dim });

        _hold.RemoveAllChildren();
        if (state.Ship == null)
        {
            _hold.AddChild(new Label { Text = Loc.GetString("wf-market-no-ship"), FontColorOverride = Dim });
            return;
        }

        _hold.AddChild(new Label
        {
            Text = Loc.GetString("wf-market-hold-summary", ("ship", state.Ship), ("tonnes", (int) MathF.Round(state.HoldTonnes)),
                ("percent", (int) MathF.Round((1f - state.Thrust) * 100f))),
            StyleClasses = { "LabelHeading" },
        });

        foreach (var entry in state.Hold)
        {
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new Label { Text = Loc.GetString("wf-market-hold-good", ("good", entry.Good), ("tonnes", entry.Tonnes)), HorizontalExpand = true, ClipText = true });
            row.AddChild(new Label { Text = Loc.GetString(entry.Bound ? "wf-market-hold-bound" : "wf-market-hold-best", ("station", entry.Best), ("amount", entry.BestAmount)), FontColorOverride = Dim });
            _hold.AddChild(row);
        }

        if (state.Hold.Count == 0)
            _hold.AddChild(new Label { Text = Loc.GetString("wf-market-hold-empty"), FontColorOverride = Dim });
    }

    private static string Trend(float factor)
    {
        return factor >= 1.02f ? "▲" : factor <= 0.98f ? "▼" : "–";
    }
}
