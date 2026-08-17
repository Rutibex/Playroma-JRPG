using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Core.Nodes;

public sealed class GainNode : AudioNodeBase
{
    public GainNode(IAudioNode source, float gain = 1f)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Gain = gain;
    }

    public IAudioNode Source { get; }
    public float Gain { get; set; }

    public override bool IsComplete => Source.IsComplete;

    protected override void OnPrepare(AudioFormat format) => Source.Prepare(format);

    public override void Reset() => Source.Reset();

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        Source.Process(output, frameCount);
        AudioMath.ApplyGain(output, Gain);
    }
}
