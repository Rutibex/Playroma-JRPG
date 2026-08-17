using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Dsp.Processing;

public sealed class ProcessorChain : IAudioProcessor
{
    private readonly List<IAudioProcessor> _processors = [];
    private AudioFormat _format;
    private bool _prepared;

    public IReadOnlyList<IAudioProcessor> Processors => _processors;

    public int LatencyFrames
    {
        get
        {
            int total = 0;
            foreach (IAudioProcessor processor in _processors)
            {
                total = checked(total + processor.LatencyFrames);
            }

            return total;
        }
    }

    public ProcessorChain Add(IAudioProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        if (_prepared)
        {
            processor.Prepare(_format);
        }

        _processors.Add(processor);
        return this;
    }

    public void Prepare(AudioFormat format)
    {
        _format = format;
        foreach (IAudioProcessor processor in _processors)
        {
            processor.Prepare(format);
        }

        _prepared = true;
    }

    public void Reset()
    {
        foreach (IAudioProcessor processor in _processors)
        {
            processor.Reset();
        }
    }

    public void Process(Span<float> interleavedSamples, int frameCount)
    {
        if (!_prepared)
        {
            throw new InvalidOperationException("The processor chain must be prepared before processing.");
        }

        foreach (IAudioProcessor processor in _processors)
        {
            processor.Process(interleavedSamples, frameCount);
        }
    }
}
