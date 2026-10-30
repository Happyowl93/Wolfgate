using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.Companies;
using Content.Client._WF.RoundSetup;
using Content.Server._NF.SectorServices;
using Content.Server._WF.Companies;
using Content.Server._WF.RoundSetup;
using Content.Server._WF.Drydock;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Voting.Managers;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Companies;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.RoundSetup;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Companies;

public sealed partial class WFCompanyTest
{
    /// <summary>A licence launches its ship through Crew Setup once per round, charging the registry and launch fees.</summary>
    [Test]
    public async Task HangarLicencesLaunchCompanyShips()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var companies = server.System<WFCompanySystem>();
        var client = pair.Client.System<WFCompanyClientSystem>();
        var crews = pair.Client.System<WFRoundSetupClientSystem>();
        var ticker = server.System<GameTicker>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var owner = pair.Player!;
        var preset = ticker.Preset;
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter;
        var slot = preferences.GetPreferences(owner.UserId).SelectedCharacterIndex;
        var guppy = server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>("Guppy").Price;
        int? id = null;
        var hands = await server.AddDummySessions(2);
        var roundSetup = server.System<WFRoundSetupSystem>();
        var onRequest = typeof(WFRoundSetupSystem).GetMethod("OnRequest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
                foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                    vote.Cancel();
                preferences.SetProfile(owner.UserId, slot, original.WithName("Ada Owner").WithBankBalance(5000)).Wait();
            });
            for (var i = 0; i < 300 && !companies.Loaded; i++)
                await pair.RunTicksSync(1);
            var state = await Send(new(WFCompanyAction.Create) { Name = "Hangar Works", Tag = "HNGR" });
            id = state.Own!.Id;
            await server.WaitPost(() => companies.AdminAdjust(id.Value, WFCompanyPolicy.LicencePrice(guppy) + 100000, "Test", "seed"));
            state = await Send(new(WFCompanyAction.SetLaunchFee) { CompanyId = id.Value, Amount = 250 });
            Assert.That(state.Own!.LicenceOffers.Any(offer => offer.Vessel == "Guppy"), Is.True);
            state = await Send(new(WFCompanyAction.BuyLicence) { CompanyId = id.Value, Vessel = "Guppy" });
            Assert.That(state.Own!.Licences, Is.Empty, "A one-person company can't buy fleet licences.");
            for (var i = 0; i < 300 && !hands.All(hand => preferences.HavePreferencesLoaded(hand)); i++)
                await pair.RunTicksSync(1);
            foreach (var hand in hands)
            {
                await Send(new(WFCompanyAction.Invite) { CompanyId = id.Value, TargetUser = hand.UserId });
                await Run(() => companies.HandleRequest(new(WFCompanyAction.AcceptInvite) { CompanyId = id.Value }, hand));
            }
            state = await Send(new(WFCompanyAction.Refresh));
            Assert.That(state.Own!.Members, Has.Length.EqualTo(3));
            state = await Send(new(WFCompanyAction.BuyLicence) { CompanyId = id.Value, Vessel = "Guppy" });
            Assert.That(state.Own!.Licences.Single().Vessel, Is.EqualTo("Guppy"), state.Message);
            Assert.That(state.Own.Balance, Is.EqualTo(100000), "The licence price comes out of the account.");
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            state = await Send(new(WFCompanyAction.Refresh));
            var licence = state.Own!.Licences.Single().Id;
            Assert.That(licence, Is.Not.Zero);

            await server.WaitPost(() =>
            {
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
                if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
                    em.AddComponent<StationSectorServiceHostComponent>(em.SpawnEntity(null, MapCoordinates.Nullspace));
            });
            state = await Send(new(WFCompanyAction.Launch) { CompanyId = id.Value, LicenceId = licence });
            Assert.That(state.Own!.Licences.Single().Launched, Is.True, state.Message);
            var crewState = await Crew(new(WFRoundSetupAction.Refresh));
            var crew = crewState.Lobbies.Single(lobby => lobby.Id == crewState.OwnLobby);
            Assert.Multiple(() =>
            {
                Assert.That(crew.Hangar, Is.True);
                Assert.That(crew.Vessel, Is.EqualTo("Guppy"));
                Assert.That(crew.CompanyTag, Is.EqualTo("HNGR"));
                Assert.That(crewState.Payment, Is.Null, "The crew pays nothing for a hangar ship.");
            });
            // A company member joins the company crew as their rank: no opening, the rank's job and title.
            await Crew(new(WFRoundSetupAction.Configure) { LobbyId = crew.Id, Access = WFLobbyAccess.Public, AllowLateJoin = true });
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => onRequest.Invoke(roundSetup, new object[]
                { new WFRoundSetupRequest(WFRoundSetupAction.Join) { LobbyId = crew.Id, AsRank = true }, new EntitySessionEventArgs(hands[0]) }));
            crewState = await Crew(new(WFRoundSetupAction.Refresh));
            var hand0 = crewState.Lobbies.Single(lobby => lobby.Id == crew.Id).Members.Single(member => member.UserId == hands[0].UserId);
            Assert.Multiple(() =>
            {
                Assert.That(hand0.RoleId, Is.Null);
                Assert.That(hand0.Title, Is.EqualTo("Recruit"));
                Assert.That(hand0.Job, Is.EqualTo("Contractor"));
            });
            await Crew(new(WFRoundSetupAction.SelectJob) { LobbyId = crew.Id, Job = "Contractor" });
            crewState = await Crew(new(WFRoundSetupAction.ConfirmJoin) { LobbyId = crew.Id });
            Assert.That(crewState.Lobbies.Single(lobby => lobby.Id == crew.Id).Started, Is.True, crewState.Message);
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            await server.WaitAssertion(() =>
            {
                var registry = WFCompanyPolicy.RegistryFee(guppy, 0);
                Assert.That(companies.GetBalance(id.Value), Is.EqualTo(100000 - registry + 250), "Registry fee out, launch fee in.");
                Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter).BankBalance, Is.EqualTo(4750));
                var grid = em.GetComponent<TransformComponent>(owner.AttachedEntity!.Value).GridUid!.Value;
                Assert.That(em.GetComponent<WFCompanyHangarShipComponent>(grid).CompanyId, Is.EqualTo(id.Value));
                Assert.That(em.GetComponent<WFCompanyShipComponent>(grid).Tag, Is.EqualTo("HNGR"));

                // Breaking it or stripping it for sale pays nothing.
                var console = em.SpawnEntity(null, MapCoordinates.Nullspace);
                var drydock = em.AddComponent<WFDrydockConsoleComponent>(console);
                Assert.That(server.System<WFDrydockSystem>().BreakRefusal(console, drydock, grid), Is.Not.Null);
                em.DeleteEntity(console);
                var pricing = server.System<PricingSystem>();
                var properties = 0;
                var query = em.EntityQueryEnumerator<WFCompanyPropertyComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.GridUid != grid)
                        continue;
                    properties++;
                    Assert.That(pricing.GetPrice(uid, includeContents: false), Is.Zero, $"{em.ToPrettyString(uid)} sells for nothing.");
                }
                Assert.That(properties, Is.GreaterThan(10));
                var loose = em.SpawnEntity("SheetSteel", new EntityCoordinates(grid, default));
                Assert.That(pricing.GetPrice(loose), Is.GreaterThan(0), "Things brought aboard later keep their value.");
            });
            state = await Send(new(WFCompanyAction.Launch) { CompanyId = id.Value, LicenceId = licence });
            Assert.That(state.Own!.Licences.Single().Launched, Is.True);
            Assert.That(state.Own.LaunchedThisRound, Is.EqualTo(1));

            // The ship wears the company colour, and Company access lets members through.
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            await server.WaitAssertion(() =>
            {
                var body = owner.AttachedEntity!.Value;
                var grid = em.GetComponent<TransformComponent>(body).GridUid!.Value;
                Assert.That(em.GetComponent<Content.Shared.Shuttles.Components.IFFComponent>(grid).Color,
                    Is.EqualTo(em.GetComponent<WFCompanyShipComponent>(grid).Colour));
                var access = server.System<Content.Server._WF.ShipAccess.WFShipAccessServerSystem>();
                var ship = new Entity<Content.Shared._WF.ShipAccess.WFShipAccessComponent>(grid, em.GetComponent<Content.Shared._WF.ShipAccess.WFShipAccessComponent>(grid));
                Assert.That(access.TryLinkCompany(ship, body), Is.True);
                Assert.That(access.SetMode(ship, Content.Shared._WF.ShipAccess.WFShipAccessMode.Company), Is.True);
                Assert.That(ship.Comp.CompanyName, Is.EqualTo("Hangar Works"));
                Assert.That(ship.Comp.CompanyKeys, Is.Not.Empty, "The owner's issued ID is a company key.");
            });
        }
        finally
        {
            if (id is { } left)
                await server.WaitPost(() => companies.AdminDisband(left));
            // The synchronous test database takes one request at a time: let company writes land, save the profile, then restart.
            await Run(() => companies.Flush());
            await server.WaitPost(() => preferences.SetProfile(owner.UserId, slot, original).Wait());
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                ticker.SetGamePreset(preset);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
            });
            await Run(() => companies.Flush());
            await server.RemoveAllDummySessions();
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        async Task<WFCompanyState> Send(WFCompanyRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await pair.Client.WaitPost(() => client.Send(request));
            await pair.RunTicksSync(pair.SecondsToTicks(0.75f));
            WFCompanyState result = null;
            await pair.Client.WaitPost(() => result = client.State);
            return result;
        }

        async Task Run(System.Func<Task> start)
        {
            Task task = null;
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => task = start());
            for (var i = 0; i < 600 && !task.IsCompleted; i++)
                await pair.RunTicksSync(1);
            await task;
        }

        async Task<WFRoundSetupState> Crew(WFRoundSetupRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await pair.Client.WaitPost(() => crews.Send(request));
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            WFRoundSetupState result = null;
            await pair.Client.WaitPost(() => result = crews.State);
            return result;
        }
    }
}
