using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Dsp.Processing;

public abstract class AudioProcessorBase : IAudioProcessor
{
    private bool _prepared;

    protected AudioFormat Format { get; private set; }
    protected bool IsPrepared => _prepared;

    public virtual int LatencyFrames => 0;

    public void Prepare(AudioFormat format)
    {
        Format = format;
        OnPrepare(format);
        _prepared = true;
        Reset();
    }

    public abstract void Reset();

    public void Process(Span<float> interleavedSamples, int frameCount)
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
        if (interleavedSamples.Length < requiredSamples)
        {
            throw new ArgumentException("The sample span is too small for the requested frame count.", nameof(interleavedSamples));
        }

        ProcessCore(interleavedSamples[..requiredSamples], frameCount);
    }

    protected virtual void OnPrepare(AudioFormat format)
    {
    }

    protected abstract void ProcessCore(Span<float> interleavedSamples, int frameCount);
}
