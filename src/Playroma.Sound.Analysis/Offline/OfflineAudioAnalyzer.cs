using Playroma.Sound.Analysis.Realtime;
using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Analysis.Offline;

public sealed record OfflineAudioReport(
    AudioFormat Format,
    int FrameCount,
    TimeSpan Duration,
    float Peak,
    float Rms,
    float DcOffset,
    float StereoCorrelation,
    float EstimatedLoudnessLufs,
    long ClippedSamples,
    long NonFiniteSamples,
    IReadOnlyList<float> ChannelPeaks,
    IReadOnlyList<float> ChannelRms);

public static class OfflineAudioAnalyzer
{
    public static OfflineAudioReport Analyze(RenderedSound sound, float clippingThreshold = 0.999f)
    {
        ArgumentNullException.ThrowIfNull(sound);
        int channels = sound.Format.Channels;
        double[] squares = new double[channels];
        double[] sums = new double[channels];
        float[] peaks = new float[channels];
        long clipped = 0;
        long nonFinite = 0;
        double leftRight = 0;
        double leftSquares = 0;
        double rightSquares = 0;
        for (int frame = 0; frame < sound.FrameCount; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                float sample = sound.Samples[(frame * channels) + channel];
                if (!float.IsFinite(sample)) { nonFinite++; sample = 0; }
                if (MathF.Abs(sample) >= clippingThreshold) clipped++;
                peaks[channel] = Math.Max(peaks[channel], MathF.Abs(sample));
                squares[channel] += sample * sample;
                sums[channel] += sample;
            }
            if (channels >= 2)
            {
                float left = float.IsFinite(sound.Samples[frame * channels]) ? sound.Samples[frame * channels] : 0;
                float right = float.IsFinite(sound.Samples[(frame * channels) + 1]) ? sound.Samples[(frame * channels) + 1] : 0;
                leftRight += left * right;
                leftSquares += left * left;
                rightSquares += right * right;
            }
        }
        float[] channelRms = squares.Select(value => sound.FrameCount == 0 ? 0f : (float)Math.Sqrt(value / sound.FrameCount)).ToArray();
        float peak = peaks.Length == 0 ? 0 : peaks.Max();
        double totalSquares = squares.Sum();
        float rms = sound.Samples.Length == 0 ? 0 : (float)Math.Sqrt(totalSquares / sound.Samples.Length);
        float dc = sound.Samples.Length == 0 ? 0 : (float)(sums.Sum() / sound.Samples.Length);
        float correlation = channels < 2 ? 1f : (float)(leftRight / Math.Sqrt(Math.Max(double.Epsilon, leftSquares * rightSquares)));
        correlation = Math.Clamp(float.IsFinite(correlation) ? correlation : 0f, -1f, 1f);
        float lufs = rms <= 0.000001f ? -120f : Math.Max(-120f, -0.691f + (10f * MathF.Log10(rms * rms)));
        return new OfflineAudioReport(sound.Format, sound.FrameCount, sound.Duration, peak, rms, dc, correlation, lufs, clipped, nonFinite, peaks, channelRms);
    }
}
