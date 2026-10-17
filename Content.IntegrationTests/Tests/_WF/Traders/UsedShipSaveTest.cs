#nullable enable
using System.Linq;
using Content.Server._ES.TileFires;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Shipyard;
using Content.Server.NPC.HTN;
using Content.Server.NPC;
using Content.Server.Spreader;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Traders;

/// <summary>
/// A worn hull with a vending machine aboard has to copy cleanly, dents and stock included.
/// </summary>
[TestFixture]
public sealed class UsedShipSaveTest
{
    [Test]
    public async Task DamagedShipWithVendorRoundTrips()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var damageSys = entMan.System<DamageableSystem>();

        var shuttle = EntityUid.Invalid;
        string? data = null;
        var damaged = 0;

        await server.WaitAssertion(() =>
        {
            var vessel = protoMan.Index<VesselPrototype>("Arribane");
            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var grid), Is.True);
            shuttle = grid!.Value.Owner;
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");
            var heat = protoMan.Index<DamageTypePrototype>("Heat");
            var children = new System.Collections.Generic.List<EntityUid>();
            var xforms = entMan.GetComponent<TransformComponent>(shuttle).ChildEnumerator;
            while (xforms.MoveNext(out var c)) children.Add(c);
            var i = 0;
            foreach (var child in children)
            {
                if (!entMan.HasComponent<DamageableComponent>(child))
                    continue;
                if (i++ % 3 != 0)
                    continue;
                damageSys.TryChangeDamage(child, new DamageSpecifier(blunt, 15) + new DamageSpecifier(heat, 5), true);
                damaged++;
            }

            shipyard.SetupShipyardIfNeeded();
            Assert.That(shipyard.TrySaveShip(shuttle, out data), Is.True, "Save failed.");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True, "Load failed.");
            var count = 0;
            var xforms = entMan.GetComponent<TransformComponent>(loaded!.Value).ChildEnumerator;
            while (xforms.MoveNext(out var child))
            {
                if (entMan.TryGetComponent<DamageableComponent>(child, out var d) && d.TotalDamage > 0)
                    count++;
            }
            Assert.That(count, Is.EqualTo(damaged), "Every dent should survive the copy.");

            // Reloaded grids skip MapInit, which is where an NPC's Owner used to be set. A console
            // autopilot with no Owner throws every tick and freezes every NPC on the server.
            var npcs = 0;
            var q = entMan.EntityQueryEnumerator<HTNComponent, TransformComponent>();
            while (q.MoveNext(out var npc, out var htn, out var xf))
            {
                if (xf.GridUid != loaded.Value)
                    continue;
                npcs++;
                Assert.That(htn.Blackboard.ContainsKey(NPCBlackboard.Owner), Is.True, $"{entMan.ToPrettyString(npc)} has no Owner.");
                Assert.That(htn.Blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), Is.EqualTo(npc));
            }
            Assert.That(npcs, Is.GreaterThan(0), "The vessel should carry at least one HTN entity (autopilot console).");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull with a fire aboard still copies: the fire sits in the grid's spread queue, which is not saved.
    /// </summary>
    [Test]
    public async Task BurningShipRoundTrips()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var shipyard = entMan.System<ShipyardSystem>();

        var shuttle = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            var vessel = protoMan.Index<VesselPrototype>("Arribane");
            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var grid), Is.True);
            shuttle = grid!.Value.Owner;
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var fires = entMan.System<ESTileFireSystem>();
            var lit = false;
            var xforms = entMan.GetComponent<TransformComponent>(shuttle).ChildEnumerator;
            while (!lit && xforms.MoveNext(out var child))
                lit = fires.TryDoTileFire(entMan.GetComponent<TransformComponent>(child).Coordinates);

            Assert.That(lit, Is.True, "No tile on the vessel would take a fire.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<SpreaderGridComponent>(shuttle).SpreadQueues.Values.Any(queue => queue.Count > 0),
                Is.True, "The fire is not queued to spread, so the test proves nothing.");

            shipyard.SetupShipyardIfNeeded();
            Assert.That(shipyard.TrySaveShip(shuttle, out var data), Is.True, "Save failed.");
            Assert.That(shipyard.TryAddSavedShip(data!, out _), Is.True, "Load failed.");
        });

        await pair.CleanReturnAsync();
    }
}
