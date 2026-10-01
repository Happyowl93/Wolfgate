using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Server._Mono.Company;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.Players.JobWhitelist;
using Content.Shared._Mono.CCVar;
using Content.Shared._Mono.Company;
using Content.Shared.CCVar;
using Content.Shared.Ghost.Roles;
using Content.Shared.Roles;
using Content.Shared.Symphony;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Symphony;

/// <summary>
/// The whitelisted roles this build has, and a way to make the game re-read a player's rows, for the SSymphony panel.
/// The panel maps Discord roles onto the admin table, role_whitelists and company_members and writes those tables
/// itself; it asks here what there is to map, and after a write it asks the game to bring its caches into line for
/// anyone online, since admin and job rows are read once at connect and company membership once at boot. Both calls
/// take the admin API token, the way /admin does. Nothing upstream is touched: the handler hangs off the status host
/// on its own.
/// </summary>
public sealed partial class SymphonyRolesSystem : EntitySystem
{
    /// <summary>Status host path that answers GET with the whitelisted roles.</summary>
    public const string RolesPath = "/symphony/roles";

    /// <summary>Status host path that takes a POST to re-read players' rows.</summary>
    public const string RefreshPath = "/symphony/roles/refresh";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private CompanyManager _companies = default!;
    [Dependency] private IAdminManager _adminManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        // The status host keeps a handler for the life of the process; systems are started once, so this is too.
        _statusHost.AddHandler(HandleAsync);
    }

    // Runs on the status host's thread. Anything that touches game state hops to the main thread below.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != RolesPath && path != RefreshPath)
            return false;

        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;

        if (path == RolesPath && context.RequestMethod == HttpMethod.Get)
            await RespondCatalogue(context);
        else if (path == RefreshPath && context.RequestMethod == HttpMethod.Post)
            await RespondRefresh(context);
        else
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);

        return true;
    }

    /// <summary>
    /// Every whitelisted job, ghost role and company, with the names players see, and the switches that decide
    /// whether a whitelist means anything right now.
    /// </summary>
    private async Task RespondCatalogue(IStatusHandlerContext context)
    {
        var catalogue = await _tasks.OnMainThread(() => new Catalogue
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            RoleWhitelistEnabled = _cfg.GetCVar(CCVars.GameRoleWhitelist),
            CompanyWhitelistEnabled = _cfg.GetCVar(MonoCVars.CompanyWhitelist),
            DynamicRoles = new DynamicRoles
            {
                Enabled = _cfg.GetCVar(CCVars.DynamicRolesEnabled),
                Threshold = _cfg.GetCVar(CCVars.DynamicRolesPlayerThreshold),
            },
            Jobs = _prototypes.EnumeratePrototypes<JobPrototype>()
                .Where(job => job.Whitelisted)
                .Select(job => new Entry(job.ID, _loc.GetString(job.Name)))
                .OrderBy(e => e.Name)
                .ToList(),
            GhostRoles = _prototypes.EnumeratePrototypes<GhostRolePrototype>()
                .Where(role => role.Whitelisted)
                .Select(role => new Entry(role.ID, _loc.GetString(role.Name)))
                .OrderBy(e => e.Name)
                .ToList(),
            Companies = _prototypes.EnumeratePrototypes<CompanyPrototype>()
                .Where(company => company.Whitelisted && !company.Disabled)
                .Select(company => new Entry(company.ID, company.Name))
                .OrderBy(e => e.Name)
                .ToList(),
        });

        await context.RespondJsonAsync(catalogue);
    }

    /// <summary>
    /// Re-reads the role rows of the named players, or of everyone, and brings the game's caches into line for
    /// those online. The database is the truth; the panel has just written it.
    /// </summary>
    private async Task RespondRefresh(IStatusHandlerContext context)
    {
        RefreshBody? body = null;
        try
        {
            body = await context.RequestBodyJsonAsync<RefreshBody>();
        }
        catch (Exception)
        {
            // No body, or not JSON: everyone online, which is the safe reading.
        }

        var wanted = new HashSet<NetUserId>();
        foreach (var raw in body?.Users ?? new List<string>())
        {
            if (Guid.TryParse(raw, out var guid))
                wanted.Add(new NetUserId(guid));
        }

        // Who is online is main-thread state; the database reads are not. Collect first, compare after.
        var (online, jobs, ghostRoles, companies) = await _tasks.OnMainThread(() => (
            _players.Sessions
                .Where(s => s.Status == SessionStatus.InGame)
                .Where(s => wanted.Count == 0 || wanted.Contains(s.UserId))
                .Select(s => s.UserId)
                .ToList(),
            _prototypes.EnumeratePrototypes<JobPrototype>().Where(j => j.Whitelisted).Select(j => j.ID).ToList(),
            _prototypes.EnumeratePrototypes<GhostRolePrototype>().Where(r => r.Whitelisted).Select(r => r.ID).ToList(),
            _prototypes.EnumeratePrototypes<CompanyPrototype>().Where(c => c.Whitelisted).Select(c => c.ID).ToList()));

        var named = wanted.Count > 0;
        var changed = 0;
        foreach (var user in online)
        {
            // A row in the whitelist table opens every whitelisted role, so the per-role rows are moot for them.
            var global = await _db.GetWhitelistStatusAsync(user);
            var adminRow = await _db.GetAdminDataForAsync(user) != null;
            var rows = (await _db.GetJobWhitelists(user.UserId)).ToHashSet();
            var memberOf = new HashSet<string>();
            foreach (var company in companies)
            {
                if (await _db.GetCompanyMember(company, user.UserId) != null)
                    memberOf.Add(company);
            }

            changed += await _tasks.OnMainThread(() => Reconcile(user, named, adminRow, global, rows, memberOf, jobs, ghostRoles, companies));
        }

        if (changed > 0)
            Log.Info($"Symphony refresh: {changed} role cache changes across {online.Count} players online");

        await context.RespondJsonAsync(new RefreshResponse { Refreshed = online.Count, Changed = changed });
    }

    /// <summary>
    /// The managers' own add and remove calls update their cache, write the database idempotently and tell the
    /// client, which is exactly the path the in-game commands take. Only the differences are pushed through them.
    /// </summary>
    private int Reconcile(
        NetUserId user,
        bool named,
        bool adminRow,
        bool global,
        HashSet<string> rows,
        HashSet<string> memberOf,
        List<string> jobs,
        List<string> ghostRoles,
        List<string> companies)
    {
        if (!_players.TryGetSessionById(user, out var session) || session.Status != SessionStatus.InGame)
            return 0;

        var changes = 0;

        // The admin manager's own reload takes the row as it now stands: rank, flags, or gone. An account the
        // panel named just had its row changed, so it is reloaded outright. For everyone else only a row
        // appearing or going is certain enough, since a reload tells an active admin their permissions changed
        // whether they did or not.
        if (named || adminRow != _adminManager.IsAdmin(session, includeDeAdmin: true))
        {
            _adminManager.ReloadAdmin(session);
            changes++;
        }

        if (!global)
        {
            foreach (var job in jobs)
            {
                var id = new ProtoId<JobPrototype>(job);
                var should = rows.Contains(job);
                if (should == _jobWhitelist.IsWhitelisted(user, id))
                    continue;
                if (should)
                    _jobWhitelist.AddWhitelist(user, id);
                else
                    _jobWhitelist.RemoveWhitelist(user, id);
                changes++;
            }

            foreach (var role in ghostRoles)
            {
                var id = new ProtoId<GhostRolePrototype>(role);
                var should = rows.Contains(role);
                if (should == _jobWhitelist.IsWhitelisted(user, id))
                    continue;
                if (should)
                    _jobWhitelist.AddWhitelist(user, id);
                else
                    _jobWhitelist.RemoveWhitelist(user, id);
                changes++;
            }
        }

        foreach (var company in companies)
        {
            var id = new ProtoId<CompanyPrototype>(company);
            var should = memberOf.Contains(company);
            if (should == _companies.IsMember(user, id))
                continue;
            if (should)
                _companies.AddMember(user, id);
            else
                _ = _companies.RemoveMember(user, id);
            changes++;
        }

        return changes;
    }

    private sealed class Catalogue
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("role_whitelist_enabled")] public bool RoleWhitelistEnabled { get; set; }
        [JsonPropertyName("company_whitelist_enabled")] public bool CompanyWhitelistEnabled { get; set; }
        [JsonPropertyName("dynamic_roles")] public DynamicRoles DynamicRoles { get; set; } = new();
        [JsonPropertyName("jobs")] public List<Entry> Jobs { get; set; } = new();
        [JsonPropertyName("ghost_roles")] public List<Entry> GhostRoles { get; set; } = new();
        [JsonPropertyName("companies")] public List<Entry> Companies { get; set; } = new();
    }

    private sealed class DynamicRoles
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("threshold")] public int Threshold { get; set; }
    }

    private sealed record Entry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed class RefreshBody
    {
        [JsonPropertyName("users")] public List<string>? Users { get; set; }
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("refreshed")] public int Refreshed { get; set; }
        [JsonPropertyName("changed")] public int Changed { get; set; }
    }
}
