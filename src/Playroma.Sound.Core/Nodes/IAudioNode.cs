using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Stateful pull-based audio node. Process must overwrite exactly frameCount * channelCount samples.
/// Nodes may allocate during Prepare, but Process should remain allocation-free.
/// </summary>
public interface IAudioNode
{
    bool IsComplete { get; }

    void Prepare(AudioFormat format);

    void Reset();

    void Process(Span<float> output, int frameCount);
}
