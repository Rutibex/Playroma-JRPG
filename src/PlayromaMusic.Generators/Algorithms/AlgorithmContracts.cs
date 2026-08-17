using PlayromaMusic.Core.GenerationLab;

namespace PlayromaMusic.Generators.Algorithms;

public sealed record AlgorithmContext(ulong Seed, int SampleCount);

public sealed record AlgorithmSeries(double[] Primary, double[] Secondary)
{
    public int Count => Primary.Length;
}

public interface IAlgorithmSource
{
    AlgorithmKind Kind { get; }
    AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters);
}

internal static class AlgorithmParameterReader
{
    public static double Get(
        IReadOnlyDictionary<string, double> parameters,
        string name,
        double defaultValue,
        double minimum,
        double maximum)
    {
        if (!parameters.TryGetValue(name, out double value) || !double.IsFinite(value))
        {
            value = defaultValue;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    public static int GetInt(
        IReadOnlyDictionary<string, double> parameters,
        string name,
        int defaultValue,
        int minimum,
        int maximum) =>
        (int)Math.Round(Get(parameters, name, defaultValue, minimum, maximum));
}
