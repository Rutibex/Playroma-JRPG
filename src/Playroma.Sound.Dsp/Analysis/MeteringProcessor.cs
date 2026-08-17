using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Analysis;

/// <summary>
/// Pass-through processor that publishes a lock-free scalar analysis snapshot for UI or telemetry.
/// </summary>
public sealed class MeteringProcessor : AudioProcessorBase
{
    private float _peak;
    private float _rms;
    private float _dcOffset;
    private float _crestFactor;
    private float _zeroCrossingRate;
    private float _stereoCorrelation;
    private int _clippedSampleCount;

    public float ClippingThreshold { get; set; } = 1f;

    public override void Reset()
    {
        Volatile.Write(ref _peak, 0f);
        Volatile.Write(ref _rms, 0f);
        Volatile.Write(ref _dcOffset, 0f);
        Volatile.Write(ref _crestFactor, 0f);
        Volatile.Write(ref _zeroCrossingRate, 0f);
        Volatile.Write(ref _stereoCorrelation, 0f);
        Volatile.Write(ref _clippedSampleCount, 0);
    }

    public AnalysisSnapshot GetSnapshot() => new(
        Volatile.Read(ref _peak),
        Volatile.Read(ref _rms),
        Volatile.Read(ref _dcOffset),
        Volatile.Read(ref _crestFactor),
        Volatile.Read(ref _zeroCrossingRate),
        Volatile.Read(ref _stereoCorrelation),
        Volatile.Read(ref _clippedSampleCount));

    protected override void ProcessCore(Span<float> interleavedSamples, int frameCount)
    {
        AnalysisSnapshot snapshot = AudioAnalyzer.Analyze(
            interleavedSamples,
            frameCount,
            Format.Channels,
            ClippingThreshold);

        Volatile.Write(ref _peak, snapshot.Peak);
        Volatile.Write(ref _rms, snapshot.Rms);
        Volatile.Write(ref _dcOffset, snapshot.DcOffset);
        Volatile.Write(ref _crestFactor, snapshot.CrestFactor);
        Volatile.Write(ref _zeroCrossingRate, snapshot.ZeroCrossingRate);
        Volatile.Write(ref _stereoCorrelation, snapshot.StereoCorrelation);
        Volatile.Write(ref _clippedSampleCount, snapshot.ClippedSampleCount);
    }
}
