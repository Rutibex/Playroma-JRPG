using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Profiles;
using Playroma.Sound.Graph.BuiltIns;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Validation;

namespace Playroma.Sound.Generators.Building;

internal sealed class GeneratedGraphBuilder
{
    private readonly SoundGenerationContext _context;
    private readonly string _generatorId;
    private readonly int _generatorVersion;
    private readonly SoundGraphDefinition _graph;
    private readonly Dictionary<string, int> _idCounters = new(StringComparer.Ordinal);
    private int _processorCount;

    public GeneratedGraphBuilder(string generatorId, int generatorVersion, SoundGenerationContext context, string fallbackId, string fallbackName)
    {
        _generatorId = generatorId;
        _generatorVersion = generatorVersion;
        _context = context;
        _graph = new SoundGraphDefinition
        {
            Id = context.GraphId ?? fallbackId,
            DisplayName = context.DisplayName ?? fallbackName,
            RootSeed = context.RootSeed,
            Output = new GraphOutputDefinition
            {
                EnableLatencyCompensation = true,
                MaximumTailSeconds = context.Budget.MaximumTailSeconds
            }
        };
    }

    public SoundGraphDefinition Graph => _graph;
    public int NodeCount => _graph.Nodes.Count;
    public int ProcessorCount => _processorCount;
    public SoundComplexityBudget Budget => _context.Budget;

    public string AddNode(string stem, string typeId, params (string Id, GraphValue Value)[] parameters)
    {
        if (_graph.Nodes.Count >= Budget.MaximumNodes)
        {
            throw new InvalidOperationException($"Generator '{_generatorId}' exceeded its {Budget.MaximumNodes}-node complexity budget.");
        }

        if (typeId.StartsWith("dsp.", StringComparison.Ordinal))
        {
            if (_processorCount >= Budget.MaximumProcessors)
            {
                throw new InvalidOperationException($"Generator '{_generatorId}' exceeded its {Budget.MaximumProcessors}-processor complexity budget.");
            }

            _processorCount++;
        }

        string id = UniqueId(stem);
        var node = new NodeDefinition { Id = id, TypeId = typeId };
        foreach ((string parameterId, GraphValue value) in parameters)
        {
            node.Parameters[parameterId] = value;
        }

        _graph.Nodes.Add(node);
        return id;
    }

    public void Connect(string fromNodeId, string toNodeId, string fromPortId = "out", string toPortId = "in") =>
        _graph.Connections.Add(new ConnectionDefinition
        {
            FromNodeId = fromNodeId,
            FromPortId = fromPortId,
            ToNodeId = toNodeId,
            ToPortId = toPortId
        });

    public string AddMixer(string stem, IEnumerable<string> inputs, double gain = 1)
    {
        string mixer = AddNode(stem, "core.mixer", ("gain", gain));
        foreach (string input in inputs)
        {
            Connect(input, mixer);
        }

        return mixer;
    }

    public string AddEnvironment(string input, AcousticEnvironment environment, float distance, float amount = 1)
    {
        AcousticEnvironmentProfile profile = EnvironmentProfiles.Get(environment);
        distance = Semantic.Clamp01(distance);
        amount = Semantic.Clamp01(amount);
        string current = input;

        double cutoff = Math.Clamp(profile.DistanceLowPassHz * (1 - (distance * 0.72)), 120, 20_000);
        if (distance > 0.02f || environment == AcousticEnvironment.Underwater)
        {
            string filter = AddNode("distance-filter", "dsp.biquad",
                ("filterType", "LowPass"),
                ("frequencyHz", cutoff),
                ("q", 0.707),
                ("gainDecibels", 0));
            Connect(current, filter);
            current = filter;
        }

        double wet = Math.Clamp(profile.Wet * amount * (0.7 + (distance * 0.6)), 0, 0.75);
        if (wet > 0.005 && environment != AcousticEnvironment.SyntheticVacuum)
        {
            double tail = Math.Min(profile.TailSeconds, Budget.MaximumTailSeconds);
            string reverb = AddNode("environment", "dsp.reverb",
                ("preDelayMilliseconds", profile.PreDelayMilliseconds),
                ("roomSize", profile.RoomSize),
                ("damping", profile.Damping),
                ("wet", wet),
                ("dry", Math.Clamp(1 - (distance * 0.3), 0.55, 1)),
                ("width", profile.Width),
                ("tailSeconds", tail));
            Connect(current, reverb);
            current = reverb;
        }

        return current;
    }

