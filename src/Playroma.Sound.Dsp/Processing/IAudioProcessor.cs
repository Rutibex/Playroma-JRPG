using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Dsp.Processing;

/// <summary>
/// Stateful in-place audio processor. Implementations may allocate during Prepare,
/// but Process should not allocate or block.
/// </summary>
public interface IAudioProcessor
{
    int LatencyFrames { get; }

    void Prepare(AudioFormat format);

    void Reset();

    void Process(Span<float> interleavedSamples, int frameCount);
}
