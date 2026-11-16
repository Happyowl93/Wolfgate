using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Server._WF.Augments;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.Augments;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// A player who respawns instead of going into cryo loses the augments and unlocks of the character they spawned as;
/// a player who hasn't spawned a character loses nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(WFAugmentSystem))]
public sealed class AugmentRespawnTest
{
    [Test]
    public async Task RespawnTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var augments = server.System<WFAugmentSystem>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        var session = players.GetSessionById(userId);
        const int slot = 0;

        HumanoidCharacterProfile Stored() => (HumanoidCharacterProfile) prefs.GetPreferences(userId).Characters[slot];

        async Task Run(Func<Task?> save)
        {
            Task? task = null;
            await server.WaitPost(() => task = save());
            if (task == null)
                return;
            await PoolManager.WaitUntil(server, () => task.IsCompleted, maxTicks: 300);
            Assert.That(task.IsCompletedSuccessfully, $"The save failed: {task.Exception}");
        }

        var augmented = HumanoidCharacterProfile.DefaultWithSpecies("Human")
            .WithAugments(new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleCyborg"),
            })
            .WithAugmentUnlocks(new ProtoId<WFAugmentPrototype>[] { "WFAugmentCyberLeftArm" });
        await Run(() => prefs.SetProfile(userId, slot, augmented));

        // Never spawned this round: nothing is taken.
        await Run(() => augments.ForfeitAugments(session));
        await server.WaitAssertion(() => Assert.That(Stored().Augments, Is.Not.Empty));

        await server.WaitPost(() => augments.Track(userId,
            new WFAugmentSystem.PlayedCharacter(slot, EntityUid.Invalid, new Dictionary<WFAugmentSlot, WFAugmentChoice>())));
        await Run(() => augments.ForfeitAugments(session));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().Augments, Is.Empty);
            Assert.That(Stored().AugmentUnlocks, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
