namespace Playroma.Sound.Dsp.Distortion;

public static class Waveshaper
{
    public static float Shape(float sample, WaveshaperMode mode)
    {
        return mode switch
        {
            WaveshaperMode.HardClip => Math.Clamp(sample, -1f, 1f),
            WaveshaperMode.SoftClip => CubicSoftClip(sample),
            WaveshaperMode.Asymmetric => sample >= 0f
                ? MathF.Tanh(sample)
                : 0.7f * MathF.Tanh(sample * 1.5f),
            WaveshaperMode.WaveFold => Fold(sample),
            WaveshaperMode.HalfWaveRectify => MathF.Max(0f, sample),
            WaveshaperMode.FullWaveRectify => MathF.Abs(sample),
            _ => MathF.Tanh(sample)
        };
    }

    private static float CubicSoftClip(float sample)
    {
        if (sample <= -1f)
        {
            return -2f / 3f;
        }

        if (sample >= 1f)
        {
            return 2f / 3f;
        }

        return sample - ((sample * sample * sample) / 3f);
    }

    private static float Fold(float sample)
    {
        float wrapped = (sample + 1f) % 4f;
        if (wrapped < 0f)
        {
            wrapped += 4f;
        }

        return wrapped <= 2f ? wrapped - 1f : 3f - wrapped;
    }
}
