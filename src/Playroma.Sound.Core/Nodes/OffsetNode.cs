using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Delays a source by a fixed number of frames without allocating during processing.
/// </summary>
public sealed class OffsetNode : AudioNodeBase
{
    private long _remainingDelayFrames;

    public OffsetNode(IAudioNode source, double delaySeconds)
    {
        if (!double.IsFinite(delaySeconds) || delaySeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        DelaySeconds = delaySeconds;
    }

    public IAudioNode Source { get; }
    public double DelaySeconds { get; }
    public override bool IsComplete => _remainingDelayFrames == 0 && Source.IsComplete;

    protected override void OnPrepare(AudioFormat format) => Source.Prepare(format);

    public override void Reset()
    {
        _remainingDelayFrames = checked((long)Math.Round(DelaySeconds * Format.SampleRate));
        Source.Reset();
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();

        int silentFrames = (int)Math.Min(_remainingDelayFrames, frameCount);
        _remainingDelayFrames -= silentFrames;

        int activeFrames = frameCount - silentFrames;
        if (activeFrames <= 0)
        {
            return;
        }

        int sampleOffset = silentFrames * Format.Channels;
        Source.Process(output[sampleOffset..], activeFrames);
    }
}
