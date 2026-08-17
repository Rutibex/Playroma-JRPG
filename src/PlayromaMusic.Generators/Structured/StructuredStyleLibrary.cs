using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Generators.StylePacks;

namespace PlayromaMusic.Generators.Structured;

public sealed record FormTokenDefinition(
    string Symbol,
    string Name,
    FormSectionKind Kind,
    int LengthBars,
    double Energy,
    MotifTransformationKind Treatment);

public sealed record StructuredStyleDefinition(
    string Id,
    string Name,
    string Description,
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
    StyleFamily Family = StyleFamily.Legacy,
    StyleConstraintProfile? Constraints = null,
    StyleScorerWeights? Scoring = null,
    IReadOnlyDictionary<string, bool[]>? RhythmVocabulary = null,
    IReadOnlyList<string>? SourcePackIds = null,
    bool IsHybrid = false,
    string PackVersion = "legacy",
    IReadOnlyList<string>? SourceFiles = null)
{
    public StyleConstraintProfile EffectiveConstraints => Constraints ?? StyleConstraintProfile.Default;
    public StyleScorerWeights EffectiveScoring => Scoring ?? StyleScorerWeights.Default;
    public IReadOnlyDictionary<string, bool[]> EffectiveRhythmVocabulary => RhythmVocabulary ?? new Dictionary<string, bool[]>();
    public IReadOnlyList<string> EffectiveSourcePackIds => SourcePackIds ?? Array.Empty<string>();
    public IReadOnlyList<string> EffectiveSourceFiles => SourceFiles ?? Array.Empty<string>();
}

public static class StructuredStyleLibrary
{
    private static readonly object Sync = new();
    private static StylePackCatalog _catalog = StylePackCatalog.LoadDefault();
    private static IReadOnlyList<StructuredStyleDefinition> _styles = BuildAllStyles();

    public static IReadOnlyList<StructuredStyleDefinition> All
    {
        get
        {
            lock (Sync)
            {
                return _styles;
            }
        }
    }

    public static StylePackCatalog StylePacks
    {
        get
        {
            lock (Sync)
            {
                return _catalog;
            }
        }
    }

    public static void ReloadStylePacks(IEnumerable<string>? searchPaths = null)
    {
        lock (Sync)
        {
            _catalog = StylePackCatalog.LoadDefault(searchPaths);
            _styles = BuildAllStyles();
        }
    }

    public static StructuredStyleDefinition ComposeHybrid(StyleBlendSettings settings)
    {
        lock (Sync)
        {
            return _catalog.ComposeHybrid(settings);
        }
    }

    private static IReadOnlyList<StructuredStyleDefinition> BuildAllStyles()
    {
        List<StructuredStyleDefinition> styles =
        [
            BuildLuminousPop(),
            BuildModalJourney(),
            BuildNocturneWaltz(),
            BuildTonalLaboratory()
        ];
        styles.AddRange(_catalog.ConcretePacks.Select(_catalog.ToStructuredStyle));
        return styles;
    }

