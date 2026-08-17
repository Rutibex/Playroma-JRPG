using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Randomness;

namespace PlayromaMusic.Generators.Algorithms;

internal sealed class PinkNoiseSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.PinkNoise;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        int rows = AlgorithmParameterReader.GetInt(parameters, "Rows", 12, 2, 24);
        double blend = AlgorithmParameterReader.Get(parameters, "WhiteBlend", 0.08, 0.0, 1.0);
        DeterministicRandom random = new(context.Seed);
        double[] rowValues = new double[rows];
        for (int index = 0; index < rows; index++)
        {
            rowValues[index] = (random.NextDouble() * 2.0) - 1.0;
        }

        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];
        for (int sample = 0; sample < context.SampleCount; sample++)
        {
            int changedRows = Math.Min(rows, CountTrailingZeroes(sample + 1) + 1);
            for (int row = 0; row < changedRows; row++)
            {
                rowValues[row] = (random.NextDouble() * 2.0) - 1.0;
            }

            double white = (random.NextDouble() * 2.0) - 1.0;
            double pink = rowValues.Average();
            primary[sample] = (pink * (1.0 - blend)) + (white * blend);
            secondary[sample] = white;
        }

        return new AlgorithmSeries(primary, secondary);
    }

    private static int CountTrailingZeroes(int value)
    {
        int count = 0;
        while ((value & 1) == 0 && count < 30)
        {
            count++;
            value >>= 1;
        }

        return count;
    }
}

internal sealed class RandomWalkSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.RandomWalk;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        double position = AlgorithmParameterReader.Get(parameters, "Start", 0.5, 0.0, 1.0);
        double stepSize = AlgorithmParameterReader.Get(parameters, "StepSize", 0.075, 0.0001, 1.0);
        double center = AlgorithmParameterReader.Get(parameters, "Center", 0.5, 0.0, 1.0);
        double pull = AlgorithmParameterReader.Get(parameters, "CenterPull", 0.025, 0.0, 1.0);
        DeterministicRandom random = new(context.Seed);
        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];

        for (int index = 0; index < context.SampleCount; index++)
        {
            double step = ((random.NextDouble() * 2.0) - 1.0) * stepSize;
            position += step + ((center - position) * pull);
            if (position < 0)
            {
                position = -position;
            }
            else if (position > 1)
            {
                position = 2.0 - position;
            }

            position = Math.Clamp(position, 0.0, 1.0);
            primary[index] = position;
            secondary[index] = Math.Abs(step) / stepSize;
        }

        return new AlgorithmSeries(primary, secondary);
    }
}
