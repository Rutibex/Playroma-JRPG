using System.Buffers.Binary;

namespace PlayromaMusic.Audio.Rendering;

public static class Pcm16Converter
{
    public static double ConvertInterleavedFloatToPcm16(ReadOnlySpan<float> source, Span<byte> destination, double gain)
    {
        if (destination.Length < source.Length * sizeof(short))
        {
            throw new ArgumentException("Destination is too small for 16-bit PCM output.", nameof(destination));
        }

        double clampedGain = Math.Clamp(gain, 0.0, 2.0);
        double peak = 0;
        for (int i = 0; i < source.Length; i++)
        {
            double sample = Math.Clamp(source[i] * clampedGain, -1.0, 1.0);
            peak = Math.Max(peak, Math.Abs(sample));
            short pcm = sample <= -1.0
                ? short.MinValue
                : (short)Math.Round(sample * short.MaxValue, MidpointRounding.AwayFromZero);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(i * 2, 2), pcm);
        }

        return peak;
    }
}
