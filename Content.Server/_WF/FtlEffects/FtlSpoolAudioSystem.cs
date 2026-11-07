using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.FtlEffects;

/// <summary>
/// Times the FTL spool sound to the spool. The jump in the sound falls <see cref="ShuttleSystem.DefaultStartupTime"/>
/// into it, so a longer spool hums with the drive charging first and starts the sound that long before the jump, and a
/// shorter one starts the sound partway in. Either way the sound's jump is the ship's.
/// </summary>
public sealed class FtlSpoolAudioSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;

    /// <summary>The drive charging, looped until the spool sound proper starts.</summary>
    public static readonly SoundSpecifier ChargeSound = new SoundPathSpecifier("/Audio/Ambience/Objects/gravity_gen_hum.ogg")
    {
        Params = AudioParams.Default.WithVolume(-4f).WithLoop(true),
    };

    private readonly Dictionary<EntityUid, Spool> _spools = new();
    private readonly List<EntityUid> _done = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(ShuttleSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var lead = TimeSpan.FromSeconds(_shuttles.DefaultStartupTime);
        var query = EntityQueryEnumerator<FTLComponent>();
        while (query.MoveNext(out var uid, out var ftl))
        {
            // Docked ships go along with their lead's jump and its sound.
            if (ftl.State != FTLState.Starting || ftl.LinkedShuttle != null)
                continue;

            // A new spool on the same ship, such as after a cancelled one, starts over.
            if (!_spools.TryGetValue(uid, out var spool) || spool.Ftl != ftl)
            {
                if (spool != null)
                    _audio.Stop(spool.Charge);

                spool = new Spool(ftl);
                _spools[uid] = spool;
            }

            if (spool.Sounding)
                continue;

            var left = ftl.StateTime.End - now;
            if (left > lead)
            {
                spool.Charge ??= PlayOnGrid(ChargeSound, uid);
                continue;
            }

            spool.Charge = _audio.Stop(spool.Charge);
            spool.Sounding = true;
            var audio = _audio.PlayPvs(_shuttles.WfStartupSound, uid);
            _audio.SetGridAudio(audio);
            if (left < lead)
                _audio.SetPlaybackPosition(audio, (float) (lead - left).TotalSeconds);

            // Upstream stops this on a cancelled spool and leaves its tail behind at the departure point.
            ftl.StartupStream = audio?.Entity;
        }

        _done.Clear();
        foreach (var (uid, spool) in _spools)
        {
            if (!TryComp<FTLComponent>(uid, out var ftl) || ftl != spool.Ftl || ftl.State != FTLState.Starting)
                _done.Add(uid);
        }

        foreach (var uid in _done)
        {
            if (_spools.Remove(uid, out var spool))
                _audio.Stop(spool.Charge);
        }
    }

    private EntityUid? PlayOnGrid(SoundSpecifier sound, EntityUid grid)
    {
        var audio = _audio.PlayPvs(sound, grid);
        _audio.SetGridAudio(audio);
        return audio?.Entity;
    }

    /// <summary>One ship's spool: its FTL component, the charging hum while it plays, and whether the sound proper has begun.</summary>
    private sealed class Spool(FTLComponent ftl)
    {
        public readonly FTLComponent Ftl = ftl;
        public EntityUid? Charge;
        public bool Sounding;
    }
}
