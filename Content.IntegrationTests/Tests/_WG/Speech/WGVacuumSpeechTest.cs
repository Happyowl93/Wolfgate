using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Speech;

/// <summary>Speech only carries as a whisper where the air is too thin, whatever the speaker wears.</summary>
[TestFixture]
public sealed class WGVacuumSpeechTest
{
    [Test]
    public async Task ThinAirMuffles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var chat = server.System<ChatSystem>();
            var atmos = server.System<AtmosphereSystem>();
            var mob = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(chat.WgSpeechMuffledByVacuum(mob), Is.True, "With no air a voice only reaches whisper range.");

            atmos.SetMapAtmosphere(map.MapUid, false, Air(Atmospherics.OneAtmosphere));
            Assert.That(chat.WgSpeechMuffledByVacuum(mob), Is.False, "Normal air carries a normal voice.");

            atmos.SetMapAtmosphere(map.MapUid, false, Air(Atmospherics.HazardLowPressure / 2f));
            Assert.That(chat.WgSpeechMuffledByVacuum(mob), Is.True, "Air below hazard pressure only carries a whisper.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Room-temperature air at about <paramref name="kilopascals"/>.</summary>
    private static GasMixture Air(float kilopascals)
    {
        var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
        var share = kilopascals / Atmospherics.OneAtmosphere;
        air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard * share);
        air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard * share);
        return air;
    }
}
