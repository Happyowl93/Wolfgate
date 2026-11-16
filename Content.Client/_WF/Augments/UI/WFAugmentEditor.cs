using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client.Humanoid;
using Content.Shared._NF.Bank;
using Content.Shared._WF.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Augments.UI;

/// <summary>
/// The creator's Augments tab: the character as a doll whose parts are clicked to pick their limb, organs and implants,
/// each priced, with the loadout and what confirming it costs or pays back beside it.
/// </summary>
public sealed partial class WFAugmentEditor : BoxContainer
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color MutedColor = Color.FromHex("#A0A0A8");
    private static readonly Color DueColor = Color.FromHex("#E0A040");
    private static readonly Color RefundColor = Color.FromHex("#6CC070");
    private static readonly Color ShortColor = Color.FromHex("#E0605A");

    /// <summary>Raised with the new slot choices and implants when the player changes one.</summary>
    public event Action<Dictionary<WFAugmentSlot, WFAugmentChoice>, HashSet<ProtoId<WFAugmentPrototype>>>? OnAugmentsChanged;

    /// <summary>Raised when the player confirms the purchase, which saves the profile.</summary>
    public event Action? OnConfirm;

    private readonly WFAugmentDoll _doll;
    private readonly Label _hint;
    private readonly BoxContainer _part;
    private readonly BoxContainer _loadout;
    private readonly Label _buying;
    private readonly Label _refund;
    private readonly Label _total;
    private readonly Label _balance;
    private readonly Label _short;
    private readonly Button _confirm;

    private HumanoidCharacterProfile? _profile;
    private Dictionary<WFAugmentSlot, WFAugmentChoice> _choices = new();
    private HashSet<ProtoId<WFAugmentPrototype>> _implants = new();
    private Dictionary<WFAugmentSlot, ProtoId<TraitPrototype>> _claimed = new();
    private EntityUid _dollEntity;
    private string? _dollSpecies;

    public WFAugmentEditor()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;

        AddChild(new RichTextLabel
        {
            Text = Loc.GetString("wf-augments-intro", ("percent", WFAugmentRules.RefundPercent)),
            Margin = new Thickness(0, 0, 0, 6),
        });

        var columns = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 16 };
        AddChild(columns);

        // Loadout and totals.
        var left = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, MinSize = new Vector2(280, 0) };
        left.AddChild(Heading(Loc.GetString("wf-augments-loadout")));
        _loadout = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        left.AddChild(_loadout);
        left.AddChild(new Control { MinSize = new Vector2(0, 8) });
        _buying = new Label();
        _refund = new Label { FontColorOverride = RefundColor };
        _total = new Label();
        _balance = new Label { FontColorOverride = MutedColor };
        _short = new Label { FontColorOverride = ShortColor, Text = Loc.GetString("wf-augments-short") };
        left.AddChild(_buying);
        left.AddChild(_refund);
        left.AddChild(_total);
        left.AddChild(_balance);
        left.AddChild(_short);
        _confirm = new Button { Text = Loc.GetString("wf-augments-confirm"), Margin = new Thickness(0, 6, 0, 0) };
        _confirm.OnPressed += _ => OnConfirm?.Invoke();
        left.AddChild(_confirm);
        columns.AddChild(left);

        // The doll.
        var center = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 6 };
        _doll = new WFAugmentDoll { HorizontalAlignment = HAlignment.Center };
        _doll.OnRegionHovered += _ => UpdateHint();
        _doll.OnRegionPressed += _ => RebuildPart();
        center.AddChild(_doll);
        _hint = new Label { Align = Label.AlignMode.Center, FontColorOverride = MutedColor };
        center.AddChild(_hint);
        columns.AddChild(center);

        // The clicked part.
        _part = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true };
        columns.AddChild(_part);
    }

    /// <summary>Shows a profile: its doll, its loadout and the selected part's choices.</summary>
    public void SetProfile(HumanoidCharacterProfile profile)
    {
        _profile = profile;
        _choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>(profile.Augments);
        _implants = new HashSet<ProtoId<WFAugmentPrototype>>(profile.AugmentImplants);
        _claimed = WFAugmentRules.ClaimedSlots(profile.TraitPreferences, _proto);

        UpdateDoll();
        RebuildLoadout();
        RebuildPart();
        UpdateHint();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        UpdateDoll();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        DeleteDoll();
    }

    /// <summary>Spawns the species' doll if it changed, and dresses it in the profile with no clothes on.</summary>
    private void UpdateDoll()
    {
        if (_profile == null || !IsInsideTree)
            return;

        string species = _proto.HasIndex<SpeciesPrototype>(_profile.Species)
            ? (string) _profile.Species
            : (string) SharedHumanoidAppearanceSystem.DefaultSpecies;
        if (_dollSpecies != species || !_entities.EntityExists(_dollEntity))
        {
            DeleteDoll();
            _dollEntity = _entities.SpawnEntity(_proto.Index<SpeciesPrototype>(species).DollPrototype, MapCoordinates.Nullspace);
            _dollSpecies = species;
            _doll.SetEntity(_dollEntity);
        }

        _entities.System<HumanoidAppearanceSystem>().LoadProfile(_dollEntity, _profile);
    }

    private void DeleteDoll()
    {
        _doll.SetEntity(null);
        if (_dollEntity.IsValid())
            _entities.DeleteEntity(_dollEntity);
        _dollEntity = EntityUid.Invalid;
        _dollSpecies = null;
    }

    private void UpdateHint()
    {
        var region = _doll.Hovered ?? _doll.Selected;
        _hint.Text = region is { } shown ? RegionName(shown) : Loc.GetString("wf-augments-pick");
    }

    private void Changed()
    {
        OnAugmentsChanged?.Invoke(new Dictionary<WFAugmentSlot, WFAugmentChoice>(_choices),
            new HashSet<ProtoId<WFAugmentPrototype>>(_implants));
    }

    #region Loadout

    private void RebuildLoadout()
    {
        _loadout.RemoveAllChildren();
        if (_profile == null)
            return;

        var any = false;
        foreach (var (slot, choice) in _choices.OrderBy(c => WFAugmentRegions.Of(c.Key)).ThenBy(c => c.Key))
        {
            if (_proto.TryIndex(choice.Augment, out var augment))
            {
                _loadout.AddChild(LoadoutRow(SlotName(slot), augment));
                any = true;
            }
        }

        foreach (var id in _implants.OrderBy(i => i.Id))
        {
            if (_proto.TryIndex(id, out var implant))
            {
                _loadout.AddChild(LoadoutRow(SlotName(implant.Slot), implant));
                any = true;
            }
        }

        foreach (var sold in WFAugmentRules.Sold(_profile, _proto))
        {
            _loadout.AddChild(Row(Loc.GetString("wf-augments-sold", ("name", WFAugmentRules.Name(sold, _proto))),
                Negative(WFAugmentRules.Refund(sold)),
                RefundColor));
            any = true;
        }

        if (!any)
            _loadout.AddChild(new Label { Text = Loc.GetString("wf-augments-loadout-empty"), FontColorOverride = MutedColor });

        var due = WFAugmentRules.Due(_profile, _proto);
        var refund = WFAugmentRules.Sold(_profile, _proto).Sum(WFAugmentRules.Refund);
        var net = due - refund;
        _buying.Text = Loc.GetString("wf-augments-buying", ("amount", BankSystemExtensions.ToSpesoString(due)));
        _refund.Text = Loc.GetString("wf-augments-refund", ("amount", Negative(refund)));
        _refund.Visible = refund > 0;
        _total.Text = Loc.GetString("wf-augments-total", ("amount", Signed(net)));
        _total.FontColorOverride = net > 0 ? DueColor : net < 0 ? RefundColor : null;
        _balance.Text = Loc.GetString("wf-augments-balance",
            ("balance", BankSystemExtensions.ToSpesoString(_profile.BankBalance)),
            ("after", Signed(_profile.BankBalance - net)));
        _short.Visible = net > _profile.BankBalance;
        _confirm.Disabled = due == 0 && refund == 0;
    }

    private Control LoadoutRow(string where, WFAugmentPrototype augment)
    {
        var (price, color) = PriceText(augment);
        return Row($"{where}: {WFAugmentRules.Name(augment, _proto)}", price, color);
    }

    #endregion

    #region Part

    /// <summary>Lists the selected region's choices: each slot's options, a limb's style, and the part's implants.</summary>
    private void RebuildPart()
    {
        _part.RemoveAllChildren();
        UpdateHint();
        if (_profile == null || _doll.Selected is not { } region)
        {
            _part.AddChild(new Label { Text = Loc.GetString("wf-augments-pick"), FontColorOverride = MutedColor });
            return;
        }

        _part.AddChild(Heading(RegionName(region)));
        var any = false;
        foreach (var slot in WFAugmentRegions.Slots(region))
        {
            if (_claimed.TryGetValue(slot, out var trait))
            {
                _part.AddChild(SubHeading(SlotName(slot)));
                var name = _proto.TryIndex(trait, out var traitProto) ? Loc.GetString(traitProto.Name) : trait.Id;
                _part.AddChild(new Label { Text = Loc.GetString("wf-augments-claimed", ("trait", name)), FontColorOverride = MutedColor });
                any = true;
                continue;
            }

            var augments = WFAugmentRules.AugmentsFor(_profile.Species, slot, _proto, _factory);
            if (augments.Count == 0)
                continue;

            any = true;
            _part.AddChild(SubHeading(SlotName(slot)));
            AddSlotOptions(slot, augments);
        }

        var host = WFAugmentRegions.Host(region);
        var implants = WFAugmentRules.ImplantsFor(_profile.Species, host, _proto, _factory);
        if (implants.Count > 0)
        {
            any = true;
            AddImplants(host, implants);
        }

        if (!any)
            _part.AddChild(new Label { Text = Loc.GetString("wf-augments-nothing"), FontColorOverride = MutedColor });
    }

    private void AddSlotOptions(WFAugmentSlot slot, List<WFAugmentPrototype> augments)
    {
        _choices.TryGetValue(slot, out var current);
        var picked = augments.FirstOrDefault(a => a.ID == current.Augment.Id);

        _part.AddChild(OptionRow(Loc.GetString("wf-augments-none"), null, string.Empty, null, picked == null, false, () =>
        {
            _choices.Remove(slot);
            Changed();
        }));

        foreach (var augment in augments)
        {
            var (price, color) = PriceText(augment);
            _part.AddChild(OptionRow(WFAugmentRules.Name(augment, _proto),
                WFAugmentRules.Description(augment, _proto),
                price,
                color,
                augment == picked,
                false,
                () =>
                {
                    _choices[slot] = new WFAugmentChoice(augment.ID, current.Style);
                    Changed();
                }));
        }

        if (picked is not { Styled: true })
            return;

        var styles = WFAugmentRules.StylesFor(slot, _proto);
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6, Margin = new Thickness(12, 0, 0, 0) };
        row.AddChild(new Label { Text = Loc.GetString("wf-augments-style"), MinSize = new Vector2(60, 0), FontColorOverride = MutedColor });
        var styleButton = new OptionButton { HorizontalExpand = true };
        for (var i = 0; i < styles.Count; i++)
        {
            styleButton.AddItem(Loc.GetString(styles[i].Name), i);
            if (styles[i].ID == current.Style?.Id)
                styleButton.SelectId(i);
        }
        styleButton.OnItemSelected += args =>
        {
            _choices[slot] = new WFAugmentChoice(picked.ID, styles[args.Id].ID);
            Changed();
        };
        row.AddChild(styleButton);
        _part.AddChild(row);
    }

    /// <summary>The part's implants, each toggled on or off, greyed when the part can't take it.</summary>
    private void AddImplants(WFAugmentSlot host, List<WFAugmentPrototype> implants)
    {
        var slots = WFAugmentRules.ImplantSlots(host, _choices, _claimed, _proto);
        var synthetic = WFAugmentRules.Synthetic(_profile!.Species, host, _choices, _claimed, _proto, _factory);
        var used = implants.Count(i => _implants.Contains(i.ID));
        _part.AddChild(SubHeading(Loc.GetString("wf-augments-implants", ("used", used), ("slots", slots))));

        foreach (var implant in implants)
        {
            var installed = _implants.Contains(implant.ID);
            string? blocked = null;
            if (!WFAugmentRules.HostFits(implant, synthetic))
                blocked = Loc.GetString(implant.Host == WFAugmentHost.Synthetic ? "wf-augments-needs-synthetic" : "wf-augments-needs-organic");
            else if (!installed && used >= slots)
                blocked = Loc.GetString("wf-augments-no-room");

            var description = WFAugmentRules.Description(implant, _proto);
            if (blocked != null)
                description = string.IsNullOrEmpty(description) ? blocked : $"{blocked}\n{description}";

            var (price, color) = PriceText(implant);
            _part.AddChild(OptionRow(WFAugmentRules.Name(implant, _proto),
                description,
                price,
                color,
                installed,
                blocked != null && !installed,
                () =>
                {
                    if (!_implants.Remove(implant.ID))
                        _implants.Add(implant.ID);
                    Changed();
                }));
        }
    }

    /// <summary>A pickable option: a toggle with its name and the price beside it, greyed out when disabled.</summary>
    private static Control OptionRow(string name, string? tooltip, string price, Color? priceColor, bool pressed, bool disabled, Action onPressed)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        var button = new Button
        {
            Text = name,
            ToggleMode = true,
            Pressed = pressed,
            Disabled = disabled,
            HorizontalExpand = true,
            ToolTip = string.IsNullOrEmpty(tooltip) ? null : tooltip,
        };
        button.Label.HorizontalAlignment = HAlignment.Left;
        button.OnPressed += _ => onPressed();
        row.AddChild(button);
        row.AddChild(new Label
        {
            Text = price,
            MinSize = new Vector2(110, 0),
            Align = Label.AlignMode.Right,
            VAlign = Label.VAlignMode.Center,
            FontColorOverride = disabled ? MutedColor.WithAlpha(0.5f) : priceColor ?? MutedColor,
        });
        return row;
    }

    #endregion

    #region Text

    /// <summary>What an augment costs this character: free, owned, or its price.</summary>
    private (string Text, Color Color) PriceText(WFAugmentPrototype augment)
    {
        if (augment.Price <= 0)
            return (Loc.GetString("wf-augments-free"), MutedColor);
        if (_profile != null && _profile.AugmentUnlocks.Contains(augment.ID))
            return (Loc.GetString("wf-augments-owned"), RefundColor);
        return (BankSystemExtensions.ToSpesoString(augment.Price), DueColor);
    }

    private static Control Row(string text, string amount, Color amountColor)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        row.AddChild(new Label { Text = text, HorizontalExpand = true, ClipText = true });
        row.AddChild(new Label { Text = amount, FontColorOverride = amountColor });
        return row;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.StyleClasses.Add("LabelHeading");
        return label;
    }

    private static Label SubHeading(string text)
    {
        return new Label { Text = text, FontColorOverride = MutedColor, Margin = new Thickness(0, 6, 0, 0) };
    }

    private static string Negative(int amount)
    {
        return "-" + BankSystemExtensions.ToSpesoString(amount);
    }

    private static string Signed(int amount)
    {
        return amount < 0 ? Negative(-amount) : BankSystemExtensions.ToSpesoString(amount);
    }

    private static string RegionName(WFAugmentRegion region)
    {
        return region == WFAugmentRegion.Torso
            ? Loc.GetString("wf-augments-torso")
            : SlotName(WFAugmentRegions.Host(region));
    }

    /// <summary>The slot's name as players read it.</summary>
    internal static string SlotName(WFAugmentSlot slot)
    {
        return Loc.GetString("wf-augment-slot-" + SlotKey(slot));
    }

    /// <summary>The slot's name in kebab case, as its Fluent key ends: LeftHand is left-hand.</summary>
    private static string SlotKey(WFAugmentSlot slot)
    {
        var name = slot.ToString();
        var key = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                key.Append('-');
            key.Append(char.ToLowerInvariant(name[i]));
        }

        return key.ToString();
    }

    #endregion
}
