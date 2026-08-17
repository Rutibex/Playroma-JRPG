using PlayromaMusic.Core.GenerationLab;

namespace PlayromaMusic.Generators.Algorithms;

public sealed class AlgorithmSourceFactory
{
    private readonly IReadOnlyDictionary<AlgorithmKind, IAlgorithmSource> _sources;

    public AlgorithmSourceFactory()
    {
        IAlgorithmSource[] sources =
        {
            new ThueMorseSource(),
            new CollatzSource(),
            new LogisticSource(),
            new HenonSource(),
            new LorenzSource(),
            new HopalongSource(),
            new PinkNoiseSource(),
            new RandomWalkSource(),
            new CellularAutomataSource()
        };
        _sources = sources.ToDictionary(source => source.Kind);
    }

    public IAlgorithmSource Get(AlgorithmKind kind) =>
        _sources.TryGetValue(kind, out IAlgorithmSource? source)
            ? source
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown algorithm source.");

    public AlgorithmSeries Generate(
        AlgorithmKind kind,
        ulong seed,
        int sampleCount,
        IReadOnlyDictionary<string, double> parameters)
    {
        if (sampleCount is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        return Get(kind).Generate(new AlgorithmContext(seed, sampleCount), parameters);
    }
}