    public static StructuredStyleDefinition Get(string id) =>
        All.FirstOrDefault(style => string.Equals(style.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown structured style: {id}.");

    public static StructuredStyleDefinition GetNext(string currentId, int direction = 1)
    {
        IReadOnlyList<StructuredStyleDefinition> styles = All;
        int index = styles.ToList().FindIndex(style => string.Equals(style.Id, currentId, StringComparison.OrdinalIgnoreCase));
        index = index < 0 ? 0 : index;
        int next = ((index + direction) % styles.Count + styles.Count) % styles.Count;
        return styles[next];
    }

    public static List<CompositionRoleSettings> CloneRoles(StructuredStyleDefinition style) =>
        style.Roles.Select(role => new CompositionRoleSettings
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
        }).ToList();

    private static StructuredStyleDefinition BuildLuminousPop()
    {
        Dictionary<string, FormTokenDefinition> tokens = CommonTokens(4);
        tokens["I"] = tokens["I"] with { LengthBars = 2 };
        tokens["O"] = tokens["O"] with { LengthBars = 2 };
        return new StructuredStyleDefinition(
            "luminous-pop",
            "Luminous Pop",
            "Clear verse/chorus form, singable motifs, functional harmony and energetic arpeggios.",
            "pop-arc-v1",
            "functional-pop-v1",
            new[]
            {
                new[] { "I", "A", "B", "A'", "B'", "C", "B''", "O" },
                new[] { "I", "A", "A'", "B", "C", "B'", "O" },
                new[] { "I", "A", "B", "D", "B'", "C", "B''", "O" }
            },
            tokens,
            Progressions(
                verse: new[] { new[] { 0, 5, 3, 4 }, new[] { 0, 3, 5, 4 }, new[] { 5, 3, 0, 4 } },
                chorus: new[] { new[] { 0, 4, 5, 3 }, new[] { 0, 5, 3, 4 }, new[] { 3, 4, 0, 5 } },
                bridge: new[] { new[] { 5, 3, 0, 4 }, new[] { 3, 0, 4, 4 }, new[] { 1, 3, 4, 4 } }),
            new[] { ScaleKind.Major, ScaleKind.Mixolydian, ScaleKind.Dorian },
            96,
            128,
            4,
            4,
            10,
            DefaultRoles(leadProgram: 80, chordProgram: 89, bassProgram: 38, arpProgram: 4));
    }

    private static StructuredStyleDefinition BuildModalJourney()
    {
        Dictionary<string, FormTokenDefinition> tokens = CommonTokens(4);
        tokens["I"] = tokens["I"] with { LengthBars = 4, Energy = 0.25 };
        tokens["O"] = tokens["O"] with { LengthBars = 4, Energy = 0.20 };
        return new StructuredStyleDefinition(
            "modal-journey",
            "Modal Journey",
            "Longer sections, modal harmony, evolving motif transformations and spacious rhythmic roles.",
            "journey-arch-v1",
            "modal-cycle-v1",
            new[]
            {
                new[] { "I", "A", "A'", "D", "B", "C", "A''", "O" },
                new[] { "I", "A", "D", "A'", "C", "B", "D'", "O" },
                new[] { "I", "A", "B", "D", "C", "B'", "A'", "O" }
            },
            tokens,
            Progressions(
                verse: new[] { new[] { 0, 6, 3, 0 }, new[] { 0, 3, 6, 3 }, new[] { 0, 1, 6, 0 } },
                chorus: new[] { new[] { 3, 0, 6, 0 }, new[] { 0, 6, 3, 6 }, new[] { 0, 4, 3, 0 } },
                bridge: new[] { new[] { 1, 6, 0, 3 }, new[] { 5, 3, 6, 0 }, new[] { 3, 1, 6, 0 } }),
            new[] { ScaleKind.Dorian, ScaleKind.Mixolydian, ScaleKind.Lydian, ScaleKind.NaturalMinor },
            72,
            104,
            4,
            4,
            12,
            DefaultRoles(leadProgram: 73, chordProgram: 88, bassProgram: 35, arpProgram: 46, densityOffset: -15));
    }

    private static StructuredStyleDefinition BuildNocturneWaltz()
    {
        Dictionary<string, FormTokenDefinition> tokens = CommonTokens(4);
        return new StructuredStyleDefinition(
            "nocturne-waltz",
            "Nocturne Waltz",
            "Three-beat phrases, minor-key cadences, lyrical melody and broken-chord accompaniment.",
            "rounded-binary-v1",
            "minor-cadential-v1",
            new[]
            {
                new[] { "I", "A", "A'", "B", "C", "A''", "O" },
                new[] { "I", "A", "B", "A'", "C", "B'", "O" },
                new[] { "I", "A", "D", "B", "A'", "O" }
            },
            tokens,
            Progressions(
                verse: new[] { new[] { 0, 3, 4, 0 }, new[] { 0, 5, 1, 4 }, new[] { 0, 6, 3, 4 } },
                chorus: new[] { new[] { 3, 6, 0, 4 }, new[] { 5, 3, 1, 4 }, new[] { 0, 3, 6, 4 } },
                bridge: new[] { new[] { 5, 1, 4, 0 }, new[] { 3, 6, 1, 4 }, new[] { 1, 5, 4, 4 } }),
            new[] { ScaleKind.NaturalMinor, ScaleKind.Dorian },
            68,
            96,
            3,
            4,
            10,
            DefaultRoles(leadProgram: 0, chordProgram: 0, bassProgram: 43, arpProgram: 0, densityOffset: -5));
    }

    private static StructuredStyleDefinition BuildTonalLaboratory()
    {
        Dictionary<string, FormTokenDefinition> tokens = CommonTokens(4);
        tokens["D"] = tokens["D"] with { Kind = FormSectionKind.Solo, Treatment = MotifTransformationKind.Sequence };
        return new StructuredStyleDefinition(
            "tonal-laboratory",
            "Tonal Laboratory",
            "A denser demonstration style designed to expose grammar choices, motif mutations and candidate scoring.",
            "development-rondo-v1",
            "extended-functional-v1",
            new[]
            {
                new[] { "I", "A", "B", "A'", "C", "A''", "D", "B'", "O" },
                new[] { "I", "A", "C", "A'", "B", "D", "A''", "O" },
                new[] { "I", "A", "B", "C", "A'", "D", "B'", "A''", "O" }
            },
            tokens,
            Progressions(
                verse: new[] { new[] { 0, 1, 4, 0 }, new[] { 0, 5, 1, 4 }, new[] { 0, 3, 1, 4 } },
                chorus: new[] { new[] { 0, 4, 5, 3 }, new[] { 3, 4, 0, 5 }, new[] { 0, 2, 5, 4 } },
                bridge: new[] { new[] { 5, 1, 4, 0 }, new[] { 2, 5, 1, 4 }, new[] { 3, 1, 4, 4 } }),
            new[] { ScaleKind.Major, ScaleKind.NaturalMinor, ScaleKind.Dorian, ScaleKind.Lydian },
            88,
            132,
            4,
            4,
            16,
            DefaultRoles(leadProgram: 81, chordProgram: 90, bassProgram: 39, arpProgram: 11, densityOffset: 10));
    }

    private static Dictionary<string, FormTokenDefinition> CommonTokens(int bars) =>
        new(StringComparer.Ordinal)
        {
            ["I"] = new("I", "Intro", FormSectionKind.Intro, bars, 0.30, MotifTransformationKind.Fragment),
            ["A"] = new("A", "Verse", FormSectionKind.Verse, bars, 0.55, MotifTransformationKind.Original),
            ["A'"] = new("A'", "Verse Variation", FormSectionKind.Verse, bars, 0.62, MotifTransformationKind.Rotate),
            ["A''"] = new("A''", "Verse Development", FormSectionKind.Verse, bars, 0.70, MotifTransformationKind.Sequence),
            ["B"] = new("B", "Chorus", FormSectionKind.Chorus, bars, 0.82, MotifTransformationKind.Transpose),
            ["B'"] = new("B'", "Chorus Variation", FormSectionKind.Chorus, bars, 0.90, MotifTransformationKind.Augment),
            ["B''"] = new("B''", "Final Chorus", FormSectionKind.Chorus, bars, 1.00, MotifTransformationKind.Sequence),
            ["C"] = new("C", "Bridge", FormSectionKind.Bridge, bars, 0.73, MotifTransformationKind.Invert),
            ["D"] = new("D", "Breakdown", FormSectionKind.Breakdown, bars, 0.42, MotifTransformationKind.Fragment),
            ["D'"] = new("D'", "Solo", FormSectionKind.Solo, bars, 0.78, MotifTransformationKind.Retrograde),
            ["O"] = new("O", "Outro", FormSectionKind.Outro, bars, 0.24, MotifTransformationKind.Diminish)
        };

    private static IReadOnlyDictionary<FormSectionKind, IReadOnlyList<int[]>> Progressions(
        IReadOnlyList<int[]> verse,
        IReadOnlyList<int[]> chorus,
        IReadOnlyList<int[]> bridge) =>
        new Dictionary<FormSectionKind, IReadOnlyList<int[]>>
        {
            [FormSectionKind.Intro] = verse,
            [FormSectionKind.Verse] = verse,
            [FormSectionKind.Chorus] = chorus,
            [FormSectionKind.Bridge] = bridge,
            [FormSectionKind.Breakdown] = bridge,
            [FormSectionKind.Solo] = chorus,
            [FormSectionKind.Outro] = verse
        };

    private static IReadOnlyList<CompositionRoleSettings> DefaultRoles(
        int leadProgram,
        int chordProgram,
        int bassProgram,
        int arpProgram,
        int densityOffset = 0) =>
        new[]
        {
            Role("melody", "Melody", TrackRole.Lead, 0, leadProgram, false, "syncopated", 60, 88, 70 + densityOffset),
            Role("bass", "Bass", TrackRole.Bass, 2, bassProgram, false, "quarters", 28, 55, 78 + densityOffset),
            Role("chords", "Chords", TrackRole.Chords, 1, chordProgram, false, "quarters", 45, 78, 92 + densityOffset),
            Role("arpeggio", "Arpeggio", TrackRole.Arpeggio, 3, arpProgram, false, "eighths", 48, 88, 65 + densityOffset),
            Role("drums", "Drums", TrackRole.Drums, 9, 0, true, "four-on-floor", 35, 81, 82 + densityOffset)
        };

    private static CompositionRoleSettings Role(
        string id,
        string name,
        TrackRole role,
        int channel,
        int program,
        bool percussion,
        string rhythm,
        int minimumPitch,
        int maximumPitch,
        int density) =>
        new()
        {
            Id = id,
            Name = name,
            Role = role,
            MidiChannel = channel,
            GeneralMidiProgram = program,
            IsPercussion = percussion,
            RhythmPatternId = rhythm,
            MinimumPitch = minimumPitch,
            MaximumPitch = maximumPitch,
            Density = Math.Clamp(density, 20, 100)
        };
}
