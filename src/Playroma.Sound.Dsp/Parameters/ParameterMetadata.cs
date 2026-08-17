namespace Playroma.Sound.Dsp.Parameters;

public sealed record ParameterMetadata(
    string Id,
    string DisplayName,
    string Unit,
    float Minimum,
    float Maximum,
    float DefaultValue,
    ParameterScale Scale = ParameterScale.Linear)
{
    public ParameterMetadata Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("A parameter ID is required.", nameof(Id));
        }

        if (!float.IsFinite(Minimum) || !float.IsFinite(Maximum) || Minimum >= Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(Minimum), "Minimum must be finite and lower than maximum.");
        }

        if (!float.IsFinite(DefaultValue) || DefaultValue < Minimum || DefaultValue > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultValue));
        }

        if (Scale == ParameterScale.Logarithmic && Minimum <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Minimum), "Logarithmic parameters require a positive minimum.");
        }

        return this;
    }
}
