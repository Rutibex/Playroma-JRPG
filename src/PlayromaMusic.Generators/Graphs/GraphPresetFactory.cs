using PlayromaMusic.Core.GenerationLab;

namespace PlayromaMusic.Generators.Graphs;

public static class GraphPresetFactory
{
    public static ValueGraphDefinition CreateDefault(double pitchSmoothing = 0.24, int rhythmLevels = 8)
    {
        return new ValueGraphDefinition
        {
            Nodes = new List<ValueTransformNodeDefinition>
            {
                Node("primary-normalize", "Primary Normalize", ValueTransformKind.Normalize, "source.primary"),
                Node("pitch-shaper", "Pitch Smooth", ValueTransformKind.Smooth, "primary-normalize", ("Alpha", pitchSmoothing)),
                Node("pitch-curve", "Pitch Curve", ValueTransformKind.Power, "pitch-shaper", ("Exponent", 0.85)),
                Node("secondary-normalize", "Secondary Normalize", ValueTransformKind.Normalize, "source.secondary"),
                Node("rhythm-quantize", "Rhythm Quantize", ValueTransformKind.Quantize, "secondary-normalize", ("Levels", rhythmLevels)),
                Node("velocity-fold", "Velocity Fold", ValueTransformKind.Fold, "primary-normalize", ("Scale", 2.0)),
                Node("control-smooth", "Control Smooth", ValueTransformKind.Smooth, "secondary-normalize", ("Alpha", 0.12))
            },
            PitchOutputNodeId = "pitch-curve",
            RhythmOutputNodeId = "rhythm-quantize",
            VelocityOutputNodeId = "velocity-fold",
            ControlOutputNodeId = "control-smooth"
        };
    }

    public static void CyclePitchShaper(ValueGraphDefinition graph)
    {
        ValueTransformKind[] sequence =
        {
            ValueTransformKind.Smooth,
            ValueTransformKind.Quantize,
            ValueTransformKind.Fold,
            ValueTransformKind.Derivative,
            ValueTransformKind.Invert,
            ValueTransformKind.SampleHold
        };
        ValueTransformNodeDefinition? node = graph.Nodes.FirstOrDefault(item =>
            string.Equals(item.Id, "pitch-shaper", StringComparison.OrdinalIgnoreCase));
        int index = node is null ? -1 : Array.IndexOf(sequence, node.Kind);
        SetPitchShaper(graph, sequence[(index + 1 + sequence.Length) % sequence.Length]);
    }

    public static void SetPitchShaper(ValueGraphDefinition graph, ValueTransformKind kind)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValueTransformNodeDefinition? node = graph.Nodes.FirstOrDefault(item =>
            string.Equals(item.Id, "pitch-shaper", StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            node = Node("pitch-shaper", "Pitch " + kind, kind, "source.primary");
            graph.Nodes.Insert(0, node);
        }

        node.Kind = kind;
        node.Name = "Pitch " + kind;
        node.Parameters = Defaults(kind);
        graph.PitchOutputNodeId = node.Id;
    }

    private static ValueTransformNodeDefinition Node(
        string id,
        string name,
        ValueTransformKind kind,
        string input,
        params (string Name, double Value)[] parameters) =>
        new()
        {
            Id = id,
            Name = name,
            Kind = kind,
            InputNodeId = input,
            Parameters = parameters.ToDictionary(item => item.Name, item => item.Value, StringComparer.OrdinalIgnoreCase)
        };

    private static Dictionary<string, double> Defaults(ValueTransformKind kind) => kind switch
    {
        ValueTransformKind.Smooth => new(StringComparer.OrdinalIgnoreCase) { ["Alpha"] = 0.24 },
        ValueTransformKind.Quantize => new(StringComparer.OrdinalIgnoreCase) { ["Levels"] = 9 },
        ValueTransformKind.Fold => new(StringComparer.OrdinalIgnoreCase) { ["Scale"] = 2.5 },
        ValueTransformKind.SampleHold => new(StringComparer.OrdinalIgnoreCase) { ["Samples"] = 4 },
        _ => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    };
}
