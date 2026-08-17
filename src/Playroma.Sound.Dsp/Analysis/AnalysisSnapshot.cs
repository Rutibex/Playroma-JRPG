namespace Playroma.Sound.Dsp.Analysis;

public readonly record struct AnalysisSnapshot(
    float Peak,
    float Rms,
    float DcOffset,
    float CrestFactor,
    float ZeroCrossingRate,
    float StereoCorrelation,
    int ClippedSampleCount);
