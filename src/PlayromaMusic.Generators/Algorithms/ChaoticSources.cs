using PlayromaMusic.Core.GenerationLab;

namespace PlayromaMusic.Generators.Algorithms;

internal sealed class LogisticSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.Logistic;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        double r = AlgorithmParameterReader.Get(parameters, "R", 3.91, 0.01, 4.0);
        double x = AlgorithmParameterReader.Get(parameters, "X0", 0.381966, 0.000001, 0.999999);
        x = Math.Clamp(x + (SeedPerturbation(context.Seed, 0) * 0.0002), 0.000001, 0.999999);
        int burnIn = AlgorithmParameterReader.GetInt(parameters, "BurnIn", 64, 0, 10_000);
        for (int index = 0; index < burnIn; index++)
        {
            x = r * x * (1.0 - x);
        }

        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];
        double previous = x;
        for (int index = 0; index < context.SampleCount; index++)
        {
            x = r * x * (1.0 - x);
            if (!double.IsFinite(x))
            {
                x = 0.5;
            }

            primary[index] = x;
            secondary[index] = Math.Abs(x - previous);
            previous = x;
        }

        return new AlgorithmSeries(primary, secondary);
    }

    private static double SeedPerturbation(ulong seed, int stream) =>
        ChaoticSeed.Unit(seed, (ulong)stream);
}

internal sealed class HenonSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.Henon;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        double a = AlgorithmParameterReader.Get(parameters, "A", 1.4, -2.0, 2.0);
        double b = AlgorithmParameterReader.Get(parameters, "B", 0.3, -1.0, 1.0);
        double x = AlgorithmParameterReader.Get(parameters, "X0", 0.1, -2.0, 2.0);
        double y = AlgorithmParameterReader.Get(parameters, "Y0", 0.0, -2.0, 2.0);
        double seedUnit = ChaoticSeed.Unit(context.Seed, 1);
        x += seedUnit * 0.0002;
        y -= seedUnit * 0.0002;
        int burnIn = AlgorithmParameterReader.GetInt(parameters, "BurnIn", 64, 0, 10_000);

        for (int index = 0; index < burnIn; index++)
        {
            Step(ref x, ref y, a, b);
        }

        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];
        for (int index = 0; index < context.SampleCount; index++)
        {
            Step(ref x, ref y, a, b);
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000)
            {
                x = 0.1;
                y = 0;
            }

            primary[index] = x;
            secondary[index] = y;
        }

        return new AlgorithmSeries(primary, secondary);
    }

    private static void Step(ref double x, ref double y, double a, double b)
    {
        double nextX = 1.0 - (a * x * x) + y;
        double nextY = b * x;
        x = nextX;
        y = nextY;
    }
}

internal sealed class LorenzSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.Lorenz;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        double sigma = AlgorithmParameterReader.Get(parameters, "Sigma", 10.0, 0.1, 50.0);
        double rho = AlgorithmParameterReader.Get(parameters, "Rho", 28.0, 0.1, 100.0);
        double beta = AlgorithmParameterReader.Get(parameters, "Beta", 8.0 / 3.0, 0.01, 20.0);
        double dt = AlgorithmParameterReader.Get(parameters, "Dt", 0.006, 0.00001, 0.05);
        int iterationsPerSample = AlgorithmParameterReader.GetInt(parameters, "IterationsPerSample", 4, 1, 64);
        int burnIn = AlgorithmParameterReader.GetInt(parameters, "BurnIn", 512, 0, 100_000);
        double x = AlgorithmParameterReader.Get(parameters, "X0", 0.1, -100.0, 100.0);
        double y = AlgorithmParameterReader.Get(parameters, "Y0", 0.0, -100.0, 100.0);
        double z = AlgorithmParameterReader.Get(parameters, "Z0", 0.0, -100.0, 100.0);
        double seedUnit = ChaoticSeed.Unit(context.Seed, 2);
        x += seedUnit * 0.02;
        y -= seedUnit * 0.013;
        z += seedUnit * 0.007;

        for (int index = 0; index < burnIn; index++)
        {
            Step(ref x, ref y, ref z, sigma, rho, beta, dt);
        }

        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];
        for (int sample = 0; sample < context.SampleCount; sample++)
        {
            for (int iteration = 0; iteration < iterationsPerSample; iteration++)
            {
                Step(ref x, ref y, ref z, sigma, rho, beta, dt);
            }

            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                x = 0.1;
                y = 0;
                z = 0;
            }

            primary[sample] = x;
            secondary[sample] = z;
        }

        return new AlgorithmSeries(primary, secondary);
    }

    private static void Step(
        ref double x,
        ref double y,
        ref double z,
        double sigma,
        double rho,
        double beta,
        double dt)
    {
        double dx = sigma * (y - x);
        double dy = (x * (rho - z)) - y;
        double dz = (x * y) - (beta * z);
        x += dx * dt;
        y += dy * dt;
        z += dz * dt;
    }
}

internal sealed class HopalongSource : IAlgorithmSource
{
    public AlgorithmKind Kind => AlgorithmKind.Hopalong;

    public AlgorithmSeries Generate(AlgorithmContext context, IReadOnlyDictionary<string, double> parameters)
    {
        double a = AlgorithmParameterReader.Get(parameters, "A", 0.4, -20.0, 20.0);
        double b = AlgorithmParameterReader.Get(parameters, "B", 1.0, -20.0, 20.0);
        double c = AlgorithmParameterReader.Get(parameters, "C", 0.0, -20.0, 20.0);
        double x = AlgorithmParameterReader.Get(parameters, "X0", 0.1, -20.0, 20.0);
        double y = AlgorithmParameterReader.Get(parameters, "Y0", 0.0, -20.0, 20.0);
        double seedUnit = ChaoticSeed.Unit(context.Seed, 3);
        x += seedUnit * 0.001;
        y -= seedUnit * 0.001;
        int burnIn = AlgorithmParameterReader.GetInt(parameters, "BurnIn", 32, 0, 10_000);

        for (int index = 0; index < burnIn; index++)
        {
            Step(ref x, ref y, a, b, c);
        }

        double[] primary = new double[context.SampleCount];
        double[] secondary = new double[context.SampleCount];
        for (int index = 0; index < context.SampleCount; index++)
        {
            Step(ref x, ref y, a, b, c);
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000)
            {
                x = 0.1;
                y = 0;
            }

            primary[index] = x;
            secondary[index] = y;
        }

        return new AlgorithmSeries(primary, secondary);
    }

    private static void Step(ref double x, ref double y, double a, double b, double c)
    {
        double sign = x < 0 ? -1.0 : 1.0;
        double nextX = y - (sign * Math.Sqrt(Math.Abs((b * x) - c)));
        double nextY = a - x;
        x = nextX;
        y = nextY;
    }
}

internal static class ChaoticSeed
{
    public static double Unit(ulong seed, ulong stream)
    {
        ulong mixed;
        unchecked
        {
            // This mixer intentionally wraps at 64 bits.
            mixed = seed + 0x9E3779B97F4A7C15UL + (stream * 0xD1B54A32D192ED03UL);
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            mixed ^= mixed >> 31;
        }

        return ((mixed >> 11) * (1.0 / 9007199254740992.0)) - 0.5;
    }
}
