using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// Plays the emulator's continuous 48 kHz stereo output. Content can't stream audio, so it plays short clips that
/// overlap by a crossfade, which hides the frame-long jitter in when each one starts.
/// </summary>
public sealed class MameArcadeAudio : IDisposable
{
    public const int SampleRate = 48000;

    private const int ChunkFrames = SampleRate / 10;
    private const int OverlapFrames = SampleRate * 3 / 100;
    private const float ChunkSeconds = (float) ChunkFrames / SampleRate;

    // Past this the emulator is running ahead of playback; drop the oldest sound rather than drift further behind.
    private const int MaxBacklogFrames = SampleRate / 2;

    private readonly IAudioManager _audio;
    private readonly IConfigurationManager _cfg;
    private readonly List<short> _pending = new();
    private readonly short[] _chunk = new short[(ChunkFrames + OverlapFrames) * 2];
    private Clip? _current;
    private Clip? _previous;

    // Set when playback ran dry mid-game; it then waits for half a clip more, so short waits don't turn into crackle.
    private bool _starved;

    public MameArcadeAudio(IAudioManager audio, IConfigurationManager cfg)
    {
        _audio = audio;
        _cfg = cfg;
    }

    /// <summary>Adds interleaved 16-bit stereo samples.</summary>
    public void Feed(short[] samples)
    {
        _pending.AddRange(samples);

        var excess = _pending.Count - MaxBacklogFrames * 2;
        if (excess > 0)
            _pending.RemoveRange(0, excess);
    }

    /// <summary>Starts the next clip when the playing one reaches its fade.</summary>
    public void Update()
    {
        if (_current != null)
        {
            _current.Source.Gain = Gain();
            if (_current.Source.Playing && _current.Source.PlaybackPosition < ChunkSeconds)
                return;
        }

        var needed = _starved ? _chunk.Length + ChunkFrames : _chunk.Length;
        if (_pending.Count < needed)
        {
            if (_current is { Source.Playing: false })
                _starved = true;
            return;
        }

        _starved = false;

        // Waiting out an underrun leaves extra sound queued; drop it rather than keep the added lag.
        var excess = _pending.Count - (_chunk.Length + ChunkFrames * 2);
        if (excess > 0)
            _pending.RemoveRange(0, excess & ~1);

        _pending.CopyTo(0, _chunk, 0, _chunk.Length);
        _pending.RemoveRange(0, ChunkFrames * 2);
        Fade(_chunk);

        var stream = _audio.LoadAudioRaw(_chunk, 2, SampleRate);
        if (_audio.CreateAudioSource(stream) is not { } source)
        {
            stream.Dispose();
            return;
        }

        source.Global = true;
        source.RolloffFactor = 0;
        source.Gain = Gain();
        source.StartPlaying();

        _previous?.Dispose();
        _previous = _current;
        _current = new Clip(stream, source);
    }

    public void Dispose()
    {
        _previous?.Dispose();
        _current?.Dispose();
        _previous = _current = null;
    }

    private float Gain() => _cfg.GetCVar(CCVars.InterfaceVolume);

    /// <summary>Ramps the overlapping head and tail so two clips sum to the original signal.</summary>
    private static void Fade(short[] chunk)
    {
        var frames = chunk.Length / 2;
        for (var i = 0; i < OverlapFrames; i++)
        {
            var rise = (float) i / OverlapFrames;
            ScaleFrame(chunk, i, rise);
            ScaleFrame(chunk, frames - 1 - i, rise);
        }
    }

    private static void ScaleFrame(short[] chunk, int frame, float gain)
    {
        chunk[frame * 2] = (short) (chunk[frame * 2] * gain);
        chunk[frame * 2 + 1] = (short) (chunk[frame * 2 + 1] * gain);
    }

    private sealed record Clip(AudioStream Stream, IAudioSource Source) : IDisposable
    {
        public void Dispose()
        {
            Source.Dispose();
            Stream.Dispose();
        }
    }
}
