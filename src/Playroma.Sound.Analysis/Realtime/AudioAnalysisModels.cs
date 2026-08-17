using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Analysis.Realtime;

public sealed class AudioAnalysisOptions
{
    public int FftSize { get; init; } = 2_048;
    public int WaveformPoints { get; init; } = 512;
    public int SpectrumBins { get; init; } = 256;
    public int SpectrogramRows { get; init; } = 120;
    public int UpdateRateHz { get; init; } = 20;
    public int BufferMilliseconds { get; init; } = 2_000;
    public float ClippingThreshold { get; init; } = 0.999f;

    public void Validate()
    {
        if (FftSize is < 256 or > 65_536 || (FftSize & (FftSize - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(FftSize));
        if (WaveformPoints is < 32 or > 8_192) throw new ArgumentOutOfRangeException(nameof(WaveformPoints));
        if (SpectrumBins < 16 || SpectrumBins > FftSize / 2) throw new ArgumentOutOfRangeException(nameof(SpectrumBins));
        if (SpectrogramRows is < 8 or > 2_048) throw new ArgumentOutOfRangeException(nameof(SpectrogramRows));
        if (UpdateRateHz is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(UpdateRateHz));
        if (BufferMilliseconds is < 100 or > 60_000) throw new ArgumentOutOfRangeException(nameof(BufferMilliseconds));
        if (!float.IsFinite(ClippingThreshold) || ClippingThreshold <= 0f || ClippingThreshold > 1f) throw new ArgumentOutOfRangeException(nameof(ClippingThreshold));
    }
}

public sealed record AudioAnalysisSnapshot(
    long Sequence,
    DateTimeOffset CapturedAtUtc,
    AudioFormat Format,
    float Peak,
    float Rms,
    float MaximumPeak,
    float DcOffset,
    float StereoCorrelation,
    float EstimatedLoudnessLufs,
    long ClippedSamples,
    long NonFiniteSamples,
    long InputFrames,
    long DroppedFrames,
    int QueuedFrames,
    double WorkerMilliseconds,
    IReadOnlyList<float> WaveformMinimum,
    IReadOnlyList<float> WaveformMaximum,
    IReadOnlyList<float> SpectrumDecibels,
    IReadOnlyList<float> SpectrumFrequencies,
    IReadOnlyList<IReadOnlyList<float>> SpectrogramDecibels)
{
    public static AudioAnalysisSnapshot Empty(AudioFormat format) => new(
        0, DateTimeOffset.MinValue, format, 0, 0, 0, 0, 0, -120, 0, 0, 0, 0, 0, 0,
        [], [], [], [], []);
}
