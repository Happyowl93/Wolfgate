using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Fragments;
using Content.Shared._WF.TradeGoods;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

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

/// <summary>A header and three tabs: prices by station as cards, alerts tagged by kind, and the hold with its thrust.</summary>
public sealed class MarketUiFragment : BoxContainer
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _resources = default!;

    private static readonly ResPath IconRsi = new("/Textures/_WF/TradeGoods/market.rsi");

    private static readonly Color Up = Color.FromHex("#80d080");
    private static readonly Color Down = Color.FromHex("#d08080");
    private static readonly Color Hot = Color.FromHex("#e0b050");
    private static readonly Color Dim = Color.FromHex("#9aa7b1");
    private static readonly Color Bright = Color.FromHex("#e8ecef");
    private static readonly Color CardBackground = Color.FromHex("#1d2127");
    private static readonly Color HeaderBackground = Color.FromHex("#14171b");
    private static readonly Color RowShade = Color.FromHex("#ffffff0a");
    private static readonly Color Quiet = Color.FromHex("#4a5560");

    private readonly TabContainer _tabs;
    private readonly Label _status;
    private readonly LineEdit _filter;
    private readonly BoxContainer _prices;
    private readonly BoxContainer _alerts;
    private readonly BoxContainer _hold;

    private WFMarketUiState? _state;

    public MarketUiFragment()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        HorizontalExpand = true;
        Margin = new Thickness(5);

        var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        header.AddChild(new TextureRect
        {
            Texture = _resources.GetResource<RSIResource>(IconRsi).RSI.TryGetState("icon", out var icon) ? icon.Frame0 : null,
            Stretch = TextureRect.StretchMode.KeepCentered,
            MinSize = new Vector2(36, 36),
            Margin = new Thickness(0, 0, 6, 0),
        });

        var titles = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        titles.AddChild(new Label { Text = Loc.GetString("wf-market-title"), StyleClasses = { "LabelHeading" }, FontColorOverride = Hot });
        _status = new Label { FontColorOverride = Dim, StyleClasses = { "LabelSmall" } };
        titles.AddChild(_status);
        header.AddChild(titles);
        AddChild(Panel(header, HeaderBackground, Hot, new Thickness(0, 0, 0, 2), new Thickness(6, 4)));

        _tabs = new TabContainer { VerticalExpand = true, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
        AddChild(_tabs);

        var pricesTab = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true };
        _filter = new LineEdit { PlaceHolder = Loc.GetString("wf-market-filter"), HorizontalExpand = true, Margin = new Thickness(4, 4, 4, 2) };
        _filter.OnTextChanged += _ => UpdatePrices();
        pricesTab.AddChild(_filter);
        _prices = AddScroll(pricesTab);
        AddTab(pricesTab, Loc.GetString("wf-market-tab-prices"));

        _alerts = AddScroll(null);
        _hold = AddScroll(null);
        SetTitles(0, null);
    }

    /// <summary>A scrolling list, added to a parent or as a tab of its own.</summary>
    private BoxContainer AddScroll(BoxContainer? parent)
    {
        var list = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(4) };
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(list);
        if (parent != null)
            parent.AddChild(scroll);
        else
            AddTab(scroll, string.Empty);

        return list;
    }

    private void AddTab(Control tab, string title)
    {
        _tabs.AddChild(tab);
        _tabs.SetTabTitle(_tabs.ChildCount - 1, title);
    }

    private void SetTitles(int alerts, float? tonnes)
    {
        _tabs.SetTabTitle(1, alerts > 0 ? Loc.GetString("wf-market-tab-alerts-count", ("count", alerts)) : Loc.GetString("wf-market-tab-alerts"));
        _tabs.SetTabTitle(2, tonnes is { } load ? Loc.GetString("wf-market-tab-hold-tonnes", ("tonnes", (int) MathF.Round(load))) : Loc.GetString("wf-market-tab-hold"));
    }

    /// <summary>A panel with a flat background and an accent border.</summary>
    private static PanelContainer Panel(Control content, Color background, Color accent, Thickness border, Thickness padding)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = background,
                BorderColor = accent,
                BorderThickness = border,
                ContentMarginLeftOverride = padding.Left,
                ContentMarginTopOverride = padding.Top,
                ContentMarginRightOverride = padding.Right,
                ContentMarginBottomOverride = padding.Bottom,
            },
        };
        panel.AddChild(content);
        return panel;
    }

    /// <summary>A card with a coloured stripe down its left edge.</summary>
    private static PanelContainer Card(Control content, Color accent)
    {
        var card = Panel(content, CardBackground, accent, new Thickness(3, 0, 0, 0), new Thickness(8, 4, 6, 5));
        card.Margin = new Thickness(0, 0, 0, 5);
        return card;
    }

    private static Label Dimmed(string text, int minWidth = 0)
    {
        return new Label { Text = text, FontColorOverride = Dim, StyleClasses = { "LabelSmall" }, MinWidth = minWidth, VerticalAlignment = VAlignment.Center };
    }

    public void UpdateState(WFMarketUiState state)
    {
        _state = state;
        _status.Text = state.Ship is { } ship
            ? Loc.GetString("wf-market-status-ship", ("stations", state.Stations.Count), ("ship", ship))
            : Loc.GetString("wf-market-status", ("stations", state.Stations.Count));

        SetTitles(state.Alerts.Count, state.Ship != null ? state.HoldTonnes : null);
        UpdatePrices();
        UpdateAlerts(state);
        UpdateHold(state);
    }

    private void UpdatePrices()
    {
        _prices.RemoveAllChildren();
        if (_state is not { } state)
            return;

        // The best price for each good across the sector, starred where a station pays it and others buy it too.
        var best = new Dictionary<string, int>();
        var buyers = new Dictionary<string, int>();
        foreach (var price in state.Stations.SelectMany(station => station.Prices))
        {
            best[price.Good] = Math.Max(best.GetValueOrDefault(price.Good), price.Amount);
            buyers[price.Good] = buyers.GetValueOrDefault(price.Good) + 1;
        }

        bool IsTop(WFMarketPrice price) => buyers[price.Good] > 1 && price.Amount == best[price.Good];

        var filter = _filter.Text.Trim();
        var shown = 0;
        foreach (var station in state.Stations)
        {
            var prices = station.Prices
                .Where(price => filter.Length == 0 || price.Good.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (prices.Count == 0)
                continue;

            shown++;
            var tops = prices.Count(IsTop);
            var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
            var title = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 3) };
            title.AddChild(new Label { Text = station.Name, StyleClasses = { "LabelHeading" }, FontColorOverride = Bright, HorizontalExpand = true, ClipText = true });
            if (tops > 0)
                title.AddChild(new Label { Text = Loc.GetString("wf-market-station-best", ("count", tops)), FontColorOverride = Hot, StyleClasses = { "LabelSmall" }, VerticalAlignment = VAlignment.Center });
            content.AddChild(title);

            for (var i = 0; i < prices.Count; i++)
            {
                var price = prices[i];
                var top = IsTop(price);
                var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
                row.AddChild(new Label { Text = top ? "★" : string.Empty, FontColorOverride = Hot, MinWidth = 14 });
                row.AddChild(new Label { Text = price.Good, HorizontalExpand = true, ClipText = true });
                row.AddChild(new Label
                {
                    Text = Trend(price.Factor),
                    FontColorOverride = Colour(price.Factor),
                    MinWidth = 14,
                });
                row.AddChild(new Label
                {
                    Text = Loc.GetString("wf-market-price-amount", ("amount", price.Amount)),
                    FontColorOverride = price.Highlight ? Hot : Bright,
                    MinWidth = 52,
                    Align = Label.AlignMode.Right,
                });
                row.AddChild(Dimmed(Loc.GetString("wf-market-price-weight", ("tonnes", price.Tonnes),
                    ("perTonne", price.Tonnes > 0 ? (int) (price.Amount / price.Tonnes) : 0)), 92));
                var reason = Dimmed(price.Reason, 96);
                reason.ClipText = true;
                reason.FontColorOverride = price.Factor >= 1.02f || price.Factor <= 0.98f ? Colour(price.Factor) : Quiet;
                row.AddChild(reason);
                content.AddChild(i % 2 == 1 ? Panel(row, RowShade, Color.Transparent, new Thickness(0), new Thickness(0)) : row);
            }

            _prices.AddChild(Card(content, tops > 0 || prices.Any(price => price.Highlight) ? Hot : Quiet));
        }

        if (shown == 0)
        {
            _prices.AddChild(new Label
            {
                Text = Loc.GetString(state.Stations.Count == 0 ? "wf-market-no-stations" : "wf-market-no-match"),
                FontColorOverride = Dim,
            });
        }
    }

    private void UpdateAlerts(WFMarketUiState state)
    {
        _alerts.RemoveAllChildren();
        foreach (var alert in state.Alerts)
        {
            var accent = KindColour(alert.Kind);
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            var body = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
            body.AddChild(new Label
            {
                Text = Loc.GetString($"wf-market-alert-kind-{alert.Kind.ToString().ToLowerInvariant()}"),
                FontColorOverride = accent,
                StyleClasses = { "LabelSmall" },
            });

            var text = new RichTextLabel { HorizontalExpand = true };
            text.SetMessage(FormattedMessage.FromUnformatted(alert.Text), alert.Urgent ? Bright : Dim);
            body.AddChild(text);
            row.AddChild(body);

            if (alert.Ends is { } ends)
            {
                var minutes = Math.Max(0, (int) Math.Ceiling((ends - _timing.CurTime).TotalMinutes));
                row.AddChild(Panel(new Label
                    {
                        Text = Loc.GetString("wf-market-minutes", ("minutes", minutes)),
                        FontColorOverride = minutes <= 5 ? Down : accent,
                        StyleClasses = { "LabelSmall" },
                    },
                    HeaderBackground, accent, new Thickness(1), new Thickness(5, 1)));
            }

            _alerts.AddChild(Card(row, accent));
        }

        if (state.Alerts.Count == 0)
            _alerts.AddChild(new Label { Text = Loc.GetString("wf-market-no-alerts"), FontColorOverride = Dim });
    }

    private void UpdateHold(WFMarketUiState state)
    {
        _hold.RemoveAllChildren();
        if (state.Ship == null)
        {
            _hold.AddChild(new Label { Text = Loc.GetString("wf-market-no-ship"), FontColorOverride = Dim });
            return;
        }

        var cut = (int) MathF.Round((1f - state.Thrust) * 100f);
        var thrustColour = state.Thrust >= 0.8f ? Up : state.Thrust >= 0.6f ? Hot : Down;
        var summary = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        summary.AddChild(new Label
        {
            Text = Loc.GetString("wf-market-hold-summary", ("ship", state.Ship), ("tonnes", (int) MathF.Round(state.HoldTonnes)), ("percent", cut)),
            StyleClasses = { "LabelHeading" },
            FontColorOverride = Bright,
            ClipText = true,
        });

        var thrust = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 3, 0, 1) };
        thrust.AddChild(Dimmed(Loc.GetString("wf-market-hold-thrust", ("percent", 100 - cut)), 84));
        thrust.AddChild(new ProgressBar
        {
            MinValue = 0f,
            MaxValue = 1f,
            Value = state.Thrust,
            HorizontalExpand = true,
            MinHeight = 10,
            VerticalAlignment = VAlignment.Center,
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = HeaderBackground, BorderColor = Quiet, BorderThickness = new Thickness(1) },
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = thrustColour },
        });
        summary.AddChild(thrust);

        if (state.StoredTonnes >= 0.05f)
            summary.AddChild(Dimmed(Loc.GetString("wf-market-hold-stored", ("tonnes", MathF.Round(state.StoredTonnes, 1)))));

        _hold.AddChild(Card(summary, thrustColour));

        if (state.Hold.Count == 0)
        {
            _hold.AddChild(new Label { Text = Loc.GetString("wf-market-hold-empty"), FontColorOverride = Dim });
            return;
        }

        // Identical crates bound for the same best sale share one row.
        var groups = state.Hold
            .GroupBy(entry => (entry.Good, entry.Best, entry.BestAmount, entry.Bound))
            .OrderByDescending(group => group.Key.BestAmount * group.Count())
            .ToList();

        var crates = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var (good, station, amount, bound) = group.Key;
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new Label
            {
                Text = group.Count() > 1 ? Loc.GetString("wf-market-hold-count", ("good", good), ("count", group.Count())) : good,
                HorizontalExpand = true,
                ClipText = true,
            });
            row.AddChild(Dimmed(Loc.GetString("wf-market-hold-tonnes", ("tonnes", MathF.Round(group.Sum(entry => entry.Tonnes), 1))), 40));
            row.AddChild(new Label
            {
                Text = Loc.GetString(bound ? "wf-market-hold-bound" : "wf-market-hold-best", ("station", station), ("amount", amount)),
                FontColorOverride = amount <= 0 ? Quiet : bound ? Hot : Up,
                Align = Label.AlignMode.Right,
                MinWidth = 150,
                ClipText = true,
            });
            crates.AddChild(i % 2 == 1 ? Panel(row, RowShade, Color.Transparent, new Thickness(0), new Thickness(0)) : row);
        }

        _hold.AddChild(Card(crates, Quiet));
    }

    private static string Trend(float factor)
    {
        return factor >= 1.02f ? "▲" : factor <= 0.98f ? "▼" : "–";
    }

    private static Color Colour(float factor)
    {
        return factor >= 1.02f ? Up : factor <= 0.98f ? Down : Dim;
    }

    private static Color KindColour(WFMarketAlertKind kind)
    {
        return kind switch
        {
            WFMarketAlertKind.Deal => Hot,
            WFMarketAlertKind.Traffic => Color.FromHex("#70a8e0"),
            WFMarketAlertKind.Warning => Down,
            WFMarketAlertKind.Personal => Up,
            WFMarketAlertKind.Hunt => Color.FromHex("#c080e0"),
            _ => Dim,
        };
    }
}
