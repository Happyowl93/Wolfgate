using System.Linq;
using System.Numerics;
using Content.Shared._WF.Companies;
using Content.Shared.Clothing;
using Content.Shared.Preferences.Loadouts;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Companies;

/// <summary>Picks the loadouts a rank is issued at each spawn, from those its base job can take.</summary>
public sealed class WFCompanyLoadoutWindow : DefaultWindow
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;

    /// <summary>Search results shown at once; typing narrows the rest.</summary>
    private const int ShownResults = 60;

    private readonly List<LoadoutPrototype> _allowed;
    private readonly Dictionary<string, string> _names = new();
    private readonly List<string> _picked;
    private readonly LineEdit _search = new() { HorizontalExpand = true, PlaceHolder = Loc.GetString("wf-company-loadout-search") };
    private readonly BoxContainer _results = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
    private readonly BoxContainer _selected = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
    private readonly Label _total = new();
    private readonly Action<string[]> _save;

    public WFCompanyLoadoutWindow(string title, string job, string[] current, Action<string[]> save)
    {
        IoCManager.InjectDependencies(this);
        Title = title;
        MinSize = new Vector2(640, 440);
        SetSize = new Vector2(760, 540);
        _save = save;
        var loadouts = _entities.System<LoadoutSystem>();
        _allowed = WFCompanyLoadouts.Allowed(_prototypes, job);
        foreach (var loadout in _allowed)
            _names[loadout.ID] = loadouts.GetName(loadout);
        _allowed.Sort((a, b) => string.Compare(_names[a.ID], _names[b.ID], StringComparison.CurrentCultureIgnoreCase));
        _picked = current.Where(_names.ContainsKey).Distinct().ToList();

        var left = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        left.AddChild(_search);
        left.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { _results } });
        var right = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        right.AddChild(new Label { Text = Loc.GetString("wf-company-loadout-picked"), StyleClasses = { "LabelHeading" } });
        right.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { _selected } });
        right.AddChild(_total);
        var saveButton = new Button { Text = Loc.GetString("wf-company-loadout-save") };
        saveButton.AddStyleClass("ButtonAccept");
        saveButton.OnPressed += _ =>
        {
            _save(_picked.ToArray());
            Close();
        };
        right.AddChild(saveButton);

        var columns = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 12, VerticalExpand = true };
        columns.AddChild(left);
        columns.AddChild(right);
        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(8) };
        var help = new RichTextLabel { HorizontalExpand = true };
        help.SetMessage(Loc.GetString(_allowed.Count == 0 ? "wf-company-loadout-none" : "wf-company-loadout-help"));
        layout.AddChild(help);
        layout.AddChild(columns);
        Contents.AddChild(layout);

        _search.OnTextChanged += _ => Rebuild();
        Rebuild();
    }

    private void Rebuild()
    {
        _results.RemoveAllChildren();
        var full = _picked.Count >= WFCompanyPolicy.MaximumRankLoadouts;
        var query = _search.Text.Trim();
        foreach (var loadout in _allowed.Where(entry => query.Length == 0 ||
                     _names[entry.ID].Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(ShownResults))
        {
            var button = Entry(loadout);
            button.Disabled = full || _picked.Contains(loadout.ID);
            button.OnPressed += _ =>
            {
                _picked.Add(loadout.ID);
                Rebuild();
            };
            _results.AddChild(button);
        }

        _selected.RemoveAllChildren();
        foreach (var id in _picked)
        {
            var loadout = _allowed.First(entry => entry.ID == id);
            var button = Entry(loadout);
            button.ToolTip = Loc.GetString("wf-company-loadout-remove");
            button.OnPressed += _ =>
            {
                _picked.Remove(id);
                Rebuild();
            };
            _selected.AddChild(button);
        }
        _total.Text = Loc.GetString("wf-company-loadout-total", ("count", _picked.Count), ("max", WFCompanyPolicy.MaximumRankLoadouts),
            ("cost", Cost(_prototypes, _picked)));
    }

    /// <summary>A loadout's icon, name and price as one button.</summary>
    private Button Entry(LoadoutPrototype loadout)
    {
        var view = new EntityPrototypeView { SetSize = new Vector2(32, 32), Scale = new Vector2(1, 1) };
        view.SetPrototype(_entities.System<LoadoutSystem>().GetFirstOrNull(loadout));
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        row.AddChild(view);
        row.AddChild(new Label { Text = _names[loadout.ID], HorizontalExpand = true, ClipText = true, VerticalAlignment = VAlignment.Center });
        row.AddChild(new Label { Text = Loc.GetString("wf-company-credits", ("amount", loadout.Price)), VerticalAlignment = VAlignment.Center });
        return new Button { HorizontalExpand = true, Children = { row } };
    }

    /// <summary>What a rank's loadout costs at each spawn.</summary>
    public static long Cost(IPrototypeManager prototypes, IEnumerable<string> loadouts) =>
        loadouts.Sum(id => prototypes.TryIndex<LoadoutPrototype>(id, out var loadout) ? (long) Math.Max(0, loadout.Price) : 0);
}
