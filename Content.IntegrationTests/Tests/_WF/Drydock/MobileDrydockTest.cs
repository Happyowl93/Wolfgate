using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Bank;
using Content.Server._WF.Drydock;
using Content.Server.Power.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Construction.Components;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Tests the ship-buildable mobile drydock: a console that pays in stored materials, and its crane.</summary>
[TestFixture]
public sealed class MobileDrydockTest
{
    private static readonly string[] StockMaterials = { "Steel", "Glass", "Plastic" };

    /// <summary>Spawns a host grid with a mobile crane, a material console, and MV power from a substation on the HV debug generator.</summary>
    private static (EntityUid Grid, EntityUid Crane, EntityUid Console) SpawnMobileYard(TestPair pair, MapId map)
    {
        var host = DrydockTest.CreateShip(pair, map, new Vector2(-300f, 0f));
        DrydockTest.SetFloor(pair, host, 6);
        var crane = DrydockTest.SpawnAnchored(pair, host, "WFMobileDrydockCrane", new Vector2i(0, 5));

        // Power comes from the far side of the bay, so the arm column (x = 0) stays clear of solid machines.
        for (var x = 0; x <= 4; x++)
        {
            DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(x, 5));
            DrydockTest.SpawnAnchored(pair, host, "CableMV", new Vector2i(x, 5));
        }

        DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(4, 4));
        DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(4, 3));
        DrydockTest.SpawnAnchored(pair, host, "DebugGenerator", new Vector2i(4, 3));
        DrydockTest.SpawnAnchored(pair, host, "DebugSubstation", new Vector2i(4, 4));
        DrydockTest.SpawnAnchored(pair, host, "CableMV", new Vector2i(4, 4));
        var console = DrydockTest.SpawnAnchored(pair, host, "WFComputerMobileDrydock", new Vector2i(5, 0));
        DrydockTest.LinkCrane(pair, console, crane);
        return (host, crane, console);
    }

    /// <summary>Builds a mobile yard with a damaged ship in its reach, and a funded payer.</summary>
    private static (EntityUid Console, EntityUid Crane, EntityUid Ship, EntityUid Payer) SetUpMobileRepair(TestPair pair, MapId map)
    {
        var yard = SpawnMobileYard(pair, map);
        var comp = pair.Server.EntMan.GetComponent<WFDrydockConsoleComponent>(yard.Console);
        Assert.That(pair.Server.System<WFDrydockSystem>().TryGetBerth(yard.Console, comp, out var berth), "The crane should make a berth.");

        var ship = DrydockTest.CreateDamagedShip(pair, map);
        DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
        return (yard.Console, yard.Crane, ship, DrydockTest.CreatePayer(pair, map));
    }

    /// <summary>Whether a crane has at least half its draw, the power a job needs to start.</summary>
    private static bool CraneHasPower(TestPair pair, EntityUid crane)
    {
        var consumer = pair.Server.EntMan.GetComponent<PowerConsumerComponent>(crane);
        return consumer.ReceivedPower >= 0.5f * consumer.DrawRate;
    }

    /// <summary>The material bill a console quotes for a ship's repair: each item's cost times the console's rates.</summary>
    private static Dictionary<string, int> QuotedBill(TestPair pair, EntityUid console, EntityUid ship)
    {
        var em = pair.Server.EntMan;
        var items = pair.Server.System<WFDrydockSystem>().BuildRepairItems(ship, em.GetComponent<WFDrydockConsoleComponent>(console));
        var pay = em.GetComponent<WFDrydockMaterialPaymentComponent>(console);

        var bill = new Dictionary<string, int>();
        foreach (var item in items)
        {
            foreach (var (id, rate) in pay.Rates)
            {
                var amount = (int) Math.Ceiling(item.Cost * rate);
                if (amount > 0)
                    bill[id] = bill.GetValueOrDefault(id) + amount;
            }
        }

        return bill;
    }
    /// <summary>A mobile crane holds the sheets a repair's bill is paid in, and the console holds none.</summary>
    [Test]
    public async Task MaterialConsoleChargesMaterials()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        var bank = server.System<BankSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default;
        var bill = new Dictionary<string, int>();
        var before = new Dictionary<string, int>();
        var bankBefore = 0;

        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            var storage = em.GetComponent<MaterialStorageComponent>(crane);
            Assert.That(materials.TryChangeMaterialAmount((crane, storage),
                StockMaterials.ToDictionary(m => m, _ => 10_000)), Is.True, "The crane should take its stock.");

            bill = QuotedBill(pair, console, ship);
            Assert.That(bill.GetValueOrDefault("Steel"), Is.GreaterThan(0), "The repair should quote steel.");
            Assert.That(bank.TryGetBalance(payer, out bankBefore), Is.True, "The payer should have an account.");
        });

        await DrydockTest.RunUntil(pair, 100, 10, () => CraneHasPower(pair, crane));

        await server.WaitAssertion(() =>
        {
            Assert.That(CraneHasPower(pair, crane), Is.True, "The mobile crane should be powered.");
            var storage = em.GetComponent<MaterialStorageComponent>(crane);
            foreach (var id in bill.Keys)
                before[id] = materials.GetMaterialAmount(crane, id, storage, localOnly: true);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.True, "The repair should start.");
            Assert.Multiple(() =>
            {
                Assert.That(em.TryGetComponent(console, out WFDrydockJobComponent? job), Is.True, "The job should exist.");
                Assert.That(job?.PaidInMaterials, Is.True, "The job should be paid in materials.");
                foreach (var (id, amount) in bill)
                {
                    Assert.That(materials.GetMaterialAmount(crane, id, storage, localOnly: true), Is.EqualTo(before[id] - amount),
                        $"The crane's {id} should drop by the quoted bill.");
                }

                Assert.That(em.HasComponent<MaterialStorageComponent>(console), Is.False, "The console should hold no materials.");
                Assert.That(bank.TryGetBalance(payer, out var bankAfter) && bankAfter == bankBefore, Is.True,
                    "The bank balance should be unchanged.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A mobile crane with no stock of its own draws a repair's bill from a silo it is linked to.</summary>
    [Test]
    public async Task MaterialConsoleDrawsFromSilo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        var silos = server.System<SharedOreSiloSystem>();
        var bank = server.System<BankSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default, silo = default;
        var bill = new Dictionary<string, int>();
        var before = new Dictionary<string, int>();
        var bankBefore = 0;

        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            var grid = server.System<SharedTransformSystem>().GetGrid(console)!.Value;

            // Off the arm column (x = 0) and clear of the console at (5, 0).
            silo = DrydockTest.SpawnAnchored(pair, grid, "MachineMaterialSilo", new Vector2i(5, 2));
            // Always powered, so the test does not need an APC on the grid.
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(silo, false);
            var siloStorage = em.GetComponent<MaterialStorageComponent>(silo);
            Assert.That(materials.TryChangeMaterialAmount((silo, siloStorage),
                StockMaterials.ToDictionary(m => m, _ => 10_000)), Is.True, "The silo should take its stock.");

            bill = QuotedBill(pair, console, ship);
            Assert.That(bill.GetValueOrDefault("Steel"), Is.GreaterThan(0), "The repair should quote steel.");
            Assert.That(bank.TryGetBalance(payer, out bankBefore), Is.True, "The payer should have an account.");
        });

        await DrydockTest.RunUntil(pair, 100, 10, () => CraneHasPower(pair, crane));
        await DrydockTest.RunUntil(pair, 100, 10, () => em.GetComponent<ApcPowerReceiverComponent>(silo).Powered);

        await server.WaitAssertion(() =>
        {
            Assert.That(CraneHasPower(pair, crane), Is.True, "The mobile crane should be powered.");
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(silo).Powered, Is.True, "The silo should be powered.");

            // Link the crane with the silo's toggle message, as its window sends it.
            var siloComp = em.GetComponent<OreSiloComponent>(silo);
            Assert.That(silos.CanTransmitMaterials((silo, siloComp, em.GetComponent<TransformComponent>(silo)), crane), Is.True,
                "A powered silo on the same grid should reach the crane.");
            em.EventBus.RaiseLocalEvent(silo, new ToggleOreSiloClientMessage(em.GetNetEntity(crane)));
            Assert.That(em.GetComponent<OreSiloClientComponent>(crane).Silo, Is.EqualTo(silo), "The crane should link to the silo.");

            var craneStorage = em.GetComponent<MaterialStorageComponent>(crane);
            Assert.That(materials.GetMaterialAmount(crane, "Steel", craneStorage, localOnly: true), Is.EqualTo(0),
                "The crane should hold no steel of its own.");
            Assert.That(materials.GetMaterialAmount(crane, "Steel", craneStorage), Is.GreaterThan(0),
                "The linked silo's steel should count toward the crane.");

            var siloStorage = em.GetComponent<MaterialStorageComponent>(silo);
            foreach (var id in bill.Keys)
                before[id] = materials.GetMaterialAmount(silo, id, siloStorage, localOnly: true);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.True, "The repair should start.");
            Assert.Multiple(() =>
            {
                Assert.That(em.TryGetComponent(console, out WFDrydockJobComponent? job), Is.True, "The job should exist.");
                Assert.That(job?.PaidInMaterials, Is.True, "The job should be paid in materials.");
                foreach (var (id, amount) in bill)
                {
                    Assert.That(materials.GetMaterialAmount(silo, id, siloStorage, localOnly: true), Is.EqualTo(before[id] - amount),
                        $"The silo's {id} should drop by the quoted bill.");
                    Assert.That(materials.GetMaterialAmount(crane, id, craneStorage, localOnly: true), Is.EqualTo(0),
                        $"The crane should stay empty of {id}.");
                }

                Assert.That(bank.TryGetBalance(payer, out var bankAfter) && bankAfter == bankBefore, Is.True,
                    "The bank balance should be unchanged.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A mobile crane with no stock refuses a repair, so no job starts and nothing is taken.</summary>
    [Test]
    public async Task MaterialConsoleRefusesWhenShort()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        var bank = server.System<BankSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default;
        var bankBefore = 0;

        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            Assert.That(bank.TryGetBalance(payer, out bankBefore), Is.True, "The payer should have an account.");
        });

        await DrydockTest.RunUntil(pair, 100, 10, () => CraneHasPower(pair, crane));

        await server.WaitAssertion(() =>
        {
            Assert.That(CraneHasPower(pair, crane), Is.True, "The mobile crane should be powered.");
            var storage = em.GetComponent<MaterialStorageComponent>(crane);
            var steelBefore = materials.GetMaterialAmount(crane, "Steel", storage, localOnly: true);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.False, "Work should be refused without materials.");
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "No job should start.");
                Assert.That(materials.GetMaterialAmount(crane, "Steel", storage, localOnly: true), Is.EqualTo(steelBefore), "Storage should be unchanged.");
                Assert.That(bank.TryGetBalance(payer, out var bankAfter) && bankAfter == bankBefore, Is.True,
                    "The bank balance should be unchanged.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The mobile crane's board builds it, and the Shuttlecraft tech unlocks both of its boards.</summary>
    [Test]
    public async Task CraneIsConstructible()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ProtoMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var board = em.SpawnEntity("WFMobileDrydockCraneMachineCircuitboard", new MapCoordinates(0, 0, map.MapId));
            Assert.That(em.GetComponent<MachineBoardComponent>(board).Prototype.Id, Is.EqualTo("WFMobileDrydockCrane"),
                "The crane board should build a mobile drydock crane.");

            var unlocks = protos.Index<TechnologyPrototype>("Shuttlecraft").RecipeUnlocks.Select(r => r.ToString()).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(unlocks, Does.Contain("WFComputerMobileDrydockCircuitboard"), "Shuttlecraft should unlock the console board.");
                Assert.That(unlocks, Does.Contain("WFMobileDrydockCraneMachineCircuitboard"), "Shuttlecraft should unlock the crane board.");
                Assert.That(protos.HasIndex<LatheRecipePrototype>("WFComputerMobileDrydockCircuitboard"), Is.True, "The console board recipe should exist.");
                Assert.That(protos.HasIndex<LatheRecipePrototype>("WFMobileDrydockCraneMachineCircuitboard"), Is.True, "The crane board recipe should exist.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
