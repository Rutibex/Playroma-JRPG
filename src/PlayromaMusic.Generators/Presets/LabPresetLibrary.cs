using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Generators.Graphs;

namespace PlayromaMusic.Generators.Presets;

public sealed record LabPresetDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<AlgorithmVoiceDefinition> Voices);

public static class LabPresetLibrary
{
    private static readonly IReadOnlyList<LabPresetDefinition> Presets = BuildPresets();

    public static IReadOnlyList<LabPresetDefinition> All => Presets;

    public static LabPresetDefinition Get(string id) =>
        Presets.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown laboratory preset: {id}");

    public static LabPresetDefinition GetNext(string currentId, int direction = 1)
    {
        int current = -1;
        for (int index = 0; index < Presets.Count; index++)
        {
            if (string.Equals(Presets[index].Id, currentId, StringComparison.OrdinalIgnoreCase))
            {
                current = index;
                break;
            }
        }

        int next = Mod(current + direction, Presets.Count);
        return Presets[next];
    }

    public static GenerationLabDocument CreateLab(string presetId)
    {
        LabPresetDefinition preset = Get(presetId);
        return new GenerationLabDocument
        {
            Enabled = true,
            ActivePresetId = preset.Id,
            PreviewSampleCount = 256,
            Voices = preset.Voices.Select(CloneVoice).ToList()
        };
    }

