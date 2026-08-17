using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Generators.Algorithms;

namespace PlayromaMusic.Generators.Graphs;

public sealed record GraphEvaluationResult(
    AlgorithmSeries Source,
    IReadOnlyDictionary<string, double[]> NodeValues,
    double[] PitchValues,
    double[] RhythmValues,
    double[] VelocityValues,
    double[] ControlValues)
{
    public int Count => Source.Count;
}

public sealed class ValueGraphEvaluator
{
    public GraphEvaluationResult Evaluate(AlgorithmSeries source, ValueGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(graph);
        if (source.Primary.Length != source.Secondary.Length)
        {
            throw new ArgumentException("Algorithm source channels must have equal lengths.", nameof(source));
        }

        Dictionary<string, double[]> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["source.primary"] = source.Primary.ToArray(),
            ["source.secondary"] = source.Secondary.ToArray()
        };

        foreach (ValueTransformNodeDefinition node in graph.Nodes)
        {
            if (!values.TryGetValue(node.InputNodeId, out double[]? input))
            {
                throw new InvalidOperationException($"Graph node {node.Id} references missing input {node.InputNodeId}.");
            }

            if (values.ContainsKey(node.Id))
            {
                throw new InvalidOperationException($"Graph node ID {node.Id} is duplicated.");
            }

            values[node.Id] = Apply(node.Kind, input, node.Parameters);
        }

