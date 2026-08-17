using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Examples;

public static class GraphExamples
{
    public static SoundGraphDefinition CreateLayeredImpact(ulong seed = 12_345)
    {
        var graph = new SoundGraphDefinition
        {
            Id = "example.layered-impact",
            DisplayName = "Layered Procedural Impact",
            RootSeed = seed,
            Output = new GraphOutputDefinition
            {
                NodeId = "output",
                MaximumTailSeconds = 8,
                EnableLatencyCompensation = true
            },
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category"] = "Impact",
                ["authoringPhase"] = "3"
            }
        };

        graph.Nodes.Add(Node("body", "core.oscillator",
            ("waveform", "sine"),
            ("frequencyHz", 82),
            ("amplitude", 0.85),
            ("durationSeconds", 0.42),
            ("attackSeconds", 0.001),
            ("releaseSeconds", 0.30)));

        graph.Nodes.Add(Node("crack", "core.noise",
            ("color", "white"),
            ("amplitude", 0.65),
            ("durationSeconds", 0.16),
            ("attackSeconds", 0.0005),
            ("releaseSeconds", 0.12)));

        graph.Nodes.Add(Node("crack-filter", "dsp.biquad",
            ("filterType", "bandPass"),
            ("frequencyHz", 2_800),
            ("q", 0.8)));

        graph.Nodes.Add(Node("debris", "core.noise",
            ("color", "brown"),
            ("amplitude", 0.24),
            ("durationSeconds", 0.55),
            ("attackSeconds", 0.01),
            ("releaseSeconds", 0.4)));

        graph.Nodes.Add(Node("debris-offset", "core.offset", ("delaySeconds", 0.035)));
        graph.Nodes.Add(Node("mixer", "core.mixer", ("gain", 0.72)));
        graph.Nodes.Add(Node("shape", "dsp.waveshaper",
            ("mode", "hyperbolicTangent"),
            ("driveDecibels", 7),
            ("mix", 0.65),
            ("outputDecibels", -2)));
        graph.Nodes.Add(Node("room", "dsp.reverb",
            ("roomSize", 0.48),
            ("damping", 0.56),
            ("wet", 0.18),
            ("dry", 1.0),
            ("tailSeconds", 3.5)));
        graph.Nodes.Add(Node("limiter", "dsp.lookahead-limiter",
            ("lookAheadMilliseconds", 3.0),
            ("ceilingDecibels", -0.7),
            ("releaseMilliseconds", 70)));
        graph.Nodes.Add(Node("output", "core.output"));

        Connect(graph, "body", "mixer");
        Connect(graph, "crack", "crack-filter");
        Connect(graph, "crack-filter", "mixer");
        Connect(graph, "debris", "debris-offset");
        Connect(graph, "debris-offset", "mixer");
        Connect(graph, "mixer", "shape");
        Connect(graph, "shape", "room");
        Connect(graph, "room", "limiter");
        Connect(graph, "limiter", "output");

        graph.Macros.Add(new MacroDefinition
        {
            Id = "punch",
            DisplayName = "Punch",
            Minimum = 0,
            Maximum = 1,
            DefaultValue = 0.62f,
            Bindings =
            [
                Binding("body", "amplitude", 0.35f, 1.25f, MacroMappingCurve.SmoothStep),
                Binding("shape", "driveDecibels", 1f, 18f, MacroMappingCurve.Exponential)
            ]
        });

        graph.Macros.Add(new MacroDefinition
        {
            Id = "brightness",
            DisplayName = "Brightness",
            Minimum = 0,
            Maximum = 1,
            DefaultValue = 0.55f,
            Bindings =
            [
                Binding("crack-filter", "frequencyHz", 650f, 7_500f, MacroMappingCurve.Logarithmic)
            ]
        });

        graph.Macros.Add(new MacroDefinition
        {
            Id = "space",
            DisplayName = "Space",
            Minimum = 0,
            Maximum = 1,
            DefaultValue = 0.25f,
            Bindings =
            [
                Binding("room", "wet", 0.03f, 0.48f, MacroMappingCurve.SmoothStep),
                Binding("room", "roomSize", 0.22f, 0.92f, MacroMappingCurve.SmoothStep)
            ]
        });