    private static IReadOnlyList<LabPresetDefinition> BuildPresets() =>
        new List<LabPresetDefinition>
        {
            new(
                "fractmus-orchestra",
                "FractMus Orchestra",
                "A five-voice survey of chaos, number theory, pink noise and cellular rhythm.",
                new[]
                {
                    Voice("lorenz-lead", "Lorenz Lead", AlgorithmKind.Lorenz, "lorenz-classic", TrackRole.Lead, 0, 81, 4, -5, 12, 2, 0.30, 1, 2, 50, 116, true, 1, 0, 100, 2),
                    Voice("henon-harmony", "Henon Harmony", AlgorithmKind.Henon, "henon-classic", TrackRole.Harmony, 1, 89, 3, -3, 10, 1, 0.40, 2, 4, 42, 94, true, 74, 22, 92, 4),
                    Voice("collatz-bass", "Collatz Bass", AlgorithmKind.Collatz, "collatz-27", TrackRole.Bass, 2, 38, 2, -1, 7, 2, 0.48, 1, 2, 58, 110, false, 1, 0, 100, 2),
                    Voice("pink-texture", "Pink Texture", AlgorithmKind.PinkNoise, "pink-balanced", TrackRole.Texture, 3, 92, 4, -8, 16, 1, 0.62, 4, 8, 30, 80, true, 11, 10, 86, 4),
                    DrumVoice("cellular-drums", "Cellular Drums", "ca-rule30", 0.32, 4, new[] { 36, 38, 42, 46, 45, 49 })
                }),
            new(
                "number-theory-clockwork",
                "Number Theory Clockwork",
                "Thue-Morse symmetry and Collatz excursions over a rule-90 clockwork pulse.",
                new[]
                {
                    Voice("thue-lead", "Thue Morse Lead", AlgorithmKind.ThueMorse, "thue-classic", TrackRole.Lead, 0, 12, 4, -7, 14, 4, 0.38, 1, 1, 60, 116, true, 1, 0, 110, 2),
                    Voice("collatz-counter", "Collatz Counter", AlgorithmKind.Collatz, "collatz-6171", TrackRole.CounterMelody, 1, 10, 4, -9, 11, 2, 0.50, 1, 2, 45, 100, true, 74, 18, 102, 2),
                    Voice("thue-bass", "Binary Bass", AlgorithmKind.ThueMorse, "thue-shifted", TrackRole.Bass, 2, 34, 2, -1, 7, 2, 0.40, 1, 2, 68, 112, false, 1, 0, 100, 4),
                    DrumVoice("rule90-drums", "Rule 90 Drums", "ca-rule90", 0.26, 4, new[] { 36, 38, 42, 46, 49 })
                }),
            new(
                "chaos-garden",
                "Chaos Garden",
                "Interlocking Lorenz, Henon, logistic and Hopalong voices with a restless walk bass.",
                new[]
                {
                    Voice("logistic-lead", "Logistic Lead", AlgorithmKind.Logistic, "logistic-full", TrackRole.Lead, 0, 80, 5, -8, 15, 4, 0.28, 1, 2, 54, 120, true, 1, 0, 110, 1),
                    Voice("lorenz-choir", "Lorenz Choir", AlgorithmKind.Lorenz, "lorenz-wide", TrackRole.Harmony, 1, 52, 3, -5, 12, 1, 0.52, 3, 6, 36, 92, true, 11, 14, 94, 3),
                    Voice("hopalong-counter", "Hopalong Counter", AlgorithmKind.Hopalong, "hopalong-spiral", TrackRole.CounterMelody, 2, 14, 4, -7, 12, 2, 0.46, 1, 3, 42, 106, true, 74, 12, 108, 2),
                    Voice("walk-bass", "Walking Chaos Bass", AlgorithmKind.RandomWalk, "walk-restless", TrackRole.Bass, 3, 39, 2, -2, 8, 2, 0.35, 1, 2, 62, 116, false, 1, 0, 100, 2),
                    DrumVoice("rule110-drums", "Rule 110 Drums", "ca-rule110", 0.34, 4, new[] { 36, 38, 42, 46, 45, 47, 49 })
                }),
            new(
                "pink-drift",
                "Pink Drift",
                "Slow stochastic contour, wide pads and sparse cellular punctuation.",
                new[]
                {
                    Voice("pink-lead", "Pink Flute", AlgorithmKind.PinkNoise, "pink-slow", TrackRole.Lead, 0, 73, 5, -7, 10, 2, 0.54, 2, 4, 42, 92, true, 1, 15, 92, 3),
                    Voice("walk-pad", "Random Walk Pad", AlgorithmKind.RandomWalk, "walk-gentle", TrackRole.Harmony, 1, 89, 3, -7, 14, 1, 0.44, 4, 8, 30, 76, true, 74, 25, 98, 4),
                    Voice("hopalong-bells", "Hopalong Bells", AlgorithmKind.Hopalong, "hopalong-classic", TrackRole.Texture, 2, 98, 5, -10, 14, 1, 0.70, 2, 6, 32, 86, true, 10, 20, 96, 4),
                    Voice("pink-bass", "Pink Bass", AlgorithmKind.PinkNoise, "pink-balanced", TrackRole.Bass, 3, 35, 2, -1, 6, 1, 0.50, 2, 4, 48, 92, false, 1, 0, 100, 4),
                    DrumVoice("sparse-drums", "Sparse Automata", "ca-rule150", 0.68, 2, new[] { 36, 42, 46, 49 })
                }),
            new(
                "automata-machine",
                "Automata Machine",
                "Multiple cellular rules drive melody, bass, percussion and controller motion.",
                new[]
                {
                    Voice("rule30-lead", "Rule 30 Lead", AlgorithmKind.CellularAutomata, "ca-rule30", TrackRole.Lead, 0, 82, 4, -6, 14, 4, 0.30, 1, 2, 58, 122, true, 1, 0, 127, 1),
                    Voice("rule90-harmony", "Rule 90 Harmony", AlgorithmKind.CellularAutomata, "ca-rule90", TrackRole.Harmony, 1, 88, 3, -4, 11, 2, 0.42, 2, 4, 42, 96, true, 74, 20, 108, 2),
                    Voice("rule110-bass", "Rule 110 Bass", AlgorithmKind.CellularAutomata, "ca-rule110", TrackRole.Bass, 2, 37, 2, -1, 7, 2, 0.36, 1, 2, 66, 116, false, 1, 0, 100, 2),
                    DrumVoice("rule150-drums", "Rule 150 Drums", "ca-rule150", 0.24, 4, new[] { 36, 38, 42, 46, 45, 47, 49, 51 })
                }),
            new(
                "lorenz-chamber",
                "Lorenz Chamber",
                "A transparent chamber texture in which Lorenz streams divide melody, counterpoint and bow-like controller curves.",
                new[]
                {
                    Voice("lorenz-violin", "Lorenz Violin", AlgorithmKind.Lorenz, "lorenz-classic", TrackRole.Lead, 0, 40, 5, -7, 12, 2, 0.44, 1, 3, 44, 108, true, 11, 18, 104, 2),
                    Voice("henon-clarinet", "Henon Clarinet", AlgorithmKind.Henon, "henon-open", TrackRole.CounterMelody, 1, 71, 4, -6, 10, 2, 0.52, 1, 3, 38, 96, true, 1, 8, 96, 3),
                    Voice("pink-cello", "Pink Cello", AlgorithmKind.PinkNoise, "pink-slow", TrackRole.Bass, 2, 42, 2, -1, 8, 1, 0.56, 2, 5, 42, 92, true, 11, 8, 84, 4),
                    DrumVoice("chamber-pulse", "Chamber Pulse", "ca-rule90", 0.72, 2, new[] { 41, 43, 45, 47, 50 })
                }),
            new(
                "logistic-club",
                "Logistic Club",
                "Full-chaos logistic motion becomes a club lead while cellular rules lock bass and drums to a four-step grid.",
                new[]
                {
                    Voice("logistic-club-lead", "Logistic Club Lead", AlgorithmKind.Logistic, "logistic-full", TrackRole.Lead, 0, 81, 5, -8, 15, 4, 0.31, 1, 2, 58, 124, true, 74, 16, 118, 1),
                    Voice("rule110-club-bass", "Rule 110 Club Bass", AlgorithmKind.CellularAutomata, "ca-rule110", TrackRole.Bass, 1, 38, 2, -1, 7, 4, 0.39, 1, 2, 68, 122, false, 1, 0, 100, 2),
                    Voice("pink-club-pad", "Pink Club Pad", AlgorithmKind.PinkNoise, "pink-balanced", TrackRole.Harmony, 2, 90, 3, -4, 11, 1, 0.60, 4, 8, 34, 86, true, 11, 16, 100, 4),
                    DrumVoice("club-rule30", "Club Rule 30", "ca-rule30", 0.28, 4, new[] { 36, 38, 42, 46, 49 })
                }),
            new(
                "collatz-jazz-combo",
                "Collatz Jazz Combo",
                "Collatz excursions outline a horn line and walking bass over Henon comping and a sparse automata kit.",
                new[]
                {
                    Voice("collatz-horn", "Collatz Horn", AlgorithmKind.Collatz, "collatz-6171", TrackRole.Lead, 0, 66, 4, -6, 12, 3, 0.42, 1, 3, 48, 112, true, 1, 0, 100, 2),
                    Voice("henon-comp", "Henon Comp", AlgorithmKind.Henon, "henon-classic", TrackRole.Chords, 1, 4, 3, -4, 10, 2, 0.56, 2, 4, 36, 88, true, 11, 10, 92, 4),
                    Voice("collatz-walk", "Collatz Walk", AlgorithmKind.Collatz, "collatz-late", TrackRole.Bass, 2, 32, 2, -1, 9, 2, 0.36, 1, 2, 62, 112, false, 1, 0, 100, 2),
                    DrumVoice("jazz-rule90", "Jazz Rule 90", "ca-rule90", 0.53, 4, new[] { 36, 38, 42, 44, 51 })
                }),
            new(
                "henon-mallets",
                "Henon Mallet Circle",
                "Multiple attractor projections create interlocking mallet hockets over a low random walk.",
                new[]
                {
                    Voice("henon-vibes", "Henon Vibes", AlgorithmKind.Henon, "henon-open", TrackRole.Lead, 0, 11, 5, -8, 12, 4, 0.36, 1, 2, 48, 112, true, 10, 8, 104, 2),
                    Voice("hopalong-marimba", "Hopalong Marimba", AlgorithmKind.Hopalong, "hopalong-spiral", TrackRole.CounterMelody, 1, 12, 4, -7, 11, 4, 0.43, 1, 2, 44, 104, true, 11, 8, 92, 3),
                    Voice("walk-low-mallet", "Walk Low Mallet", AlgorithmKind.RandomWalk, "walk-gentle", TrackRole.Bass, 2, 13, 2, -1, 7, 2, 0.47, 1, 3, 52, 100, false, 1, 0, 100, 2),
                    DrumVoice("mallet-clicks", "Mallet Clicks", "ca-rule150", 0.55, 4, new[] { 37, 39, 42, 44, 56 })
                }),
            new(
                "fractal-cinematic",
                "Fractal Cinematic",
                "Wide Lorenz and pink-noise streams shape a slow orchestral rise with sparse impact punctuation.",
                new[]
                {
                    Voice("wide-strings", "Wide Strings", AlgorithmKind.Lorenz, "lorenz-wide", TrackRole.Harmony, 0, 48, 3, -5, 13, 1, 0.51, 4, 8, 32, 96, true, 11, 10, 112, 3),
                    Voice("pink-horn", "Pink Horn", AlgorithmKind.PinkNoise, "pink-slow", TrackRole.Lead, 1, 60, 4, -6, 11, 2, 0.63, 2, 5, 38, 108, true, 1, 4, 100, 3),
                    Voice("hopalong-bass-drum", "Hopalong Low Brass", AlgorithmKind.Hopalong, "hopalong-classic", TrackRole.Bass, 2, 58, 2, -1, 7, 1, 0.66, 2, 6, 46, 106, false, 1, 0, 100, 4),
                    DrumVoice("cinematic-impacts", "Cinematic Impacts", "ca-rule110", 0.78, 2, new[] { 35, 36, 41, 45, 49, 57 })
                }),
            new(
                "binary-minimalism",
                "Binary Minimalism",
                "Thue-Morse cells phase against rule-90 harmony and a patient centered random walk.",
                new[]
                {
                    Voice("thue-piano", "Thue Piano", AlgorithmKind.ThueMorse, "thue-classic", TrackRole.Lead, 0, 0, 5, -9, 13, 4, 0.32, 1, 2, 52, 112, true, 11, 12, 90, 4),
                    Voice("rule90-arpeggio", "Rule 90 Arpeggio", AlgorithmKind.CellularAutomata, "ca-rule90", TrackRole.Arpeggio, 1, 8, 5, -10, 14, 4, 0.40, 1, 2, 42, 102, true, 10, 10, 96, 2),
                    Voice("gentle-walk-bass", "Gentle Walk Bass", AlgorithmKind.RandomWalk, "walk-gentle", TrackRole.Bass, 2, 32, 2, -1, 7, 2, 0.50, 1, 3, 48, 94, false, 1, 0, 100, 4),
                    DrumVoice("binary-pulse", "Binary Pulse", "ca-rule150", 0.60, 4, new[] { 36, 42, 44, 46 })
                }),
            new(
                "hopalong-world",
                "Hopalong World Ensemble",
                "Hopalong and Collatz streams map into plucked, bowed and wind-like world-instrument roles.",
                new[]
                {
                    Voice("hopalong-sitar", "Hopalong Sitar", AlgorithmKind.Hopalong, "hopalong-spiral", TrackRole.Lead, 0, 104, 4, -8, 13, 3, 0.35, 1, 3, 48, 116, true, 1, 0, 110, 2),
                    Voice("collatz-koto", "Collatz Koto", AlgorithmKind.Collatz, "collatz-27", TrackRole.CounterMelody, 1, 107, 5, -10, 12, 3, 0.47, 1, 2, 44, 104, true, 10, 8, 102, 3),
                    Voice("pink-fiddle", "Pink Fiddle", AlgorithmKind.PinkNoise, "pink-balanced", TrackRole.Harmony, 2, 110, 3, -4, 11, 1, 0.58, 3, 6, 34, 94, true, 11, 16, 98, 4),
                    DrumVoice("world-rule30", "World Rule 30", "ca-rule30", 0.46, 4, new[] { 36, 45, 47, 50, 54, 56 })
                }),
            new(
                "noise-nocturne",
                "Noise Nocturne",
                "Pink-noise correlation shapes a quiet nocturne while a restrained Lorenz stream supplies rare colour notes.",
                new[]
                {
                    Voice("pink-piano", "Pink Piano", AlgorithmKind.PinkNoise, "pink-slow", TrackRole.Lead, 0, 0, 5, -8, 12, 2, 0.51, 2, 5, 34, 92, true, 11, 10, 86, 4),
                    Voice("walk-strings", "Walk Strings", AlgorithmKind.RandomWalk, "walk-gentle", TrackRole.Harmony, 1, 48, 3, -5, 12, 1, 0.58, 4, 8, 28, 78, true, 1, 8, 84, 4),
                    Voice("lorenz-bells", "Lorenz Bells", AlgorithmKind.Lorenz, "lorenz-classic", TrackRole.Texture, 2, 98, 6, -12, 10, 1, 0.82, 2, 6, 24, 76, true, 10, 16, 96, 4),
                    Voice("pink-low", "Pink Low", AlgorithmKind.PinkNoise, "pink-balanced", TrackRole.Bass, 3, 35, 2, -1, 6, 1, 0.64, 3, 6, 38, 82, false, 1, 0, 100, 4)
                }),
            new(
                "chaotic-brass",
                "Chaotic Brass Engine",
                "Chaotic attractors drive short brass bursts, low pedals and automata percussion.",
                new[]
                {
                    Voice("lorenz-trumpet", "Lorenz Trumpet", AlgorithmKind.Lorenz, "lorenz-wide", TrackRole.Lead, 0, 56, 4, -7, 12, 4, 0.32, 1, 2, 58, 124, true, 1, 0, 112, 2),
                    Voice("henon-brass", "Henon Brass", AlgorithmKind.Henon, "henon-open", TrackRole.Chords, 1, 61, 3, -4, 10, 2, 0.49, 1, 3, 44, 108, true, 11, 8, 100, 3),
                    Voice("logistic-tuba", "Logistic Tuba", AlgorithmKind.Logistic, "logistic-edge", TrackRole.Bass, 2, 58, 2, -1, 7, 2, 0.43, 1, 3, 58, 114, false, 1, 0, 100, 2),
                    DrumVoice("brass-machine", "Brass Machine", "ca-rule110", 0.36, 4, new[] { 36, 38, 42, 49, 57 })
                }),
            new(
                "rule110-breaks",
                "Rule 110 Breaks",
                "Rule 110 drives a dense break kit while logistic and random-walk voices leave harmonic space around it.",
                new[]
                {
                    Voice("logistic-stab", "Logistic Stab", AlgorithmKind.Logistic, "logistic-window", TrackRole.Lead, 0, 80, 5, -10, 13, 4, 0.54, 1, 2, 48, 116, true, 74, 12, 112, 2),
                    Voice("walk-pad-breaks", "Walk Pad", AlgorithmKind.RandomWalk, "walk-restless", TrackRole.Harmony, 1, 91, 3, -6, 12, 1, 0.57, 4, 8, 28, 88, true, 11, 18, 104, 4),
                    Voice("collatz-break-bass", "Collatz Break Bass", AlgorithmKind.Collatz, "collatz-late", TrackRole.Bass, 2, 38, 2, -1, 8, 4, 0.38, 1, 2, 66, 122, false, 1, 0, 100, 2),
                    DrumVoice("rule110-break-kit", "Rule 110 Break Kit", "ca-rule110", 0.18, 4, new[] { 36, 38, 40, 42, 44, 46, 47, 49, 51 })
                })
        };