        return new GraphEvaluationResult(
            source,
            values,
            Get(values, graph.PitchOutputNodeId),
            Get(values, graph.RhythmOutputNodeId),
            Get(values, graph.VelocityOutputNodeId),
            Get(values, graph.ControlOutputNodeId));
    }

    public static double[] Normalize01(IReadOnlyList<double> input)
    {
        if (input.Count == 0)
        {
            return Array.Empty<double>();
        }

        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        for (int index = 0; index < input.Count; index++)
        {
            double value = double.IsFinite(input[index]) ? input[index] : 0.0;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        double range = maximum - minimum;
        double[] output = new double[input.Count];
        if (!double.IsFinite(range) || range < 1e-12)
        {
            Array.Fill(output, 0.5);
            return output;
        }

        for (int index = 0; index < input.Count; index++)
        {
            double value = double.IsFinite(input[index]) ? input[index] : minimum;
            output[index] = Math.Clamp((value - minimum) / range, 0.0, 1.0);
        }

        return output;
    }

    private static double[] Get(IReadOnlyDictionary<string, double[]> values, string id) =>
        values.TryGetValue(id, out double[]? output)
            ? output.ToArray()
            : throw new InvalidOperationException($"Graph output references missing node {id}.");

    private static double[] Apply(
        ValueTransformKind kind,
        IReadOnlyList<double> input,
        IReadOnlyDictionary<string, double> parameters) =>
        kind switch
        {
            ValueTransformKind.Normalize => Normalize01(input),
            ValueTransformKind.Invert => MapNormalized(input, value => 1.0 - value),
            ValueTransformKind.Power => Power(input, Read(parameters, "Exponent", 1.0, 0.01, 16.0)),
            ValueTransformKind.Smooth => Smooth(input, Read(parameters, "Alpha", 0.25, 0.001, 1.0)),
            ValueTransformKind.Quantize => Quantize(input, ReadInt(parameters, "Levels", 8, 2, 128)),
            ValueTransformKind.Threshold => Threshold(input, Read(parameters, "Threshold", 0.5, 0.0, 1.0)),
            ValueTransformKind.Wrap => Wrap(input, Read(parameters, "Scale", 1.0, -64.0, 64.0), Read(parameters, "Offset", 0.0, -64.0, 64.0)),
            ValueTransformKind.Fold => Fold(input, Read(parameters, "Scale", 2.0, 0.01, 64.0)),
            ValueTransformKind.Derivative => Derivative(input),
            ValueTransformKind.SampleHold => SampleHold(input, ReadInt(parameters, "Samples", 4, 1, 1024)),
            ValueTransformKind.BiasScale => BiasScale(input, Read(parameters, "Scale", 1.0, -64.0, 64.0), Read(parameters, "Bias", 0.0, -64.0, 64.0)),
            ValueTransformKind.Clamp => Clamp(input, Read(parameters, "Minimum", 0.0, -1_000_000, 1_000_000), Read(parameters, "Maximum", 1.0, -1_000_000, 1_000_000)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown transform kind.")
        };

    private static double[] MapNormalized(IReadOnlyList<double> input, Func<double, double> transform)
    {
        double[] normalized = Normalize01(input);
        for (int index = 0; index < normalized.Length; index++)
        {
            normalized[index] = Math.Clamp(transform(normalized[index]), 0.0, 1.0);
        }

        return normalized;
    }

    private static double[] Power(IReadOnlyList<double> input, double exponent) =>
        MapNormalized(input, value => Math.Pow(value, exponent));

    private static double[] Smooth(IReadOnlyList<double> input, double alpha)
    {
        if (input.Count == 0)
        {
            return Array.Empty<double>();
        }

        double[] output = new double[input.Count];
        double current = double.IsFinite(input[0]) ? input[0] : 0.0;
        output[0] = current;
        for (int index = 1; index < input.Count; index++)
        {
            double next = double.IsFinite(input[index]) ? input[index] : current;
            current += (next - current) * alpha;
            output[index] = current;
        }

        return output;
    }

    private static double[] Quantize(IReadOnlyList<double> input, int levels)
    {
        double[] normalized = Normalize01(input);
        double denominator = levels - 1.0;
        for (int index = 0; index < normalized.Length; index++)
        {
            normalized[index] = Math.Round(normalized[index] * denominator) / denominator;
        }

        return normalized;
    }

    private static double[] Threshold(IReadOnlyList<double> input, double threshold)
    {
        double[] normalized = Normalize01(input);
        for (int index = 0; index < normalized.Length; index++)
        {
            normalized[index] = normalized[index] >= threshold ? 1.0 : 0.0;
        }

        return normalized;
    }

    private static double[] Wrap(IReadOnlyList<double> input, double scale, double offset)
    {
        double[] output = new double[input.Count];
        for (int index = 0; index < input.Count; index++)
        {
            double value = (double.IsFinite(input[index]) ? input[index] : 0.0) * scale + offset;
            output[index] = value - Math.Floor(value);
        }

        return output;
    }

    private static double[] Fold(IReadOnlyList<double> input, double scale)
    {
        double[] normalized = Normalize01(input);
        for (int index = 0; index < normalized.Length; index++)
        {
            double value = normalized[index] * scale;
            double wrapped = value % 2.0;
            normalized[index] = wrapped <= 1.0 ? wrapped : 2.0 - wrapped;
        }

        return normalized;
    }

    private static double[] Derivative(IReadOnlyList<double> input)
    {
        double[] output = new double[input.Count];
        if (input.Count == 0)
        {
            return output;
        }

        output[0] = 0;
        for (int index = 1; index < input.Count; index++)
        {
            double current = double.IsFinite(input[index]) ? input[index] : 0.0;
            double previous = double.IsFinite(input[index - 1]) ? input[index - 1] : current;
            output[index] = Math.Abs(current - previous);
        }

        return Normalize01(output);
    }

    private static double[] SampleHold(IReadOnlyList<double> input, int samples)
    {
        double[] output = new double[input.Count];
        for (int index = 0; index < input.Count; index++)
        {
            int source = (index / samples) * samples;
            output[index] = double.IsFinite(input[source]) ? input[source] : 0.0;
        }

        return output;
    }

    private static double[] BiasScale(IReadOnlyList<double> input, double scale, double bias)
    {
        double[] output = new double[input.Count];
        for (int index = 0; index < input.Count; index++)
        {
            double value = double.IsFinite(input[index]) ? input[index] : 0.0;
            output[index] = (value * scale) + bias;
        }

        return output;
    }

    private static double[] Clamp(IReadOnlyList<double> input, double minimum, double maximum)
    {
        if (maximum < minimum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        double[] output = new double[input.Count];
        for (int index = 0; index < input.Count; index++)
        {
            double value = double.IsFinite(input[index]) ? input[index] : minimum;
            output[index] = Math.Clamp(value, minimum, maximum);
        }

        return output;
    }

    private static double Read(
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

    private static int ReadInt(
        IReadOnlyDictionary<string, double> parameters,
        string name,
        int defaultValue,
        int minimum,
        int maximum) =>
        (int)Math.Round(Read(parameters, name, defaultValue, minimum, maximum));
}
