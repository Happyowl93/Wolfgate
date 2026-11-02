using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client._WF.Companies;
using Content.Client.Lobby;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared._WF.Companies;
using Content.Shared._WF.FactionLock;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Utility;

namespace Content.Client._WF.FactionLock;

/// <summary>Locks the jobs the server would refuse a character, using the company snapshot because profiles only refresh on lobby join.</summary>
public sealed partial class WFFactionLockClientSystem : EntitySystem
{
    [Dependency] private IClientPreferencesManager _preferences = default!;
    [Dependency] private JobRequirementsManager _requirements = default!;
    [Dependency] private WFCompanyClientSystem _companies = default!;

    /// <summary>The last snapshot with companies loaded; one taken while they reload knows no memberships.</summary>
    private WFCompanyState? _state;

    /// <summary>The locks last seen, so job lists only rebuild when one changes.</summary>
    private string _seen = string.Empty;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TickerJoinLobbyEvent>(OnJoinLobby);
        _companies.Received += OnCompanyState;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _companies.Received -= OnCompanyState;
    }

    private void OnJoinLobby(TickerJoinLobbyEvent args) => _companies.Send(new WFCompanyRequest(WFCompanyAction.Refresh));

    private void OnCompanyState(WFCompanyState state)
    {
        if (!state.Loaded)
            return;
        _state = state;
        var members = state.Characters.Select(character => character.Slot.ToString()).OrderBy(slot => slot);
        var played = state.PlayedFactions.Select(pair => $"{pair.Key}={string.Join(',', pair.Value)}").OrderBy(entry => entry);
        var seen = string.Join(';', members.Concat(played));
        if (seen == _seen)
            return;
        _seen = seen;
        _requirements.FactionLocksChanged();
    }

    /// <summary>Whether this saved character may take the job, with the reason shown on the job list if not.</summary>
    public bool IsAllowed(JobPrototype job, HumanoidCharacterProfile? profile, [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = null;
        if (profile == null || WFFactionLockPolicy.FactionOf(job.AssignedCompany.Id) is not { } wanted ||
            _state is not { } state || _preferences.Preferences is not { } preferences ||
            !preferences.TryIndexOfCharacter(profile, out var slot))
            return true;
        if (state.Characters.Any(character => character.Slot == slot))
        {
            reason = FormattedMessage.FromUnformatted(Loc.GetString("wf-faction-lock-company"));
            return false;
        }
        if (!state.PlayedFactions.TryGetValue(slot, out var played) || WFFactionLockPolicy.Barring(played, wanted) is not { } barring)
            return true;
        reason = FormattedMessage.FromUnformatted(Loc.GetString("wf-faction-lock-played", ("faction", barring)));
        return false;
    }
}
