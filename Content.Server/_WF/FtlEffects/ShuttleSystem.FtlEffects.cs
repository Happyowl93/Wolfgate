using Robust.Shared.Audio;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>The FTL spool sound, for <see cref="Content.Server._WF.FtlEffects.FtlSpoolAudioSystem"/> to time.</summary>
    public SoundSpecifier WfStartupSound => _startupSound;
}
