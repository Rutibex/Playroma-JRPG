using Playroma.Sound.Core.Generation;
using Playroma.Sound.Core.Random;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Deterministic white, pink, brown, blue, or violet noise.
/// </summary>
public sealed class NoiseNode : AudioNodeBase
{
    private Pcg32 _random;
    private double _pink0;
    private double _pink1;
    private double _pink2;
    private double _pink3;
    private double _pink4;
    private double _pink5;
    private double _pink6;
    private float _brown;
    private float _previousWhite;
    private float _previousBlue;

    public NoiseNode(NoiseColor color = NoiseColor.White, ulong seed = 1UL, float amplitude = 1f)
    {
        Color = color;
        Seed = seed;
        Amplitude = amplitude;
    }

    public NoiseColor Color { get; set; }
    public ulong Seed { get; set; }
    public float Amplitude { get; set; }

    public override void Reset()
    {
        _random = new Pcg32(Seed);
        _pink0 = 0;
        _pink1 = 0;
        _pink2 = 0;
        _pink3 = 0;
        _pink4 = 0;
        _pink5 = 0;
        _pink6 = 0;
        _brown = 0f;
        _previousWhite = 0f;
        _previousBlue = 0f;
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        int channels = Format.Channels;
        int sampleIndex = 0;
        float amplitude = Amplitude;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float white = _random.NextSignedFloat();
            float value = Color switch
            {
                NoiseColor.White => white,
                NoiseColor.Pink => GeneratePink(white),
                NoiseColor.Brown => GenerateBrown(white),
                NoiseColor.Blue => GenerateBlue(white),
                NoiseColor.Violet => GenerateViolet(white),
                _ => white
            };

            value *= amplitude;

            for (int channel = 0; channel < channels; channel++)
            {
                output[sampleIndex++] = value;
            }
        }
    }

    private float GeneratePink(float white)
    {
        _pink0 = (0.99886 * _pink0) + (white * 0.0555179);
        _pink1 = (0.99332 * _pink1) + (white * 0.0750759);
        _pink2 = (0.96900 * _pink2) + (white * 0.1538520);
        _pink3 = (0.86650 * _pink3) + (white * 0.3104856);
        _pink4 = (0.55000 * _pink4) + (white * 0.5329522);
        _pink5 = (-0.7616 * _pink5) - (white * 0.0168980);
        double pink = _pink0 + _pink1 + _pink2 + _pink3 + _pink4 + _pink5 + _pink6 + (white * 0.5362);
        _pink6 = white * 0.115926;
        return Math.Clamp((float)(pink * 0.11), -1f, 1f);
    }

    private float GenerateBrown(float white)
    {
        _brown = (_brown + (0.02f * white)) / 1.02f;
        return Math.Clamp(_brown * 3.5f, -1f, 1f);
    }

    private float GenerateBlue(float white)
    {
        float blue = (white - _previousWhite) * 0.5f;
        _previousWhite = white;
        _previousBlue = blue;
        return blue;
    }

    private float GenerateViolet(float white)
    {
        float blue = (white - _previousWhite) * 0.5f;
        float violet = (blue - _previousBlue) * 0.5f;
        _previousWhite = white;
        _previousBlue = blue;
        return violet;
    }
}
