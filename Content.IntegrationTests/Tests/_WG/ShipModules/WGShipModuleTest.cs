using System.Numerics;
using Content.Server._WG.ShipModules;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.ShipModules;

/// <summary>Checks the helm strip, charges and locks of ship modules, and the maneuvering jets' burn.</summary>
[TestFixture]
public sealed class WGShipModuleTest
{
    private const string Jets = "WGShipModuleManeuveringJets";

    [Test]
    public async Task JetsKickTheHullAndSpendCharges()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var modules = entities.System<WGShipModuleSystem>();
        var grid = map.Grid.Owner;
        EntityUid jets = default;
        EntityUid spare = default;
        EntityUid pilot = default;
        var burnTime = 0f;

        await server.WaitPost(() =>
        {
            jets = entities.SpawnEntity(Jets, map.GridCoords);
            spare = entities.SpawnEntity(Jets, map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(jets).NeedsPower = false;
            entities.GetComponent<ApcPowerReceiverComponent>(spare).NeedsPower = false;
            pilot = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeRight;
        });
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            // One slot per kind: the second set of jets is a spare.
            var ship = entities.GetComponent<WGShipModulesComponent>(grid);
            Assert.That(ship.Slots, Has.Count.EqualTo(1));
            Assert.That(ship.Slots[0].Module, Is.EqualTo(entities.GetNetEntity(jets)));
            Assert.That(ship.Slots[0].Charges, Is.EqualTo(3));
            Assert.That(ship.Slots[0].Lock, Is.EqualTo(WGShipModuleLock.None));

            var module = entities.GetComponent<WGShipModuleComponent>(jets);
            Assert.That(modules.TryActivate((spare, entities.GetComponent<WGShipModuleComponent>(spare)), pilot), Is.False,
                "A spare module fired.");

            // Nothing held: no burn, no charge spent.
            entities.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.None;
            Assert.That(modules.TryActivate((jets, module), pilot), Is.False);
            Assert.That(module.Charges, Is.EqualTo(3));

            // A field seen a moment ago still locks the jets.
            ship.LastShielded = timing.CurTime;
            Assert.That(modules.GetLock((jets, module), grid, ship, timing.CurTime), Is.EqualTo(WGShipModuleLock.Shields));
            ship.LastShielded = null;

            // A deflector's partial field leaves the jets free; any other field holds the hull.
            var emitter = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<WGShipShieldDeflectorComponent>(emitter);
            var field = entities.AddComponent<ShipShieldedComponent>(grid);
            field.Shield = emitter;
            field.Source = emitter;
            Assert.That(modules.GetLock((jets, module), grid, ship, timing.CurTime), Is.EqualTo(WGShipModuleLock.None),
                "The deflector locked the jets.");
            entities.RemoveComponent<WGShipShieldDeflectorComponent>(emitter);
            Assert.That(modules.GetLock((jets, module), grid, ship, timing.CurTime), Is.EqualTo(WGShipModuleLock.Shields));
            entities.RemoveComponent<ShipShieldedComponent>(grid);
            entities.DeleteEntity(emitter);

            entities.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeRight;
            Assert.That(modules.TryActivate((jets, module), pilot), Is.True);
            Assert.That(module.Charges, Is.EqualTo(2));
            Assert.That(entities.GetComponent<WGShipModulesComponent>(grid).Slots[0].Charges, Is.EqualTo(2));
            Assert.That(modules.TryActivate((jets, module), pilot), Is.False, "The cooldown was ignored.");
            burnTime = entities.GetComponent<WGManeuveringJetsComponent>(jets).BurnTime;
        });
        // An unmanned grid is braked hard by GridSettle afterwards, so measure as it ends.
        await pair.RunTicksSync((int) MathF.Ceiling(burnTime / (float) timing.TickPeriod.TotalSeconds) + 1);

        await server.WaitAssertion(() =>
        {
            // The whole burn has landed along the held direction; damping takes a little back.
            // The one-tile test grid is laden by its own machines, so the kick is the rated one scaled for the hull.
            var body = entities.GetComponent<PhysicsComponent>(grid);
            var velocity = body.LinearVelocity;
            var jetsComp = entities.GetComponent<WGManeuveringJetsComponent>(jets);
            var scale = entities.System<WGManeuveringJetsSystem>().KickScale(jetsComp, grid, body);
            Assert.That(scale, Is.InRange(0.1f, 1f));
            var rated = jetsComp.DeltaV * scale;
            var along = Vector2.Dot(velocity, entities.System<SharedTransformSystem>().GetWorldRotation(grid).RotateVec(Vector2.UnitX));
            Assert.That(along, Is.InRange(rated * 0.8f, rated * 1.02f));
            Assert.That(velocity.Length(), Is.EqualTo(along).Within(0.5f));

            // A powered module refills one charge when its timer runs out.
            var module = entities.GetComponent<WGShipModuleComponent>(jets);
            module.NextCharge = timing.CurTime;
        });
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGShipModuleComponent>(jets).Charges, Is.EqualTo(3));
            Assert.That(entities.GetComponent<WGShipModulesComponent>(grid).Slots[0].Charges, Is.EqualTo(3));

            // With every module gone the ship loses its strip.
            entities.DeleteEntity(jets);
            entities.DeleteEntity(spare);
        });
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WGShipModulesComponent>(grid), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
