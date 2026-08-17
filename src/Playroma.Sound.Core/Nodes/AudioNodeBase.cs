using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Core.Nodes;

public abstract class AudioNodeBase : IAudioNode
{
    private bool _prepared;

    protected AudioFormat Format { get; private set; }
    protected bool IsPrepared => _prepared;

    public virtual bool IsComplete => false;

    public void Prepare(AudioFormat format)
    {
        Format = format;
        OnPrepare(format);
        _prepared = true;
        Reset();
    }

    public abstract void Reset();

    public void Process(Span<float> output, int frameCount)
    {
        if (!_prepared)
        {
            throw new InvalidOperationException($"{GetType().Name} must be prepared before processing.");
        }

        if (frameCount < 0 || frameCount > Format.BlockFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        int requiredSamples = checked(frameCount * Format.Channels);
        if (output.Length < requiredSamples)
        {
            throw new ArgumentException("Output span is too small for the requested frame count.", nameof(output));
        }

        ProcessCore(output[..requiredSamples], frameCount);
    }

    protected virtual void OnPrepare(AudioFormat format)
    {
    }

    protected abstract void ProcessCore(Span<float> output, int frameCount);
}