        return graph;
    }

    public static SoundGraphDefinition CreateEngineDrone(ulong seed = 90_001)
    {
        var graph = new SoundGraphDefinition
        {
            Id = "example.engine-drone",
            DisplayName = "Reactive Engine Drone",
            RootSeed = seed,
            Output = new GraphOutputDefinition { NodeId = "output", MaximumTailSeconds = 4 }
        };

        graph.Nodes.Add(Node("fundamental", "core.oscillator",
            ("waveform", "saw"), ("frequencyHz", 52), ("amplitude", 0.24)));
        graph.Nodes.Add(Node("sub", "core.oscillator",
            ("waveform", "sine"), ("frequencyHz", 26), ("amplitude", 0.35)));
        graph.Nodes.Add(Node("air", "core.noise",
            ("color", "pink"), ("amplitude", 0.055)));
        graph.Nodes.Add(Node("mixer", "core.mixer", ("gain", 0.75)));
        graph.Nodes.Add(Node("filter", "dsp.state-variable",
            ("mode", "lowPass"), ("frequencyHz", 1_400), ("resonance", 0.24), ("drive", 0.4)));
        graph.Nodes.Add(Node("chorus", "dsp.chorus",
            ("baseDelayMilliseconds", 9), ("depthMilliseconds", 3.5), ("rateHz", 0.18),
            ("feedback", 0.08), ("wet", 0.28), ("dry", 1.0)));
        graph.Nodes.Add(Node("compressor", "dsp.compressor",
            ("thresholdDecibels", -20), ("ratio", 3.5), ("attackMilliseconds", 18),
            ("releaseMilliseconds", 140), ("makeupGainDecibels", 2), ("mix", 1)));
        graph.Nodes.Add(Node("output", "core.output"));

        Connect(graph, "fundamental", "mixer");
        Connect(graph, "sub", "mixer");
        Connect(graph, "air", "mixer");
        Connect(graph, "mixer", "filter");
        Connect(graph, "filter", "chorus");
        Connect(graph, "chorus", "compressor");
        Connect(graph, "compressor", "output");

        graph.Macros.Add(new MacroDefinition
        {
            Id = "throttle",
            DisplayName = "Throttle",
            Minimum = 0,
            Maximum = 1,
            DefaultValue = 0.35f,
            Bindings =
            [
                Binding("fundamental", "frequencyHz", 32, 135, MacroMappingCurve.Exponential),
                Binding("sub", "frequencyHz", 16, 67.5f, MacroMappingCurve.Exponential),
                Binding("filter", "frequencyHz", 420, 6_500, MacroMappingCurve.Logarithmic),
                Binding("filter", "drive", 0.05f, 1.8f, MacroMappingCurve.SmoothStep)
            ]
        });

        graph.Macros.Add(new MacroDefinition
        {
            Id = "damage",
            DisplayName = "Damage",
            Minimum = 0,
            Maximum = 1,
            DefaultValue = 0,
            Bindings =
            [
                Binding("air", "amplitude", 0.03f, 0.28f, MacroMappingCurve.Exponential),
                Binding("chorus", "feedback", 0.05f, 0.72f, MacroMappingCurve.Exponential)
            ]
        });

        return graph;
    }

    private static NodeDefinition Node(string id, string typeId, params (string Id, GraphValue Value)[] parameters)
    {
        var node = new NodeDefinition { Id = id, TypeId = typeId };
        foreach ((string parameterId, GraphValue value) in parameters)
        {
            node.Parameters[parameterId] = value;
        }

        return node;
    }

    private static void Connect(SoundGraphDefinition graph, string from, string to) =>
        graph.Connections.Add(new ConnectionDefinition { FromNodeId = from, ToNodeId = to });

    private static MacroBinding Binding(
        string nodeId,
        string parameterId,
        float outputMinimum,
        float outputMaximum,
        MacroMappingCurve curve) =>
        new()
        {
            TargetPath = $"node:{nodeId}/parameter:{parameterId}",
            InputMinimum = 0,
            InputMaximum = 1,
            OutputMinimum = outputMinimum,
            OutputMaximum = outputMaximum,
            Curve = curve
        };
}
