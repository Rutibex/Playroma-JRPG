using System.Text;
using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Core.IO;

public static class WavWriter
{
    public static void Write(string path, RenderedSound sound, WavEncoding encoding = WavEncoding.Pcm16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sound);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(path);
        Write(stream, sound, encoding, leaveOpen: false);
    }

    public static void Write(Stream destination, RenderedSound sound, WavEncoding encoding = WavEncoding.Pcm16, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sound);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        }

        int bitsPerSample = encoding switch
        {
            WavEncoding.Pcm16 => 16,
            WavEncoding.Pcm24 => 24,
            WavEncoding.Pcm32 => 32,
            WavEncoding.Float32 => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };

        short formatCode = encoding == WavEncoding.Float32 ? (short)3 : (short)1;
        int bytesPerSample = bitsPerSample / 8;
        int blockAlign = checked(sound.Format.Channels * bytesPerSample);
        int byteRate = checked(sound.Format.SampleRate * blockAlign);
        int dataSize = checked(sound.Samples.Length * bytesPerSample);
        int riffSize = checked(36 + dataSize);

        using var writer = new BinaryWriter(destination, Encoding.ASCII, leaveOpen);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(riffSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write(formatCode);
        writer.Write((short)sound.Format.Channels);
        writer.Write(sound.Format.SampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (float sourceSample in sound.Samples)
        {
            float sample = AudioMath.ClampSample(sourceSample);

            switch (encoding)
            {
                case WavEncoding.Pcm16:
                    writer.Write((short)Math.Round(sample * short.MaxValue));
                    break;

                case WavEncoding.Pcm24:
                    int value24 = (int)Math.Round(sample * 8_388_607f);
                    writer.Write((byte)(value24 & 0xFF));
                    writer.Write((byte)((value24 >> 8) & 0xFF));
                    writer.Write((byte)((value24 >> 16) & 0xFF));
                    break;

                case WavEncoding.Pcm32:
                    writer.Write((int)Math.Round(sample * int.MaxValue));
                    break;

                case WavEncoding.Float32:
                    writer.Write(sample);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding));
            }
        }
    }
}
