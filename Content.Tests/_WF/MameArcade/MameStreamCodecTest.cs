using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.MameArcade;
using NUnit.Framework;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._WF.MameArcade;

/// <summary>What a cabinet streams to bystanders comes back out as it went in, and bad packets are refused.</summary>
[TestFixture]
[TestOf(typeof(MameVideoEncoder))]
[TestOf(typeof(MameAudioCodec))]
public sealed class MameStreamCodecTest
{
    private const int Width = 320;
    private const int Height = 224;

    [Test]
    public void WatcherSeesTheSameScreen()
    {
        var frame = Scene(0);
        var encoder = new MameVideoEncoder();
        var decoder = new MameVideoDecoder();

        var key = encoder.Encode(frame, Width, Height, keyframe: true);
        Assert.That(decoder.Decode(key));
        Assert.That(decoder.Width, Is.EqualTo(Width));
        AssertSame(decoder.Frame, frame);

        // A sprite moves: only its tiles go out again.
        var moved = Scene(12);
        var change = encoder.Encode(moved, Width, Height, keyframe: false);
        Assert.That(change.Length, Is.LessThan(key.Length / 4), "A change sends only the tiles that differ");
        Assert.That(decoder.Decode(change));
        AssertSame(decoder.Frame, moved);

        var still = encoder.Encode(moved, Width, Height, keyframe: false);
        Assert.That(still.Length, Is.LessThan(64), "An unchanged screen costs next to nothing");
        Assert.That(decoder.Decode(still));
        AssertSame(decoder.Frame, moved);
    }

    [Test]
    public void LateWatcherWaitsForAKeyframe()
    {
        var encoder = new MameVideoEncoder();
        encoder.Encode(Scene(0), Width, Height, keyframe: true);
        var change = encoder.Encode(Scene(8), Width, Height, keyframe: false);

        var late = new MameVideoDecoder();
        Assert.That(late.Decode(change), Is.False, "A change means nothing without the screen it changes");

        var key = encoder.Encode(Scene(16), Width, Height, keyframe: true);
        Assert.That(late.Decode(key));
        AssertSame(late.Frame, Scene(16));
    }

    [Test]
    public void BadPicturePacketsAreRefused()
    {
        var decoder = new MameVideoDecoder();
        Assert.That(decoder.Decode(Array.Empty<byte>()), Is.False);
        Assert.That(decoder.Decode(new byte[] { 1, 0xff, 0xff, 0xff, 0xff, 1, 2, 3 }), Is.False, "Too large a screen");
        Assert.That(decoder.Decode(new byte[] { 1, 64, 1, 224, 0, 0xde, 0xad, 0xbe, 0xef }), Is.False, "Not deflate data");

        var key = new MameVideoEncoder().Encode(Scene(0), Width, Height, keyframe: true);
        Assert.That(decoder.Decode(key[..(key.Length / 2)]), Is.False, "Cut short");
    }

    [Test]
    public void SoundSurvivesTheTrip()
    {
        var samples = new short[MameAudioCodec.SampleRate / 10];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short) (12000 * Math.Sin(2 * Math.PI * 440 * i / MameAudioCodec.SampleRate));
        }

        var packet = MameAudioCodec.Encode(samples);
        Assert.That(packet.Length, Is.EqualTo(3 + samples.Length / 2), "4 bits a sample");

        var decoded = MameAudioCodec.Decode(packet);
        Assert.That(decoded, Has.Length.EqualTo(samples.Length));

        double signal = 0, noise = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            signal += (double) samples[i] * samples[i];
            noise += Math.Pow(samples[i] - decoded[i], 2);
        }

        Assert.That(10 * Math.Log10(signal / noise), Is.GreaterThan(20), "Signal to noise in dB");
        Assert.That(MameAudioCodec.Decode(new byte[] { 1 }), Is.Empty);
    }

    [Test]
    public void DownmixAveragesThreeStereoFrames()
    {
        var stereo = new List<short> { 300, 0, 0, 0, 0, 600, 100, 100, 50 };
        var mono = new List<short>();
        MameAudioCodec.Downmix(stereo, mono);

        Assert.That(mono, Is.EqualTo(new short[] { 150 }));
        Assert.That(stereo, Is.EqualTo(new short[] { 100, 100, 50 }), "A partial group waits for the rest");
    }

    /// <summary>A gradient backdrop with a block sprite at <paramref name="x"/>, in colours 15-bit holds exactly.</summary>
    private static Rgba32[] Scene(int x)
    {
        var pixels = new Rgba32[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var i = 0; i < Width; i++)
            {
                pixels[y * Width + i] = Expand((byte) (i * 31 / Width), (byte) (y * 31 / Height), 8);
            }
        }

        for (var y = 100; y < 140; y++)
        {
            for (var i = 40 + x; i < 72 + x; i++)
            {
                pixels[y * Width + i] = Expand(31, (byte) (i % 7), (byte) (y % 5));
            }
        }

        return pixels;
    }

    private static Rgba32 Expand(byte r, byte g, byte b) => MameVideoCodec.Unpack((ushort) (r << 10 | g << 5 | b));

    private static void AssertSame(Rgba32[] actual, Rgba32[] expected)
    {
        var first = Enumerable.Range(0, expected.Length).FirstOrDefault(i => actual[i] != expected[i], -1);
        Assert.That(first, Is.EqualTo(-1), $"Pixel {first} differs");
    }
}
