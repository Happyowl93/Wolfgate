using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Server._WF.Augments;
using Content.Server.Preferences.Managers;
using Content.Shared._Goobstation.Autosurgeon;
using Content.Shared._WF.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.EntityTable;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// Cyberware of every kind is applied to a body and read back from it; a body's cyberware is written to the profile
/// it belongs to, keeping creator edits; every augment names real cyberware; scavenging finds the priced ones only.
/// </summary>
[TestFixture]
[TestOf(typeof(WFAugmentSystem))]
public sealed class AugmentPersistenceTest
{
    private static readonly Dictionary<WFAugmentSlot, WFAugmentChoice> Cyberware = new()
    {
        [WFAugmentSlot.RightArm] = new("WFAugmentJawsOfLifeRightArm", "WFAugmentStyleCyborg"),
        [WFAugmentSlot.LeftHand] = new("WFAugmentDexLeftHand", "WFAugmentStyleCyborg"),
        [WFAugmentSlot.Heart] = new("WFAugmentLotusSandevistan", null),
        [WFAugmentSlot.LeftArmImplant] = new("WFAugmentLeftMantisBlade", null),
        [WFAugmentSlot.Dermal] = new("WFAugmentDermalArmor", null),
    };

    private static void Apply(IEntityManager entities, EntityUid mob, Dictionary<WFAugmentSlot, WFAugmentChoice> augments)
    {
        var ev = new WFApplyAugmentsEvent(HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(augments));
        entities.EventBus.RaiseLocalEvent(mob, ref ev);
    }

    [Test]
    public async Task ReadBodyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var augments = server.System<WFAugmentSystem>();
        var body = server.System<SharedBodySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            Apply(entities, mob, Cyberware);

            var read = augments.ReadBody(mob, new Dictionary<WFAugmentSlot, WFAugmentChoice>(), Cyberware);
            Assert.That(read.Keys, Is.EquivalentTo(Cyberware.Keys), "The J.W.L arm's own hand reads as an augment, or one is missing.");
            Assert.That(read[WFAugmentSlot.RightArm].Style?.Id, Is.EqualTo("WFAugmentStyleCyborg"));

            // Nothing picked it, so it wears the trait limbs' style.
            var unpicked = augments.ReadBody(mob, new Dictionary<WFAugmentSlot, WFAugmentChoice>(), new Dictionary<WFAugmentSlot, WFAugmentChoice>());
            Assert.That(unpicked[WFAugmentSlot.RightArm].Style, Is.EqualTo(WFAugmentSystem.TraitLimbStyle));

            // A heart cut out in the round is no longer carried.
            var heart = body.GetBodyOrgans(mob).First(o => o.Component.SlotId == "heart").Id;
            body.RemoveOrgan(heart);
            Assert.That(augments.ReadBody(mob, Cyberware, Cyberware), Does.Not.ContainKey(WFAugmentSlot.Heart));

            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SaveBodyTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var augments = server.System<WFAugmentSystem>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();
        var map = await pair.CreateTestMap();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
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

        // Spawned with a cybernetic left leg, and bought a right one in the creator mid-round.
        var spawned = new Dictionary<WFAugmentSlot, WFAugmentChoice>
        {
            [WFAugmentSlot.LeftLeg] = new("WFAugmentCyberLeftLeg", "WFAugmentStyleCyborg"),
        };
        var edited = new Dictionary<WFAugmentSlot, WFAugmentChoice>(spawned)
        {
            [WFAugmentSlot.RightLeg] = new("WFAugmentCyberRightLeg", "WFAugmentStyleCyborg"),
        };
        await Run(() => prefs.SetProfile(userId, slot, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(edited)));

        // In the round the left leg was lost and a Sandevistan put in.
        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            Apply(entities, mob, new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.Heart] = new("WFAugmentLotusSandevistan", null),
            });
        });

        var played = new WFAugmentSystem.PlayedCharacter(slot, EntityUid.Invalid, spawned);
        await Run(() => augments.SaveBody(userId, played, mob));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().Augments.Keys, Is.EquivalentTo(new[] { WFAugmentSlot.RightLeg, WFAugmentSlot.Heart }));
            Assert.That(Stored().AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WFAugmentLotusSandevistan" }));
        });

        // Respawning without cryo takes it all.
        await server.WaitPost(() => augments.Track(userId, played));
        await Run(() => augments.ForfeitAugments(players.GetSessionById(userId)));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().Augments, Is.Empty);
            Assert.That(Stored().AugmentUnlocks, Is.Empty);
            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RegistryTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var installed = new Dictionary<string, string>();
            foreach (var augment in proto.EnumeratePrototypes<WFAugmentPrototype>().Where(a => !a.ID.StartsWith("WFTest")))
            {
                if (WFAugmentRules.Installed(augment) is not { } id)
                    continue;

                Assert.That(proto.TryIndex(id, out var entity), $"{augment.ID} names a missing entity.");
                Assert.That(installed.TryAdd(id, augment.ID), $"{augment.ID} and {installed.GetValueOrDefault(id)} both install {id}.");
                if (augment.Kind == WFAugmentKind.Upgrade)
                {
                    Assert.That(entity!.TryComp(out AutoSurgeonComponent? surgeon, factory) && surgeon.NewPartProto == null,
                        $"{augment.ID} names {id}, which is not an upgrading autosurgeon.");
                }
            }

            var table = proto.Index<EntityTablePrototype>("WFAugmentScavengeTable").Table;
            var odds = table.ListSpawns(server.EntMan, proto, new EntityTableContext()).ToDictionary(s => s.spawn.Id, s => s.prob);
            Assert.That(odds.Values.Sum(), Is.EqualTo(1).Within(1e-6));
            Assert.That(odds, Does.Not.ContainKey("WFAugmentLeftArm"), "A free augment is scavenged.");
            Assert.That(odds["OrganHeartMMCSandevistan"], Is.LessThan(odds["BasicCyberneticEyes"]));
            Assert.That(odds, Contains.Key("AutosurgeonDermalArmorPDV"));
        });

        await pair.CleanReturnAsync();
    }
}
