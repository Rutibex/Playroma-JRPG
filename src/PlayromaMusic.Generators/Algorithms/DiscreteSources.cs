using System.Numerics;
using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Randomness;

namespace PlayromaMusic.Generators.Algorithms;

internal sealed class ThueMorseSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.ThueMorse;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        int offset = AlgorithmParameterReader.GetInt(parameters, "Offset", 0, 0, 1_000_000) + (int)(context.Seed % 4096UL);
        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];
        int running = 0;

        for (int index = 0; index < context.SampleCount; index++)
        {
            uint value = (uint)(index + offset);
            int bit = BitOperations.PopCount(value) & 1;
            primary[index] = bit;
            running += bit == 0 ? -1 : 1;
            secondary[index] = running;
        }

        return new AlgorithmSeries(primary, secondary);
    }
}

internal sealed class CollatzSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.Collatz;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        ulong start = (ulong)AlgorithmParameterReader.Get(parameters, "Start", 27, 2, 9_000_000_000_000_000_000d);
        start = Math.Min(ulong.MaxValue - 1000UL, start) + (context.Seed % 997UL);
        int skip = AlgorithmParameterReader.GetInt(parameters, "Skip", 0, 0, 10_000);
        ulong value = start;
        for (int index = 0; index < skip; index++)
        {
            value = Next(value);
        }

        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];
        for (int index = 0; index < context.SampleCount; index++)
        {
            primary[index] = Math.Log2(value + 1.0);
            secondary[index] = (value & 1UL) == 0UL ? 0.0 : 1.0;
            value = Next(value);
        }

        return new AlgorithmSeries(primary, secondary);
    }

    private static ulong Next(ulong value)
    {
        if ((value & 1UL) == 0UL)
        {
            return Math.Max(1UL, value / 2UL);
        }

        const ulong safeLimit = (ulong.MaxValue - 1UL) / 3UL;
        if (value <= safeLimit)
        {
            return (3UL * value) + 1UL;
        }

        return ((value % 1_000_003UL) * 3UL) + 1UL;
    }
}

internal sealed class CellularAutomataSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.CellularAutomata;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        int rule = AlgorithmParameterReader.GetInt(parameters, "Rule", 30, 0, 255);
        int width = AlgorithmParameterReader.GetInt(parameters, "Width", 64, 8, 512);
        int initialMode = AlgorithmParameterReader.GetInt(parameters, "InitialMode", 0, 0, 2);
        double density = AlgorithmParameterReader.Get(parameters, "InitialDensity", 0.2, 0.01, 0.99);
        DeterministicRandom random = new(context.Seed);

        bool[] current = new bool[width];
        bool[] next = new bool[width];
        switch (initialMode)
        {
            case 0:
                current[(width / 2 + (int)(context.Seed % (ulong)width)) % width] = true;
                break;
            case 1:
                for (int index = 0; index < width; index++)
                {
                    current[index] = random.Chance(density);
                }
                break;
            default:
                for (int index = 0; index < width; index++)
                {
                    current[index] = ((index + (int)(context.Seed % 17UL)) % 7) is 0 or 1;
                }
                break;
        }

        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];
        for (int sample = 0; sample < context.SampleCount; sample++)
        {
            int active = 0;
            double weighted = 0;
            for (int index = 0; index < width; index++)
            {
                if (!current[index])
                {
                    continue;
                }

                active++;
                weighted += index;
            }

            primary[sample] = active / (double)width;
            secondary[sample] = active == 0 ? 0.5 : weighted / active / Math.Max(1, width - 1);

            for (int index = 0; index < width; index++)
            {
                int left = current[(index - 1 + width) % width] ? 1 : 0;
                int center = current[index] ? 1 : 0;
                int right = current[(index + 1) % width] ? 1 : 0;
                int neighborhood = (left << 2) | (center << 1) | right;
                next[index] = ((rule >> neighborhood) & 1) != 0;
            }

            (current, next) = (next, current);
            Array.Clear(next);
        }

        return new AlgorithmSeries(primary, secondary);
    }
}
