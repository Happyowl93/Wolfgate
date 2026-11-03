namespace Content.Client._WF.MameArcade;

/// <summary>
/// Shrinks the game's sound for nearby players: 16 kHz mono, 4-bit IMA ADPCM. Each packet starts its own predictor,
/// so one can be decoded without the ones before it.
/// </summary>
public static class MameAudioCodec
{
    public const int SampleRate = 16000;

    // The emulator's 48 kHz frames averaged three at a time.
    private const int Decimation = MameArcadeAudio.SampleRate / SampleRate;

    private const int HeaderBytes = 3;

    private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

    private static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97,
        107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
        876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871,
        5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
        27086, 29794, 32767,
    };

    /// <summary>Moves interleaved 48 kHz stereo into 16 kHz mono; a partial group of frames stays behind.</summary>
    public static void Downmix(List<short> stereo, List<short> into)
    {
        var frames = stereo.Count / 2 / Decimation * Decimation;
        for (var frame = 0; frame < frames; frame += Decimation)
        {
            var sum = 0;
            for (var i = 0; i < Decimation; i++)
            {
                sum += stereo[(frame + i) * 2] + stereo[(frame + i) * 2 + 1];
            }

            into.Add((short) (sum / (Decimation * 2)));
        }

        stereo.RemoveRange(0, frames * 2);
    }

    public static byte[] Encode(IReadOnlyList<short> samples)
    {
        var packet = new byte[HeaderBytes + (samples.Count + 1) / 2];
        int predictor = samples.Count > 0 ? samples[0] : 0;
        var index = 0;
        packet[0] = (byte) predictor;
        packet[1] = (byte) (predictor >> 8);
        packet[2] = (byte) index;

        for (var i = 0; i < samples.Count; i++)
        {
            var step = StepTable[index];
            var diff = samples[i] - predictor;
            var code = 0;
            if (diff < 0)
            {
                code = 8;
                diff = -diff;
            }

            var delta = step >> 3;
            if (diff >= step)
            {
                code |= 4;
                diff -= step;
                delta += step;
            }

            if (diff >= step >> 1)
            {
                code |= 2;
                diff -= step >> 1;
                delta += step >> 1;
            }

            if (diff >= step >> 2)
            {
                code |= 1;
                delta += step >> 2;
            }

            predictor = Math.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
            index = Math.Clamp(index + IndexTable[code], 0, StepTable.Length - 1);
            packet[HeaderBytes + i / 2] |= (byte) (i % 2 == 0 ? code : code << 4);
        }

        return packet;
    }

    /// <summary>The samples in a packet, or none if it is malformed.</summary>
    public static short[] Decode(byte[] packet)
    {
        if (packet.Length < HeaderBytes)
            return Array.Empty<short>();

        int predictor = (short) (packet[0] | packet[1] << 8);
        var index = Math.Clamp((int) packet[2], 0, StepTable.Length - 1);
        var samples = new short[(packet.Length - HeaderBytes) * 2];

        for (var i = 0; i < samples.Length; i++)
        {
            var b = packet[HeaderBytes + i / 2];
            var code = i % 2 == 0 ? b & 0xf : b >> 4;
            var step = StepTable[index];
            var delta = step >> 3;
            if ((code & 4) != 0)
                delta += step;
            if ((code & 2) != 0)
                delta += step >> 1;
            if ((code & 1) != 0)
                delta += step >> 2;

            predictor = Math.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
            index = Math.Clamp(index + IndexTable[code], 0, StepTable.Length - 1);
            samples[i] = (short) predictor;
        }

        return samples;
    }
}
