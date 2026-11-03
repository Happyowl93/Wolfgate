using System.Linq;
using System.Threading.Tasks;
using Content.Server._Mono.Persistence;
using Content.Server._WF.Companies;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Events;
using Content.Shared._WF.FactionLock;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.FactionLock;

/// <summary>Binds a character to the faction they spawn as, and keeps them out of opposing factions' jobs and of player companies.</summary>
public sealed partial class WFFactionLockSystem : EntitySystem
{
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private PersistentProfileSystem _persistence = default!;
    [Dependency] private WFCompanySystem _companies = default!;

    /// <summary>Spawns to bind on the next update: Crew Setup can still roll a finished spawn back.</summary>
    private readonly List<(ICommonSession Session, string Flag)> _pendingBinds = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IsJobAllowedEvent>(OnIsJobAllowed);
        SubscribeLocalEvent<GetDisallowedJobsEvent>(OnGetDisallowedJobs);
        SubscribeLocalEvent<StationJobsGetCandidatesEvent>(OnGetCandidates);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawned);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        foreach (var (session, flag) in _pendingBinds)
        {
            if (_ticker.UserHasJoinedGame(session) && session.AttachedEntity is { } body && _persistence.AddFlag(body, flag))
                _companies.OnFactionBound(session);
        }
        _pendingBinds.Clear();
    }

    /// <summary>Whether the session's selected character can't take a job: an opposing faction's, or any faction's while in a company.</summary>
    public bool IsBarred(ICommonSession session, ProtoId<JobPrototype> jobId)
    {
        if (WFFactionLockPolicy.IsOpen(jobId.Id) ||
            !_prototypes.TryIndex(jobId, out var job) || WFFactionLockPolicy.FactionOf(job.AssignedCompany.Id) is not { } wanted ||
            _preferences.GetPreferencesOrNull(session.UserId)?.SelectedCharacter is not HumanoidCharacterProfile profile)
            return false;
        return _companies.CompanyIdOf(session) != null ||
               WFFactionLockPolicy.Barring(WFFactionLockPolicy.Played(profile.Flags), wanted) != null;
    }

    private void OnIsJobAllowed(ref IsJobAllowedEvent args)
    {
        if (IsBarred(args.Player, args.JobId))
            args.Cancelled = true;
    }

    private void OnGetDisallowedJobs(ref GetDisallowedJobsEvent args)
    {
        foreach (var job in _prototypes.EnumeratePrototypes<JobPrototype>())
        {
            if (IsBarred(args.Player, job.ID))
                args.Jobs.Add(job.ID);
        }
    }

    private void OnGetCandidates(ref StationJobsGetCandidatesEvent args)
    {
        if (!_players.TryGetSessionById(args.Player, out var session))
            return;
        args.Jobs.RemoveAll(job => IsBarred(session, job));
    }

    private void OnSpawned(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId != null && !WFFactionLockPolicy.IsOpen(args.JobId) && _prototypes.TryIndex<JobPrototype>(args.JobId, out var job) &&
            WFFactionLockPolicy.FactionOf(job.AssignedCompany.Id) is { } faction)
            _pendingBinds.Add((args.Player, WFFactionLockPolicy.Flag(faction)));
    }

    /// <summary>A connected player's characters, if they have loaded.</summary>
    public PlayerPreferences? GetCachedPreferences(NetUserId user) =>
        _preferences.TryGetCachedPreferences(user, out var preferences) ? preferences : null;

    /// <summary>A player's characters, read from the database if they aren't connected.</summary>
    public async Task<PlayerPreferences?> GetPreferences(NetUserId user) =>
        GetCachedPreferences(user) ?? await _db.GetPlayerPreferencesAsync(user, default);

    /// <summary>Clears a character's played factions, online or off; false if they have none.</summary>
    public async Task<bool> Unlock(NetUserId user, int slot)
    {
        if (await GetPreferences(user) is not { } preferences || preferences.Characters.GetValueOrDefault(slot) is not HumanoidCharacterProfile profile ||
            !WFFactionLockPolicy.Played(profile.Flags).Any())
            return false;
        var unlocked = profile.WithPersistentData(WFFactionLockPolicy.WithoutFactions(profile.Flags), profile.Components, profile.Items);
        if (GetCachedPreferences(user) == null)
        {
            await _db.SaveCharacterSlotAsync(user, unlocked, slot);
            return true;
        }
        // SetProfile selects the slot it saves, so select the player's own character back before anything can read it.
        var saves = new List<Task> { _preferences.SetProfile(user, slot, unlocked) };
        if (slot != preferences.SelectedCharacterIndex)
            saves.Add(_preferences.SetProfile(user, preferences.SelectedCharacterIndex, preferences.SelectedCharacter));
        await Task.WhenAll(saves);
        if (_players.TryGetSessionById(user, out var session))
            _companies.RefreshState(session);
        return true;
    }
}
