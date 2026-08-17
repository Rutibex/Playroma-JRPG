using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlayromaMusic.Core.Extensions;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Generators.Extensions;
using PlayromaMusic.Generators.Rhythms;
using PlayromaMusic.Generators.Structured;

namespace PlayromaMusic.Generators.StylePacks;

public sealed class StylePackCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly Dictionary<string, StylePackSourceDocument> _sources;
    private readonly Dictionary<string, StylePackDefinition> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StylePackDiagnostic> _diagnostics;

    private StylePackCatalog(
        Dictionary<string, StylePackSourceDocument> sources,
        List<StylePackDiagnostic> diagnostics)
    {
        _sources = sources;
        _diagnostics = diagnostics;
        ResolveAndValidateAll();
        RegisterRhythms();
    }

    public IReadOnlyList<StylePackDefinition> AllPacks => _resolved.Values
        .OrderBy(pack => pack.IsAbstract)
        .ThenBy(pack => pack.Family)
        .ThenBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Concrete packs that passed semantic validation and are safe to use for generation.
    /// Invalid overrides remain visible through <see cref="AllPacks"/> and the validation
    /// diagnostics, but cannot crash the composer or rhythm registry.
    /// </summary>
    public IReadOnlyList<StylePackDefinition> ConcretePacks => AllPacks
        .Where(pack => !pack.IsAbstract && !HasErrors(pack.Id))
        .ToArray();

    public StylePackValidationSummary Validation => new()
    {
        SourceDocumentCount = _sources.Count,
        ResolvedPackCount = _resolved.Count,
        ConcretePackCount = _resolved.Values.Count(pack => !pack.IsAbstract && !HasErrors(pack.Id)),
        Diagnostics = _diagnostics.ToList()
    };

    public static StylePackCatalog LoadDefault(IEnumerable<string>? additionalSearchPaths = null)
    {
        Dictionary<string, StylePackSourceDocument> sources = new(StringComparer.OrdinalIgnoreCase);
        List<StylePackDiagnostic> diagnostics = new();
        LoadEmbedded(sources, diagnostics);

        List<string> paths = new()
        {
            Path.Combine(AppContext.BaseDirectory, "StylePacks"),
            Path.Combine(Environment.CurrentDirectory, "StylePacks"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlayromaMusic", "StylePacks")
        };
        if (additionalSearchPaths is not null)
        {
            paths.AddRange(additionalSearchPaths.Where(path => !string.IsNullOrWhiteSpace(path)));
        }

        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            LoadDirectory(path, sources, diagnostics);
        }

        foreach (StylePackTextSource pluginSource in GeneratorPluginHost.Default.StyleSources)
        {
            ParseAndStore(pluginSource.Json, "plugin:" + pluginSource.SourceName, sources, diagnostics);
        }
        foreach (PluginDiagnostic diagnostic in GeneratorPluginHost.Default.Diagnostics.Where(item => !item.Severity.Equals("Information", StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new StylePackDiagnostic(diagnostic.PluginId, diagnostic.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ? StyleDiagnosticSeverity.Error : StyleDiagnosticSeverity.Warning, diagnostic.Message, diagnostic.Source));
        }

        return new StylePackCatalog(sources, diagnostics);
    }

    public StylePackDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_resolved.TryGetValue(id, out StylePackDefinition? pack))
        {
            return pack;
        }

        throw new KeyNotFoundException($"Unknown style pack: {id}.");
    }

    public bool TryGet(string id, out StylePackDefinition? pack) => _resolved.TryGetValue(id, out pack);

    public StructuredStyleDefinition ToStructuredStyle(string id) => ToStructuredStyle(Get(id));

    public StructuredStyleDefinition ComposeHybrid(StyleBlendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        StylePackDefinition primary = Get(settings.PrimaryPackId);
        StylePackDefinition secondary = Get(settings.SecondaryPackId);
        EnsurePlayable(primary);
        EnsurePlayable(secondary);

        double secondaryWeight = Math.Clamp(settings.SecondaryWeight, 0, 100) / 100.0;
        double primaryWeight = 1.0 - secondaryWeight;
        string hybridId = $"hybrid:{primary.Id}+{secondary.Id}:{settings.SecondaryWeight}";

        IReadOnlyList<string[]> forms = settings.BlendForms
            ? WeightedMerge(primary.FormProductions, secondary.FormProductions, primaryWeight, secondaryWeight)
            : primary.FormProductions.Select(item => item.ToArray()).ToArray();

        Dictionary<string, FormTokenDefinition> tokens = primary.Tokens.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (settings.BlendForms && secondaryWeight > 0)
        {
            foreach ((string key, FormTokenDefinition value) in secondary.Tokens)
            {
                if (!tokens.ContainsKey(key) || secondaryWeight >= 0.5)
                {
                    tokens[key] = value;
                }
            }
        }

        IReadOnlyDictionary<FormSectionKind, IReadOnlyList<int[]>> progressions = settings.BlendHarmony
            ? MergeProgressions(primary.Progressions, secondary.Progressions, primaryWeight, secondaryWeight)
            : CloneProgressions(primary.Progressions);

        IReadOnlyList<ScaleKind> scales = settings.BlendHarmony
            ? WeightedMerge(primary.Scales, secondary.Scales, primaryWeight, secondaryWeight)
            : primary.Scales.ToArray();

        IReadOnlyList<CompositionRoleSettings> roles = settings.BlendInstruments
            ? BlendRoles(primary.Roles, secondary.Roles, secondaryWeight)
            : CloneRoles(primary.Roles);

        StyleConstraintProfile constraints = settings.BlendScoring
            ? BlendConstraints(primary.Constraints, secondary.Constraints, secondaryWeight)
            : primary.Constraints;
        StyleScorerWeights scoring = settings.BlendScoring
            ? BlendScoring(primary.Scoring, secondary.Scoring, secondaryWeight)
            : primary.Scoring;

        Dictionary<string, bool[]> rhythms = primary.RhythmVocabulary.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        if (settings.BlendRhythms && secondaryWeight > 0)
        {
            foreach ((string key, bool[] value) in secondary.RhythmVocabulary)
            {
                rhythms[key] = value.ToArray();
            }
        }
        RhythmPatternLibrary.RegisterRange(rhythms);

        return new StructuredStyleDefinition(
            hybridId,
            $"{primary.Name} / {secondary.Name}",
            $"Hybrid style using {primary.Name} at {primaryWeight:P0} and {secondary.Name} at {secondaryWeight:P0}.",
            $"hybrid-form:{primary.Id}+{secondary.Id}",
            $"hybrid-harmony:{primary.Id}+{secondary.Id}",
            forms,
            tokens,
            progressions,
            scales,
            WeightedInt(primary.MinimumTempo, secondary.MinimumTempo, secondaryWeight),
            WeightedInt(primary.MaximumTempo, secondary.MaximumTempo, secondaryWeight),
            secondaryWeight >= 0.65 ? secondary.BeatsPerBar : primary.BeatsPerBar,
            secondaryWeight >= 0.65 ? secondary.BeatUnit : primary.BeatUnit,
            WeightedInt(primary.CandidateCount, secondary.CandidateCount, secondaryWeight),
            roles,
            StyleFamily.Hybrid,
            constraints,
            scoring,
            rhythms,
            new[] { primary.Id, secondary.Id },
            true,
            $"Hybrid {settings.SecondaryWeight}%",
            new[] { primary.Source, secondary.Source });
    }

    public StructuredStyleDefinition ToStructuredStyle(StylePackDefinition pack)
    {
        EnsurePlayable(pack);
        RhythmPatternLibrary.RegisterRange(pack.RhythmVocabulary);
        return new StructuredStyleDefinition(
            pack.Id,
            pack.Name,
            pack.Description,
            pack.FormGrammarId,
            pack.HarmonicGrammarId,
            pack.FormProductions.Select(form => form.ToArray()).ToArray(),
            pack.Tokens.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            CloneProgressions(pack.Progressions),
            pack.Scales.ToArray(),
            pack.MinimumTempo,
            pack.MaximumTempo,
            pack.BeatsPerBar,
            pack.BeatUnit,
            pack.CandidateCount,
            CloneRoles(pack.Roles),
            pack.Family,
            pack.Constraints,
            pack.Scoring,
            pack.RhythmVocabulary.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            pack.ResolutionChain.ToArray(),
            false,
            pack.Version,
            new[] { pack.Source });
    }

    private void ResolveAndValidateAll()
    {
        foreach (StylePackSourceDocument source in _sources.Values)
        {
            try
            {
                Resolve(source.Id, new Stack<string>());
            }
            catch (Exception exception)
            {
                _diagnostics.Add(new StylePackDiagnostic(
                    source.Id,
                    StyleDiagnosticSeverity.Error,
                    exception.Message,
                    source.Source));
            }
        }

        foreach (StylePackDefinition pack in _resolved.Values)
        {
            ValidateResolved(pack);
        }
    }

    private StylePackDefinition Resolve(string id, Stack<string> stack)
    {
        if (_resolved.TryGetValue(id, out StylePackDefinition? existing))
        {
            return existing;
        }
        if (!_sources.TryGetValue(id, out StylePackSourceDocument? source))
        {
            throw new InvalidDataException($"Style pack dependency '{id}' was not found.");
        }
        if (stack.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Style pack inheritance cycle: " + string.Join(" -> ", stack.Reverse().Append(id)));
        }

        stack.Push(id);
        StylePackAccumulator accumulator = new();
        if (!string.IsNullOrWhiteSpace(source.Extends))
        {
            accumulator.Merge(Resolve(source.Extends, stack));
        }
        foreach (string mixin in source.Mixins)
        {
            accumulator.Merge(Resolve(mixin, stack));
        }
        accumulator.Merge(source);
        stack.Pop();

        StylePackDefinition resolved = accumulator.Build(source.Source);
        _resolved[id] = resolved;
        return resolved;
    }

    private void ValidateResolved(StylePackDefinition pack)
    {
        void Error(string message) => _diagnostics.Add(new StylePackDiagnostic(pack.Id, StyleDiagnosticSeverity.Error, message, pack.Source));
        void Warning(string message) => _diagnostics.Add(new StylePackDiagnostic(pack.Id, StyleDiagnosticSeverity.Warning, message, pack.Source));

        if (string.IsNullOrWhiteSpace(pack.Name)) Error("Pack name is empty.");
        if (pack.MinimumTempo is < 20 or > 400 || pack.MaximumTempo is < 20 or > 400 || pack.MinimumTempo > pack.MaximumTempo)
            Error("Tempo range is invalid.");
        if (pack.BeatsPerBar is < 1 or > 16 || pack.BeatUnit is not (1 or 2 or 4 or 8 or 16))
            Error("Meter is invalid.");
        if (!pack.IsAbstract && pack.FormProductions.Count == 0) Error("Concrete pack has no form productions.");
        if (!pack.IsAbstract && pack.Roles.Count == 0) Error("Concrete pack has no role mappings.");
        if (!pack.IsAbstract && pack.Scales.Count == 0) Error("Concrete pack has no scales.");
        if (pack.CandidateCount is < 2 or > 64) Error("Candidate count must be between 2 and 64.");

        foreach (string[] form in pack.FormProductions)
        {
            if (form.Length == 0) Error("A form production is empty.");
            foreach (string symbol in form)
            {
                if (!pack.Tokens.ContainsKey(symbol)) Error($"Form references undefined token '{symbol}'.");
            }
        }

        foreach ((string rhythmId, bool[] pattern) in pack.RhythmVocabulary)
        {
            if (string.IsNullOrWhiteSpace(rhythmId)) Error("A rhythm pattern has an empty ID.");
            if (pattern.Length is < 1 or > 256) Error($"Rhythm '{rhythmId}' must contain between 1 and 256 steps.");
            else if (!pattern.Any(value => value)) Error($"Rhythm '{rhythmId}' contains no onsets.");
        }

        foreach ((string symbol, FormTokenDefinition token) in pack.Tokens)
        {
            if (string.IsNullOrWhiteSpace(symbol)) Error("A form token has an empty symbol.");
            if (token.LengthBars is < 1 or > 128) Error($"Form token '{symbol}' has an invalid bar length.");
            if (!double.IsFinite(token.Energy) || token.Energy is < 0 or > 1) Error($"Form token '{symbol}' has invalid energy.");
        }

        foreach ((FormSectionKind kind, IReadOnlyList<int[]> progressions) in pack.Progressions)
        {
            foreach (int[] progression in progressions)
            {
                if (progression.Length == 0) Error($"Harmony library for {kind} contains an empty progression.");
                if (progression.Any(degree => degree is < -14 or > 20)) Error($"Harmony library for {kind} contains an unreasonable scale degree.");
            }
        }

        HashSet<string> roleIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (CompositionRoleSettings role in pack.Roles)
        {
            if (string.IsNullOrWhiteSpace(role.Id)) Error("A role mapping has an empty ID.");
            else if (!roleIds.Add(role.Id)) Error($"Duplicate role ID '{role.Id}'.");
            if (string.IsNullOrWhiteSpace(role.Name)) Error($"Role '{role.Id}' has an empty display name.");
            if (role.MidiChannel is < 0 or > 15 || role.GeneralMidiProgram is < 0 or > 127) Error($"Role '{role.Name}' has invalid MIDI settings.");
            if (role.MinimumPitch is < 0 or > 127 || role.MaximumPitch is < 0 or > 127 || role.MinimumPitch > role.MaximumPitch) Error($"Role '{role.Name}' has an invalid range.");
            if (role.Density is < 0 or > 100) Error($"Role '{role.Name}' has an invalid density.");
            InstrumentSelectorSettings selector = role.InstrumentSelector ?? new InstrumentSelectorSettings();
            if (selector.MinimumPitch is < 0 or > 127 || selector.MaximumPitch is < 0 or > 127 || selector.MinimumPitch > selector.MaximumPitch ||
                selector.MaximumLayers is < 1 or > 4 ||
                !ValidUnitRange(selector.MinimumAttack, selector.MaximumAttack) ||
                !ValidUnitRange(selector.MinimumSustain, selector.MaximumSustain) ||
                !ValidUnitRange(selector.MinimumBrightness, selector.MaximumBrightness) ||
                !ValidUnitRange(selector.MinimumPercussiveness, selector.MaximumPercussiveness) ||
                !double.IsFinite(selector.StyleAffinityWeight) || selector.StyleAffinityWeight is < 0 or > 10 ||
                !double.IsFinite(selector.NoveltyWeight) || selector.NoveltyWeight is < 0 or > 10 ||
                !double.IsFinite(selector.RandomWeight) || selector.RandomWeight is < 0 or > 10)
            {
                Error($"Role '{role.Name}' has an invalid instrument selector.");
            }
            if (string.IsNullOrWhiteSpace(role.RhythmPatternId)) Error($"Role '{role.Name}' has no rhythm mapping.");
            else if (!pack.RhythmVocabulary.ContainsKey(role.RhythmPatternId) && !RhythmPatternLibrary.Contains(role.RhythmPatternId))
                Warning($"Role '{role.Name}' references rhythm '{role.RhythmPatternId}' outside this pack.");
        }

        if (pack.Constraints.MaximumMelodicLeap < 1 || pack.Constraints.MaximumMelodicLeap > 48)
            Error("Maximum melodic leap must be between 1 and 48 semitones.");
        if (pack.Constraints.MinimumNotesPerPhrase < 1 || pack.Constraints.MaximumNotesPerPhrase < pack.Constraints.MinimumNotesPerPhrase)
            Error("Phrase note-count constraints are invalid.");
        if (pack.Constraints.MaximumRepeatedPitch < 1 || pack.Constraints.MaximumRepeatedPitch > 64)
            Error("Maximum repeated-pitch run must be between 1 and 64.");
        if (!double.IsFinite(pack.Constraints.MinimumChordToneRatio) || pack.Constraints.MinimumChordToneRatio is < 0 or > 1)
            Error("Minimum chord-tone ratio must be between 0 and 1.");
        if (!double.IsFinite(pack.Constraints.MaximumDensityRatio) || pack.Constraints.MaximumDensityRatio is <= 0 or > 4)
            Error("Maximum density ratio must be greater than 0 and no more than 4.");
        if (pack.Constraints.PreferredMinimumRange < 0 || pack.Constraints.PreferredMaximumRange < pack.Constraints.PreferredMinimumRange)
            Error("Preferred melodic range is invalid.");

        if (!_diagnostics.Any(item => item.PackId.Equals(pack.Id, StringComparison.OrdinalIgnoreCase) && item.Severity == StyleDiagnosticSeverity.Error))
        {
            _diagnostics.Add(new StylePackDiagnostic(pack.Id, StyleDiagnosticSeverity.Information, "Resolved and validated successfully.", pack.Source));
        }
    }

    private static bool ValidUnitRange(double minimum, double maximum) =>
        double.IsFinite(minimum) && double.IsFinite(maximum) &&
        minimum is >= 0 and <= 1 && maximum is >= 0 and <= 1 && minimum <= maximum;

    private void RegisterRhythms()
    {
        foreach (StylePackDefinition pack in _resolved.Values.Where(pack => !HasErrors(pack.Id)))
        {
            try
            {
                RhythmPatternLibrary.RegisterRange(pack.RhythmVocabulary);
            }
            catch (Exception exception)
            {
                _diagnostics.Add(new StylePackDiagnostic(
                    pack.Id,
                    StyleDiagnosticSeverity.Error,
                    "Rhythm registration failed: " + exception.Message,
                    pack.Source));
            }
        }
    }

    private bool HasErrors(string packId) => _diagnostics.Any(item =>
        item.PackId.Equals(packId, StringComparison.OrdinalIgnoreCase) &&
        item.Severity == StyleDiagnosticSeverity.Error);

    private void EnsurePlayable(StylePackDefinition pack)
    {
        if (pack.IsAbstract)
        {
            throw new InvalidOperationException($"Style pack {pack.Id} is abstract and cannot generate music directly.");
        }
        StylePackDiagnostic[] errors = _diagnostics
            .Where(item => item.PackId.Equals(pack.Id, StringComparison.OrdinalIgnoreCase) && item.Severity == StyleDiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException($"Style pack {pack.Id} is invalid: {string.Join("; ", errors.Select(item => item.Message))}");
        }
    }

    private static void LoadEmbedded(
        Dictionary<string, StylePackSourceDocument> sources,
        List<StylePackDiagnostic> diagnostics)
    {
        Assembly assembly = typeof(StylePackCatalog).Assembly;
        foreach (string resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains("StylePacks", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) continue;
            using StreamReader reader = new(stream);
            ParseAndStore(reader.ReadToEnd(), "embedded:" + resourceName, sources, diagnostics);
        }
    }

    private static void LoadDirectory(
        string path,
        Dictionary<string, StylePackSourceDocument> sources,
        List<StylePackDiagnostic> diagnostics)
    {
        if (!Directory.Exists(path)) return;
        foreach (string file in Directory.EnumerateFiles(path, "*.style.json", SearchOption.AllDirectories)
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ParseAndStore(File.ReadAllText(file), Path.GetFullPath(file), sources, diagnostics);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new StylePackDiagnostic(Path.GetFileNameWithoutExtension(file), StyleDiagnosticSeverity.Error, exception.Message, file));
            }
        }
    }

    private static void ParseAndStore(
        string json,
        string sourcePath,
        Dictionary<string, StylePackSourceDocument> sources,
        List<StylePackDiagnostic> diagnostics)
    {
        try
        {
            StylePackSourceDocument document = JsonSerializer.Deserialize<StylePackSourceDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("JSON did not contain a style pack document.");
            document.Source = sourcePath;
            if (document.SchemaVersion != 1) throw new InvalidDataException($"Unsupported style-pack schema {document.SchemaVersion}.");
            if (string.IsNullOrWhiteSpace(document.Id)) throw new InvalidDataException("Style pack ID is empty.");
            if (sources.TryGetValue(document.Id, out StylePackSourceDocument? replaced))
            {
                diagnostics.Add(new StylePackDiagnostic(document.Id, StyleDiagnosticSeverity.Information, $"Definition overrides {replaced.Source}.", sourcePath));
            }
            sources[document.Id] = document;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            diagnostics.Add(new StylePackDiagnostic("unknown", StyleDiagnosticSeverity.Error, exception.Message, sourcePath));
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static IReadOnlyList<T> WeightedMerge<T>(IReadOnlyList<T> primary, IReadOnlyList<T> secondary, double primaryWeight, double secondaryWeight)
    {
        List<T> result = new();
        int primaryCopies = primaryWeight <= 0 || primary.Count == 0
            ? 0
            : Math.Max(1, (int)Math.Round(primaryWeight * 4));
        int secondaryCopies = secondaryWeight <= 0 || secondary.Count == 0
            ? 0
            : Math.Max(1, (int)Math.Round(secondaryWeight * 4));
        for (int index = 0; index < primaryCopies; index++) result.AddRange(primary);
        for (int index = 0; index < secondaryCopies; index++) result.AddRange(secondary);
        if (result.Count == 0)
        {
            result.AddRange(primary.Count > 0 ? primary : secondary);
        }
        return result;
    }

    private static IReadOnlyDictionary<FormSectionKind, IReadOnlyList<int[]>> MergeProgressions(
        IReadOnlyDictionary<FormSectionKind, IReadOnlyList<int[]>> primary,
        IReadOnlyDictionary<FormSectionKind, IReadOnlyList<int[]>> secondary,
        double primaryWeight,
        double secondaryWeight)
    {
        Dictionary<FormSectionKind, IReadOnlyList<int[]>> result = new();
        foreach (FormSectionKind kind in Enum.GetValues<FormSectionKind>())
        {
            IReadOnlyList<int[]> a = primary.GetValueOrDefault(kind, Array.Empty<int[]>());
            IReadOnlyList<int[]> b = secondary.GetValueOrDefault(kind, Array.Empty<int[]>());
            result[kind] = WeightedMerge(a, b, primaryWeight, secondaryWeight).Select(item => item.ToArray()).ToArray();
        }
        return result;
    }

    private static IReadOnlyDictionary<FormSectionKind, IReadOnlyList<int[]>> CloneProgressions(
        IReadOnlyDictionary<FormSectionKind, IReadOnlyList<int[]>> source) =>
        source.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int[]>)pair.Value.Select(item => item.ToArray()).ToArray());

    private static IReadOnlyList<CompositionRoleSettings> BlendRoles(
        IReadOnlyList<CompositionRoleSettings> primary,
        IReadOnlyList<CompositionRoleSettings> secondary,
        double secondaryWeight)
    {
        Dictionary<string, CompositionRoleSettings> secondaryById = secondary.ToDictionary(role => role.Id, StringComparer.OrdinalIgnoreCase);
        List<CompositionRoleSettings> result = new();
        foreach (CompositionRoleSettings role in primary)
        {
            if (!secondaryById.TryGetValue(role.Id, out CompositionRoleSettings? other))
            {
                result.Add(CloneRole(role));
                continue;
            }
            bool preferSecondary = secondaryWeight >= 0.5;
            result.Add(new CompositionRoleSettings
            {
                Id = role.Id,
                Name = preferSecondary ? other.Name : role.Name,
                Role = role.Role,
                Enabled = role.Enabled || other.Enabled,
                Locked = false,
                MidiChannel = preferSecondary ? other.MidiChannel : role.MidiChannel,
                GeneralMidiProgram = preferSecondary ? other.GeneralMidiProgram : role.GeneralMidiProgram,
                IsPercussion = role.IsPercussion || other.IsPercussion,
                RhythmPatternId = preferSecondary ? other.RhythmPatternId : role.RhythmPatternId,
                GeneratorId = preferSecondary ? other.GeneratorId : role.GeneratorId,
                MinimumPitch = WeightedInt(role.MinimumPitch, other.MinimumPitch, secondaryWeight),
                MaximumPitch = WeightedInt(role.MaximumPitch, other.MaximumPitch, secondaryWeight),
                Density = WeightedInt(role.Density, other.Density, secondaryWeight),
                InstrumentSelector = BlendSelector(role.InstrumentSelector, other.InstrumentSelector, secondaryWeight)
            });
            secondaryById.Remove(role.Id);
        }
        result.AddRange(secondaryById.Values.Select(CloneRole));
        return result;
    }

    private static IReadOnlyList<CompositionRoleSettings> CloneRoles(IReadOnlyList<CompositionRoleSettings> roles) => roles.Select(CloneRole).ToArray();

    private static CompositionRoleSettings CloneRole(CompositionRoleSettings role) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Role = role.Role,
        Enabled = role.Enabled,
        Locked = role.Locked,
        Revision = role.Revision,
        MidiChannel = role.MidiChannel,
        GeneralMidiProgram = role.GeneralMidiProgram,
        IsPercussion = role.IsPercussion,
        RhythmPatternId = role.RhythmPatternId,
        GeneratorId = role.GeneratorId,
        MinimumPitch = role.MinimumPitch,
        MaximumPitch = role.MaximumPitch,
        Density = role.Density,
        InstrumentSelector = role.InstrumentSelector?.Clone() ?? new InstrumentSelectorSettings()
    };

    private static InstrumentSelectorSettings BlendSelector(InstrumentSelectorSettings? a, InstrumentSelectorSettings? b, double weight)
    {
        InstrumentSelectorSettings left = a?.Clone() ?? new InstrumentSelectorSettings();
        InstrumentSelectorSettings right = b?.Clone() ?? new InstrumentSelectorSettings();
        bool preferRight = weight >= 0.5;
        return new InstrumentSelectorSettings
        {
            Families = left.Families.Union(right.Families).Distinct().ToList(),
            RequiredTags = (preferRight ? right.RequiredTags : left.RequiredTags).ToList(),
            PreferredTags = left.PreferredTags.Union(right.PreferredTags, StringComparer.OrdinalIgnoreCase).ToList(),
            AvoidTags = left.AvoidTags.Union(right.AvoidTags, StringComparer.OrdinalIgnoreCase).ToList(),
            MinimumPitch = WeightedInt(left.MinimumPitch, right.MinimumPitch, weight),
            MaximumPitch = WeightedInt(left.MaximumPitch, right.MaximumPitch, weight),
            MinimumAttack = Weighted(left.MinimumAttack, right.MinimumAttack, weight),
            MaximumAttack = Weighted(left.MaximumAttack, right.MaximumAttack, weight),
            MinimumSustain = Weighted(left.MinimumSustain, right.MinimumSustain, weight),
            MaximumSustain = Weighted(left.MaximumSustain, right.MaximumSustain, weight),
            MinimumBrightness = Weighted(left.MinimumBrightness, right.MinimumBrightness, weight),
            MaximumBrightness = Weighted(left.MaximumBrightness, right.MaximumBrightness, weight),
            MinimumPercussiveness = Weighted(left.MinimumPercussiveness, right.MinimumPercussiveness, weight),
            MaximumPercussiveness = Weighted(left.MaximumPercussiveness, right.MaximumPercussiveness, weight),
            StyleAffinityWeight = Weighted(left.StyleAffinityWeight, right.StyleAffinityWeight, weight),
            NoveltyWeight = Weighted(left.NoveltyWeight, right.NoveltyWeight, weight),
            RandomWeight = Weighted(left.RandomWeight, right.RandomWeight, weight),
            MaximumLayers = WeightedInt(left.MaximumLayers, right.MaximumLayers, weight),
            AllowedStrategies = left.AllowedStrategies.Union(right.AllowedStrategies).Distinct().ToList()
        };
    }

    private static StyleConstraintProfile BlendConstraints(StyleConstraintProfile a, StyleConstraintProfile b, double weight) => new(
        WeightedInt(a.MinimumNotesPerPhrase, b.MinimumNotesPerPhrase, weight),
        WeightedInt(a.MaximumNotesPerPhrase, b.MaximumNotesPerPhrase, weight),
        WeightedInt(a.MaximumMelodicLeap, b.MaximumMelodicLeap, weight),
        WeightedInt(a.MaximumRepeatedPitch, b.MaximumRepeatedPitch, weight),
        Weighted(a.MinimumChordToneRatio, b.MinimumChordToneRatio, weight),
        weight >= 0.5 ? b.RequireCadentialFinalTone : a.RequireCadentialFinalTone,
        WeightedInt(a.PreferredMinimumRange, b.PreferredMinimumRange, weight),
        WeightedInt(a.PreferredMaximumRange, b.PreferredMaximumRange, weight),
        Weighted(a.MaximumDensityRatio, b.MaximumDensityRatio, weight));

    private static StyleScorerWeights BlendScoring(StyleScorerWeights a, StyleScorerWeights b, double weight) => new(
        Weighted(a.ChordTone, b.ChordTone, weight),
        Weighted(a.StepwiseMotion, b.StepwiseMotion, weight),
        Weighted(a.MotifFidelity, b.MotifFidelity, weight),
        Weighted(a.PhraseClosure, b.PhraseClosure, weight),
        Weighted(a.RhythmBalance, b.RhythmBalance, weight),
        Weighted(a.Range, b.Range, weight),
        Weighted(a.LeapPenalty, b.LeapPenalty, weight),
        Weighted(a.Syncopation, b.Syncopation, weight),
        Weighted(a.RepetitionControl, b.RepetitionControl, weight));

    private static int WeightedInt(int a, int b, double weight) => (int)Math.Round(Weighted(a, b, weight));
    private static double Weighted(double a, double b, double weight) => (a * (1.0 - weight)) + (b * weight);
}

