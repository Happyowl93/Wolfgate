using System.Linq;
using Content.Shared._WF.Companies;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Companies;

/// <summary>A Join the round as your rank button for the late-join window, shown while the member can use it.</summary>
public sealed class WFCompanyJoinAsRankBar : BoxContainer
{
    private readonly WFCompanyClientSystem _system;
    private readonly Button _join = new() { HorizontalExpand = true, StyleClasses = { "ButtonAccept" } };

    public WFCompanyJoinAsRankBar()
    {
        Orientation = LayoutOrientation.Horizontal;
        Margin = new Thickness(6, 0, 6, 6);
        Visible = false;
        _system = IoCManager.Resolve<IEntityManager>().System<WFCompanyClientSystem>();
        _join.OnPressed += _ =>
        {
            if (_system.State?.Own is { } own)
                _system.Send(new WFCompanyRequest(WFCompanyAction.JoinAsRank) { CompanyId = own.Id });
        };
        AddChild(_join);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _system.Received += Update;
        if (_system.State is { } state)
            Update(state);
        _system.Send(new WFCompanyRequest(WFCompanyAction.Refresh));
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _system.Received -= Update;
    }

    private void Update(WFCompanyState state)
    {
        Visible = state.CanJoinRound && state.Own != null;
        if (state.Own is not { } own)
            return;
        var rank = own.YourProfile == own.OwnerProfile ? own.OwnerRank : own.Ranks.FirstOrDefault(entry => entry.Id == own.YourRank);
        _join.Text = Loc.GetString("wf-company-join-as-rank-company", ("rank", rank?.Title ?? string.Empty), ("company", own.Name));
    }
}
