namespace Playroma.Sound.Dsp.Parameters;

public static class ParameterMapping
{
    public static float Normalize(float value, ParameterMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        metadata.Validate();
        value = Math.Clamp(value, metadata.Minimum, metadata.Maximum);

        return metadata.Scale switch
        {
            ParameterScale.Logarithmic => MathF.Log(value / metadata.Minimum) /
                                          MathF.Log(metadata.Maximum / metadata.Minimum),
            ParameterScale.Bipolar => NormalizeBipolar(value, metadata.Minimum, metadata.Maximum),
            _ => (value - metadata.Minimum) / (metadata.Maximum - metadata.Minimum)
        };
    }

    public static float Denormalize(float normalized, ParameterMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        metadata.Validate();
        normalized = Math.Clamp(normalized, 0f, 1f);

        return metadata.Scale switch
        {
            ParameterScale.Logarithmic => metadata.Minimum *
                                          MathF.Pow(metadata.Maximum / metadata.Minimum, normalized),
            ParameterScale.Bipolar => DenormalizeBipolar(normalized, metadata.Minimum, metadata.Maximum),
            _ => metadata.Minimum + ((metadata.Maximum - metadata.Minimum) * normalized)
        };
    }

    private static float NormalizeBipolar(float value, float minimum, float maximum)
    {
        if (minimum >= 0f || maximum <= 0f)
        {
            return (value - minimum) / (maximum - minimum);
        }

        return value < 0f
            ? 0.5f * ((value - minimum) / -minimum)
            : 0.5f + (0.5f * (value / maximum));
    }

    private static float DenormalizeBipolar(float normalized, float minimum, float maximum)
    {
        if (minimum >= 0f || maximum <= 0f)
        {
            return minimum + ((maximum - minimum) * normalized);
        }

        return normalized < 0.5f
            ? minimum + ((-minimum) * (normalized * 2f))
            : maximum * ((normalized - 0.5f) * 2f);
    }
}