public sealed record StylePackDefinition(
    string Id,
    string Name,
    string Description,
    string Version,
    StyleFamily Family,
    bool IsAbstract,
    string FormGrammarId,
    string HarmonicGrammarId,
    IReadOnlyList<string[]> FormProductions,
    IReadOnlyDictionary<string, FormTokenDefinition> Tokens,
    IReadOnlyDictionary<FormSectionKind, IReadOnlyList<int[]>> Progressions,
    IReadOnlyList<ScaleKind> Scales,
    int MinimumTempo,
    int MaximumTempo,
    int BeatsPerBar,
    int BeatUnit,
    int CandidateCount,
    IReadOnlyList<CompositionRoleSettings> Roles,
    IReadOnlyDictionary<string, bool[]> RhythmVocabulary,
    StyleConstraintProfile Constraints,
    StyleScorerWeights Scoring,
    IReadOnlyList<string> ResolutionChain,
    string Source);

internal sealed class StylePackSourceDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public StyleFamily? Family { get; set; }
    public bool? Abstract { get; set; }
    public string Extends { get; set; } = string.Empty;
    public List<string> Mixins { get; set; } = new();
    public TempoDocument? Tempo { get; set; }
    public MeterDocument? Meter { get; set; }
    public int? CandidateCount { get; set; }
    public List<ScaleKind> Scales { get; set; } = new();
    public FormLibraryDocument? Forms { get; set; }
    public HarmonyDocument? Harmony { get; set; }
    public List<RhythmDocument> Rhythms { get; set; } = new();
    public List<RoleDocument> Roles { get; set; } = new();
    public ConstraintDocument? Constraints { get; set; }
    public ScoringDocument? Scoring { get; set; }
    [JsonIgnore] public string Source { get; set; } = string.Empty;
}

