using Content.Shared._WG.Settlers;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Settlers;

/// <summary>
/// One cell per resource: its icon and a count. With buttons the counts can be set, each up to its limit;
/// without, the row just shows them, as for a hand.
/// </summary>
public sealed class SettlersCounterRow : BoxContainer
{
    private readonly Label[] _labels = new Label[SettlersRules.ResourceCount];
    private readonly Button?[] _less = new Button?[SettlersRules.ResourceCount];
    private readonly Button?[] _more = new Button?[SettlersRules.ResourceCount];

    public readonly int[] Counts = new int[SettlersRules.ResourceCount];

    /// <summary>The most of each resource that may be set.</summary>
    public readonly int[] Limits = new int[SettlersRules.ResourceCount];

    public event Action? OnChanged;

    public SettlersCounterRow(bool editable)
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        Array.Fill(Limits, SettlersRules.BankStock);

        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            var index = resource;
            var cell = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                ToolTip = SettlersArt.ResourceName(resource),
                MouseFilter = MouseFilterMode.Pass,
            };

            cell.AddChild(new TextureRect
            {
                Texture = SettlersArt.ResourceTexture(resource),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                MinSize = new System.Numerics.Vector2(28f, 28f),
            });

            _labels[resource] = new Label { MinWidth = 18f, Align = Label.AlignMode.Center, VerticalAlignment = VAlignment.Center };

            if (editable)
            {
                var less = new Button { Text = "-", MinWidth = 18f };
                var more = new Button { Text = "+", MinWidth = 18f };
                less.OnPressed += _ => Set(index, Counts[index] - 1);
                more.OnPressed += _ => Set(index, Counts[index] + 1);
                _less[resource] = less;
                _more[resource] = more;
                cell.AddChild(less);
                cell.AddChild(_labels[resource]);
                cell.AddChild(more);
            }
            else
            {
                _labels[resource].StyleClasses.Add("LabelHeading");
                cell.AddChild(_labels[resource]);
            }

            AddChild(cell);
        }

        Refresh();
    }

    private void Set(int resource, int count)
    {
        count = Math.Clamp(count, 0, Limits[resource]);
        if (count == Counts[resource])
            return;

        Counts[resource] = count;
        Refresh();
        OnChanged?.Invoke();
    }

    /// <summary>Shows these counts.</summary>
    public void Show(int[] counts)
    {
        Array.Copy(counts, Counts, Counts.Length);
        Refresh();
    }

    public void Clear()
    {
        Array.Clear(Counts);
        Refresh();
    }

    /// <summary>Sets new limits and pulls any count over them back down.</summary>
    public void SetLimits(int[] limits)
    {
        Array.Copy(limits, Limits, Limits.Length);
        for (var resource = 0; resource < Counts.Length; resource++)
        {
            Counts[resource] = Math.Min(Counts[resource], Limits[resource]);
        }

        Refresh();
    }

    private void Refresh()
    {
        for (var resource = 0; resource < Counts.Length; resource++)
        {
            _labels[resource].Text = Counts[resource].ToString();
            if (_less[resource] is not { } less || _more[resource] is not { } more)
                continue;

            less.Disabled = Counts[resource] <= 0;
            more.Disabled = Counts[resource] >= Limits[resource];
        }
    }
}
