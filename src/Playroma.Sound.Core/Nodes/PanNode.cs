using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Equal-power stereo panner. Mono formats pass through unchanged.
/// </summary>
public sealed class PanNode : AudioNodeBase
{
    public PanNode(IAudioNode source, float pan = 0f)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Pan = pan;
    }

    public IAudioNode Source { get; }
    public float Pan { get; set; }

    public override bool IsComplete => Source.IsComplete;

    protected override void OnPrepare(AudioFormat format) => Source.Prepare(format);

    public override void Reset() => Source.Reset();

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        Source.Process(output, frameCount);

        if (Format.Channels != 2)
        {
            return;
        }

        float normalizedPan = Math.Clamp(Pan, -1f, 1f);
        float angle = (normalizedPan + 1f) * (MathF.PI / 4f);
        float leftGain = MathF.Cos(angle);
        float rightGain = MathF.Sin(angle);

        for (int frame = 0, sample = 0; frame < frameCount; frame++, sample += 2)
        {
            output[sample] *= leftGain;
            output[sample + 1] *= rightGain;
        }
    }
}
