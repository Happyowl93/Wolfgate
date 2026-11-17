using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.Augments;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// A client save buys a priced augment once from the character's bank and keeps it unlocked; one the bank can't cover
/// is dropped, unlocks a client sends are ignored, and one taken off is sold back for half its price, or for nothing
/// if it was kept from a round.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerPreferencesManager))]
public sealed class AugmentPurchaseTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wgAugment
  id: WGTestPricedLeftLeg
  name: wg-augment-cyber-leg
  description: wg-augment-cyber-leg-desc
  slot: LeftLeg
  kind: Limb
  part: WGAugmentLeftLeg
  styled: true
  price: 15000

- type: wgAugment
  id: WGTestPricedRightLeg
  name: wg-augment-cyber-leg
  description: wg-augment-cyber-leg-desc
  slot: RightLeg
  kind: Limb
  part: WGAugmentRightLeg
  styled: true
  price: 15000
";

    [Test]
    public async Task PurchaseTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        const int slot = 0;

        HumanoidCharacterProfile Stored() => (HumanoidCharacterProfile) prefs.GetPreferences(userId).Characters[slot];

        // Starts a save on the server thread and ticks until its database write finishes.
        async Task Save(Func<Task> save)
        {
            Task task = default!;
            await server.WaitPost(() => task = save());
            await PoolManager.WaitUntil(server, () => task.IsCompleted, maxTicks: 300);
            Assert.That(task.IsCompletedSuccessfully, $"The save failed: {task.Exception}");
        }

        var start = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithBankBalance(20000);
        await Save(() => prefs.SetProfile(userId, slot, start));

        Dictionary<WGAugmentSlot, WGAugmentChoice> Legs(bool both)
        {
            var legs = new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftLeg] = new("WGTestPricedLeftLeg", "WGAugmentStyleCyborg"),
            };
            if (both)
                legs[WGAugmentSlot.RightLeg] = new("WGTestPricedRightLeg", "WGAugmentStyleCyborg");
            return legs;
        }

        // Buys the left leg: 15000 of 20000.
        await Save(() => prefs.SetProfile(userId, slot, Stored().WithAugments(Legs(false)), authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().BankBalance, Is.EqualTo(5000));
            Assert.That(Stored().AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGTestPricedLeftLeg" }));
            Assert.That(Stored().Augments, Contains.Key(WGAugmentSlot.LeftLeg));
        });

        // The right leg costs more than is left, so it is dropped; the left one is already paid for.
        await Save(() => prefs.SetProfile(userId, slot, Stored().WithAugments(Legs(true)), authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().BankBalance, Is.EqualTo(5000));
            Assert.That(Stored().Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.LeftLeg }));
        });

        // A client can't grant itself an unlock.
        var forged = Stored().WithAugmentUnlocks(new ProtoId<WGAugmentPrototype>[] { "WGTestPricedLeftLeg", "WGTestPricedRightLeg" })
            .WithAugments(Legs(true));
        await Save(() => prefs.SetProfile(userId, slot, forged, authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGTestPricedLeftLeg" }));
            Assert.That(Stored().Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.LeftLeg }));
        });

        // Taking the leg off sells it back for half: 5000 + 7500.
        await Save(() => prefs.SetProfile(userId, slot,
            Stored().WithAugments(new Dictionary<WGAugmentSlot, WGAugmentChoice>()), authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().BankBalance, Is.EqualTo(12500));
            Assert.That(Stored().AugmentUnlocks, Is.Empty);
            Assert.That(Stored().Augments, Is.Empty);
        });

        // A leg kept from a round was never paid for: taking it off pays nothing, whatever the client says of it.
        var left = new ProtoId<WGAugmentPrototype>[] { "WGTestPricedLeftLeg" };
        await Save(() => prefs.SetProfile(userId, slot,
            Stored().WithAugments(Legs(false)).WithAugmentUnlocks(left).WithAugmentFound(left)));
        await server.WaitAssertion(() => Assert.That(Stored().AugmentFound.Select(u => u.Id), Is.EquivalentTo(new[] { "WGTestPricedLeftLeg" })));
        await Save(() => prefs.SetProfile(userId, slot,
            Stored().WithAugmentFound(new ProtoId<WGAugmentPrototype>[0]).WithAugments(new Dictionary<WGAugmentSlot, WGAugmentChoice>()),
            authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().BankBalance, Is.EqualTo(12500));
            Assert.That(Stored().AugmentUnlocks, Is.Empty);
            Assert.That(Stored().AugmentFound, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
