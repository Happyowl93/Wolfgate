using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    [Dependency] private AtmosphereSystem _wgAtmosphere = default!;

    /// <summary>Whether the air around a speaker is too thin to carry a normal voice, so it only reaches whisper range.</summary>
    public bool WgSpeechMuffledByVacuum(EntityUid source)
    {
        return _wgAtmosphere.GetContainingMixture(source) is not { } air || air.Pressure <= Atmospherics.HazardLowPressure;
    }
}
