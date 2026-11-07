using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.FtlEffects;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.FtlEffects;

/// <summary>The FTL spool sound's jump lands on the ship's jump, however long the spool; a long one hums first.</summary>
[TestFixture]
[TestOf(typeof(FtlSpoolAudioSystem))]
public sealed class FtlSpoolAudioTest
{
    /// <summary>How far off the jump the sound's jump may land: a couple of ticks.</summary>
    private const double Tolerance = 0.1;

    private static bool Humming(IEntityManager entities, EntityUid grid)
    {
        var query = entities.EntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (query.MoveNext(out _, out var audio, out var xform))
        {
            var file = audio.FileName;
            if (xform.ParentUid == grid && file.EndsWith("gravity_gen_hum.ogg"))
                return true;
        }

        return false;
    }

    /// <summary>Starts a jump with a given spool from a small test grid, and returns the grid.</summary>
    private static EntityUid Spool(IEntityManager entities, TestMapData source, TestMapData target, float startup)
    {
        var maps = entities.System<SharedMapSystem>();
        var shuttles = entities.System<ShuttleSystem>();
        var shuttle = source.Grid.Owner;
        maps.SetTiles(shuttle, source.Grid.Comp, new List<(Vector2i, Tile)>
        {
            (new Vector2i(0, 0), new Tile(1)),
            (new Vector2i(1, 0), new Tile(1)),
        });
        Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
        shuttles.FTLToCoordinates(shuttle, entities.GetComponent<ShuttleComponent>(shuttle),
            new EntityCoordinates(target.MapUid, new Vector2(50, 50)), Angle.Zero,
            startupTime: startup, hyperspaceTime: shuttles.DefaultArrivalTime + 2f);
        return shuttle;
    }

    /// <summary>When the spool sound reaches its jump, by its start and the lead-in before the jump in it.</summary>
    private static TimeSpan SoundJump(IEntityManager entities, FTLComponent ftl)
    {
        var lead = TimeSpan.FromSeconds(entities.System<ShuttleSystem>().DefaultStartupTime);
        return entities.GetComponent<AudioComponent>(ftl.StartupStream!.Value).AudioStart + lead;
    }

    /// <summary>Deletes the test grid and maps, and the FTL map the jump made.</summary>
    private static void CleanUp(IEntityManager entities, EntityUid shuttle, TestMapData source, TestMapData target)
    {
        entities.DeleteEntity(shuttle);
        var maps = new List<EntityUid> { source.MapUid, target.MapUid };
        var query = entities.AllEntityQueryEnumerator<FTLMapComponent>();
        while (query.MoveNext(out var uid, out _))
            maps.Add(uid);
        foreach (var uid in maps)
            entities.DeleteEntity(uid);
    }

    [Test]
    public async Task ALongSpoolHumsFirstAndItsSoundJumpsWithTheShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var shuttle = EntityUid.Invalid;

        await server.WaitPost(() =>
            shuttle = Spool(entities, source, target, entities.System<ShuttleSystem>().DefaultStartupTime + 4f));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<FTLComponent>(shuttle).StartupStream, Is.Null, "The spool sound waits for its time.");
            Assert.That(Humming(entities, shuttle), "The drive hums while it charges.");
        });

        var sounding = false;
        for (var i = 0; i < 600 && !sounding; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => sounding = entities.GetComponent<FTLComponent>(shuttle).StartupStream != null);
        }

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(sounding, "The spool sound starts before the jump.");
            var ftl = entities.GetComponent<FTLComponent>(shuttle);
            Assert.That(ftl.State, Is.EqualTo(FTLState.Starting));
            Assert.That(Math.Abs((SoundJump(entities, ftl) - ftl.StateTime.End).TotalSeconds), Is.LessThan(Tolerance),
                "The sound's jump lands on the ship's.");
            Assert.That(Humming(entities, shuttle), Is.False, "The hum stops as the spool sound starts.");
        });

        await server.WaitPost(() => CleanUp(entities, shuttle, source, target));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AShortSpoolStartsItsSoundPartway()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var shuttle = EntityUid.Invalid;

        await server.WaitPost(() => shuttle = Spool(entities, source, target, 2f));
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            var ftl = entities.GetComponent<FTLComponent>(shuttle);
            Assert.That(ftl.State, Is.EqualTo(FTLState.Starting));
            Assert.That(ftl.StartupStream, Is.Not.Null, "A short spool sounds at once.");
            Assert.That(Humming(entities, shuttle), Is.False, "A short spool has no time to hum.");
            Assert.That(Math.Abs((SoundJump(entities, ftl) - ftl.StateTime.End).TotalSeconds), Is.LessThan(Tolerance),
                "The sound starts partway, so its jump still lands on the ship's.");
        });

        await server.WaitPost(() => CleanUp(entities, shuttle, source, target));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACancelledSpoolStopsHumming()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var shuttle = EntityUid.Invalid;

        await server.WaitPost(() => shuttle = Spool(entities, source, target, 30f));
        await pair.RunTicksSync(5);
        await server.WaitPost(() =>
        {
            Assert.That(Humming(entities, shuttle));
            Assert.That(entities.System<ShuttleSystem>().WfAbortFtlSpool(shuttle), Is.True);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(Humming(entities, shuttle), Is.False, "Cancelling the spool stops the hum."));

        await server.WaitPost(() => CleanUp(entities, shuttle, source, target));
        await pair.CleanReturnAsync();
    }
}