    private static AlgorithmVoiceDefinition Voice(
        string id,
        string name,
        AlgorithmKind algorithm,
        string parameterPresetId,
        TrackRole role,
        int channel,
        int program,
        int octave,
        int minimumDegree,
        int maximumDegree,
        int stepsPerBeat,
        double gate,
        int minimumDuration,
        int maximumDuration,
        int minimumVelocity,
        int maximumVelocity,
        bool ccEnabled,
        int ccNumber,
        int ccMinimum,
        int ccMaximum,
        int ccEvery)
    {
        AlgorithmParameterPreset parameterPreset = AlgorithmParameterPresetLibrary.Get(parameterPresetId);
        return new AlgorithmVoiceDefinition
        {
            Id = id,
            Name = name,
            Algorithm = algorithm,
            ParameterPresetId = parameterPresetId,
            AlgorithmParameters = AlgorithmParameterPresetLibrary.CloneParameters(parameterPreset),
            Role = role,
            MidiChannel = channel,
            GeneralMidiProgram = program,
            Graph = GraphPresetFactory.CreateDefault(role == TrackRole.Harmony ? 0.16 : 0.28, role == TrackRole.Bass ? 4 : 8),
            PitchMapping = new ScaleMapperSettings
            {
                UseProjectScale = true,
                ScaleOverride = ScaleKind.Chromatic,
                BaseOctave = octave,
                MinimumDegree = minimumDegree,
                MaximumDegree = maximumDegree,
                MinimumMidiPitch = role == TrackRole.Bass ? 28 : 36,
                MaximumMidiPitch = role == TrackRole.Bass ? 60 : 100
            },
            RhythmMapping = new RhythmMapperSettings
            {
                StepsPerBeat = stepsPerBeat,
                GateThreshold = gate,
                MinimumDurationSteps = minimumDuration,
                MaximumDurationSteps = maximumDuration,
                NoteLengthRatio = role == TrackRole.Harmony ? 0.96 : 0.78,
                Swing = 0.0
            },
            VelocityMapping = new VelocityMapperSettings
            {
                MinimumVelocity = minimumVelocity,
                MaximumVelocity = maximumVelocity,
                AccentEverySteps = Math.Max(1, stepsPerBeat * 2),
                AccentAmount = role == TrackRole.Bass ? 10 : 7
            },
            ControlChangeMapping = new ControlChangeMapperSettings
            {
                Enabled = ccEnabled,
                ControllerNumber = ccNumber,
                MinimumValue = ccMinimum,
                MaximumValue = ccMaximum,
                SampleEverySteps = ccEvery
            }
        };
    }

