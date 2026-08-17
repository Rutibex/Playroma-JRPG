using Playroma.Sound.Core.Random;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;
using Playroma.Sound.Graph.Serialization;
using Playroma.Sound.Graph.Validation;

namespace Playroma.Sound.Generators.Mutation;

public enum MutationMode
{
    Subtle,
    Timbral,
    Temporal,
    Spatial,
    Aggressive
}

public sealed record MutationOptions
{
    public ulong Seed { get; init; } = 1;
    public float Amount { get; init; } = .12f;
    public MutationMode Mode { get; init; } = MutationMode.Subtle;
    public bool PreserveDuration { get; init; } = true;
    public bool PreserveMacroLayout { get; init; } = true;
    public IReadOnlySet<string> LockedPaths { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed class SoundMutator
{
    private readonly GraphNodeRegistry _registry;
    private readonly SoundGraphSerializer _serializer = new();

    public SoundMutator(GraphNodeRegistry registry) => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public SoundGraphDefinition Mutate(SoundGraphDefinition original, MutationOptions options)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(options);
        SoundGraphDefinition result = _serializer.Load(_serializer.Serialize(original));
        var random = new Pcg32(options.Seed, 0x4D55544154494F4EUL);
        float amount = Math.Clamp(options.Amount, 0, 1);

        foreach (NodeDefinition node in result.Nodes)
        {
            if (!_registry.TryGetFactory(node.TypeId, out IGraphNodeFactory factory))
            {
                continue;
            }

            foreach (GraphParameterDescriptor descriptor in factory.Descriptor.Parameters)
            {
                if (descriptor.Type is not (GraphParameterType.Number or GraphParameterType.Integer) ||
                    !node.Parameters.TryGetValue(descriptor.Id, out GraphValue current))
                {
                    continue;
                }

                string path = $"node:{node.Id}/parameter:{descriptor.Id}";
                if (options.LockedPaths.Contains(path) || ShouldPreserve(descriptor.Id, options))
                {
                    continue;
                }

                double minimum = descriptor.Minimum ?? current.AsDouble();
                double maximum = descriptor.Maximum ?? current.AsDouble();
                double value = current.AsDouble();
                double range = Math.Max(0, maximum - minimum);
                double modeScale = ParameterScale(descriptor.Id, options.Mode);
                double delta = random.NextSignedFloat() * range * amount * modeScale;
                double mutated = Math.Clamp(value + delta, minimum, maximum);
                if (descriptor.Type == GraphParameterType.Integer)
                {
                    mutated = Math.Round(mutated);
                }

                node.Parameters[descriptor.Id] = mutated;
            }
        }

        result.RootSeed = unchecked(result.RootSeed + options.Seed + 1);
        result.Id = $"{original.Id}.mutation-{options.Seed:X}";
        result.DisplayName = $"{original.DisplayName} Mutation";
        result.Metadata["mutation.seed"] = options.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata["mutation.amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata["mutation.mode"] = options.Mode.ToString();

        GraphValidationReport report = new GraphValidator(_registry).Validate(result);
        if (report.HasErrors)
        {
            throw new InvalidOperationException("Mutation produced an invalid graph: " + string.Join("; ", report.Diagnostics.Select(item => item.Message)));
        }

        return result;
    }

    private static bool ShouldPreserve(string parameterId, MutationOptions options)
    {
        if (options.PreserveDuration && (parameterId.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
                                         parameterId.Contains("tail", StringComparison.OrdinalIgnoreCase) ||
                                         parameterId.Contains("delaySeconds", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return options.Mode switch
        {
            MutationMode.Timbral => parameterId.Contains("delay", StringComparison.OrdinalIgnoreCase) || parameterId.Contains("wet", StringComparison.OrdinalIgnoreCase),
            MutationMode.Temporal => parameterId.Contains("frequency", StringComparison.OrdinalIgnoreCase) || parameterId.Contains("gain", StringComparison.OrdinalIgnoreCase),
            MutationMode.Spatial => !(parameterId.Contains("pan", StringComparison.OrdinalIgnoreCase) || parameterId.Contains("wet", StringComparison.OrdinalIgnoreCase) || parameterId.Contains("width", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static double ParameterScale(string parameterId, MutationMode mode) => mode switch
    {
        MutationMode.Subtle => .18,
        MutationMode.Aggressive => .8,
        MutationMode.Timbral when parameterId.Contains("frequency", StringComparison.OrdinalIgnoreCase) => .55,
        MutationMode.Timbral => .28,
        MutationMode.Temporal => parameterId.Contains("attack", StringComparison.OrdinalIgnoreCase) || parameterId.Contains("release", StringComparison.OrdinalIgnoreCase) ? .55 : .08,
        MutationMode.Spatial => .5,
        _ => .25
    };
}
