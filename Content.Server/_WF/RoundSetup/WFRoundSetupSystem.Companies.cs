using System.Linq;
using Content.Server._WF.Companies;
using Content.Shared._WF.Companies;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private WFCompanySystem _companies = default!;

    /// <summary>Grids currently marked with a crew's company, so a change of ship or company unmarks the old ones.</summary>
    private readonly HashSet<EntityUid> _companyShips = new();

    /// <summary>Flies the crew under the host's company, or under none.</summary>
    private bool SetCrewCompany(Lobby lobby, ICommonSession host, int? companyId)
    {
        if (companyId is { } id && !_companies.CanFieldCrew(host, id))
            return false;
        lobby.CompanyId = companyId;
        SyncCompanyShips();
        return true;
    }

    /// <summary>The crew's company identity, dropping a company that no longer exists.</summary>
    private (string Name, string Tag, Color Colour)? CrewCompany(Lobby lobby)
    {
        if (lobby.CompanyId is not { } id)
            return null;
        if (_companies.CompanyIdentity(id) is { } identity)
            return identity;
        lobby.CompanyId = null;
        return null;
    }

    /// <summary>The crew name with its company tag, for postings.</summary>
    private string TaggedCrewName(Lobby lobby) =>
        CrewCompany(lobby) is { } company ? $"[{company.Tag}] {CrewName(lobby)}" : CrewName(lobby);

    /// <summary>Companies the session's character may run this crew under.</summary>
    private WFRoundSetupCompany[] FieldCompanies(ICommonSession session)
    {
        if (_companies.CompanyIdOf(session) is not { } id || !_companies.CanFieldCrew(session, id) ||
            _companies.CompanyIdentity(id) is not { } identity)
            return Array.Empty<WFRoundSetupCompany>();
        return new[] { new WFRoundSetupCompany(id, identity.Name, identity.Tag) };
    }

    /// <summary>The request and membership role id of a member who joined as their company rank.</summary>
    private const int RankRole = -1;

    /// <summary>Whether the session's character may join this crew as their company rank, in any crew that can field it.</summary>
    private bool JoinsAsRank(Lobby lobby, ICommonSession session)
    {
        if (_companies.CompanyIdOf(session) == null || _companies.RankFor(session) is not { } rank)
            return false;
        // A live member keeps their job; a new spawn needs the rank's job to be one this crew can field.
        return !CanEnter(session) || ScenarioJobs(lobby.Scenario, lobby.Faction).Contains(rank.Job) && IsJobAllowed(session, rank.Job);
    }

    /// <summary>Adds a company member as their rank: its base job and its title on their ID, without taking an opening.</summary>
    private bool AddRankMember(Lobby lobby, ICommonSession session)
    {
        if (FindLobby(session.UserId) != null || lobby.Members.Count >= WFRoundSetupPolicy.MaximumMembers || !JoinsAsRank(lobby, session) ||
            _companies.RankFor(session) is not { } rank)
            return false;
        if (!CanEnter(session))
        {
            if (!CanJoinLive(session) || !WFRoundSetupPolicy.AllowsLiveMembers(lobby.Scenario, lobby.Started))
                return false;
            RegisterLiveMember(lobby, session, null);
        }
        else
            RegisterMember(lobby, session, new Member { Name = session.Name, Job = rank.Job, Title = rank.Title });
        NotifyCrewJoined(lobby, session.UserId);
        return true;
    }

    /// <summary>How many crews flew under a company this round.</summary>
    public int CompanyCrewCount(int companyId) => _lobbies.Values.Count(lobby => lobby.Started && lobby.CompanyId == companyId);

    /// <summary>Marks every company crew's ship for IFF and unmarks ships that left it.</summary>
    private void SyncCompanyShips()
    {
        var marked = new HashSet<EntityUid>();
        foreach (var lobby in _lobbies.Values.Where(lobby => lobby.Started))
        {
            if (CrewCompany(lobby) is not { } company)
                continue;
            foreach (var grid in CrewGrids(lobby))
            {
                if (TerminatingOrDeleted(grid))
                    continue;
                marked.Add(grid);
                var ship = EnsureComp<WFCompanyShipComponent>(grid);
                if (ship.CompanyId == lobby.CompanyId && ship.Name == company.Name && ship.Tag == company.Tag && ship.Colour == company.Colour)
                    continue;
                ship.CompanyId = lobby.CompanyId!.Value;
                ship.Name = company.Name;
                ship.Tag = company.Tag;
                ship.Colour = company.Colour;
                Dirty(grid, ship);
            }
        }
        foreach (var grid in _companyShips.Where(grid => !marked.Contains(grid)))
        {
            if (!TerminatingOrDeleted(grid))
                RemComp<WFCompanyShipComponent>(grid);
        }
        _companyShips.Clear();
        _companyShips.UnionWith(marked);
    }
}
