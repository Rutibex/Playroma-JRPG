using Playroma.Sound.Core.Nodes;

namespace Playroma.Sound.Graph.BuiltIns;

internal sealed class ImpulseAudioNode : AudioNodeBase
{
    private bool _emitted;
    public float Amplitude { get; set; } = 1f;
    public override bool IsComplete => _emitted;

    public override void Reset() => _emitted = false;

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();
        if (_emitted || frameCount == 0)
        {
            return;
        }

        for (int channel = 0; channel < Format.Channels; channel++)
        {
            output[channel] = Amplitude;
        }

        _emitted = true;
    }
}
