using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.ShipArmor;

/// <summary>Raising shields on a grid with anchored adamantite switches them off.</summary>
[TestFixture]
public sealed class WFShieldDisruptingArmorTest
{
    private const string WallProto = "WFWallAdamantite";
    private const string GeneratorProto = "ShieldGeneratorSmall";

    [Test]
    public async Task AdamantiteBlocksShieldDeployment()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var mapSys = server.System<SharedMapSystem>();
            var xformSys = server.System<SharedTransformSystem>();
            var shields = server.System<ShipShieldsSystem>();
            var grid = map.Grid;

            mapSys.SetTile(grid.Owner, grid.Comp, new Vector2i(1, 0), new Tile(1));
            var wall = entMan.SpawnEntity(WallProto, new EntityCoordinates(grid.Owner, new Vector2(1.5f, 0.5f)));
            var wallXform = entMan.GetComponent<TransformComponent>(wall);
            if (!wallXform.Anchored)
                xformSys.AnchorEntity((wall, wallXform));

            var generator = entMan.SpawnEntity(GeneratorProto, map.GridCoords);
            var emitter = entMan.GetComponent<ShipShieldEmitterComponent>(generator);
            entMan.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null, "Shield should not deploy with adamantite.");
            Assert.That(entMan.GetComponent<WFShipShieldShuntComponent>(grid.Owner).Enabled, Is.False,
                "Adamantite should switch the shields off.");

            xformSys.Unanchor(wall, wallXform);
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null, "Shields should stay off until raised again.");

            shields.SetWolfgateShieldEnabled(grid.Owner, true);
            shields.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null, "Raising again without adamantite should deploy the shield.");
        });

        await pair.CleanReturnAsync();
    }
}
