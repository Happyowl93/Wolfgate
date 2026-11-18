#nullable enable
using Content.Server._WG.SafetyDepositBox;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.SafetyDepositBox;

/// <summary>
/// A box takes nothing that carries a plant's genes into the next round: a plant analyzer saves them in its gene
/// banks and a seed packet in its seed.
/// </summary>
[TestFixture]
[TestOf(typeof(SafetyDepositBoxSystem))]
public sealed class SafetyDepositBoxBlacklistTest
{
    private const string BoxProto = "SafetyDepositBoxLarge";

    [TestCase("Wrench", true, TestName = "ATakenItemGoesIn")]
    [TestCase("PlantAnalyzer", false, TestName = "APlantAnalyzerIsRefused")]
    [TestCase("TomatoSeeds", false, TestName = "ASeedPacketIsRefused")]
    public async Task BoxTakes(string proto, bool taken)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var box = entMan.SpawnEntity(BoxProto, map.GridCoords);
            var item = entMan.SpawnEntity(proto, map.GridCoords);

            Assert.That(entMan.System<SharedStorageSystem>().CanInsert(box, item, out _), Is.EqualTo(taken),
                taken ? $"The box refuses {proto}." : $"The box takes {proto}.");
        });

        await pair.CleanReturnAsync();
    }
}
