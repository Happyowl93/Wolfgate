using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.RoundSetup;
using Content.Server._NF.SectorServices;
using Content.Server._WF.RoundSetup;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Radio.EntitySystems;
using Content.Server.Station.Components;
using Content.Server.Voting.Managers;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Radio.Components;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

public sealed partial class WFRoundSetupLobbyTest
{
    /// <summary>Approval cards preserve typing; radio and leader signals follow authoritative membership across respawn.</summary>
    [Test]
    public async Task CrewSignalsAndCaptainRequestsFollowMembership()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var host = pair.Player!;
        var slot = prefs.GetPreferences(host.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) prefs.GetPreferences(host.UserId).SelectedCharacter!;
        var ticker = server.System<GameTicker>();
        var setup = server.System<WFRoundSetupSystem>();
        var spy = server.System<WFCrewRadioTestSystem>();
        var onRequest = typeof(WFRoundSetupSystem).GetMethod("OnRequest", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var voting = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray()) vote.Cancel();
        });
        await pair.RunTicksSync(2);
        var guests = await server.AddDummySessions(2);
        var crew = guests[0];
        var outsider = guests[1];
        LineEdit typing = null!;
        try
        {
            var loaded = false;
            for (var i = 0; i < 300 && !loaded; i++)
            {
                await server.WaitPost(() => loaded = guests.All(player => player.Status == SessionStatus.InGame &&
                    prefs.HavePreferencesLoaded(player) && server.ResolveDependency<UserDbDataManager>().IsLoadComplete(player)));
                if (!loaded) { await pair.RunTicksSync(1); await Task.Delay(10); }
            }
            Assert.That(loaded, Is.True);
            await server.WaitPost(() =>
            {
                prefs.SetProfile(host.UserId, slot, HumanoidCharacterProfile.DefaultWithSpecies("Human")
                    .WithoutAllTraitPreferences().WithName("Crew Captain").WithBankBalance(1000000)).Wait();
                prefs.SetProfile(crew.UserId, prefs.GetPreferences(crew.UserId).SelectedCharacterIndex,
                    HumanoidCharacterProfile.DefaultWithSpecies("Human").WithoutAllTraitPreferences().WithName("Crew Scout")).Wait();
                ticker.StartRound(force: true);
                if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
                    server.EntMan.AddComponent<StationSectorServiceHostComponent>(server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace));
            });
            var state = await Request(pair, new(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship, Title = "Quiet Crew" });
            var id = state.OwnLobby!.Value;
            await Request(pair, new(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = "Contractor" });
            await Request(pair, new(WFRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = "Guppy" });
            state = await Request(pair, new(WFRoundSetupAction.UpsertRole) { LobbyId = id, Job = "Contractor", RoleName = "Scout", Capacity = 2 });
            var role = OwnLobby(state).Roles.Single().Id;
            await pair.Client.WaitPost(() =>
            {
                typing = new LineEdit { SetWidth = 120, SetHeight = 30 };
                pair.Client.ResolveDependency<IUserInterfaceManager>().PopupRoot.AddChild(typing);
                typing.GrabKeyboardFocus();
            });
            await AsGuest(crew, new(WFRoundSetupAction.Join) { LobbyId = id, RoleId = role });
            Button deny = null!;
            await pair.Client.WaitAssertion(() =>
            {
                var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                Assert.That(pair.Client.System<WFCrewHudSystem>().RequestVisible, Is.True);
                Assert.That(ui.KeyboardFocused, Is.SameAs(typing), "Request cards must not take focus from typing.");
                deny = CrewControls(ui.PopupRoot).OfType<Button>().Single(button => button.VisibleInTree && button.Text == Loc.GetString("wf-round-setup-request-popup-reject"));
            });
            state = await Request(pair, new(WFRoundSetupAction.AcceptRequest) { LobbyId = id, Target = crew.UserId, RoleId = role + 100 });
            Assert.That(OwnLobby(state).Members.Length, Is.EqualTo(1), "Stale approval cards must not admit a different role.");
            Assert.That(OwnLobby(state).Requests.Length, Is.EqualTo(1));
            await RoundSetupUiTest.Click(pair, deny);
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).Requests, Is.Empty);
            await AsGuest(crew, new(WFRoundSetupAction.Join) { LobbyId = id, RoleId = role });
            Button accept = null!;
            await pair.Client.WaitPost(() => accept = CrewControls(pair.Client.ResolveDependency<IUserInterfaceManager>().PopupRoot)
                .OfType<Button>().Single(button => button.VisibleInTree && button.Text == Loc.GetString("wf-round-setup-request-popup-accept")));
            await RoundSetupUiTest.Click(pair, accept);
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).Members.Length, Is.EqualTo(2));
            await pair.Client.WaitAssertion(() => { Assert.That(pair.Client.System<WFCrewHudSystem>().RequestVisible, Is.False); typing.Orphan(); });
            await AsGuest(crew, new(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
            state = await Request(pair, new(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
            Assert.That(OwnLobby(state).Members.All(member => member.Spawned), Is.True, state.Message);
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            var hostBody = host.AttachedEntity!.Value;
            var crewBody = crew.AttachedEntity!.Value;
            EntityUid outsiderBody = default;
            await server.WaitAssertion(() =>
            {
                outsiderBody = server.EntMan.SpawnEntity("MobHuman", server.EntMan.GetComponent<TransformComponent>(hostBody).Coordinates);
                server.PlayerMan.SetAttachedEntity(outsider, outsiderBody);
                server.EntMan.EnsureComponent<ActiveRadioComponent>(outsiderBody).ReceiveAllChannels = true;
                server.EntMan.EnsureComponent<IntrinsicRadioReceiverComponent>(outsiderBody);
                foreach (var body in new[] { hostBody, crewBody, outsiderBody })
                    server.EntMan.EnsureComponent<WFCrewRadioTestReceiverComponent>(body);
                spy.Deliveries.Clear();
                server.System<RadioSystem>().SendRadioMessage(hostBody, "Crew only", "WFCrew", hostBody);
                Assert.That(spy.Deliveries.Select(entry => entry.Receiver), Is.EquivalentTo(new[] { hostBody, crewBody }));
                Assert.That(spy.Deliveries.All(entry => entry.Text.Contains("Quiet Crew")), Is.True);
                spy.Deliveries.Clear();
                server.System<RadioSystem>().SendRadioMessage(outsiderBody, "Forged crew transmission", "WFCrew", outsiderBody);
                Assert.That(spy.Deliveries, Is.Empty);
            });
            await pair.Client.WaitAssertion(() => Assert.That(pair.Client.System<WFCrewHudSystem>().Signal!.Status, Is.EqualTo(WFCrewLeaderStatus.Self)));
            await Request(pair, new(WFRoundSetupAction.TransferHost) { LobbyId = id, Target = crew.UserId });
            await server.WaitPost(() =>
            {
                var xform = server.EntMan.GetComponent<TransformComponent>(hostBody);
                server.System<SharedTransformSystem>().SetCoordinates(crewBody, xform.Coordinates.Offset(new Vector2(2000, 0)));
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await pair.Client.WaitAssertion(() =>
            {
                var hud = pair.Client.System<WFCrewHudSystem>();
                Assert.That(hud.Signal!.Leader, Is.EqualTo("Crew Scout"));
                Assert.That(hud.Signal.Status, Is.EqualTo(WFCrewLeaderStatus.Tracking));
                Assert.That(hud.Signal.Offset!.Value.Length(), Is.GreaterThan(1990), "Leader tracking must work beyond the client's PVS.");
            });
            await server.WaitPost(() =>
            {
                server.EntMan.GetComponent<IntrinsicRadioTransmitterComponent>(hostBody).Channels.Add("Common");
                server.EntMan.GetComponent<ActiveRadioComponent>(hostBody).Channels.Add("Common");
                ticker.Respawn(host);
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await server.WaitAssertion(() =>
            {
                Assert.That(server.EntMan.HasComponent<WFPlayerCrewRadioComponent>(hostBody), Is.False);
                Assert.That(server.EntMan.GetComponent<IntrinsicRadioTransmitterComponent>(hostBody).Channels.Select(channel => channel.Id), Does.Contain("Common"));
                Assert.That(server.EntMan.GetComponent<ActiveRadioComponent>(hostBody).Channels.Select(channel => channel.Id), Does.Contain("Common"));
                spy.Deliveries.Clear();
                server.System<RadioSystem>().SendRadioMessage(crewBody, "After departure", "WFCrew", crewBody);
                Assert.That(spy.Deliveries.Select(entry => entry.Receiver), Is.EqualTo(new[] { crewBody }));
            });
            await pair.Client.WaitAssertion(() => Assert.That(pair.Client.System<WFCrewHudSystem>().Signal?.Body, Is.Null));
        }
        finally
        {
            await pair.Client.WaitPost(() => typing?.Orphan());
            await server.RemoveAllDummySessions();
            await server.WaitPost(() =>
            {
                spy.Deliveries.Clear();
                prefs.SetProfile(host.UserId, slot, original).Wait();
                ticker.RestartRound();
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, voting);
            });
            await pair.RunTicksSync(2);
        }
        await pair.CleanReturnAsync();

        static IEnumerable<Control> CrewControls(Control root)
        {
            yield return root;
            foreach (var child in root.Children)
            foreach (var descendant in CrewControls(child))
                yield return descendant;
        }

        async Task AsGuest(ICommonSession guest, WFRoundSetupRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await server.WaitPost(() => onRequest.Invoke(setup, new object[] { request, new EntitySessionEventArgs(guest) }));
            await pair.RunTicksSync(pair.SecondsToTicks(0.75f));
        }
    }
}
