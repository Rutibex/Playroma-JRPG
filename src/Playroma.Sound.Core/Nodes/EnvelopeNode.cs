using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Envelopes;

namespace Playroma.Sound.Core.Nodes;

public sealed class EnvelopeNode : AudioNodeBase
{
    public EnvelopeNode(IAudioNode source, IEnvelope envelope)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
    }

    public IAudioNode Source { get; }
    public IEnvelope Envelope { get; }

    public override bool IsComplete => Envelope.IsComplete || Source.IsComplete;

    protected override void OnPrepare(AudioFormat format)
    {
        Source.Prepare(format);
        Envelope.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        Source.Reset();
        Envelope.Reset();
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        if (Envelope.IsComplete)
        {
            output.Clear();
            return;
        }

        Source.Process(output, frameCount);
        int channels = Format.Channels;
        int sampleIndex = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float envelopeValue = Envelope.NextValue();

            for (int channel = 0; channel < channels; channel++)
            {
                output[sampleIndex++] *= envelopeValue;
            }
        }
    }
}
