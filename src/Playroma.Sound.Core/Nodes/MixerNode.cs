using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Core.Nodes;

public sealed class MixerNode : AudioNodeBase
{
    private readonly List<MixerInput> _inputs = [];
    private float[] _scratch = [];

    public IReadOnlyList<MixerInput> Inputs => _inputs;

    public override bool IsComplete => _inputs.Count > 0 && _inputs.All(input => input.Source.IsComplete);

    public MixerNode Add(IAudioNode source, float gain = 1f)
    {
        ArgumentNullException.ThrowIfNull(source);
        _inputs.Add(new MixerInput(source, gain));
        return this;
    }

    protected override void OnPrepare(AudioFormat format)
    {
        _scratch = new float[format.SamplesPerBlock];

        foreach (MixerInput input in _inputs)
        {
            input.Source.Prepare(format);
        }
    }

    public override void Reset()
    {
        foreach (MixerInput input in _inputs)
        {
            input.Source.Reset();
        }
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();
        int sampleCount = frameCount * Format.Channels;
        Span<float> scratch = _scratch.AsSpan(0, sampleCount);

        foreach (MixerInput input in _inputs)
        {
            input.Source.Process(scratch, frameCount);
            AudioMath.AddInPlace(output, scratch, input.Gain);
        }
    }

    public sealed class MixerInput
    {
        internal MixerInput(IAudioNode source, float gain)
        {
            Source = source;
            Gain = gain;
        }

        public IAudioNode Source { get; }
        public float Gain { get; set; }
    }
}
