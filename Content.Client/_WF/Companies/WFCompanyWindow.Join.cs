using System.Linq;
using System.Numerics;
using Content.Client._WF.UserInterface.Controls;
using Content.Shared._WF.Companies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Companies;

public sealed partial class WFCompanyWindow
{
    private string _newName = string.Empty;
    private string _newTag = string.Empty;
    private Color _newColour = Color.FromHex("#7FD3C4");
    private Rope.Leaf _newDescription = Rope.Leaf.Empty;

    /// <summary>Every company: the player's own first with a way into its pages, then invitations, then the rest.</summary>
    private Control BuildCompanies(WFCompanyState state)
    {
        var page = Column(8);
        if (state.Own is { } own)
        {
            var mine = Column(4);
            mine.AddChild(new Label { Text = Text("your-company"), FontColorOverride = Color.FromHex("#E3B65C") });
            mine.AddChild(new Label { Text = $"[{own.Tag}] {own.Name}", FontColorOverride = own.Colour, StyleClasses = { "LabelHeading" } });
            mine.AddChild(new Label { Text = Text("your-rank", ("rank", RankTitle(own, own.YourProfile, own.YourRank))) });
            var open = Button("open-company", () => _tabs.CurrentTab = 1);
            open.AddStyleClass("ButtonAccept");
            mine.AddChild(open);
            page.AddChild(Card(Row(Logo(own.Id, own.LogoVersion, 64), mine)));
        }
        if (state.CharacterName == null)
            page.AddChild(Paragraph(Text("no-character-help")));
        else if (state.Own == null && !state.CanJoin)
            page.AddChild(Paragraph(Text("cannot-join")));
        if (state.Own == null && state.Invitations.Length > 0)
        {
            var invites = Column(6);
            invites.AddChild(Heading("invitations"));
            foreach (var company in state.Invitations)
            {
                invites.AddChild(Row(Logo(company.Id, company.LogoVersion, 32), new Label { Text = $"[{company.Tag}] {company.Name}", HorizontalExpand = true },
                    Button("accept", () => Send(WFCompanyAction.AcceptInvite, company.Id), !state.CanJoin),
                    Button("decline", () => Send(WFCompanyAction.DeclineInvite, company.Id))));
            }
            page.AddChild(Card(invites));
        }
        page.AddChild(Paragraph(Text(state.Directory.Length == 0 ? "directory-empty" : "directory-help")));
        var place = 0;
        foreach (var company in state.Directory)
        {
            place++;
            if (company.Id == state.Own?.Id)
                continue;
            var details = Column(4);
            var header = Row(new Label { Text = Text("directory-place", ("place", place)), FontColorOverride = Color.FromHex("#E3B65C") },
                new Label { Text = $"[{company.Tag}] {company.Name}", HorizontalExpand = true, FontColorOverride = company.Colour,
                StyleClasses = { "LabelHeading" } });
            if (company.Recruiting)
                header.AddChild(new Label { Text = Text("recruiting"), FontColorOverride = Color.FromHex("#7FD3C4") });
            details.AddChild(header);
            details.AddChild(new Label { Text = Text("directory-headline", ("count", company.Members), ("value", Credits(company.Value))),
                FontColorOverride = Color.FromHex("#C8C8C8") });
            details.AddChild(new Label { Text = Text("owned-by", ("name", company.OwnerName)) });
            if (company.Description.Length > 0)
                details.AddChild(Paragraph(company.Description));
            if (state.Own == null && state.CharacterName != null)
            {
                details.AddChild(state.Applications.Contains(company.Id)
                    ? Button("apply-cancel", () => Send(WFCompanyAction.CancelApplication, company.Id))
                    : Button("apply", () => Send(WFCompanyAction.Apply, company.Id), !state.CanJoin));
            }
            page.AddChild(Card(Row(Logo(company.Id, company.LogoVersion, 64), details)));
        }
        return page;
    }

    private Control BuildCreate(WFCompanyState state)
    {
        var create = Column(6);
        create.AddChild(Paragraph(Text("create-help")));
        var name = new LineEdit { Text = _newName, PlaceHolder = Text("name"), HorizontalExpand = true };
        name.OnTextChanged += _ => _newName = name.Text;
        var tag = new LineEdit { Text = _newTag, PlaceHolder = Text("tag"), MinWidth = 90 };
        tag.OnTextChanged += _ => _newTag = tag.Text;
        create.AddChild(Row(name, tag));
        var description = new TextEdit { MinHeight = 90, HorizontalExpand = true, TextRope = _newDescription };
        description.OnTextChanged += _ => _newDescription = new Rope.Leaf(Rope.Collapse(description.TextRope));
        create.AddChild(description);
        create.AddChild(new Label { Text = Text("colour") });
        var colour = new WolfgateColorPicker { Color = _newColour, HorizontalExpand = true, SelectorType = ColorSelectorSliders.ColorSelectorType.Hsv };
        colour.OnColorChanged += value => _newColour = value;
        create.AddChild(colour);
        create.AddChild(Paragraph(Text("create-logo-help")));
        create.AddChild(Button("create-button", () => Send(new WFCompanyRequest(WFCompanyAction.Create)
        {
            Name = _newName, Tag = _newTag, Description = Rope.Collapse(_newDescription), Colour = _newColour,
        }), !state.CanJoin));
        return Card(create);
    }

