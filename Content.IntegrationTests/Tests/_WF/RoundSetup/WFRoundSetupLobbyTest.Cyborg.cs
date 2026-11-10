using System.Linq;
using Content.Server._NF.SectorServices;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Voting.Managers;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.ShipAccess;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

public sealed partial class WFRoundSetupLobbyTest
{
    /// <summary>A host who picks the cyborg job spawns as a chassis, holds the deed on its issued card and is in its ship's crew.</summary>
    [Test]
    public async Task CyborgHostStartsShipCrew()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var owner = pair.Player!;
        var ticker = server.System<GameTicker>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var original = preferences.GetPreferences(owner.UserId);
        var slot = original.SelectedCharacterIndex;
        var profile = (HumanoidCharacterProfile) original.Characters[slot];
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var preset = ticker.Preset;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                vote.Cancel();
        });
        await pair.RunTicksSync(2);
        try
        {
            await server.WaitPost(() =>
            {
                preferences.SetProfile(owner.UserId, slot,
                    HumanoidCharacterProfile.DefaultWithSpecies("Human").WithoutAllTraitPreferences().WithName("Ship Owner").WithBankBalance(10000000)).Wait();
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
                if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
                    em.AddComponent<StationSectorServiceHostComponent>(em.SpawnEntity(null, MapCoordinates.Nullspace));
            });
            var state = await Request(pair, new(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
            var id = OwnLobby(state).Id;
            Assert.That(state.Jobs.Select(job => job.Id), Does.Contain("Borg"), "A ship crew should offer the cyborg job.");
            await Request(pair, new(WFRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = "Guppy" });
            await Request(pair, new(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = "Borg" });
            state = await Request(pair, new(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
            Assert.That(OwnLobby(state).Started, Is.True, state.Message);
            await server.WaitAssertion(() =>
            {
                var body = owner.AttachedEntity!.Value;
                Assert.That(em.HasComponent<BorgChassisComponent>(body), Is.True, "The cyborg job should spawn a chassis, not a humanoid.");
                var grid = em.GetComponent<TransformComponent>(body).GridUid!.Value;
                Assert.That(em.TryGetComponent<ShuttleDeedComponent>(grid, out var deed) && deed.ShuttleUid == grid, Is.True,
                    "The ship should be deeded.");
                var access = em.GetComponent<WFShipAccessComponent>(grid);
                Assert.That(access.CrewId, Is.EqualTo(id));
                Assert.That(access.Mode, Is.EqualTo(WFShipAccessMode.Crew));
                // A chassis has no card until it picks a type, so it owns the ship by account.
                Assert.That(access.OwnerUsers, Does.Contain(owner.UserId), "The cardless cyborg host should own its ship.");
                Assert.That(server.System<WFShipAccessSystem>().IsOwner(body, grid), Is.True);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                // A client still attached to a cyborg throws while the round's cleanup deletes it.
                server.System<SharedMindSystem>().WipeMind(owner.ContentData()?.Mind);
                ticker.RestartRound();
                ticker.SetGamePreset(preset);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                preferences.SetProfile(owner.UserId, slot, profile).Wait();
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
            });
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }
}