internal sealed class TempoDocument { public int? Minimum { get; set; } public int? Maximum { get; set; } }
internal sealed class MeterDocument { public int? BeatsPerBar { get; set; } public int? BeatUnit { get; set; } }
internal sealed class FormLibraryDocument
{
    public string GrammarId { get; set; } = string.Empty;
    public List<List<string>> Productions { get; set; } = new();
    public List<FormTokenDocument> Tokens { get; set; } = new();
}
internal sealed class FormTokenDocument
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FormSectionKind Kind { get; set; }
    public int LengthBars { get; set; } = 4;
    public double Energy { get; set; } = 0.5;
    public MotifTransformationKind Treatment { get; set; }
}
internal sealed class HarmonyDocument
{
    public string GrammarId { get; set; } = string.Empty;
    public Dictionary<FormSectionKind, List<List<int>>> Progressions { get; set; } = new();
}
internal sealed class RhythmDocument { public string Id { get; set; } = string.Empty; public List<int> Steps { get; set; } = new(); }
internal sealed class RoleDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TrackRole Role { get; set; }
    public bool? Enabled { get; set; }
    public int? MidiChannel { get; set; }
    public int? GeneralMidiProgram { get; set; }
    public bool? IsPercussion { get; set; }
    public string RhythmPatternId { get; set; } = string.Empty;
    public string GeneratorId { get; set; } = string.Empty;
    public int? MinimumPitch { get; set; }
    public int? MaximumPitch { get; set; }
    public int? Density { get; set; }
    public InstrumentSelectorDocument? InstrumentSelector { get; set; }
}
internal sealed class InstrumentSelectorDocument
{
    public List<InstrumentFamily> Families { get; set; } = new();
    public List<string> RequiredTags { get; set; } = new();
    public List<string> PreferredTags { get; set; } = new();
    public List<string> AvoidTags { get; set; } = new();
    public int? MinimumPitch { get; set; }
    public int? MaximumPitch { get; set; }
    public double? MinimumAttack { get; set; }
    public double? MaximumAttack { get; set; }
    public double? MinimumSustain { get; set; }
    public double? MaximumSustain { get; set; }
    public double? MinimumBrightness { get; set; }
    public double? MaximumBrightness { get; set; }
    public double? MinimumPercussiveness { get; set; }
    public double? MaximumPercussiveness { get; set; }
    public double? StyleAffinityWeight { get; set; }
    public double? NoveltyWeight { get; set; }
    public double? RandomWeight { get; set; }
    public int? MaximumLayers { get; set; }
    public List<RoleDistributionStrategy> AllowedStrategies { get; set; } = new();
}
internal sealed class ConstraintDocument
{
    public int? MinimumNotesPerPhrase { get; set; }
    public int? MaximumNotesPerPhrase { get; set; }
    public int? MaximumMelodicLeap { get; set; }
    public int? MaximumRepeatedPitch { get; set; }
    public double? MinimumChordToneRatio { get; set; }
    public bool? RequireCadentialFinalTone { get; set; }
    public int? PreferredMinimumRange { get; set; }
    public int? PreferredMaximumRange { get; set; }
    public double? MaximumDensityRatio { get; set; }
}
internal sealed class ScoringDocument
{
    public double? ChordTone { get; set; }
    public double? StepwiseMotion { get; set; }
    public double? MotifFidelity { get; set; }
    public double? PhraseClosure { get; set; }
    public double? RhythmBalance { get; set; }
    public double? Range { get; set; }
    public double? LeapPenalty { get; set; }
    public double? Syncopation { get; set; }
    public double? RepetitionControl { get; set; }
}

