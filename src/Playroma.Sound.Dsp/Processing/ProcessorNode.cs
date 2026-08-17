using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Nodes;

namespace Playroma.Sound.Dsp.Processing;

/// <summary>
/// Adapts one or more in-place DSP processors to the core pull-node API.
/// </summary>
public sealed class ProcessorNode : AudioNodeBase
{
    private readonly IAudioNode _source;
    private readonly ProcessorChain _chain = new();

    public ProcessorNode(IAudioNode source, params IAudioProcessor[] processors)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;

        foreach (IAudioProcessor processor in processors)
        {
            _chain.Add(processor);
        }
    }

    public override bool IsComplete => _source.IsComplete;

    public ProcessorChain Chain => _chain;

    protected override void OnPrepare(AudioFormat format)
    {
        _source.Prepare(format);
        _chain.Prepare(format);
    }

    public override void Reset()
    {
        _source.Reset();
        _chain.Reset();
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        _source.Process(output, frameCount);
        _chain.Process(output, frameCount);
    }
}