    private static AlgorithmVoiceDefinition DrumVoice(
        string id,
        string name,
        string parameterPresetId,
        double gate,
        int stepsPerBeat,
        IReadOnlyList<int> palette)
    {
        AlgorithmVoiceDefinition voice = Voice(
            id,
            name,
            AlgorithmKind.CellularAutomata,
            parameterPresetId,
            TrackRole.Drums,
            9,
            0,
            3,
            0,
            Math.Max(0, palette.Count - 1),
            stepsPerBeat,
            gate,
            1,
            1,
            62,
            120,
            false,
            1,
            0,
            100,
            2);
        voice.IsPercussion = true;
        voice.PitchMapping.PercussionPalette = palette.ToList();
        voice.RhythmMapping.NoteLengthRatio = 0.45;
        return voice;
    }

    private static AlgorithmVoiceDefinition CloneVoice(AlgorithmVoiceDefinition source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Enabled = source.Enabled,
            Locked = source.Locked,
            Revision = source.Revision,
            Algorithm = source.Algorithm,
            ParameterPresetId = source.ParameterPresetId,
            AlgorithmParameters = source.AlgorithmParameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
            Role = source.Role,
            MidiChannel = source.MidiChannel,
            GeneralMidiProgram = source.GeneralMidiProgram,
            IsPercussion = source.IsPercussion,
            Graph = new ValueGraphDefinition
            {
                PitchOutputNodeId = source.Graph.PitchOutputNodeId,
                RhythmOutputNodeId = source.Graph.RhythmOutputNodeId,
                VelocityOutputNodeId = source.Graph.VelocityOutputNodeId,
                ControlOutputNodeId = source.Graph.ControlOutputNodeId,
                Nodes = source.Graph.Nodes.Select(node => new ValueTransformNodeDefinition
                {
                    Id = node.Id,
                    Name = node.Name,
                    Kind = node.Kind,
                    InputNodeId = node.InputNodeId,
                    Parameters = node.Parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
                }).ToList()
            },
            PitchMapping = new ScaleMapperSettings
            {
                UseProjectScale = source.PitchMapping.UseProjectScale,
                ScaleOverride = source.PitchMapping.ScaleOverride,
                BaseOctave = source.PitchMapping.BaseOctave,
                MinimumDegree = source.PitchMapping.MinimumDegree,
                MaximumDegree = source.PitchMapping.MaximumDegree,
                MinimumMidiPitch = source.PitchMapping.MinimumMidiPitch,
                MaximumMidiPitch = source.PitchMapping.MaximumMidiPitch,
                PercussionPalette = source.PitchMapping.PercussionPalette.ToList()
            },
            RhythmMapping = new RhythmMapperSettings
            {
                StepsPerBeat = source.RhythmMapping.StepsPerBeat,
                GateThreshold = source.RhythmMapping.GateThreshold,
                MinimumDurationSteps = source.RhythmMapping.MinimumDurationSteps,
                MaximumDurationSteps = source.RhythmMapping.MaximumDurationSteps,
                NoteLengthRatio = source.RhythmMapping.NoteLengthRatio,
                Swing = source.RhythmMapping.Swing
            },
            VelocityMapping = new VelocityMapperSettings
            {
                MinimumVelocity = source.VelocityMapping.MinimumVelocity,
                MaximumVelocity = source.VelocityMapping.MaximumVelocity,
                AccentEverySteps = source.VelocityMapping.AccentEverySteps,
                AccentAmount = source.VelocityMapping.AccentAmount
            },
            ControlChangeMapping = new ControlChangeMapperSettings
            {
                Enabled = source.ControlChangeMapping.Enabled,
                ControllerNumber = source.ControlChangeMapping.ControllerNumber,
                MinimumValue = source.ControlChangeMapping.MinimumValue,
                MaximumValue = source.ControlChangeMapping.MaximumValue,
                SampleEverySteps = source.ControlChangeMapping.SampleEverySteps
            }
        };

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
