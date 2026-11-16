using System.Collections.Generic;
using System.Numerics;
using Content.Client._White.Overlays;
using Content.Shared._White.Overlays;
using Content.Shared._WF.Augments;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Flash.Components;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// The spectral thermal eyes: a body that gets them gets thermal vision and the cycle action, and each use steps the
/// vision through every band and then off, on the server and on the owner's client.
/// </summary>
[TestFixture]
[TestOf(typeof(WFThermalTintsSystem))]
public sealed class AugmentThermalEyesTest
{
    [Test]
    public async Task CycleTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid body = default;
        List<WFThermalTint> tints = default!;
        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(
                new Dictionary<WFAugmentSlot, WFAugmentChoice> { [WFAugmentSlot.Eyes] = new("WFAugmentThermalTintEyes", null) });
            var ev = new WFApplyAugmentsEvent(profile);
            entities.EventBus.RaiseLocalEvent(body, ref ev);

            Assert.That(entities.TryGetComponent<WFThermalTintsComponent>(body, out var eyes), "The eyes gave no tints.");
            Assert.That(entities.TryGetComponent<ThermalVisionComponent>(body, out var thermal), "The eyes gave no thermal vision.");
            Assert.That(eyes!.ActionEntity, Is.Not.Null, "The eyes gave no cycle action.");
            Assert.That(thermal!.ToggleActionEntity, Is.Null, "The thermal vision kept its own toggle.");
            Assert.That(thermal.IsActive, Is.False);
            Assert.That(entities.HasComponent<EyeProtectionComponent>(body));
            Assert.That(entities.HasComponent<FlashImmunityComponent>(body), Is.False, "The eyes are flash-proof.");
            Assert.That(eyes.Tints, Has.Count.GreaterThan(1));
            tints = eyes.Tints;
        });
        await pair.RunTicksSync(5);

        var overlays = client.ResolveDependency<IOverlayManager>();
        var clientBody = pair.ToClientUid(body);

        async Task Cycle()
        {
            await server.WaitPost(() =>
                entities.EventBus.RaiseLocalEvent(body, new WFCycleThermalTintEvent { Performer = body }));
            await pair.RunTicksSync(5);
        }

        async Task AssertBand(int index)
        {
            await server.WaitAssertion(() =>
            {
                var thermal = entities.GetComponent<ThermalVisionComponent>(body);
                Assert.That(entities.GetComponent<WFThermalTintsComponent>(body).Index, Is.EqualTo(index));
                Assert.That(thermal.IsActive, Is.EqualTo(index >= 0));
                if (index >= 0)
                    Assert.That(thermal.Color, Is.EqualTo(tints[index].Color));
            });
            await client.WaitAssertion(() =>
            {
                var thermal = client.EntMan.GetComponent<ThermalVisionComponent>(clientBody);
                Assert.That(thermal.IsActive, Is.EqualTo(index >= 0), $"The client's vision is wrong at band {index}.");
                Assert.That(overlays.HasOverlay<ThermalVisionOverlay>(), Is.EqualTo(index >= 0), $"The overlay is wrong at band {index}.");
                if (index < 0)
                    return;
                Assert.That(thermal.Color, Is.EqualTo(tints[index].Color));
                Assert.That(Vector3.Distance(thermal.Tint, tints[index].Tint), Is.LessThan(0.001f), $"The client's wash is wrong at band {index}.");
            });
        }

        await AssertBand(-1);
        for (var i = 0; i < tints.Count; i++)
        {
            await Cycle();
            await AssertBand(i);
        }

        await Cycle();
        await AssertBand(-1);
        await Cycle();
        await AssertBand(0);

        await server.WaitPost(() => entities.DeleteEntity(body));
        await pair.CleanReturnAsync();
    }
}
