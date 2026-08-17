using PlayromaMusic.Core.GenerationLab;

namespace PlayromaMusic.Generators.Presets;

public sealed record AlgorithmParameterPreset(
    string Id,
    string Name,
    AlgorithmKind Algorithm,
    IReadOnlyDictionary<string, double> Parameters);

public static class AlgorithmParameterPresetLibrary
{
    private static readonly IReadOnlyList<AlgorithmParameterPreset> Presets = BuildPresets();

    public static IReadOnlyList<AlgorithmParameterPreset> All => Presets;

    public static IReadOnlyList<AlgorithmParameterPreset> GetFor(AlgorithmKind algorithm) =>
        Presets.Where(preset => preset.Algorithm == algorithm).ToArray();

    public static AlgorithmParameterPreset GetDefault(AlgorithmKind algorithm) =>
        GetFor(algorithm).First();

    public static AlgorithmParameterPreset Get(string id) =>
        Presets.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown algorithm parameter preset: {id}");

    public static AlgorithmParameterPreset GetNext(AlgorithmKind algorithm, string currentId, int direction = 1)
    {
        IReadOnlyList<AlgorithmParameterPreset> candidates = GetFor(algorithm);
        int current = -1;
        for (int index = 0; index < candidates.Count; index++)
        {
            if (string.Equals(candidates[index].Id, currentId, StringComparison.OrdinalIgnoreCase))
            {
                current = index;
                break;
            }
        }

        int next = Mod(current + direction, candidates.Count);
        return candidates[next];
    }

    public static Dictionary<string, double> CloneParameters(AlgorithmParameterPreset preset) =>
        preset.Parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<AlgorithmParameterPreset> BuildPresets() =>
        new List<AlgorithmParameterPreset>
        {
            P("thue-classic", "Classic Binary", AlgorithmKind.ThueMorse, ("Offset", 0)),
            P("thue-shifted", "Shifted Sequence", AlgorithmKind.ThueMorse, ("Offset", 37)),

            P("collatz-27", "Twenty Seven", AlgorithmKind.Collatz, ("Start", 27), ("Skip", 0)),
            P("collatz-6171", "Long Excursion", AlgorithmKind.Collatz, ("Start", 6171), ("Skip", 0)),
            P("collatz-late", "Late Orbit", AlgorithmKind.Collatz, ("Start", 871), ("Skip", 24)),

            P("logistic-edge", "Edge of Chaos", AlgorithmKind.Logistic, ("R", 3.56995), ("X0", 0.381966), ("BurnIn", 96)),
            P("logistic-full", "Full Chaos", AlgorithmKind.Logistic, ("R", 3.99), ("X0", 0.271828), ("BurnIn", 96)),
            P("logistic-window", "Periodic Window", AlgorithmKind.Logistic, ("R", 3.835), ("X0", 0.618034), ("BurnIn", 128)),

            P("henon-classic", "Classic Attractor", AlgorithmKind.Henon, ("A", 1.4), ("B", 0.3), ("X0", 0.1), ("Y0", 0), ("BurnIn", 64)),
            P("henon-open", "Open Wings", AlgorithmKind.Henon, ("A", 1.25), ("B", 0.28), ("X0", 0.11), ("Y0", 0.03), ("BurnIn", 80)),

            P("lorenz-classic", "Classic Butterfly", AlgorithmKind.Lorenz, ("Sigma", 10), ("Rho", 28), ("Beta", 2.6666666667), ("Dt", 0.006), ("IterationsPerSample", 4), ("BurnIn", 512), ("X0", 0.1), ("Y0", 0), ("Z0", 0)),
            P("lorenz-wide", "Wide Orbit", AlgorithmKind.Lorenz, ("Sigma", 10), ("Rho", 35), ("Beta", 2.6666666667), ("Dt", 0.0045), ("IterationsPerSample", 5), ("BurnIn", 640), ("X0", 0.11), ("Y0", 0.02), ("Z0", 0)),

            P("hopalong-classic", "Classic Hopalong", AlgorithmKind.Hopalong, ("A", 0.4), ("B", 1), ("C", 0), ("X0", 0.1), ("Y0", 0), ("BurnIn", 32)),
            P("hopalong-spiral", "Spiral Hops", AlgorithmKind.Hopalong, ("A", -0.73), ("B", 1.21), ("C", 0.37), ("X0", 0.1), ("Y0", 0), ("BurnIn", 48)),

            P("pink-balanced", "Balanced Pink", AlgorithmKind.PinkNoise, ("Rows", 12), ("WhiteBlend", 0.08)),
            P("pink-slow", "Slow Pink Drift", AlgorithmKind.PinkNoise, ("Rows", 18), ("WhiteBlend", 0.02)),
            P("pink-grain", "Grainy Pink", AlgorithmKind.PinkNoise, ("Rows", 8), ("WhiteBlend", 0.22)),

            P("walk-gentle", "Gentle Walk", AlgorithmKind.RandomWalk, ("Start", 0.5), ("StepSize", 0.055), ("Center", 0.5), ("CenterPull", 0.03)),
            P("walk-restless", "Restless Walk", AlgorithmKind.RandomWalk, ("Start", 0.5), ("StepSize", 0.16), ("Center", 0.5), ("CenterPull", 0.01)),

            P("ca-rule30", "Rule 30", AlgorithmKind.CellularAutomata, ("Rule", 30), ("Width", 64), ("InitialMode", 0), ("InitialDensity", 0.2)),
            P("ca-rule90", "Rule 90", AlgorithmKind.CellularAutomata, ("Rule", 90), ("Width", 64), ("InitialMode", 0), ("InitialDensity", 0.2)),
            P("ca-rule110", "Rule 110", AlgorithmKind.CellularAutomata, ("Rule", 110), ("Width", 96), ("InitialMode", 1), ("InitialDensity", 0.12)),
            P("ca-rule150", "Rule 150", AlgorithmKind.CellularAutomata, ("Rule", 150), ("Width", 72), ("InitialMode", 2), ("InitialDensity", 0.2))
        };

    private static AlgorithmParameterPreset P(
        string id,
        string name,
        AlgorithmKind algorithm,
        params (string Key, double Value)[] values) =>
        new(
            id,
            name,
            algorithm,
            values.ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase));

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