    private Control BuildLedger(WFCompanyDetail own)
    {
        var page = Column(4);
        page.AddChild(Paragraph(Text((own.YourPermissions & (WFCompanyPermissions.Withdraw | WFCompanyPermissions.EditRanks)) != 0
            ? "ledger-help" : "ledger-own-help")));
        if (own.Ledger.Length == 0)
            page.AddChild(Paragraph(Text("ledger-empty")));
        foreach (var entry in own.Ledger)
        {
            page.AddChild(Row(
                new Label { Text = entry.Time.ToLocalTime().ToString("g"), MinWidth = 150 },
                new Label { Text = Text("ledger-kind-" + entry.Kind.ToString().ToLowerInvariant()), MinWidth = 110 },
                new Label { Text = Credits(entry.Amount), MinWidth = 110, FontColorOverride = entry.Amount < 0 ? Color.OrangeRed : Color.LightGreen },
                new Label { Text = entry.Note.Length > 0 ? $"{entry.Actor}: {entry.Note}" : entry.Actor, HorizontalExpand = true, ClipText = true },
                new Label { Text = Credits(entry.BalanceAfter) }));
        }
        return page;
    }

    private Control BuildSettings(WFCompanyDetail own)
    {
        var page = Column(10);
        var art = Column(6);
        art.AddChild(Heading("logo"));
        var canDesign = Has(own, WFCompanyPermissions.Design) && !own.ArtLocked;
        art.AddChild(Row(Logo(own.Id, own.LogoVersion, 96), Paragraph(Text(own.ArtLocked ? "logo-locked" : canDesign ? "logo-help" : "logo-no-permission"))));
        if (canDesign)
        {
            art.AddChild(Button("logo-edit", () =>
            {
                var pixels = _system.GetLogoPixels(own.Id, own.LogoVersion);
                new WFCompanyLogoWindow(Text("logo-title"), WFCompanyPolicy.LogoSize, pixels,
                    logo => Send(new WFCompanyRequest(WFCompanyAction.SetLogo) { CompanyId = own.Id, Logo = logo })).OpenCentered();
            }));
        }
        page.AddChild(Card(art));

        var details = Column(6);
        details.AddChild(Heading("details"));
        var editable = Has(own, WFCompanyPermissions.EditDetails);
        var description = new TextEdit { MinHeight = 110, HorizontalExpand = true, TextRope = new Rope.Leaf(own.Description), Editable = editable };
        var colour = new WolfgateColorPicker { Color = own.Colour, HorizontalExpand = true, Visible = editable,
            SelectorType = ColorSelectorSliders.ColorSelectorType.Hsv };
        var recruiting = new CheckBox { Text = Text("recruiting-toggle"), Pressed = own.Recruiting, Disabled = !editable,
            ToolTip = Text("recruiting-toggle-help") };
        details.AddChild(description);
        details.AddChild(recruiting);
        if (editable)
        {
            details.AddChild(new Label { Text = Text("colour") });
            details.AddChild(colour);
            details.AddChild(Button("save", () => Send(new WFCompanyRequest(WFCompanyAction.EditDetails)
            {
                CompanyId = own.Id, Description = Rope.Collapse(description.TextRope), Recruiting = recruiting.Pressed, Colour = colour.Color,
            })));
        }
        page.AddChild(Card(details));
        if (!IsOwner(own))
            return page;

        var owner = Column(6);
        owner.AddChild(Heading("owner-settings"));
        var cut = Number(Text("cut"), own.Cut);
        owner.AddChild(Row(new Label { Text = Text("cut-label") }, cut, Button("save", () =>
            Send(new WFCompanyRequest(WFCompanyAction.SetCut) { CompanyId = own.Id, Amount = ParseNumber(cut) }))));
        owner.AddChild(Paragraph(Text("cut-help")));
        var name = new LineEdit { Text = own.Name, HorizontalExpand = true };
        var tag = new LineEdit { Text = own.Tag, MinWidth = 90 };
        owner.AddChild(Row(name, tag, Button("rename", () =>
            Send(new WFCompanyRequest(WFCompanyAction.Rename) { CompanyId = own.Id, Name = name.Text, Tag = tag.Text }))));
        owner.AddChild(Confirm("disband", () => Send(WFCompanyAction.Disband, own.Id)));
        owner.AddChild(Paragraph(Text("disband-help")));
        page.AddChild(Card(owner));
        return page;
    }

    /// <summary>The company's logo, or an empty square of the same size until it arrives.</summary>
    private Control Logo(int company, int version, int size)
    {
        var rect = new TextureRect
        {
            SetSize = new Vector2(size, size), Stretch = TextureRect.StretchMode.Scale, VerticalAlignment = VAlignment.Top,
            Texture = _system.GetLogo(company, version),
        };
        rect.TextureScale = Vector2.One;
        return rect;
    }
}
