using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.Companies;
using Content.Client._WF.FactionLock;
using Content.Client.Lobby;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Companies;
using Content.Server._WF.FactionLock;
using Content.Server._WF.RoundSetup;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Voting.Managers;
using Content.Shared._WF.Companies;
using Content.Shared._WF.FactionLock;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.FactionLock;

/// <summary>One faction per character, and factions and player companies exclude each other.</summary>
[TestFixture]
[TestOf(typeof(WFFactionLockSystem))]
public sealed class WFFactionLockTest
{
    private static readonly ProtoId<JobPrototype> Pirate = "Pirate";
    private static readonly ProtoId<JobPrototype> Sheriff = "Sheriff";
    private static readonly ProtoId<JobPrototype> Deputy = "Deputy";

    /// <summary>A job's company is either a faction or deliberately neutral, so a new faction can't slip past the lock.</summary>
    [Test]
    public async Task EveryJobCompanyIsAFactionOrNeutral()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var job in prototypes.EnumeratePrototypes<JobPrototype>())
            {
                var company = job.AssignedCompany.Id;
                Assert.That(WFFactionLockPolicy.FactionOf(company) != null || company is "None" or "Colonial", Is.True,
                    $"{job.ID} flies {company}, which WFFactionLockPolicy.FactionOf doesn't know.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Spawning in a faction job binds that character alone, closes the other factions and companies, and offers neutral jobs still.</summary>
    [Test]
    public async Task SpawningBindsTheCharacterNotThePlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var locks = server.System<WFFactionLockSystem>();
        var companies = server.System<WFCompanySystem>();
        var clientCompanies = pair.Client.System<WFCompanyClientSystem>();
        var clientLocks = pair.Client.System<WFFactionLockClientSystem>();
        var clientPreferences = pair.Client.ResolveDependency<IClientPreferencesManager>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var player = pair.Player!;
        var preset = ticker.Preset;
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var slot = preferences.GetPreferences(player.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(player.UserId).SelectedCharacter;
        var otherSlot = slot + 1;
        var spawnStation = EntityUid.Invalid;
        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
                foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                    vote.Cancel();
                preferences.SetProfile(player.UserId, otherSlot, original.WithName("Bo Other")).Wait();
                preferences.SetProfile(player.UserId, slot, original.WithName("Ada Sworn").WithBankBalance(1000000)).Wait();
                Assert.That(locks.IsBarred(player, "Pirate"), Is.False, "Nothing is barred before a faction is played.");
            });
            await FlushCompanies(pair, companies);
            await server.WaitPost(() =>
            {
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
                var jobs = server.System<StationJobsSystem>();
                var setup = server.System<WFRoundSetupSystem>();
                var query = server.EntMan.EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
                while (query.MoveNext(out var station, out _, out _))
                {
                    if (!setup.IsStandardStation(station))
                        continue;
                    jobs.TrySetJobSlot(station, "Deputy", 4, createSlot: true);
                    spawnStation = station;
                }
            });
            // Crew Setup sends a spawn back to the lobby when the ship can't be delivered; that character never played.
            await server.WaitPost(() =>
            {
                ticker.MakeJoinGame(player, spawnStation, "Deputy");
                Assert.That(ticker.UserHasJoinedGame(player), Is.True);
                ticker.Respawn(player);
            });
            await pair.RunTicksSync(5);
            await FlushCompanies(pair, companies);
            await server.WaitAssertion(() => Assert.That(locks.IsBarred(player, "Pirate"), Is.False, "A rolled-back spawn binds nobody."));

            await server.WaitPost(() => ticker.MakeJoinGame(player, spawnStation, "Deputy"));
            await pair.RunTicksSync(pair.SecondsToTicks(1.5f));

            await server.WaitAssertion(() =>
            {
                Assert.That(ticker.UserHasJoinedGame(player), Is.True, "The character spawned as a Deputy.");
                var profile = (HumanoidCharacterProfile) preferences.GetPreferences(player.UserId).Characters[slot];
                Assert.That(profile.Flags, Does.Contain(WFFactionLockPolicy.Flag("TSF")));
                Assert.That(profile.Name, Is.EqualTo("Ada Sworn"), "Binding kept the character.");
                Assert.That(locks.IsBarred(player, "Pirate"), Is.True, "PDV is barred.");
                Assert.That(locks.IsBarred(player, "MMCEmployee"), Is.True, "MMC is barred.");
                Assert.That(locks.IsBarred(player, "Fugitive"), Is.True, "The Underground is barred.");
                Assert.That(locks.IsBarred(player, "TsfCivilian"), Is.False, "Their own faction's civilians are open.");
                Assert.That(locks.IsBarred(player, "Sheriff"), Is.False, "Their own faction is open.");
                Assert.That(locks.IsBarred(player, "Contractor"), Is.False, "Neutral jobs are open.");
                var allowed = new IsJobAllowedEvent(player, new ProtoId<JobPrototype>("Pirate"));
                server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref allowed);
                Assert.That(allowed.Cancelled, Is.True, "Spawning refuses the barred job.");
                var candidates = new System.Collections.Generic.List<ProtoId<JobPrototype>> { "Pirate", "Contractor", "Sheriff" };
                var query = new Content.Server.Station.Events.StationJobsGetCandidatesEvent(player.UserId, candidates);
                server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref query);
                Assert.That(candidates.Select(job => job.Id), Is.EquivalentTo(new[] { "Contractor", "Sheriff" }), "Round start skips the barred job.");
            });

            // The client's profile copy is stale, so the snapshot has to carry the lock to the job lists.
            await pair.Client.WaitPost(() => clientCompanies.Send(new WFCompanyRequest(WFCompanyAction.Refresh)));
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await FlushCompanies(pair, companies);
            await pair.RunTicksSync(5);
            await pair.Client.WaitAssertion(() =>
            {
                var state = clientCompanies.State;
                Assert.That(state, Is.Not.Null);
                Assert.That(state!.PlayedFactions[slot], Is.EqualTo(new[] { "TSF" }));
                Assert.That(state.PlayedFactions.ContainsKey(otherSlot), Is.False, "The player's other character is free.");
                Assert.That(state.CanJoin, Is.False, "The window closes companies to this character.");
                var clientPrototypes = pair.Client.ResolveDependency<IPrototypeManager>();
                var pirate = clientPrototypes.Index(Pirate);
                var saved = (HumanoidCharacterProfile) clientPreferences.Preferences!.Characters[slot];
                Assert.That(clientLocks.IsAllowed(pirate, saved, out var reason), Is.False);
                Assert.That(reason!.ToString(), Does.Contain("Trans-Solarian Federation"));
                Assert.That(clientLocks.IsAllowed(clientPrototypes.Index(Sheriff), saved, out _), Is.True);
            });

            // Companies refuse the character however it tries to get in.
            await Run(pair, () => companies.HandleRequest(new(WFCompanyAction.Create) { Name = "Sworn Company", Tag = "SWRN" }, player));
            await Run(pair, () => companies.Flush());
            await server.WaitAssertion(() => Assert.That(companies.CompanyIdOf(player), Is.Null, "A faction character can't found a company."));

            // Unlocking a character the player hasn't selected leaves the selection and the bound character alone.
            await server.WaitPost(() =>
            {
                var current = preferences.GetPreferences(player.UserId).Characters[slot];
                preferences.SetProfile(player.UserId, otherSlot, original.WithName("Bo Other").WithPersistentData(
                    new[] { WFFactionLockPolicy.Flag("MMC") }, original.Components, original.Items)).Wait();
                preferences.SetProfile(player.UserId, slot, current).Wait();
            });
            await Run(pair, async () => Assert.That(await locks.Unlock(player.UserId, otherSlot), Is.True));
            await server.WaitAssertion(() =>
            {
                var prefs = preferences.GetPreferences(player.UserId);
                Assert.That(prefs.SelectedCharacterIndex, Is.EqualTo(slot), "Unlocking doesn't switch the player's character.");
                Assert.That(((HumanoidCharacterProfile) prefs.Characters[otherSlot]).Flags, Is.Empty);
                Assert.That(locks.IsBarred(player, "Pirate"), Is.True, "The bound character is still bound.");
            });

            // The admin command frees the character, keeps the selection and tells the client at once.
            var balance = 0;
            await server.WaitPost(() =>
            {
                balance = ((HumanoidCharacterProfile) preferences.GetPreferences(player.UserId).Characters[slot]).BankBalance;
                server.ConsoleHost.ExecuteCommand($"factionunlock {player.Name} {slot}");
            });
            await WaitFor(pair, () => !locks.IsBarred(player, "Pirate"));
            await FlushCompanies(pair, companies);
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await server.WaitAssertion(() =>
            {
                var prefs = preferences.GetPreferences(player.UserId);
                Assert.That(prefs.SelectedCharacterIndex, Is.EqualTo(slot));
                Assert.That(((HumanoidCharacterProfile) prefs.Characters[slot]).BankBalance, Is.EqualTo(balance), "Unlocking changes nothing else.");
            });
            await pair.Client.WaitAssertion(() =>
            {
                var pirate = pair.Client.ResolveDependency<IPrototypeManager>().Index(Pirate);
                var saved = (HumanoidCharacterProfile) clientPreferences.Preferences!.Characters[slot];
                Assert.That(clientLocks.IsAllowed(pirate, saved, out _), Is.True, "The job list opens without a reconnect.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                ticker.SetGamePreset(preset);
                ticker.RestartRound();
                preferences.SetProfile(player.UserId, slot, original).Wait();
            });
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            await FlushCompanies(pair, companies);
        }
        await pair.CleanReturnAsync();
    }

    /// <summary>A company member can't take a faction job until they leave, and a faction character can't apply, be invited or accept.</summary>
    [Test]
    public async Task CompaniesAndFactionsExcludeEachOther()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var locks = server.System<WFFactionLockSystem>();
        var companies = server.System<WFCompanySystem>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var owner = pair.Player!;
        var slot = preferences.GetPreferences(owner.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter;
        var players = await server.AddDummySessions(2);
        var sworn = players[0];
        var free = players[1];
        int? id = null;
        try
        {
            await WaitFor(pair, () => companies.Loaded && players.All(session => session.Status == SessionStatus.InGame && preferences.HavePreferencesLoaded(session)));
            await server.WaitPost(() =>
            {
                preferences.SetProfile(owner.UserId, slot, original.WithName("Ada Owner").WithBankBalance(1000000)).Wait();
                var prefs = preferences.GetPreferences(sworn.UserId);
                var profile = (HumanoidCharacterProfile) prefs.SelectedCharacter;
                preferences.SetProfile(sworn.UserId, prefs.SelectedCharacterIndex, profile.WithName("Bo Sworn").WithPersistentData(
                    new[] { WFFactionLockPolicy.Flag("PDV") }, profile.Components, profile.Items)).Wait();
            });

            await RequestAs(pair, companies, owner, new(WFCompanyAction.Create) { Name = "Free Traders", Tag = "FREE" });
            await server.WaitPost(() => id = companies.CompanyIdOf(owner));
            Assert.That(id, Is.Not.Null, "The owner can found a company.");
            await server.WaitAssertion(() =>
            {
                Assert.That(locks.IsBarred(owner, "Deputy"), Is.True, "Company members can't serve a faction.");
                Assert.That(locks.IsBarred(owner, "Contractor"), Is.False, "Company members keep neutral jobs.");
            });

            // A snapshot taken while companies reload at round restart knows no memberships; the job lists keep their locks.
            var clientCompanies = pair.Client.System<WFCompanyClientSystem>();
            var clientLocks = pair.Client.System<WFFactionLockClientSystem>();
            var clientPreferences = pair.Client.ResolveDependency<IClientPreferencesManager>();
            await pair.Client.WaitPost(() => clientCompanies.Send(new WFCompanyRequest(WFCompanyAction.Refresh)));
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await FlushCompanies(pair, companies);
            await server.WaitPost(() => server.EntMan.EntityNetManager!.SendSystemNetworkMessage(new WFCompanyState { Loaded = false }, owner.Channel));
            await pair.RunTicksSync(5);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(clientCompanies.State!.Loaded, Is.False);
                var deputy = pair.Client.ResolveDependency<IPrototypeManager>().Index(Deputy);
                var selected = (HumanoidCharacterProfile) clientPreferences.Preferences!.SelectedCharacter;
                Assert.That(clientLocks.IsAllowed(deputy, selected, out _), Is.False);
            });

            await RequestAs(pair, companies, owner, new(WFCompanyAction.Invite) { CompanyId = id.Value, TargetUser = sworn.UserId });
            await RequestAs(pair, companies, sworn, new(WFCompanyAction.AcceptInvite) { CompanyId = id.Value });
            await RequestAs(pair, companies, sworn, new(WFCompanyAction.Apply) { CompanyId = id.Value, Description = "Let me in." });
            await RequestAs(pair, companies, sworn, new(WFCompanyAction.Create) { Name = "Sworn Company", Tag = "SWRN" });
            await server.WaitAssertion(() => Assert.That(companies.CompanyIdOf(sworn), Is.Null, "A faction character stays out of companies."));
            await server.WaitAssertion(() => Assert.That(companies.ListCompanies().Count(), Is.EqualTo(1)));

            // Playing a faction for the first time withdraws an application, from the officers' lists too.
            await RequestAs(pair, companies, free, new(WFCompanyAction.Apply) { CompanyId = id.Value, Description = "Let me in." });
            await pair.RunTicksSync(5);
            await pair.Client.WaitAssertion(() => Assert.That(clientCompanies.State!.Own!.Applications, Has.Length.EqualTo(1)));
            await server.WaitPost(() => companies.OnFactionBound(free));
            await FlushCompanies(pair, companies);
            await pair.RunTicksSync(5);
            await pair.Client.WaitAssertion(() => Assert.That(clientCompanies.State!.Own!.Applications, Is.Empty, "The officer's window drops them."));

            await RequestAs(pair, companies, owner, new(WFCompanyAction.Disband) { CompanyId = id.Value });
            await server.WaitAssertion(() => Assert.That(locks.IsBarred(owner, "Deputy"), Is.False, "Leaving a company opens the factions."));
            id = null;
        }
        finally
        {
            if (id is { } left)
                await RequestAs(pair, companies, owner, new(WFCompanyAction.Disband) { CompanyId = left });
            await server.WaitPost(() => preferences.SetProfile(owner.UserId, slot, original).Wait());
            await FlushCompanies(pair, companies);
            await server.RemoveAllDummySessions();
        }
        await pair.CleanReturnAsync();
    }

    /// <summary>An offline character is unlocked in the database, for their next login.</summary>
    [Test]
    public async Task UnlockReachesOfflineCharacters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var locks = server.System<WFFactionLockSystem>();
        var user = new Robust.Shared.Network.NetUserId(System.Guid.NewGuid());
        var bound = new HumanoidCharacterProfile().WithName("Cy Offline").WithBankBalance(777).WithPersistentData(
            new[] { "some-other-flag", WFFactionLockPolicy.Flag("MMC") }, new HumanoidCharacterProfile().Components, new HumanoidCharacterProfile().Items);
        await Run(pair, () => db.InitPrefsAsync(user, bound, default));
        var unlocked = false;
        await Run(pair, async () => unlocked = await locks.Unlock(user, 0));
        Assert.That(unlocked, Is.True);
        PlayerPreferences stored = null;
        await Run(pair, async () => stored = await db.GetPlayerPreferencesAsync(user, default));
        var profile = (HumanoidCharacterProfile) stored!.Characters[0];
        Assert.Multiple(() =>
        {
            Assert.That(profile.Flags, Is.EqualTo(new[] { "some-other-flag" }), "Only the faction flag goes.");
            Assert.That(profile.BankBalance, Is.EqualTo(777));
            Assert.That(profile.Name, Is.EqualTo("Cy Offline"));
        });
        await Run(pair, async () => unlocked = await locks.Unlock(user, 0));
        Assert.That(unlocked, Is.False, "A free character has nothing to unlock.");
        await pair.CleanReturnAsync();
    }

    private static async Task RequestAs(TestPair pair, WFCompanySystem companies, ICommonSession player, WFCompanyRequest request)
    {
        await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
        await Run(pair, () => companies.HandleRequest(request, player));
        await Run(pair, () => companies.Flush());
    }

    private static async Task FlushCompanies(TestPair pair, WFCompanySystem companies) => await Run(pair, () => companies.Flush());

    // Server tasks continue on the server thread, so they only finish while ticks run.
    private static async Task Run(TestPair pair, System.Func<Task> start)
    {
        Task task = null;
        await pair.Server.WaitPost(() => task = start());
        for (var i = 0; i < 600 && !task.IsCompleted; i++)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(5);
        }
        Assert.That(task.IsCompleted, Is.True, "Server task timed out.");
        await task;
    }

    private static async Task WaitFor(TestPair pair, System.Func<bool> condition)
    {
        var done = false;
        for (var i = 0; i < 600 && !done; i++)
        {
            await pair.Server.WaitPost(() => done = condition());
            if (!done)
            {
                await pair.RunTicksSync(1);
                await Task.Delay(10);
            }
        }
        Assert.That(done, Is.True);
    }
}
