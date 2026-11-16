using System.Collections.Generic;
using Content.Server._WF.Augments;
using Content.Server.Atmos.Components;
using Content.Shared._WF.Augments;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// The halo burst and halo stun: each model fires only its own ring, hits the people round the wearer but never the
/// wearer, spends one charge per burst, won't fire twice inside its spacing, and brings spent charges back.
/// </summary>
[TestFixture]
[TestOf(typeof(WFBurstLauncherSystem))]
public sealed class AugmentHaloBurstTest
{
    [Test]
    public async Task BurstTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var actions = server.System<SharedActionsSystem>();
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();

        EntityUid wearer = default, target = default, stunAction = default, burstAction = default;
        await server.WaitAssertion(() =>
        {
            wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
            target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(1f, 0f)));
            // The test map is airless, and low pressure deals Blunt.
            entities.RemoveComponent<BarotraumaComponent>(wearer);
            entities.RemoveComponent<BarotraumaComponent>(target);
            humanoids.LoadProfile(wearer, HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithAugmentImplants(["WFAugmentHaloStun", "WFAugmentHaloBurst"]));

            Assert.That(entities.TryGetComponent<WFHaloStunComponent>(wearer, out var stun), "The stun model wasn't fitted.");
            Assert.That(entities.TryGetComponent<WFHaloBurstComponent>(wearer, out var burst), "The lethal model wasn't fitted.");
            stunAction = stun!.ActionEntity!.Value;
            burstAction = burst!.ActionEntity!.Value;
            Assert.That(Charges(stunAction), Is.EqualTo(2));
            Assert.That(Charges(burstAction), Is.EqualTo(2));
        });

        int? Charges(EntityUid action) => entities.GetComponent<InstantActionComponent>(action).Charges;

        // Only what pellets deal.
        float PelletDamage(EntityUid body)
        {
            var dict = entities.GetComponent<DamageableComponent>(body).Damage.DamageDict;
            return (dict.GetValueOrDefault("Blunt") + dict.GetValueOrDefault("Piercing")).Float();
        }

        async Task Fire(EntityUid action)
        {
            await server.WaitPost(() =>
            {
                var comp = entities.GetComponent<InstantActionComponent>(action);
                actions.PerformAction(wearer, null, action, comp, comp.BaseEvent, timing.CurTime, false);
            });
            await pair.RunTicksSync(30);
        }

        // The stun ring tires the target out and leaves the wearer alone; the lethal model keeps its charges.
        await Fire(stunAction);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<StaminaComponent>(target).StaminaDamage, Is.GreaterThan(0f), "The stun ring missed.");
            Assert.That(entities.GetComponent<StaminaComponent>(wearer).StaminaDamage, Is.EqualTo(0f), "The wearer was hit.");
            Assert.That(PelletDamage(wearer), Is.EqualTo(0f), "The wearer was hit.");
            Assert.That(Charges(stunAction), Is.EqualTo(1));
            Assert.That(Charges(burstAction), Is.EqualTo(2), "The other model fired too.");
        });

        // A second press inside the spacing keeps the charge.
        await Fire(stunAction);
        await server.WaitAssertion(() => Assert.That(Charges(stunAction), Is.EqualTo(1), "The spacing let a second burst through."));

        // The lethal ring wounds the target and still leaves the wearer alone.
        var before = 0f;
        await server.WaitPost(() => before = PelletDamage(target));
        await Fire(burstAction);
        await server.WaitAssertion(() =>
        {
            Assert.That(PelletDamage(target), Is.GreaterThan(before), "The lethal ring missed.");
            Assert.That(PelletDamage(wearer), Is.EqualTo(0f), "The wearer was hit.");
            Assert.That(Charges(burstAction), Is.EqualTo(1));
        });

        // Emptied, the action switches off; a recharge brings it back.
        await server.WaitPost(() => entities.GetComponent<WFHaloStunComponent>(wearer).NextBurst = TimeSpan.Zero);
        await Fire(stunAction);
        await server.WaitAssertion(() =>
        {
            Assert.That(Charges(stunAction), Is.EqualTo(0));
            Assert.That(entities.GetComponent<InstantActionComponent>(stunAction).Enabled, Is.False);
            Assert.That(entities.GetComponent<WFHaloStunComponent>(wearer).NextCharge, Is.Not.Null, "No recharge was started.");
            entities.GetComponent<WFHaloStunComponent>(wearer).NextCharge = timing.CurTime;
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            var stun = entities.GetComponent<WFHaloStunComponent>(wearer);
            Assert.That(Charges(stunAction), Is.EqualTo(1));
            Assert.That(entities.GetComponent<InstantActionComponent>(stunAction).Enabled, Is.True, "The recharged action stayed off.");
            Assert.That(stun.NextCharge, Is.Not.Null, "The second charge isn't coming back.");
            stun.NextCharge = timing.CurTime;
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(Charges(stunAction), Is.EqualTo(2));
            Assert.That(entities.GetComponent<WFHaloStunComponent>(wearer).NextCharge, Is.Null);
        });

        // Removing the augment takes its action with it.
        await server.WaitPost(() => entities.RemoveComponent<WFHaloStunComponent>(wearer));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(entities.Deleted(stunAction), "The action outlived the augment."));

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(wearer);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }
}
