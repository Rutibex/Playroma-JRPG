using Playroma.Sound.Dsp.Parameters;

namespace Playroma.Sound.Dsp.Modulation;

/// <summary>
/// Combines a smoothed base parameter with a bipolar control source.
/// </summary>
public sealed class ParameterModulator
{
    private int _sampleRate;

    public ParameterModulator(AudioParameter baseParameter, IControlSource source)
    {
        BaseParameter = baseParameter ?? throw new ArgumentNullException(nameof(baseParameter));
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public AudioParameter BaseParameter { get; }
    public IControlSource Source { get; }
    public float Depth { get; set; } = 1f;
    public float Offset { get; set; }

    public void Prepare(int sampleRate)
    {
        _sampleRate = sampleRate;
        BaseParameter.Prepare(sampleRate);
        Source.Prepare(sampleRate);
    }

    public void Reset()
    {
        BaseParameter.Reset();
        Source.Reset();
    }

    public float NextValue()
    {
        if (_sampleRate <= 0)
        {
            throw new InvalidOperationException("The modulator must be prepared before use.");
        }

        float value = BaseParameter.NextValue() + Offset + (Source.NextValue() * Depth);
        return Math.Clamp(value, BaseParameter.Metadata.Minimum, BaseParameter.Metadata.Maximum);
    }
}
