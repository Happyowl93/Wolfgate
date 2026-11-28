#nullable enable
using System;
using System.Numerics;
using Content.Server._WG.Drones;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Drones;

/// <summary>A drone holds fire through its own carrier and ships of its faction, and fires past strangers.</summary>
[TestFixture]
public sealed class WGDroneFireGuardTest
{
    [Test]
    public async Task DroneSparesItsCarrierAndFaction()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var droneGrid = map.Grid.Owner;
        EntityUid drone = default, carrier = default, console = default;

        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();

            // The drone at the origin, a walled carrier five metres east, and the target past it.
            maps.SetTile(droneGrid, map.Grid.Comp, Vector2i.Zero, map.Tile.Tile);
            drone = em.SpawnEntity("DroneController", new EntityCoordinates(droneGrid, new Vector2(0.5f, 0.5f)));

            var carrierGrid = maps.CreateGridEntity(map.MapId);
            carrier = carrierGrid.Owner;
            for (var y = -1; y <= 1; y++)
            {
                maps.SetTile(carrier, carrierGrid.Comp, new Vector2i(5, y), map.Tile.Tile);
                maps.SetTile(carrier, carrierGrid.Comp, new Vector2i(6, y), map.Tile.Tile);
                em.SpawnEntity("WallSolid", new EntityCoordinates(carrier, new Vector2(5.5f, y + 0.5f)));
            }
            console = em.SpawnEntity("ComputerDroneControl", new EntityCoordinates(carrier, new Vector2(6.5f, 0.5f)));
        });

        await pair.RunTicksSync(5);

        await pair.Server.WaitAssertion(() =>
        {
            var guard = em.System<WGDroneFireGuardSystem>();
            var devices = em.System<DeviceListSystem>();
            var from = new MapCoordinates(new Vector2(0.5f, 0.5f), map.MapId);
            var through = new MapCoordinates(new Vector2(12.5f, 0.5f), map.MapId);
            var clear = new MapCoordinates(new Vector2(0.5f, 12.5f), map.MapId);

            Assert.That(guard.Fouled(drone, from, through), Is.False, "A stranger's hull in the way is the gunner's business.");

            devices.UpdateDeviceList(console, new[] { drone });
            Assert.That(guard.Fouled(drone, from, through), Is.True, "The carrier commanding it is in the line.");
            Assert.That(guard.Fouled(drone, from, clear), Is.False, "A line clear of the carrier is open.");

            // An aim just over the carrier's hull, then 0.4 m lower so the line clips it: a cached clear line isn't reused.
            var overHull = new MapCoordinates(new Vector2(7f, 2.9f), map.MapId);
            var clipsHull = new MapCoordinates(new Vector2(7f, 2.5f), map.MapId);
            Assert.That(guard.HoldsFire(drone, drone, overHull), Is.False, "A line passing over the carrier's hull is open.");
            Assert.That(guard.HoldsFire(drone, drone, clipsHull), Is.True,
                "A small move of the aim onto the carrier reused the clear line it had a moment before.");

            devices.UpdateDeviceList(console, Array.Empty<EntityUid>());
            em.EnsureComponent<CompanyComponent>(droneGrid).CompanyName = "TSF";
            em.EnsureComponent<CompanyComponent>(carrier).CompanyName = "TSF";
            Assert.That(guard.Fouled(drone, from, through), Is.True, "A ship of its faction on IFF is in the line.");

            em.System<SharedShuttleSystem>().AddIFFFlag(carrier, IFFFlags.HideLabel);
            Assert.That(guard.Fouled(drone, from, through), Is.False, "A ship running dark shows no colours to spare.");

            Assert.That(guard.HoldsFire(console, console, through), Is.False, "Only drones hold fire this way.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A gun told to hold fire stops at once: the timed burst AttemptShots gave it and a burst already under way both
    /// end, so it fires nothing more at the aim it held.
    /// </summary>
    [Test]
    public async Task HeldGunEndsItsBurstTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var guns = em.System<SharedGunSystem>();
            var gun = em.SpawnEntity("WeaponTurretHydra", map.GridCoords);
            var comp = em.GetComponent<GunComponent>(gun);

            guns.AttemptShots(gun, gun, comp, new EntityCoordinates(map.Grid.Owner, new Vector2(10f, 0f)), TimeSpan.FromSeconds(1));
            comp.BurstActivated = true;
            comp.BurstShotsCount = 1;
            Assert.That(em.GetComponent<AutoShootGunComponent>(gun).RemainingTime, Is.GreaterThan(TimeSpan.Zero));

            guns.WgStopShots(gun);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(em.GetComponent<AutoShootGunComponent>(gun).RemainingTime, Is.EqualTo(TimeSpan.Zero),
                    "The timed burst kept running.");
                Assert.That(comp.BurstActivated, Is.False, "The burst under way kept firing.");
                Assert.That(comp.BurstShotsCount, Is.Zero);
            }
        });

        await pair.CleanReturnAsync();
    }
}