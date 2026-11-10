#nullable enable
using System.Linq;
using Content.Server._WF.Cyborgs;
using Content.Server.Power.Components;
using Content.Server.Silicons.Borgs;
using Content.Server.Stack;
using Content.Shared._NF.Silicons.Borgs;
using Content.Shared._WF.Cyborgs;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Cyborgs;

/// <summary>
/// An engineering cyborg is issued stocked construction stacks and gripper hands, and its refilling stacks restock
/// only while it sits in a powered charger.
/// </summary>
[TestFixture]
[TestOf(typeof(WFBorgStackRefillSystem))]
public sealed class BorgStackRefillTest
{
    private const string Borg = "BorgChassisEngineer";
    private const string Charger = "BorgCharger";
    private const string Cell = "PowerCellMedium";
    private const string Construction = "BorgModuleConstruction";
    private const string Tool = "BorgModuleTool";
    private const string Steel = "WFSheetSteelBorg";

    [Test]
    public async Task RefillsOnlyInChargerTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var borgs = server.System<BorgSystem>();
        var stacks = server.System<StackSystem>();
        var refill = server.System<WFBorgStackRefillSystem>();
        var containers = server.System<SharedContainerSystem>();

        var borg = EntityUid.Invalid;
        var charger = EntityUid.Invalid;
        var steel = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            borg = entMan.SpawnEntity(Borg, map.GridCoords);
            charger = entMan.SpawnEntity(Charger, map.GridCoords);
            entMan.GetComponent<ApcPowerReceiverComponent>(charger).NeedsPower = false;

            // The charger reads as off unless what it holds has a battery.
            var cell = entMan.SpawnEntity(Cell, map.GridCoords);
            // Straight into the container: the chassis is locked, so the slot refuses an insert.
            var cellSlot = containers.GetContainer(borg, "cell_slot");
            Assert.That(containers.Insert(cell, cellSlot, force: true), "The cyborg did not take a power cell.");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var modules = entMan.GetComponent<BorgChassisComponent>(borg).ModuleContainer.ContainedEntities;
            string? Proto(EntityUid uid) => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;

            var tool = modules.Single(m => Proto(m) == Tool);
            Assert.That(entMan.HasComponent<DroppableBorgModuleComponent>(tool), "The tool module has no gripper hands.");

            // Modules install when a mind powers the chassis up; this one has none.
            borgs.InstallAllModules(borg);
            borgs.UnselectModule(borg);
            borgs.SelectModule(borg, modules.Single(m => Proto(m) == Construction));

            var query = entMan.EntityQueryEnumerator<WFBorgStackRefillComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.ParentUid == borg && Proto(uid) == Steel)
                    steel = uid;
            }

            Assert.That(steel.IsValid(), "The construction module gave no refilling steel.");
            var stack = entMan.GetComponent<StackComponent>(steel);
            Assert.That(stack.Count, Is.EqualTo(15), "The steel stack did not start stocked.");

            stacks.SetCount(steel, 3, stack);
            refill.RefillAll();
            Assert.That(stack.Count, Is.EqualTo(3), "A stack refilled outside a charger.");

            var storage = containers.GetContainer(charger, "entity_storage");
            Assert.That(containers.Insert(borg, storage, force: true), "The cyborg did not go into the charger.");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var status = entMan.GetComponent<ChargerComponent>(charger).Status;
            Assert.That(status, Is.EqualTo(CellChargerStatus.Charging).Or.EqualTo(CellChargerStatus.Charged));

            var stack = entMan.GetComponent<StackComponent>(steel);
            stacks.SetCount(steel, 3, stack);
            refill.RefillAll();
            Assert.That(stack.Count, Is.EqualTo(4), "A stack in hand did not refill in the charger.");

            // Stowed in its module the stack sits one level deeper.
            borgs.UnselectModule(borg);
            refill.RefillAll();
            Assert.That(stack.Count, Is.EqualTo(5), "A stowed stack did not refill in the charger.");

            stacks.SetCount(steel, 15, stack);
            refill.RefillAll();
            Assert.That(stack.Count, Is.EqualTo(15), "A stack refilled past its target.");
        });

        await pair.CleanReturnAsync();
    }
}
