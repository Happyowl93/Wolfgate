using Content.Client.Stylesheets;
using Content.Shared._WF.SectorTravel;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.SectorTravel;

/// <summary>
/// The MAP tab's jump beacon panel: a jump button per linked sector while the ship is in a beacon's zone, and a refuel
/// button while it is docked to a station that sells fusion fuel.
/// </summary>
/// <remarks>
/// The panel itself never hides: a hidden control gets no frame updates, so it would never show again. It hides its
/// contents instead.
/// </remarks>
public sealed class WFBeaconJumpPanel : BoxContainer
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly BoxContainer _content;
    private readonly Label _status;
    private readonly BoxContainer _links;
    private readonly Button _cancel;
    private readonly Button _refuel;
    private string _shown = string.Empty;

    /// <summary>The child shown only while there is a beacon zone or a fuel service: the contents, or their housing.</summary>
    private Control _body;

    /// <summary>The ship the console flies, or null.</summary>
    public Func<EntityUid?>? Shuttle;

    public event Action<string>? JumpPressed;
    public event Action? CancelPressed;
    public event Action? RefuelPressed;

    public WFBeaconJumpPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;

        var header = new Label
        {
            Text = Loc.GetString("wf-sector-beacon-panel-title"),
            HorizontalAlignment = HAlignment.Center,
            StyleClasses = { StyleNano.StyleClassLabelHeading },
        };
        _status = new Label { HorizontalAlignment = HAlignment.Center };
        _links = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        _cancel = new Button
        {
            Text = Loc.GetString("wf-sector-beacon-panel-cancel"),
            StyleClasses = { "ButtonSquare" },
            Visible = false,
        };
        _cancel.OnPressed += _ => CancelPressed?.Invoke();
        _refuel = new Button { StyleClasses = { "ButtonSquare" }, Visible = false };
        _refuel.OnPressed += _ => RefuelPressed?.Invoke();

        _content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        _content.AddChild(_status);
        _content.AddChild(_links);
        _content.AddChild(_cancel);
        _content.AddChild(_refuel);

        var body = new BoxContainer { Orientation = LayoutOrientation.Vertical, Margin = new Thickness(4), Visible = false };
        body.AddChild(header);
        body.AddChild(_content);
        _body = body;
        AddChild(_body);
    }

    /// <summary>Puts the contents in a console's own titled housing, which then shows and hides with them.</summary>
    public void Frame(Func<Control, Control> housing)
    {
        var visible = _body.Visible;
        RemoveAllChildren();
        _content.Parent?.RemoveChild(_content);
        _body = housing(_content);
        _body.Visible = visible;
        AddChild(_body);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var ship = Shuttle?.Invoke();
        var zone = ship is { } a && _entManager.TryGetComponent<WFBeaconZoneComponent>(a, out var z) ? z : null;
        var service = ship is { } b && _entManager.TryGetComponent<WFFuelServiceComponent>(b, out var s) ? s : null;
        if (zone == null && service == null)
        {
            _body.Visible = false;
            _shown = string.Empty;
            return;
        }

        _body.Visible = true;
        var locked = zone?.LockedUntil is { } until && until > _timing.CurTime
            ? (int) Math.Ceiling((until - _timing.CurTime).TotalSeconds)
            : 0;
        var key = $"{zone?.Fuel}/{zone?.Capacity}/{locked}/{zone?.Spooling}/{(zone == null ? "" : string.Join(";", zone.Links))}"
                  + $"/{service?.Station}/{service?.Units}/{service?.Cost}/{service?.Fuel}";
        if (key == _shown)
            return;

        _shown = key;
        var fuel = zone?.Fuel ?? service!.Fuel;
        var capacity = zone?.Capacity ?? service!.Capacity;
        _status.Text = capacity <= 0
            ? Loc.GetString("wf-sector-beacon-panel-no-drive")
            : locked > 0
                ? Loc.GetString("wf-sector-beacon-panel-locked", ("seconds", locked))
                : Loc.GetString("wf-sector-beacon-panel-fuel", ("fuel", (int) fuel), ("capacity", (int) capacity));

        _links.RemoveAllChildren();
        foreach (var link in zone?.Links ?? new List<WFBeaconLink>())
        {
            var name = _proto.TryIndex<WFSectorPrototype>(link.Sector, out var sector) ? Loc.GetString(sector.Name) : link.Sector;
            var button = new Button
            {
                Text = Loc.GetString("wf-sector-beacon-panel-jump", ("sector", name)),
                ToolTip = Loc.GetString(link.Closed ? "wf-sector-beacon-panel-link-closed" : "wf-sector-beacon-panel-link",
                    ("cost", (int) link.Cost), ("seconds", (int) link.Travel)),
                StyleClasses = { "ButtonSquare" },
                Disabled = zone!.Spooling != null || locked > 0 || zone.Capacity <= 0 || zone.Fuel < link.Cost,
            };
            var target = link.Sector;
            button.OnPressed += _ => JumpPressed?.Invoke(target);
            _links.AddChild(button);
            _links.AddChild(new Label
            {
                Text = Loc.GetString("wf-sector-beacon-panel-cost", ("cost", (int) link.Cost), ("seconds", (int) link.Travel)),
                HorizontalAlignment = HAlignment.Center,
                FontColorOverride = link.Closed ? Color.FromHex("#e06c5a") : Color.FromHex("#8a96a3"),
            });
        }

        _cancel.Visible = zone?.Spooling != null;
        _refuel.Visible = service != null;
        if (service != null)
        {
            _refuel.Text = Loc.GetString("wf-sector-beacon-panel-refuel", ("units", service.Units), ("cost", service.Cost));
            _refuel.ToolTip = Loc.GetString("wf-sector-beacon-panel-refuel-tooltip", ("station", service.Station));
            _refuel.Disabled = service.Units <= 0;
        }
    }
}