    public string AddProtection(string input, double ceilingDecibels = -0.8)
    {
        string limiter = AddNode("protection", "dsp.lookahead-limiter",
            ("lookAheadMilliseconds", 2.5),
            ("ceilingDecibels", ceilingDecibels),
            ("releaseMilliseconds", 65));
        Connect(input, limiter);
        return limiter;
    }

    public void AddMacro(string id, string displayName, float defaultValue, params MacroBinding[] bindings) =>
        _graph.Macros.Add(new MacroDefinition
        {
            Id = id,
            DisplayName = displayName,
            Minimum = 0,
            Maximum = 1,
            DefaultValue = Semantic.Clamp01(defaultValue),
            Bindings = [.. bindings]
        });

    public static MacroBinding Binding(string nodeId, string parameterId, float minimum, float maximum, MacroMappingCurve curve = MacroMappingCurve.Linear) =>
        new()
        {
            TargetPath = $"node:{nodeId}/parameter:{parameterId}",
            InputMinimum = 0,
            InputMaximum = 1,
            OutputMinimum = minimum,
            OutputMaximum = maximum,
            Curve = curve
        };

    public SoundGraphDefinition Complete(string input, IReadOnlyDictionary<string, string>? descriptionMetadata = null)
    {
        string output = AddNode("output", "core.output");
        Connect(input, output);
        _graph.Output.NodeId = output;

        _graph.Metadata["generator.id"] = _generatorId;
        _graph.Metadata["generator.version"] = _generatorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _graph.Metadata["generator.seed"] = _context.RootSeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _graph.Metadata["generator.familySeed"] = _context.FamilySeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _graph.Metadata["generator.variationIndex"] = _context.VariationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _graph.Metadata["generator.qualityMode"] = _context.QualityMode.ToString();
        _graph.Metadata["generator.nodeCount"] = _graph.Nodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _graph.Metadata["generator.processorCount"] = _processorCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (descriptionMetadata is not null)
        {
            foreach ((string key, string value) in descriptionMetadata)
            {
                _graph.Metadata[$"description.{key}"] = value;
            }
        }

        GraphValidationReport report = new GraphValidator(BuiltInGraphRegistry.CreateDefault()).Validate(_graph);
        if (report.HasErrors)
        {
            string diagnostics = string.Join(Environment.NewLine, report.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
            throw new InvalidOperationException($"Generator '{_generatorId}' produced an invalid graph:{Environment.NewLine}{diagnostics}");
        }

        return _graph;
    }

    private string UniqueId(string stem)
    {
        stem = Sanitize(stem);
        if (!_idCounters.TryGetValue(stem, out int value))
        {
            _idCounters[stem] = 1;
            return stem;
        }

        value++;
        _idCounters[stem] = value;
        return $"{stem}-{value}";
    }

    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        int written = 0;
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                buffer[written++] = char.ToLowerInvariant(character);
            }
        }

        return written == 0 ? "node" : new string(buffer[..written]);
    }
}

internal static class LayerBuilders
{
    public static string AddNoiseBurst(
        GeneratedGraphBuilder builder,
        string stem,
        string color,
        double amplitude,
        double duration,
        double frequencyHz,
        double q,
        double attack = 0.0005,
        double releaseRatio = 0.8)
    {
        return AddNoiseBurst(
            builder,
            stem,
            color,
            amplitude,
            duration,
            frequencyHz,
            q,
            out _,
            attack,
            releaseRatio);
    }

