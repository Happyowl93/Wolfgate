#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WG.DockLock;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.DockLock;

/// <summary>A locked port takes no docking, its own helm won't undock it, and only the ship's crew can lock it.</summary>
[TestFixture]
public sealed class WGDockLockTest
{
    [Test]
    public async Task LockedPortRefusesDockingAndItsOwnUndock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        (EntityUid Grid, EntityUid Dock, EntityUid Helm) ours = default, theirs = default;
        EntityUid hand = default;

        await pair.Server.WaitAssertion(() =>
        {
            // Two decks 0.1 m apart, their ports facing each other across the gap.
            ours = Deck(new Vector2(500f, 500f), 0, 2, new Vector2(-1f, 0f));
            theirs = Deck(new Vector2(500f - 3.1f, 500f), 2, 0, new Vector2(1f, 0f));
            hand = em.SpawnEntity(null, new EntityCoordinates(ours.Grid, new Vector2(1.5f, 0.5f)));
        });

        await pair.RunTicksSync(5);

        await pair.Server.WaitAssertion(() =>
        {
            var docking = em.System<DockingSystem>();
            var locks = em.System<WGDockLockSystem>();
            var ourDock = Port(ours.Dock);
            var theirDock = Port(theirs.Dock);
            Assert.That(docking.CanDock(ourDock, theirDock), Is.True, "Precondition: the ports line up.");

            Assert.That(locks.TryToggle(ours.Helm, ours.Dock, true, hand), Is.False, "A stranger can't lock the port.");
            Assert.That(locks.IsLocked(ours.Dock), Is.False);

            em.EnsureComponent<CompanyComponent>(ours.Grid).CompanyName = "TSF";
            em.EnsureComponent<CompanyComponent>(hand).CompanyName = "TSF";
            Assert.That(locks.TryToggle(ours.Helm, theirs.Dock, true, hand), Is.False, "Only the helm's own ship's ports.");
            Assert.That(locks.TryToggle(ours.Helm, ours.Dock, true, hand), Is.True, "Its crew can.");
            Assert.That(locks.IsLocked(ours.Dock), Is.True);
            Assert.That(ScreenLocked(ours.Dock), Is.True, "The docking screen shows the lock.");

            Assert.That(docking.CanDock(ourDock, theirDock), Is.False, "A locked port takes no docking.");
            Assert.That(docking.CanDock(theirDock, ourDock), Is.False, "From either side.");
            Assert.That(locks.RefusesDock(theirs.Helm, theirs.Dock, ours.Dock, null), Is.True, "The other helm is told why.");

            locks.SetLocked(ours.Dock, false);
            Assert.That(docking.CanDock(ourDock, theirDock), Is.True, "Unlocked, it docks again.");
            docking.Dock(ourDock, theirDock);
            locks.SetLocked(ours.Dock, true);

            Assert.That(locks.RefusesUndock(ours.Helm, ourDock, null), Is.True, "Its own helm won't undock it.");
            Assert.That(locks.RefusesUndock(ours.Helm, theirDock, null), Is.True, "Nor by the other ship's port.");
            Assert.That(locks.RefusesUndock(theirs.Helm, theirDock, null), Is.False, "The other ship can cast off.");
            Assert.That(locks.RefusesUndock(theirs.Helm, ourDock, null), Is.False);

            var all = new List<NetEntity> { em.GetNetEntity(ours.Dock) };
            locks.HoldLocked(ours.Helm, all, null);
            Assert.That(all, Is.Empty, "Undock All leaves the locked port docked.");
            var theirAll = new List<NetEntity> { em.GetNetEntity(theirs.Dock) };
            locks.HoldLocked(theirs.Helm, theirAll, null);
            Assert.That(theirAll, Has.Count.EqualTo(1));

            docking.Undock(ourDock);
            locks.SetLocked(ours.Dock, false);
        });

        await pair.CleanReturnAsync();

        (EntityUid Grid, EntityUid Dock, EntityUid Helm) Deck(Vector2 at, int dockTile, int helmTile, Vector2 facing)
        {
            var maps = em.System<SharedMapSystem>();
            var xforms = em.System<SharedTransformSystem>();
            var grid = maps.CreateGridEntity(map.MapId);
            for (var x = 0; x < 3; x++)
                maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            xforms.SetWorldPosition(grid.Owner, at);
            var dock = em.SpawnEntity("AirlockShuttle", new EntityCoordinates(grid.Owner, new Vector2(dockTile + 0.5f, 0.5f)));
            xforms.SetLocalRotation(dock, facing.ToWorldAngle());
            var helm = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid.Owner, new Vector2(helmTile + 0.5f, 0.5f)));
            return (grid.Owner, dock, helm);
        }

        Entity<DockingComponent> Port(EntityUid uid) => (uid, em.GetComponent<DockingComponent>(uid));

        bool ScreenLocked(EntityUid dock)
        {
            var net = em.GetNetEntity(dock);
            foreach (var ports in em.System<ShuttleConsoleSystem>().GetAllDocks().Values)
            {
                foreach (var port in ports)
                {
                    if (port.Entity == net)
                        return port.WgDockLocked;
                }
            }

            return false;
        }
    }
}