internal sealed class StylePackAccumulator
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _version = "1.0";
    private StyleFamily _family = StyleFamily.Foundation;
    private bool _isAbstract = true;
    private string _formGrammarId = "style-pack-form-v1";
    private string _harmonicGrammarId = "style-pack-harmony-v1";
    private readonly List<string[]> _forms = new();
    private readonly Dictionary<string, FormTokenDefinition> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<FormSectionKind, List<int[]>> _progressions = new();
    private readonly List<ScaleKind> _scales = new();
    private int _minimumTempo = 80;
    private int _maximumTempo = 120;
    private int _beatsPerBar = 4;
    private int _beatUnit = 4;
    private int _candidateCount = 10;
    private readonly Dictionary<string, CompositionRoleSettings> _roles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool[]> _rhythms = new(StringComparer.OrdinalIgnoreCase);
    private StyleConstraintProfile _constraints = StyleConstraintProfile.Default;
    private StyleScorerWeights _scoring = StyleScorerWeights.Default;
    private readonly List<string> _resolutionChain = new();

    public void Merge(StylePackDefinition pack)
    {
        _id = pack.Id;
        _name = pack.Name;
        _description = pack.Description;
        _version = pack.Version;
        _family = pack.Family;
        _isAbstract = pack.IsAbstract;
        _formGrammarId = pack.FormGrammarId;
        _harmonicGrammarId = pack.HarmonicGrammarId;
        _forms.AddRange(pack.FormProductions.Select(item => item.ToArray()));
        foreach ((string key, FormTokenDefinition value) in pack.Tokens) _tokens[key] = value;
        foreach ((FormSectionKind key, IReadOnlyList<int[]> value) in pack.Progressions)
        {
            if (!_progressions.TryGetValue(key, out List<int[]>? list)) _progressions[key] = list = new();
            list.AddRange(value.Select(item => item.ToArray()));
        }
        _scales.AddRange(pack.Scales);
        _minimumTempo = pack.MinimumTempo;
        _maximumTempo = pack.MaximumTempo;
        _beatsPerBar = pack.BeatsPerBar;
        _beatUnit = pack.BeatUnit;
        _candidateCount = pack.CandidateCount;
        foreach (CompositionRoleSettings role in pack.Roles) _roles[role.Id] = CloneRole(role);
        foreach ((string key, bool[] value) in pack.RhythmVocabulary) _rhythms[key] = value.ToArray();
        _constraints = pack.Constraints;
        _scoring = pack.Scoring;
        foreach (string item in pack.ResolutionChain) if (!_resolutionChain.Contains(item, StringComparer.OrdinalIgnoreCase)) _resolutionChain.Add(item);
    }

    public void Merge(StylePackSourceDocument source)
    {
        _id = source.Id;
        if (!string.IsNullOrWhiteSpace(source.Name)) _name = source.Name;
        if (!string.IsNullOrWhiteSpace(source.Description)) _description = source.Description;
        if (!string.IsNullOrWhiteSpace(source.Version)) _version = source.Version;
        if (source.Family.HasValue) _family = source.Family.Value;
        if (source.Abstract.HasValue) _isAbstract = source.Abstract.Value;
        if (source.Tempo?.Minimum is int min) _minimumTempo = min;
        if (source.Tempo?.Maximum is int max) _maximumTempo = max;
        if (source.Meter?.BeatsPerBar is int beats) _beatsPerBar = beats;
        if (source.Meter?.BeatUnit is int unit) _beatUnit = unit;
        if (source.CandidateCount is int candidates) _candidateCount = candidates;
        if (source.Scales.Count > 0)
        {
            _scales.Clear();
            _scales.AddRange(source.Scales);
        }
        if (source.Forms is not null)
        {
            if (!string.IsNullOrWhiteSpace(source.Forms.GrammarId)) _formGrammarId = source.Forms.GrammarId;
            if (source.Forms.Productions.Count > 0)
            {
                _forms.Clear();
                _forms.AddRange(source.Forms.Productions.Select(item => item.ToArray()));
            }
            foreach (FormTokenDocument token in source.Forms.Tokens)
            {
                _tokens[token.Symbol] = new FormTokenDefinition(token.Symbol, token.Name, token.Kind, token.LengthBars, token.Energy, token.Treatment);
            }
        }
        if (source.Harmony is not null)
        {
            if (!string.IsNullOrWhiteSpace(source.Harmony.GrammarId)) _harmonicGrammarId = source.Harmony.GrammarId;
            foreach ((FormSectionKind key, List<List<int>> value) in source.Harmony.Progressions)
            {
                _progressions[key] = value.Select(item => item.ToArray()).ToList();
            }
        }
        foreach (RhythmDocument rhythm in source.Rhythms)
        {
            _rhythms[rhythm.Id] = rhythm.Steps.Select(value => value != 0).ToArray();
        }
        foreach (RoleDocument role in source.Roles)
        {
            CompositionRoleSettings merged = _roles.TryGetValue(role.Id, out CompositionRoleSettings? existing)
                ? CloneRole(existing)
                : new CompositionRoleSettings { Id = role.Id, Name = role.Name, Role = role.Role };
            if (!string.IsNullOrWhiteSpace(role.Name)) merged.Name = role.Name;
            merged.Role = role.Role;
            if (role.Enabled.HasValue) merged.Enabled = role.Enabled.Value;
            if (role.MidiChannel.HasValue) merged.MidiChannel = role.MidiChannel.Value;
            if (role.GeneralMidiProgram.HasValue) merged.GeneralMidiProgram = role.GeneralMidiProgram.Value;
            if (role.IsPercussion.HasValue) merged.IsPercussion = role.IsPercussion.Value;
            if (!string.IsNullOrWhiteSpace(role.RhythmPatternId)) merged.RhythmPatternId = role.RhythmPatternId;
            if (!string.IsNullOrWhiteSpace(role.GeneratorId)) merged.GeneratorId = role.GeneratorId;
            if (role.MinimumPitch.HasValue) merged.MinimumPitch = role.MinimumPitch.Value;
            if (role.MaximumPitch.HasValue) merged.MaximumPitch = role.MaximumPitch.Value;
            if (role.Density.HasValue) merged.Density = role.Density.Value;
            if (role.InstrumentSelector is not null) merged.InstrumentSelector = OverlaySelector(merged.InstrumentSelector, role.InstrumentSelector);
            _roles[role.Id] = merged;
        }
        if (source.Constraints is not null) _constraints = Overlay(_constraints, source.Constraints);
        if (source.Scoring is not null) _scoring = Overlay(_scoring, source.Scoring);
        if (!_resolutionChain.Contains(source.Id, StringComparer.OrdinalIgnoreCase)) _resolutionChain.Add(source.Id);
    }

    public StylePackDefinition Build(string source) => new(
        _id,
        string.IsNullOrWhiteSpace(_name) ? _id : _name,
        _description,
        _version,
        _family,
        _isAbstract,
        _formGrammarId,
        _harmonicGrammarId,
        _forms.Select(item => item.ToArray()).ToArray(),
        _tokens.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        _progressions.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int[]>)pair.Value.Select(item => item.ToArray()).ToArray()),
        _scales.Distinct().ToArray(),
        _minimumTempo,
        _maximumTempo,
        _beatsPerBar,
        _beatUnit,
        _candidateCount,
        _roles.Values.Select(CloneRole).ToArray(),
        _rhythms.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
        _constraints,
        _scoring,
        _resolutionChain.ToArray(),
        source);

    private static InstrumentSelectorSettings OverlaySelector(InstrumentSelectorSettings? current, InstrumentSelectorDocument overlay)
    {
        InstrumentSelectorSettings result = current?.Clone() ?? new InstrumentSelectorSettings();
        if (overlay.Families.Count > 0) result.Families = overlay.Families.ToList();
        if (overlay.RequiredTags.Count > 0) result.RequiredTags = overlay.RequiredTags.ToList();
        if (overlay.PreferredTags.Count > 0) result.PreferredTags = overlay.PreferredTags.ToList();
        if (overlay.AvoidTags.Count > 0) result.AvoidTags = overlay.AvoidTags.ToList();
        result.MinimumPitch = overlay.MinimumPitch ?? result.MinimumPitch;
        result.MaximumPitch = overlay.MaximumPitch ?? result.MaximumPitch;
        result.MinimumAttack = overlay.MinimumAttack ?? result.MinimumAttack;
        result.MaximumAttack = overlay.MaximumAttack ?? result.MaximumAttack;
        result.MinimumSustain = overlay.MinimumSustain ?? result.MinimumSustain;
        result.MaximumSustain = overlay.MaximumSustain ?? result.MaximumSustain;
        result.MinimumBrightness = overlay.MinimumBrightness ?? result.MinimumBrightness;
        result.MaximumBrightness = overlay.MaximumBrightness ?? result.MaximumBrightness;
        result.MinimumPercussiveness = overlay.MinimumPercussiveness ?? result.MinimumPercussiveness;
        result.MaximumPercussiveness = overlay.MaximumPercussiveness ?? result.MaximumPercussiveness;
        result.StyleAffinityWeight = overlay.StyleAffinityWeight ?? result.StyleAffinityWeight;
        result.NoveltyWeight = overlay.NoveltyWeight ?? result.NoveltyWeight;
        result.RandomWeight = overlay.RandomWeight ?? result.RandomWeight;
        result.MaximumLayers = overlay.MaximumLayers ?? result.MaximumLayers;
        if (overlay.AllowedStrategies.Count > 0) result.AllowedStrategies = overlay.AllowedStrategies.ToList();
        return result;
    }

    private static StyleConstraintProfile Overlay(StyleConstraintProfile current, ConstraintDocument overlay) => new(
        overlay.MinimumNotesPerPhrase ?? current.MinimumNotesPerPhrase,
        overlay.MaximumNotesPerPhrase ?? current.MaximumNotesPerPhrase,
        overlay.MaximumMelodicLeap ?? current.MaximumMelodicLeap,
        overlay.MaximumRepeatedPitch ?? current.MaximumRepeatedPitch,
        overlay.MinimumChordToneRatio ?? current.MinimumChordToneRatio,
        overlay.RequireCadentialFinalTone ?? current.RequireCadentialFinalTone,
        overlay.PreferredMinimumRange ?? current.PreferredMinimumRange,
        overlay.PreferredMaximumRange ?? current.PreferredMaximumRange,
        overlay.MaximumDensityRatio ?? current.MaximumDensityRatio);

    private static StyleScorerWeights Overlay(StyleScorerWeights current, ScoringDocument overlay) => new(
        overlay.ChordTone ?? current.ChordTone,
        overlay.StepwiseMotion ?? current.StepwiseMotion,
        overlay.MotifFidelity ?? current.MotifFidelity,
        overlay.PhraseClosure ?? current.PhraseClosure,
        overlay.RhythmBalance ?? current.RhythmBalance,
        overlay.Range ?? current.Range,
        overlay.LeapPenalty ?? current.LeapPenalty,
        overlay.Syncopation ?? current.Syncopation,
        overlay.RepetitionControl ?? current.RepetitionControl);

    private static CompositionRoleSettings CloneRole(CompositionRoleSettings role) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Role = role.Role,
        Enabled = role.Enabled,
        Locked = role.Locked,
        Revision = role.Revision,
        MidiChannel = role.MidiChannel,
        GeneralMidiProgram = role.GeneralMidiProgram,
        IsPercussion = role.IsPercussion,
        RhythmPatternId = role.RhythmPatternId,
        GeneratorId = role.GeneratorId,
        MinimumPitch = role.MinimumPitch,
        MaximumPitch = role.MaximumPitch,
        Density = role.Density,
        InstrumentSelector = role.InstrumentSelector?.Clone() ?? new InstrumentSelectorSettings()
    };
}