    public static string AddNoiseBurst(
        GeneratedGraphBuilder builder,
        string stem,
        string color,
        double amplitude,
        double duration,
        double frequencyHz,
        double q,
        out string sourceNodeId,
        double attack = 0.0005,
        double releaseRatio = 0.8)
    {
        sourceNodeId = builder.AddNode(stem, "core.noise",
            ("color", color),
            ("amplitude", Math.Clamp(amplitude, 0, 4)),
            ("durationSeconds", Math.Clamp(duration, 0.002, 600)),
            ("attackSeconds", Math.Clamp(attack, 0, duration)),
            ("releaseSeconds", Math.Clamp(duration * releaseRatio, 0, duration)));
        string filter = builder.AddNode($"{stem}-filter", "dsp.biquad",
            ("filterType", "BandPass"),
            ("frequencyHz", Math.Clamp(frequencyHz, 20, 20_000)),
            ("q", Math.Clamp(q, 0.05, 50)),
            ("gainDecibels", 0));
        builder.Connect(sourceNodeId, filter);
        return filter;
    }

    public static string AddTonalBody(
        GeneratedGraphBuilder builder,
        string stem,
        string waveform,
        double frequencyHz,
        double amplitude,
        double duration,
        double attack,
        double release)
    {
        return builder.AddNode(stem, "core.oscillator",
            ("waveform", waveform),
            ("frequencyHz", Math.Clamp(frequencyHz, 10, 20_000)),
            ("amplitude", Math.Clamp(amplitude, 0, 4)),
            ("durationSeconds", Math.Clamp(duration, 0.002, 600)),
            ("attackSeconds", Math.Clamp(attack, 0, duration)),
            ("releaseSeconds", Math.Clamp(release, 0, duration)));
    }

    public static string AddGestureTone(
        GeneratedGraphBuilder builder,
        string stem,
        string waveform,
        double rootFrequencyHz,
        double amplitude,
        double duration,
        string pitchContour,
        string amplitudeContour,
        double vibratoDepthSemitones = 0,
        double vibratoRateHz = 5.5,
        double vibratoDelaySeconds = 0,
        double tremoloDepth = 0,
        double tremoloRateHz = 5,
        double driftDepthSemitones = 0,
        double driftRateHz = 1.4,
        double attackSeconds = 0.002,
        double releaseSeconds = 0.05,
        string quantization = "None",
        double pulseWidth = 0.5)
    {
        return builder.AddNode(stem, "core.gesture-oscillator",
            ("waveform", waveform),
            ("rootFrequencyHz", Math.Clamp(rootFrequencyHz, 10, 20_000)),
            ("amplitude", Math.Clamp(amplitude, 0, 4)),
            ("durationSeconds", Math.Clamp(duration, 0.002, 600)),
            ("attackSeconds", Math.Clamp(attackSeconds, 0, duration)),
            ("releaseSeconds", Math.Clamp(releaseSeconds, 0, duration)),
            ("pitchContour", pitchContour),
            ("amplitudeContour", amplitudeContour),
            ("vibratoDepthSemitones", Math.Clamp(vibratoDepthSemitones, 0, 12)),
            ("vibratoRateHz", Math.Clamp(vibratoRateHz, 0.01, 40)),
            ("vibratoDelaySeconds", Math.Clamp(vibratoDelaySeconds, 0, duration)),
            ("tremoloDepth", Math.Clamp(tremoloDepth, 0, 1)),
            ("tremoloRateHz", Math.Clamp(tremoloRateHz, 0.01, 40)),
            ("driftDepthSemitones", Math.Clamp(driftDepthSemitones, 0, 12)),
            ("driftRateHz", Math.Clamp(driftRateHz, 0.01, 20)),
            ("quantization", quantization),
            ("pulseWidth", Math.Clamp(pulseWidth, 0.01, 0.99)));
    }

    public static string AddDelayed(GeneratedGraphBuilder builder, string input, string stem, double seconds)
    {
        string offset = builder.AddNode(stem, "core.offset", ("delaySeconds", Math.Clamp(seconds, 0, 600)));
        builder.Connect(input, offset);
        return offset;
    }
}
