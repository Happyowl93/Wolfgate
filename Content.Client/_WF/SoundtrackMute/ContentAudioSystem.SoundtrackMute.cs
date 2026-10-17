using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Client.Audio;

public sealed partial class ContentAudioSystem
{
    private static readonly float WfMuteFloor = SharedAudioSystem.VolumeToGain(MinVolume);

    private EntityUid? _wfMutedStream;
    private float? _wfMuteHeldTarget;
    private float _wfMuteLevel = 1f;

    /// <summary>
    /// Scales the music track's gain by level and pauses the music at 0, holding any fade-in so it can't read that
    /// gain back.
    /// </summary>
    public void MuteSoundtrackWolfgate(float level)
    {
        var stream = _ambientMusicStream;
        if (stream != _wfMutedStream)
        {
            _wfMutedStream = stream;
            _wfMuteHeldTarget = null;
        }

        var last = _wfMuteLevel;
        _wfMuteLevel = level;

        // At silence the shared pause takes over, which also holds any track a music switch starts.
        if (level <= 0f)
            PauseMusicWolfgate(WfMusicPauseReason.SoundtrackMute);
        else if (last <= 0f)
            ResumeMusicWolfgate(WfMusicPauseReason.SoundtrackMute);

        // A switched-out track fades from its own volume each tick; keep it under the mute too.
        if (level < 1f)
        {
            foreach (var fading in _fadingOut.Keys)
            {
                if (TryComp<AudioComponent>(fading, out var fadingAudio))
                    ScaleMusicGain(fadingAudio, level);
            }
        }

        if (stream == null || !TryComp<AudioComponent>(stream, out var audio))
            return;

        if (level >= 1f)
        {
            if (_wfMuteHeldTarget is { } held)
                _audio.SetVolume(stream, held, audio);
            else if (last < 1f)
                audio.Gain = SharedAudioSystem.VolumeToGain(audio.Params.Volume);

            _wfMuteHeldTarget = null;
            return;
        }

        if (_fadingIn.Remove(stream.Value, out var fade))
            _wfMuteHeldTarget = fade.TargetVolume;

        if (level > last && _wfMuteHeldTarget is { } target)
        {
            _audio.SetVolume(stream, target, audio);
            _wfMuteHeldTarget = null;
        }

        ScaleMusicGain(audio, level);
    }

    private static void ScaleMusicGain(AudioComponent audio, float level)
    {
        if (audio.Playing)
            audio.Gain = SharedAudioSystem.VolumeToGain(audio.Params.Volume) * MathF.Max(level, WfMuteFloor);
    }
}
